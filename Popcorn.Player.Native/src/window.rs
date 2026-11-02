//! Wayland window (smithay-client-toolkit) on the pn_run thread.
//!
//! - main surface (xdg_toplevel): the CEF overlay, as ARGB8888 linux-dmabuf buffers copied on
//!   the GPU from CEF's shared textures (overlay_gpu.rs), or wl_shm buffers filled from CEF's
//!   software paint; gets all input, which is forwarded to CEF.
//! - video subsurface, placed *below* the main surface, desynchronised: mpv draws into it from
//!   the render thread (video.rs). Transparent overlay pixels show the video.
//!
//! Both surfaces use wp_viewporter, so buffers are in physical pixels (fractional scale aware)
//! while the surface size stays logical.

use std::time::Duration;

use cef::{BrowserHost, ImplBrowserHost, KeyEvent as CefKeyEvent, KeyEventType, MouseButtonType, MouseEvent, Rect, WindowInfo};
use smithay_client_toolkit::reexports::calloop::channel::{self, Channel};
use smithay_client_toolkit::reexports::calloop::timer::{TimeoutAction, Timer};
use smithay_client_toolkit::reexports::calloop::{EventLoop, LoopHandle};
use smithay_client_toolkit::reexports::calloop_wayland_source::WaylandSource;
use smithay_client_toolkit::reexports::csd_frame::WindowState;
use smithay_client_toolkit::{
    compositor::{CompositorHandler, CompositorState, Region},
    delegate_compositor, delegate_keyboard, delegate_output, delegate_pointer, delegate_registry,
    delegate_seat, delegate_shm, delegate_subcompositor, delegate_xdg_shell, delegate_xdg_window,
    output::{OutputHandler, OutputState},
    registry::{ProvidesRegistryState, RegistryState},
    registry_handlers,
    seat::{
        Capability, SeatHandler, SeatState,
        keyboard::{KeyEvent, KeyboardHandler, Keysym, Modifiers, RawModifiers},
        pointer::{
            AxisScroll, BTN_LEFT, BTN_MIDDLE, BTN_RIGHT, CursorIcon, PointerEvent, PointerEventKind,
            PointerHandler, ThemeSpec, ThemedPointer,
        },
    },
    shell::{
        WaylandSurface,
        xdg::{
            XdgShell,
            window::{Window, WindowConfigure, WindowDecorations, WindowHandler},
        },
    },
    shm::{
        Shm, ShmHandler,
        slot::{Buffer, SlotPool},
    },
    subcompositor::SubcompositorState,
};
use wayland_client::{
    Connection, Dispatch, Proxy, QueueHandle,
    globals::registry_queue_init,
    protocol::{wl_keyboard, wl_output, wl_pointer, wl_seat, wl_shm, wl_subsurface, wl_surface},
};
use wayland_protocols::wp::fractional_scale::v1::client::{
    wp_fractional_scale_manager_v1::WpFractionalScaleManagerV1,
    wp_fractional_scale_v1::{self, WpFractionalScaleV1},
};
use wayland_protocols::wp::linux_dmabuf::zv1::client::{
    zwp_linux_buffer_params_v1::{self, ZwpLinuxBufferParamsV1},
    zwp_linux_dmabuf_feedback_v1::{self, ZwpLinuxDmabufFeedbackV1},
    zwp_linux_dmabuf_v1::{self, ZwpLinuxDmabufV1},
};
use wayland_protocols::wp::viewporter::client::{wp_viewport::WpViewport, wp_viewporter::WpViewporter};

use crate::cef_app;
use crate::dmabuf::{self, BufferData, FeedbackData};
use crate::keys::{self, flags};
use crate::log::{info, warning};
use crate::overlay_gpu;
use crate::video::{self, Video};

const DEFAULT_SIZE: (u32, u32) = (1280, 720);
const MIN_SIZE: (u32, u32) = (480, 270);
/// Give up waiting for CEF to close the browser after this long.
const CLOSE_TIMEOUT: Duration = Duration::from_secs(3);
/// Pixels per wheel notch, as Chromium uses on Linux.
const WHEEL_STEP: f64 = 40.0;
const DOUBLE_CLICK_MS: u32 = 400;
const MAX_OVERLAY_BUFFERS: usize = 4;

pub enum Cmd {
    Close,
    SetFullscreen(bool),
    SetTitle(String),
    /// CEF painted; pick up `cef_app::FRAME`.
    OverlayPainted,
    /// `cef_cursor_type_t` value.
    Cursor(u32),
    BrowserCreated,
    BrowserClosed,
    /// The video thread finished setting up (render context created or failed).
    VideoReady,
}

pub type CmdSender = channel::Sender<Cmd>;

struct OverlayBuffer {
    buffer: Buffer,
    /// Area of the current frame this buffer doesn't have yet.
    damage: Option<Rect>,
}

struct LastClick {
    button: u32,
    time: u32,
    x: f64,
    y: f64,
    count: i32,
}

pub(crate) struct App {
    conn: Connection,
    qh: QueueHandle<App>,
    loop_handle: LoopHandle<'static, App>,
    registry_state: RegistryState,
    seat_state: SeatState,
    output_state: OutputState,
    compositor: CompositorState,
    shm: Shm,
    /// linux-dmabuf v4 and its default feedback (the compositor's GPU and formats).
    dmabuf: Option<(ZwpLinuxDmabufV1, ZwpLinuxDmabufFeedbackV1)>,

    window: Window,
    main_viewport: WpViewport,
    _fractional_scale: Option<WpFractionalScaleV1>,
    video_surface: wl_surface::WlSurface,
    _video_subsurface: wl_subsurface::WlSubsurface,
    video_viewport: WpViewport,
    video: Option<Video>,

    /// Logical window size.
    width: u32,
    height: u32,
    scale: f64,
    has_fractional_scale: bool,
    configured: bool,
    fullscreen: bool,

    pool: SlotPool,
    buffers: Vec<OverlayBuffer>,
    buffer_size: (i32, i32),
    /// Changed since the last commit of the main surface.
    commit_damage: Option<Rect>,
    /// Transparent 1x1 placeholder until CEF's first paint (needed to map the window).
    placeholder: Option<Buffer>,
    frame_pending: bool,
    overlay_dirty: bool,

    keyboard: Option<wl_keyboard::WlKeyboard>,
    keyboard_focus: bool,
    pointer: Option<ThemedPointer>,
    /// Keyboard modifier flags (cef_event_flags_t).
    modifiers: u32,
    /// Held mouse button flags (cef_event_flags_t).
    buttons: u32,
    last_click: Option<LastClick>,
    cursor: Option<CursorIcon>,

    browser_ready: bool,
    video_ready: bool,
    /// EVENT_READY sent.
    ready: bool,
    closing: bool,
    exit: bool,
}

pub fn run() -> Result<(), String> {
    let conn = Connection::connect_to_env().map_err(|e| format!("Cannot connect to Wayland: {e}"))?;
    let (globals, event_queue) = registry_queue_init::<App>(&conn).map_err(|e| format!("Wayland registry: {e}"))?;
    let qh = event_queue.handle();
    let mut event_loop: EventLoop<'static, App> = EventLoop::try_new().map_err(|e| format!("event loop: {e}"))?;
    let loop_handle = event_loop.handle();
    WaylandSource::new(conn.clone(), event_queue)
        .insert(loop_handle.clone())
        .map_err(|e| format!("Wayland source: {e}"))?;

    let compositor = CompositorState::bind(&globals, &qh).map_err(|e| format!("wl_compositor: {e}"))?;
    let subcompositor = SubcompositorState::bind(compositor.wl_compositor().clone(), &globals, &qh)
        .map_err(|e| format!("wl_subcompositor: {e}"))?;
    let xdg_shell = XdgShell::bind(&globals, &qh).map_err(|e| format!("xdg_wm_base: {e}"))?;
    let shm = Shm::bind(&globals, &qh).map_err(|e| format!("wl_shm: {e}"))?;
    let viewporter: WpViewporter = globals.bind(&qh, 1..=1, ()).map_err(|e| format!("wp_viewporter: {e}"))?;
    let fractional_manager: Option<WpFractionalScaleManagerV1> = globals.bind(&qh, 1..=1, ()).ok();
    let dmabuf = match globals.bind::<ZwpLinuxDmabufV1, _, _>(&qh, 4..=4, ()) {
        Ok(dmabuf) => {
            let feedback = dmabuf.get_default_feedback(&qh, FeedbackData::default());
            Some((dmabuf, feedback))
        }
        Err(e) => {
            info!("No zwp_linux_dmabuf_v1 v4 ({e}), using shared memory buffers");
            None
        }
    };

    let cfg = crate::config();
    let surface = compositor.create_surface(&qh);
    let window = xdg_shell.create_window(surface, WindowDecorations::RequestServer, &qh);
    window.set_title(cfg.title.clone());
    window.set_app_id(cfg.app_id.clone());
    window.set_min_size(Some(MIN_SIZE));
    let main_surface = window.wl_surface().clone();
    let main_viewport = viewporter.get_viewport(&main_surface, &qh, ());
    let fractional_scale = fractional_manager.as_ref().map(|m| m.get_fractional_scale(&main_surface, &qh, ()));

    let (video_subsurface, video_surface) = subcompositor.create_subsurface(main_surface.clone(), &qh);
    video_subsurface.set_position(0, 0);
    video_subsurface.place_below(&main_surface);
    video_subsurface.set_desync();
    let video_viewport = viewporter.get_viewport(&video_surface, &qh, ());
    // Input goes to the overlay (main surface) everywhere.
    if let Ok(empty) = Region::new(&compositor) {
        video_surface.set_input_region(Some(empty.wl_region()));
    }
    window.commit();

    let (tx, rx): (CmdSender, Channel<Cmd>) = channel::channel();
    loop_handle
        .insert_source(rx, |event, _, app: &mut App| {
            if let channel::Event::Msg(cmd) = event {
                app.handle_cmd(cmd);
            }
        })
        .map_err(|e| format!("command channel: {e}"))?;
    crate::set_loop_sender(Some(tx));

    let pool = SlotPool::new((DEFAULT_SIZE.0 * DEFAULT_SIZE.1 * 4) as usize, &shm).map_err(|e| format!("shm pool: {e}"))?;
    let mut app = App {
        conn: conn.clone(),
        qh: qh.clone(),
        loop_handle: loop_handle.clone(),
        registry_state: RegistryState::new(&globals),
        seat_state: SeatState::new(&globals, &qh),
        output_state: OutputState::new(&globals, &qh),
        compositor,
        shm,
        dmabuf,
        window,
        main_viewport,
        _fractional_scale: fractional_scale,
        video_surface,
        _video_subsurface: video_subsurface,
        video_viewport,
        video: None,
        width: DEFAULT_SIZE.0,
        height: DEFAULT_SIZE.1,
        scale: 1.0,
        has_fractional_scale: false,
        configured: false,
        fullscreen: false,
        pool,
        buffers: Vec::new(),
        buffer_size: (0, 0),
        commit_damage: None,
        placeholder: None,
        frame_pending: false,
        overlay_dirty: false,
        keyboard: None,
        keyboard_focus: false,
        pointer: None,
        modifiers: 0,
        buttons: 0,
        last_click: None,
        cursor: Some(CursorIcon::Default),
        browser_ready: false,
        video_ready: false,
        ready: false,
        closing: false,
        exit: false,
    };

    let result = run_loop(&mut event_loop, &mut app);

    // Teardown order: video (render context) before the surfaces and the mpv core; CEF last.
    if let Some(mut video) = app.video.take() {
        video.stop();
    }
    crate::set_loop_sender(None);
    if overlay_gpu::enabled() {
        info!("Overlay: {} frames presented", overlay_gpu::presented());
    }
    if result.is_ok() {
        cef_app::shutdown();
    }
    drop(app);
    let _ = conn.flush();
    result
}

fn run_loop(event_loop: &mut EventLoop<'static, App>, app: &mut App) -> Result<(), String> {
    let dispatch = |event_loop: &mut EventLoop<'static, App>, app: &mut App| {
        event_loop.dispatch(None, app).map_err(|e| format!("event loop: {e}"))
    };

    // The first configure gives the size (and fractional scale usually arrives with it); the
    // dmabuf feedback comes in the same round trip.
    while !(app.configured && app.feedback().is_some() || app.dmabuf.is_none() && app.configured) && !app.exit {
        dispatch(event_loop, app)?;
    }
    if app.exit {
        return Ok(());
    }
    info!("Window configured: {}x{} @ {}", app.width, app.height, app.scale);
    let feedback = app.feedback();
    if let Some(feedback) = &feedback {
        dmabuf::log_feedback(feedback);
        if std::env::var("POPCORN_PLAYER_OVERLAY").is_ok_and(|v| v == "shm") {
            info!("Overlay: shared memory buffers (POPCORN_PLAYER_OVERLAY=shm)");
        } else if let Err(e) = overlay_gpu::init(feedback) {
            warning!("Overlay: GPU path unavailable ({e}), using shared memory buffers");
        }
    }

    // Video first: its GPU/mpv set-up runs on its own thread while CEF starts here.
    let (pw, ph) = app.physical_size();
    let target = video::Target {
        conn: app.conn.clone(),
        qh: app.qh.clone(),
        surface: app.video_surface.clone(),
        dmabuf: app.dmabuf.as_ref().zip(feedback).map(|((dmabuf, _), feedback)| (dmabuf.clone(), feedback)),
        mpv: crate::mpv_core(),
    };
    app.video = Some(Video::start(target, pw, ph));

    cef_app::initialize(app.width as i32, app.height as i32, app.scale as f32)?;

    while !app.exit {
        dispatch(event_loop, app)?;
    }
    Ok(())
}

impl App {
    fn feedback(&self) -> Option<dmabuf::Feedback> {
        let (_, feedback) = self.dmabuf.as_ref()?;
        feedback.data::<FeedbackData>()?.result()
    }

    fn physical_size(&self) -> (i32, i32) {
        (
            (self.width as f64 * self.scale).round() as i32,
            (self.height as f64 * self.scale).round() as i32,
        )
    }

    fn handle_cmd(&mut self, cmd: Cmd) {
        match cmd {
            Cmd::Close => self.begin_close(),
            Cmd::SetFullscreen(on) => self.set_fullscreen(on),
            Cmd::SetTitle(title) => self.window.set_title(title),
            Cmd::OverlayPainted => {
                cef_app::PAINT_QUEUED.store(false, std::sync::atomic::Ordering::SeqCst);
                self.present_overlay();
            }
            Cmd::Cursor(cursor) => {
                self.cursor = cursor_icon(cursor);
                self.apply_cursor();
            }
            Cmd::VideoReady => {
                self.video_ready = true;
                self.check_ready();
            }
            Cmd::BrowserCreated => {
                self.browser_ready = true;
                self.check_ready();
                if self.keyboard_focus {
                    cef_app::with_host(|host| host.set_focus(1));
                }
            }
            Cmd::BrowserClosed => {
                if !self.closing {
                    warning!("Overlay browser went away, closing the player");
                    self.closing = true;
                    crate::emit_window_event(crate::EVENT_CLOSE_REQUESTED, 0);
                }
                self.exit = true;
            }
        }
    }

    /// Both the overlay and the video output exist: mpv's vo=libmpv refuses to open a file before
    /// the render context is there, and CEF start-up is over (see EVENT_READY).
    fn check_ready(&mut self) {
        if self.browser_ready && self.video_ready && !self.ready {
            self.ready = true;
            info!("Player ready");
            crate::emit_window_event(crate::EVENT_READY, 0);
        }
    }

    fn begin_close(&mut self) {
        if self.closing {
            return;
        }
        self.closing = true;
        info!("Closing player window");
        crate::emit_window_event(crate::EVENT_CLOSE_REQUESTED, 0);
        if !cef_app::close_browser() {
            // Browser not created yet: on_after_created closes it as soon as it exists.
            info!("Overlay browser not created yet, closing once it is");
        }
        let _ = self.loop_handle.insert_source(Timer::from_duration(CLOSE_TIMEOUT), |_, _, app| {
            if !app.exit {
                warning!("Overlay browser did not close in time");
                app.exit = true;
            }
            TimeoutAction::Drop
        });
    }

    fn set_fullscreen(&mut self, on: bool) {
        if on {
            self.window.set_fullscreen(None);
        } else {
            self.window.unset_fullscreen();
        }
    }

    /// Window size or scale changed: viewports, video buffer size, CEF view.
    fn apply_size(&mut self, qh: &QueueHandle<Self>) {
        let (w, h) = (self.width as i32, self.height as i32);
        let (pw, ph) = self.physical_size();
        self.main_viewport.set_destination(w, h);
        self.video_viewport.set_destination(w, h);
        if let Ok(opaque) = Region::new(&self.compositor) {
            opaque.add(0, 0, w, h);
            self.video_surface.set_opaque_region(Some(opaque.wl_region()));
        }
        if let Some(video) = &self.video {
            video.resize(pw, ph);
        }
        cef_app::set_view(w, h, self.scale as f32);

        let surface = self.window.wl_surface().clone();
        if self.buffers.is_empty() && self.placeholder.is_none() {
            // Must attach something on the first configure to map the window.
            if let Ok((buffer, canvas)) = self.pool.create_buffer(1, 1, 4, wl_shm::Format::Argb8888) {
                canvas.fill(0);
                if buffer.attach_to(&surface).is_ok() {
                    surface.damage_buffer(0, 0, 1, 1);
                }
                self.placeholder = Some(buffer);
            }
        }
        if !self.frame_pending {
            surface.frame(qh, surface.clone());
            self.frame_pending = true;
        }
        surface.commit();
    }

    /// Commits the newest overlay frame, at most once per frame callback.
    fn present_overlay(&mut self) {
        if self.frame_pending {
            self.overlay_dirty = true;
            return;
        }
        if overlay_gpu::enabled()
            && let Some((dmabuf, _)) = &self.dmabuf
        {
            let surface = self.window.wl_surface().clone();
            if overlay_gpu::present(&surface, dmabuf, &self.qh) {
                self.placeholder = None;
                surface.frame(&self.qh, surface.clone());
                self.frame_pending = true;
                surface.commit();
                return;
            }
        }
        // Software frames (also if CEF falls back to them with the GPU path set up).
        self.present_overlay_shm();
    }

    /// Copies what CEF painted into a free shm buffer and commits it.
    fn present_overlay_shm(&mut self) {
        let frame = cef_app::FRAME.lock().unwrap();
        let (fw, fh) = (frame.width, frame.height);
        if fw <= 0 || fh <= 0 {
            return;
        }
        let full = Rect { x: 0, y: 0, width: fw, height: fh };
        let mut frame = frame;
        let new_damage = frame.damage.take();
        if self.buffer_size != (fw, fh) {
            self.buffers.clear();
            self.buffer_size = (fw, fh);
            self.commit_damage = Some(full.clone());
        }
        if let Some(d) = &new_damage {
            for b in &mut self.buffers {
                b.damage = Some(cef_app::union(b.damage.clone(), d.clone()));
            }
            self.commit_damage = Some(cef_app::union(self.commit_damage.clone(), d.clone()));
        }
        let Some(commit_damage) = self.commit_damage.clone() else {
            return; // nothing new
        };

        let free = (0..self.buffers.len()).find(|&i| self.pool.canvas(&self.buffers[i].buffer).is_some());
        let index = match free {
            Some(i) => i,
            None if self.buffers.len() < MAX_OVERLAY_BUFFERS => {
                match self.pool.create_buffer(fw, fh, fw * 4, wl_shm::Format::Argb8888) {
                    Ok((buffer, _)) => {
                        self.buffers.push(OverlayBuffer { buffer, damage: Some(full.clone()) });
                        self.buffers.len() - 1
                    }
                    Err(e) => {
                        warning!("Cannot allocate overlay buffer: {e}");
                        return;
                    }
                }
            }
            None => {
                // All buffers still held by the compositor: retry on the next frame callback.
                self.overlay_dirty = true;
                return;
            }
        };

        let damage = self.buffers[index].damage.take().unwrap_or(full);
        let stride = fw as usize * 4;
        let Some(canvas) = self.pool.canvas(&self.buffers[index].buffer) else { return };
        let (x0, x1) = (damage.x as usize, (damage.x + damage.width) as usize);
        for y in damage.y as usize..(damage.y + damage.height) as usize {
            let range = y * stride + x0 * 4..y * stride + x1 * 4;
            canvas[range.clone()].copy_from_slice(&frame.data[range]);
        }
        drop(frame);

        let surface = self.window.wl_surface().clone();
        if self.buffers[index].buffer.attach_to(&surface).is_err() {
            return;
        }
        self.placeholder = None;
        self.commit_damage = None;
        surface.damage_buffer(commit_damage.x, commit_damage.y, commit_damage.width, commit_damage.height);
        surface.frame(&self.qh, surface.clone());
        self.frame_pending = true;
        surface.commit();
    }

    fn apply_cursor(&self) {
        let Some(pointer) = &self.pointer else { return };
        let result = match self.cursor {
            Some(icon) => pointer.set_cursor(&self.conn, icon),
            None => pointer.hide_cursor(),
        };
        if let Err(e) = result {
            warning!("Cannot set cursor: {e}");
        }
    }

    fn mouse_event(&self, position: (f64, f64)) -> MouseEvent {
        MouseEvent {
            x: position.0.round() as i32,
            y: position.1.round() as i32,
            modifiers: self.modifiers | self.buttons,
        }
    }

    fn click_count(&mut self, button: u32, time: u32, (x, y): (f64, f64)) -> i32 {
        let count = match &self.last_click {
            Some(last)
                if last.button == button
                    && time.wrapping_sub(last.time) <= DOUBLE_CLICK_MS
                    && (last.x - x).abs() < 5.0
                    && (last.y - y).abs() < 5.0 =>
            {
                (last.count % 3) + 1
            }
            _ => 1,
        };
        self.last_click = Some(LastClick { button, time, x, y, count });
        count
    }

    fn send_key(&mut self, event: &KeyEvent, pressed: bool, repeat: bool) {
        let (vk, keypad) = keys::windows_key_code(event.keysym.raw(), event.raw_code);
        let modifiers = self.modifiers | if keypad { flags::IS_KEY_PAD } else { 0 };
        let plain = self.modifiers & (flags::SHIFT | flags::CONTROL | flags::ALT) == 0;

        if pressed && !repeat && plain {
            match vk {
                keys::VK_F12 => {
                    cef_app::with_host(|host| host.show_dev_tools(Some(&WindowInfo::default()), None, None, None));
                    return;
                }
                keys::VK_F11 => {
                    self.set_fullscreen(!self.fullscreen);
                    return;
                }
                _ => {}
            }
        }

        let mut key = CefKeyEvent {
            type_: if pressed { KeyEventType::RAWKEYDOWN } else { KeyEventType::KEYUP },
            modifiers,
            windows_key_code: vk,
            native_key_code: (event.raw_code + 8) as i32, // evdev -> XKB keycode
            ..Default::default()
        };
        let text: Vec<u16> = if pressed {
            event.utf8.as_deref().unwrap_or("").encode_utf16().collect()
        } else {
            Vec::new()
        };
        cef_app::with_host(|host: &BrowserHost| {
            host.send_key_event(Some(&key));
            for unit in &text {
                key.type_ = KeyEventType::CHAR;
                key.character = *unit;
                key.unmodified_character = *unit;
                host.send_key_event(Some(&key));
            }
        });
    }
}

/// Wheel delta in CEF pixels (positive = scroll up/left, Wayland is the opposite).
fn wheel_delta(axis: &AxisScroll) -> i32 {
    if axis.value120 != 0 {
        (-(axis.value120 as f64) / 120.0 * WHEEL_STEP).round() as i32
    } else if axis.discrete != 0 {
        (-(axis.discrete as f64) * WHEEL_STEP).round() as i32
    } else {
        (-axis.absolute).round() as i32
    }
}

fn mouse_button(button: u32) -> Option<(MouseButtonType, u32)> {
    match button {
        BTN_LEFT => Some((MouseButtonType::LEFT, flags::LEFT_MOUSE)),
        BTN_MIDDLE => Some((MouseButtonType::MIDDLE, flags::MIDDLE_MOUSE)),
        BTN_RIGHT => Some((MouseButtonType::RIGHT, flags::RIGHT_MOUSE)),
        _ => None,
    }
}

/// `cef_cursor_type_t` -> cursor shape; `None` hides the pointer (CSS `cursor: none`).
fn cursor_icon(cursor: u32) -> Option<CursorIcon> {
    use CursorIcon::*;
    Some(match cursor {
        1 => Crosshair,
        2 => Pointer,
        3 => Text,
        4 => Wait,
        5 => Help,
        6 => EResize,
        7 => NResize,
        8 => NeResize,
        9 => NwResize,
        10 => SResize,
        11 => SeResize,
        12 => SwResize,
        13 => WResize,
        14 => NsResize,
        15 => EwResize,
        16 => NeswResize,
        17 => NwseResize,
        18 => ColResize,
        19 => RowResize,
        20..=28 => AllScroll,
        29 => Move,
        30 => VerticalText,
        31 => Cell,
        32 => ContextMenu,
        33 => Alias,
        34 => Progress,
        35 => NoDrop,
        36 => Copy,
        37 => return None,
        38 => NotAllowed,
        39 => ZoomIn,
        40 => ZoomOut,
        41 => Grab,
        42 => Grabbing,
        _ => Default,
    })
}

// ---------------------------------------------------------------------------------------------
// SCTK handlers
// ---------------------------------------------------------------------------------------------

impl CompositorHandler for App {
    fn scale_factor_changed(&mut self, _: &Connection, qh: &QueueHandle<Self>, surface: &wl_surface::WlSurface, factor: i32) {
        if !self.has_fractional_scale && surface == self.window.wl_surface() && self.scale != factor as f64 {
            self.scale = factor as f64;
            if self.configured {
                self.apply_size(qh);
            }
        }
    }

    fn transform_changed(&mut self, _: &Connection, _: &QueueHandle<Self>, _: &wl_surface::WlSurface, _: wl_output::Transform) {}

    fn frame(&mut self, _: &Connection, _: &QueueHandle<Self>, surface: &wl_surface::WlSurface, _: u32) {
        if surface != self.window.wl_surface() {
            return;
        }
        self.frame_pending = false;
        if self.overlay_dirty {
            self.overlay_dirty = false;
            self.present_overlay();
        }
    }

    fn surface_enter(&mut self, _: &Connection, _: &QueueHandle<Self>, _: &wl_surface::WlSurface, _: &wl_output::WlOutput) {}
    fn surface_leave(&mut self, _: &Connection, _: &QueueHandle<Self>, _: &wl_surface::WlSurface, _: &wl_output::WlOutput) {}
}

impl OutputHandler for App {
    fn output_state(&mut self) -> &mut OutputState {
        &mut self.output_state
    }
    fn new_output(&mut self, _: &Connection, _: &QueueHandle<Self>, _: wl_output::WlOutput) {}
    fn update_output(&mut self, _: &Connection, _: &QueueHandle<Self>, _: wl_output::WlOutput) {}
    fn output_destroyed(&mut self, _: &Connection, _: &QueueHandle<Self>, _: wl_output::WlOutput) {}
}

impl WindowHandler for App {
    fn request_close(&mut self, _: &Connection, _: &QueueHandle<Self>, _: &Window) {
        self.begin_close();
    }

    fn configure(&mut self, _: &Connection, qh: &QueueHandle<Self>, _: &Window, configure: WindowConfigure, _: u32) {
        self.width = configure.new_size.0.map_or(self.width, |v| v.get()).max(1);
        self.height = configure.new_size.1.map_or(self.height, |v| v.get()).max(1);
        let fullscreen = configure.state.contains(WindowState::FULLSCREEN);
        if fullscreen != self.fullscreen {
            self.fullscreen = fullscreen;
            crate::emit_window_event(crate::EVENT_FULLSCREEN_CHANGED, fullscreen as i32);
        }
        self.configured = true;
        self.apply_size(qh);
    }
}

impl SeatHandler for App {
    fn seat_state(&mut self) -> &mut SeatState {
        &mut self.seat_state
    }

    fn new_seat(&mut self, _: &Connection, _: &QueueHandle<Self>, _: wl_seat::WlSeat) {}

    fn new_capability(&mut self, _: &Connection, qh: &QueueHandle<Self>, seat: wl_seat::WlSeat, capability: Capability) {
        if capability == Capability::Keyboard && self.keyboard.is_none() {
            match self.seat_state.get_keyboard_with_repeat(
                qh,
                &seat,
                None,
                self.loop_handle.clone(),
                Box::new(|app: &mut App, _, event| app.send_key(&event, true, true)),
            ) {
                Ok(keyboard) => self.keyboard = Some(keyboard),
                Err(e) => warning!("No keyboard: {e}"),
            }
        }
        if capability == Capability::Pointer && self.pointer.is_none() {
            let cursor_surface = self.compositor.create_surface(qh);
            match self.seat_state.get_pointer_with_theme(qh, &seat, self.shm.wl_shm(), cursor_surface, ThemeSpec::default()) {
                Ok(pointer) => self.pointer = Some(pointer),
                Err(e) => warning!("No pointer: {e}"),
            }
        }
    }

    fn remove_capability(&mut self, _: &Connection, _: &QueueHandle<Self>, _: wl_seat::WlSeat, capability: Capability) {
        if capability == Capability::Keyboard
            && let Some(keyboard) = self.keyboard.take()
        {
            keyboard.release();
        }
        if capability == Capability::Pointer
            && let Some(pointer) = self.pointer.take()
        {
            pointer.pointer().release();
        }
    }

    fn remove_seat(&mut self, _: &Connection, _: &QueueHandle<Self>, _: wl_seat::WlSeat) {}
}

impl KeyboardHandler for App {
    fn enter(
        &mut self,
        _: &Connection,
        _: &QueueHandle<Self>,
        _: &wl_keyboard::WlKeyboard,
        surface: &wl_surface::WlSurface,
        _: u32,
        _: &[u32],
        _: &[Keysym],
    ) {
        if surface == self.window.wl_surface() {
            self.keyboard_focus = true;
            cef_app::with_host(|host| host.set_focus(1));
        }
    }

    fn leave(&mut self, _: &Connection, _: &QueueHandle<Self>, _: &wl_keyboard::WlKeyboard, surface: &wl_surface::WlSurface, _: u32) {
        if surface == self.window.wl_surface() {
            self.keyboard_focus = false;
            cef_app::with_host(|host| host.set_focus(0));
        }
    }

    fn press_key(&mut self, _: &Connection, _: &QueueHandle<Self>, _: &wl_keyboard::WlKeyboard, _: u32, event: KeyEvent) {
        self.send_key(&event, true, false);
    }

    fn repeat_key(&mut self, _: &Connection, _: &QueueHandle<Self>, _: &wl_keyboard::WlKeyboard, _: u32, event: KeyEvent) {
        self.send_key(&event, true, true);
    }

    fn release_key(&mut self, _: &Connection, _: &QueueHandle<Self>, _: &wl_keyboard::WlKeyboard, _: u32, event: KeyEvent) {
        self.send_key(&event, false, false);
    }

    fn update_modifiers(
        &mut self,
        _: &Connection,
        _: &QueueHandle<Self>,
        _: &wl_keyboard::WlKeyboard,
        _: u32,
        modifiers: Modifiers,
        _: RawModifiers,
        _: u32,
    ) {
        let mut m = 0;
        for (on, flag) in [
            (modifiers.shift, flags::SHIFT),
            (modifiers.ctrl, flags::CONTROL),
            (modifiers.alt, flags::ALT),
            (modifiers.caps_lock, flags::CAPS_LOCK),
            (modifiers.num_lock, flags::NUM_LOCK),
        ] {
            if on {
                m |= flag;
            }
        }
        self.modifiers = m;
    }
}

impl PointerHandler for App {
    fn pointer_frame(&mut self, _: &Connection, _: &QueueHandle<Self>, _: &wl_pointer::WlPointer, events: &[PointerEvent]) {
        for event in events {
            if &event.surface != self.window.wl_surface() {
                continue;
            }
            match event.kind {
                PointerEventKind::Enter { .. } => {
                    self.apply_cursor();
                    let me = self.mouse_event(event.position);
                    cef_app::with_host(|host| host.send_mouse_move_event(Some(&me), 0));
                }
                PointerEventKind::Leave { .. } => {
                    self.buttons = 0;
                    let me = self.mouse_event(event.position);
                    cef_app::with_host(|host| host.send_mouse_move_event(Some(&me), 1));
                }
                PointerEventKind::Motion { .. } => {
                    let me = self.mouse_event(event.position);
                    cef_app::with_host(|host| host.send_mouse_move_event(Some(&me), 0));
                }
                PointerEventKind::Press { button, time, .. } => {
                    let Some((kind, flag)) = mouse_button(button) else { continue };
                    let count = self.click_count(button, time, event.position);
                    self.buttons |= flag;
                    let me = self.mouse_event(event.position);
                    cef_app::with_host(|host| host.send_mouse_click_event(Some(&me), kind, 0, count));
                }
                PointerEventKind::Release { button, .. } => {
                    let Some((kind, flag)) = mouse_button(button) else { continue };
                    let count = self.last_click.as_ref().filter(|c| c.button == button).map_or(1, |c| c.count);
                    self.buttons &= !flag;
                    let me = self.mouse_event(event.position);
                    cef_app::with_host(|host| host.send_mouse_click_event(Some(&me), kind, 1, count));
                }
                PointerEventKind::Axis { horizontal, vertical, .. } => {
                    let (dx, dy) = (wheel_delta(&horizontal), wheel_delta(&vertical));
                    if dx != 0 || dy != 0 {
                        let me = self.mouse_event(event.position);
                        cef_app::with_host(|host| host.send_mouse_wheel_event(Some(&me), dx, dy));
                    }
                }
            }
        }
    }
}

impl ShmHandler for App {
    fn shm_state(&mut self) -> &mut Shm {
        &mut self.shm
    }
}

impl ProvidesRegistryState for App {
    fn registry(&mut self) -> &mut RegistryState {
        &mut self.registry_state
    }
    registry_handlers![OutputState, SeatState];
}

// Protocols SCTK doesn't wrap.

impl Dispatch<WpViewporter, ()> for App {
    fn event(_: &mut Self, _: &WpViewporter, _: <WpViewporter as Proxy>::Event, _: &(), _: &Connection, _: &QueueHandle<Self>) {}
}

impl Dispatch<WpViewport, ()> for App {
    fn event(_: &mut Self, _: &WpViewport, _: <WpViewport as Proxy>::Event, _: &(), _: &Connection, _: &QueueHandle<Self>) {}
}

impl Dispatch<WpFractionalScaleManagerV1, ()> for App {
    fn event(
        _: &mut Self,
        _: &WpFractionalScaleManagerV1,
        _: <WpFractionalScaleManagerV1 as Proxy>::Event,
        _: &(),
        _: &Connection,
        _: &QueueHandle<Self>,
    ) {
    }
}

impl Dispatch<WpFractionalScaleV1, ()> for App {
    fn event(
        app: &mut Self,
        _: &WpFractionalScaleV1,
        event: wp_fractional_scale_v1::Event,
        _: &(),
        _: &Connection,
        qh: &QueueHandle<Self>,
    ) {
        if let wp_fractional_scale_v1::Event::PreferredScale { scale } = event {
            let scale = scale as f64 / 120.0;
            app.has_fractional_scale = true;
            if app.scale != scale {
                app.scale = scale;
                if app.configured {
                    app.apply_size(qh);
                }
            }
        }
    }
}

impl Dispatch<ZwpLinuxDmabufV1, ()> for App {
    fn event(_: &mut Self, _: &ZwpLinuxDmabufV1, _: zwp_linux_dmabuf_v1::Event, _: &(), _: &Connection, _: &QueueHandle<Self>) {
        // format/modifier events are only sent to version < 4.
    }
}

impl Dispatch<ZwpLinuxDmabufFeedbackV1, FeedbackData> for App {
    fn event(
        _: &mut Self,
        _: &ZwpLinuxDmabufFeedbackV1,
        event: zwp_linux_dmabuf_feedback_v1::Event,
        data: &FeedbackData,
        _: &Connection,
        _: &QueueHandle<Self>,
    ) {
        data.handle(event);
    }
}

impl Dispatch<ZwpLinuxBufferParamsV1, ()> for App {
    fn event(
        _: &mut Self,
        _: &ZwpLinuxBufferParamsV1,
        event: zwp_linux_buffer_params_v1::Event,
        _: &(),
        _: &Connection,
        _: &QueueHandle<Self>,
    ) {
        if let zwp_linux_buffer_params_v1::Event::Failed = event {
            warning!("The compositor rejected a dmabuf buffer");
        }
    }

    // Only `create` (not used, we use create_immed) sends `created` with a new wl_buffer.
    wayland_client::event_created_child!(App, ZwpLinuxBufferParamsV1, [
        zwp_linux_buffer_params_v1::EVT_CREATED_OPCODE => (wayland_client::protocol::wl_buffer::WlBuffer, dmabuf::no_op_buffer_data())
    ]);
}

impl Dispatch<wayland_client::protocol::wl_buffer::WlBuffer, BufferData> for App {
    fn event(
        _: &mut Self,
        _: &wayland_client::protocol::wl_buffer::WlBuffer,
        event: wayland_client::protocol::wl_buffer::Event,
        data: &BufferData,
        _: &Connection,
        _: &QueueHandle<Self>,
    ) {
        if let wayland_client::protocol::wl_buffer::Event::Release = event {
            data.released();
        }
    }
}

delegate_compositor!(App);
delegate_subcompositor!(App);
delegate_output!(App);
delegate_shm!(App);
delegate_seat!(App);
delegate_keyboard!(App);
delegate_pointer!(App);
delegate_xdg_shell!(App);
delegate_xdg_window!(App);
delegate_registry!(App);
