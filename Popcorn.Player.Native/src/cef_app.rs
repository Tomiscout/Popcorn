//! CEF overlay: one windowless (off-screen rendered) browser showing the player controls.
//!
//! CEF runs its UI thread itself (`multi_threaded_message_loop`), so every callback below runs on
//! that thread. It talks to the Wayland loop only through the shared state here (view size,
//! painted pixels) and `crate::send_cmd`. BrowserHost methods used from the Wayland thread
//! (input, resize) are safe to call from any thread: CEF posts them to its UI thread.
//!
//! The overlay bundle is served from disk as `http://app.local/...` (the base href baked into
//! `index-chrome.html` by Popcorn.Web's `post-build-player.js`), with `Config::inject_script` added
//! to HTML pages so they find the C# WebSocket bridge (`window.wsAddress`).

use std::ffi::{CString, c_char, c_int};
use std::path::{Component, Path, PathBuf};
use std::sync::atomic::{AtomicBool, AtomicUsize, Ordering};
use std::sync::{Arc, Mutex};

use cef::*;

use crate::log::{debug, error, info, warning};
use crate::window::Cmd;

const OVERLAY_ORIGIN: &str = "http://app.local/";

// ---------------------------------------------------------------------------------------------
// State shared with the Wayland thread
// ---------------------------------------------------------------------------------------------

/// Logical (DIP) size of the overlay and the output scale. Read by CEF in `view_rect`/`screen_info`.
#[derive(Clone, Copy)]
pub struct ViewInfo {
    pub width: i32,
    pub height: i32,
    pub scale: f32,
}

static VIEW: Mutex<ViewInfo> = Mutex::new(ViewInfo {
    width: 1280,
    height: 720,
    scale: 1.0,
});

/// Last painted overlay (BGRA premultiplied = wl_shm ARGB8888) plus the area changed since the
/// Wayland thread last looked. Software paint path only (see overlay_gpu.rs for the GPU one).
#[derive(Default)]
pub struct OverlayFrame {
    pub width: i32,
    pub height: i32,
    pub data: Vec<u8>,
    pub damage: Option<Rect>,
}

pub static FRAME: Mutex<OverlayFrame> = Mutex::new(OverlayFrame {
    width: 0,
    height: 0,
    data: Vec::new(),
    damage: None,
});
/// A `Cmd::OverlayPainted` is queued and not yet handled (coalesces paints).
pub static PAINT_QUEUED: AtomicBool = AtomicBool::new(false);

struct HostRef(BrowserHost);
// SAFETY: CEF objects are atomically ref-counted and the BrowserHost methods we call post
// themselves to the CEF UI thread.
unsafe impl Send for HostRef {}

static HOST: Mutex<Option<HostRef>> = Mutex::new(None);
static CLOSING: AtomicBool = AtomicBool::new(false);

pub fn with_host(f: impl FnOnce(&BrowserHost)) {
    if let Some(host) = HOST.lock().unwrap().as_ref() {
        f(&host.0);
    }
}

pub fn set_view(width: i32, height: i32, scale: f32) {
    let changed = {
        let mut v = VIEW.lock().unwrap();
        let changed_size = v.width != width || v.height != height;
        let changed_scale = v.scale != scale;
        *v = ViewInfo {
            width: width.max(1),
            height: height.max(1),
            scale,
        };
        (changed_size, changed_scale)
    };
    with_host(|host| {
        if changed.1 {
            host.notify_screen_info_changed();
        }
        if changed.0 || changed.1 {
            host.was_resized();
        }
    });
}

pub fn union(a: Option<Rect>, b: Rect) -> Rect {
    match a {
        None => b,
        Some(a) => {
            let x0 = a.x.min(b.x);
            let y0 = a.y.min(b.y);
            let x1 = (a.x + a.width).max(b.x + b.width);
            let y1 = (a.y + a.height).max(b.y + b.height);
            Rect {
                x: x0,
                y: y0,
                width: x1 - x0,
                height: y1 - y0,
            }
        }
    }
}

// ---------------------------------------------------------------------------------------------
// Startup / shutdown (called on the pn_run thread)
// ---------------------------------------------------------------------------------------------

/// Argv for CEF: only our own switches, not the player's file arguments.
struct CefArgs {
    _strings: Vec<CString>,
    argv: Vec<*const c_char>,
}

impl CefArgs {
    fn new() -> CefArgs {
        let strings = vec![CString::new("popcorn-player").unwrap()];
        let argv = strings.iter().map(|s| s.as_ptr()).collect();
        CefArgs {
            _strings: strings,
            argv,
        }
    }

    fn main_args(&self) -> MainArgs {
        MainArgs {
            argc: self.argv.len() as c_int,
            argv: self.argv.as_ptr() as *mut *mut c_char,
        }
    }
}

pub fn initialize(width: i32, height: i32, scale: f32) -> Result<(), String> {
    let _ = api_hash(sys::CEF_API_VERSION_LAST, 0);
    *VIEW.lock().unwrap() = ViewInfo { width, height, scale };
    CLOSING.store(false, Ordering::SeqCst);

    let cfg = crate::config();
    let runtime_dir = module_dir().ok_or("Cannot locate libpopcorn_player.so")?;
    let cache = cfg.cache_dir.to_string_lossy();
    let settings = Settings {
        browser_subprocess_path: CefString::from(cfg.subprocess_path.to_string_lossy().as_ref()),
        // CEF runs its own UI thread; the Wayland loop stays ours.
        multi_threaded_message_loop: 1,
        windowless_rendering_enabled: 1,
        root_cache_path: CefString::from(cache.as_ref()),
        cache_path: CefString::from(cache.as_ref()),
        // libcef.so's resources sit next to us, not next to the dotnet host executable.
        resources_dir_path: CefString::from(runtime_dir.to_string_lossy().as_ref()),
        locales_dir_path: CefString::from(runtime_dir.join("locales").to_string_lossy().as_ref()),
        log_file: CefString::from(cfg.cef_log_file.to_string_lossy().as_ref()),
        log_severity: LogSeverity::WARNING,
        remote_debugging_port: cfg.devtools_port.max(0),
        background_color: 0,
        no_sandbox: std::env::var_os("POPCORN_NO_SANDBOX").is_some() as _,
        ..Default::default()
    };

    let args = CefArgs::new();
    let mut app = PlayerApp::new();
    signals::save();
    let ok = cef::initialize(Some(&args.main_args()), Some(&settings), Some(&mut app), std::ptr::null_mut()) == 1;
    signals::restore("CefInitialize");
    if !ok {
        return Err(format!("CefInitialize failed (exit code {}), see {}", get_exit_code(), cfg.cef_log_file.display()));
    }
    info!("CEF initialised");
    Ok(())
}

/// We live inside a .NET process. Chromium's browser start-up resets the signal dispositions it
/// cares about (SIGCHLD, SIGSEGV, SIGTERM, SIGINT, ...) to SIG_DFL, which breaks the runtime:
/// `Process.WaitForExit` never returns (no SIGCHLD), a NullReferenceException (SIGSEGV) kills the
/// process, PosixSignalRegistration stops working. Snapshot the dispositions before CEF starts and
/// put back whatever it changed; the browser process doesn't depend on them.
mod signals {
    use std::sync::Mutex;

    use crate::log::info;

    struct Saved(Vec<(libc::c_int, libc::sigaction)>);
    // SAFETY: plain data (handler addresses and masks).
    unsafe impl Send for Saved {}

    static SAVED: Mutex<Option<Saved>> = Mutex::new(None);

    pub fn save() {
        let mut saved = Vec::new();
        for sig in 1..libc::SIGRTMIN() {
            if sig == libc::SIGKILL || sig == libc::SIGSTOP {
                continue;
            }
            // SAFETY: querying only.
            unsafe {
                let mut action: libc::sigaction = std::mem::zeroed();
                if libc::sigaction(sig, std::ptr::null(), &mut action) == 0 {
                    saved.push((sig, action));
                }
            }
        }
        *SAVED.lock().unwrap() = Some(Saved(saved));
    }

    /// Restores every disposition that differs from the snapshot. `when` is for the log.
    pub fn restore(when: &str) {
        let saved = SAVED.lock().unwrap();
        let Some(saved) = saved.as_ref() else { return };
        let mut restored = Vec::new();
        for (sig, action) in &saved.0 {
            // SAFETY: re-installing a disposition that was installed before.
            unsafe {
                let mut current: libc::sigaction = std::mem::zeroed();
                if libc::sigaction(*sig, std::ptr::null(), &mut current) != 0 {
                    continue;
                }
                if current.sa_sigaction != action.sa_sigaction || current.sa_flags != action.sa_flags {
                    libc::sigaction(*sig, action, std::ptr::null_mut());
                    restored.push(*sig);
                }
            }
        }
        if !restored.is_empty() {
            info!("Restored host signal handlers after {when}: {restored:?}");
        }
    }
}

/// Starts closing the browser; `Cmd::BrowserClosed` follows once it's gone.
pub fn close_browser() -> bool {
    CLOSING.store(true, Ordering::SeqCst);
    let mut closing = false;
    with_host(|host| {
        host.close_browser(1);
        closing = true;
    });
    closing
}

/// After `Cmd::BrowserClosed`. Same thread as `initialize`.
pub fn shutdown() {
    cef::shutdown();
    info!("CEF shut down");
}

/// Directory of libpopcorn_player.so (= bundle directory with libcef.so and its resources).
fn module_dir() -> Option<PathBuf> {
    // SAFETY: dladdr on an address inside this library; dli_fname is a static string.
    unsafe {
        let mut info: libc::Dl_info = std::mem::zeroed();
        if libc::dladdr(module_dir as *const libc::c_void, &mut info) == 0 || info.dli_fname.is_null() {
            return None;
        }
        let path = PathBuf::from(std::ffi::CStr::from_ptr(info.dli_fname).to_string_lossy().into_owned());
        let path = path.canonicalize().unwrap_or(path);
        path.parent().map(Path::to_path_buf)
    }
}

// ---------------------------------------------------------------------------------------------
// App / browser process handler
// ---------------------------------------------------------------------------------------------

wrap_app! {
    struct PlayerApp;

    impl App {
        fn on_before_command_line_processing(
            &self,
            process_type: Option<&CefString>,
            command_line: Option<&mut CommandLine>,
        ) {
            let is_browser = process_type.is_none_or(|t| t.to_string().is_empty());
            let (true, Some(cmd)) = (is_browser, command_line) else {
                return;
            };
            for switch in [
                "no-first-run",
                "no-default-browser-check",
                "disable-background-networking",
                "disable-component-update",
                "disable-default-apps",
                "disable-sync",
            ] {
                cmd.append_switch(Some(&CefString::from(switch)));
            }
            set_switch(cmd, "autoplay-policy", "no-user-gesture-required");
            set_switch(cmd, "password-store", "basic");
            set_switch(cmd, "ozone-platform", "wayland");
            // NetworkTimeServiceQuerying: stops clients2.google.com/time requests (the generic
            // --disable-background-networking doesn't).
            // LocalNetworkAccess*: the overlay (http://app.local, a "public" origin to Chromium) must reach the C#
            // WebSocket bridge on 127.0.0.1; with the checks on, the handshake is cancelled after connecting.
            //
            // WaylandFractionalScaleV1: HACK for fractional display scales (e.g. 1.5). With the feature on, the
            // Wayland ozone backend takes the device scale from its own wp_fractional_scale_v1 handling (presumably
            // never delivered, an OSR browser has no Wayland surface of its own): the page runs at dpr 1 and CEF
            // paints DIP-sized frames (1280x720 for a 1920x1080 window), which the compositor then upscales ->
            // blurry text. ScreenInfo::device_scale_factor (screen_info below) and --force-device-scale-factor are
            // both ignored in that state (scale 1 looks fine either way). Disabling the feature makes CEF honour
            // ScreenInfo again (dpr 1.5, physical-size shared textures).
            // Verified with CEF 156 (cef crate 156.3.0), Nov 2026. If fractional scales turn blurry again after a CEF
            // update, check `devicePixelRatio` in the overlay (POPCORN_PLAYER_DEVTOOLS_PORT) and whether the
            // shared-texture size equals the window's physical size; the feature may have been renamed or removed.
            // Things that did NOT help at 1.5: --force-device-scale-factor, --high-dpi-support=1,
            // --enable-features=WaylandUiScale, --disable-features=WaylandPerSurfaceScale. --ozone-platform=headless
            // honours the scale but cannot allocate shared textures (software paint only).
            append_list_switch(
                cmd,
                "disable-features",
                "Translate,MediaRouter,OptimizationHints,LocalNetworkAccessChecks,LocalNetworkAccessChecksWebSockets,\
                 WaylandFractionalScaleV1,NetworkTimeServiceQuerying",
            );
        }

        fn browser_process_handler(&self) -> Option<BrowserProcessHandler> {
            Some(PlayerBrowserProcessHandler::new())
        }
    }
}

fn set_switch(cmd: &mut CommandLine, name: &str, value: &str) {
    cmd.append_switch_with_value(Some(&CefString::from(name)), Some(&CefString::from(value)));
}

/// Adds to a comma-separated switch without dropping values already set by CEF.
fn append_list_switch(cmd: &mut CommandLine, name: &str, values: &str) {
    let key = CefString::from(name);
    let existing = CefString::from(&cmd.switch_value(Some(&key))).to_string();
    let merged = if existing.is_empty() { values.to_owned() } else { format!("{existing},{values}") };
    set_switch(cmd, name, &merged);
}

wrap_browser_process_handler! {
    struct PlayerBrowserProcessHandler;

    impl BrowserProcessHandler {
        fn on_context_initialized(&self) {
            // Chromium may install more handlers while its UI thread starts up.
            signals::restore("browser start-up");
            let mut factory = OverlaySchemeFactory::new();
            register_scheme_handler_factory(
                Some(&CefString::from("http")),
                Some(&CefString::from("app.local")),
                Some(&mut factory),
            );
            create_browser();
        }
    }
}

fn create_browser() {
    let url = format!("{OVERLAY_ORIGIN}{}", crate::config().overlay_page);
    let window_info = WindowInfo {
        windowless_rendering_enabled: 1,
        // Frames as DMA-BUFs from the GPU process (on_accelerated_paint) instead of pixels.
        shared_texture_enabled: crate::overlay_gpu::enabled() as c_int,
        runtime_style: RuntimeStyle::ALLOY,
        ..Default::default()
    };
    let settings = BrowserSettings {
        windowless_frame_rate: 60,
        // Fully transparent: the video shows through wherever the overlay doesn't paint.
        background_color: 0,
        ..Default::default()
    };
    let mut client = OverlayClient::new();
    let created = browser_host_create_browser(
        Some(&window_info),
        Some(&mut client),
        Some(&CefString::from(url.as_str())),
        Some(&settings),
        None,
        None,
    );
    if created != 1 {
        error!("Failed to create overlay browser");
        crate::send_cmd(Cmd::BrowserClosed);
    } else {
        info!("Overlay browser created: {url}");
    }
}

// ---------------------------------------------------------------------------------------------
// Browser client + handlers
// ---------------------------------------------------------------------------------------------

wrap_client! {
    struct OverlayClient;

    impl Client {
        fn render_handler(&self) -> Option<RenderHandler> {
            Some(OverlayRenderHandler::new())
        }

        fn life_span_handler(&self) -> Option<LifeSpanHandler> {
            Some(OverlayLifeSpanHandler::new())
        }

        fn display_handler(&self) -> Option<DisplayHandler> {
            Some(OverlayDisplayHandler::new())
        }

        fn load_handler(&self) -> Option<LoadHandler> {
            Some(OverlayLoadHandler::new())
        }
    }
}

wrap_render_handler! {
    struct OverlayRenderHandler;

    impl RenderHandler {
        fn view_rect(&self, _browser: Option<&mut Browser>, rect: Option<&mut Rect>) {
            if let Some(rect) = rect {
                let v = *VIEW.lock().unwrap();
                *rect = Rect { x: 0, y: 0, width: v.width, height: v.height };
            }
        }

        fn screen_info(&self, _browser: Option<&mut Browser>, screen_info: Option<&mut ScreenInfo>) -> c_int {
            let Some(info) = screen_info else { return 0 };
            let v = *VIEW.lock().unwrap();
            let rect = Rect { x: 0, y: 0, width: v.width, height: v.height };
            info.device_scale_factor = v.scale;
            info.depth = 32;
            info.depth_per_component = 8;
            info.rect = rect.clone();
            info.available_rect = rect;
            1
        }

        fn on_accelerated_paint(
            &self,
            _browser: Option<&mut Browser>,
            type_: PaintElementType,
            dirty_rects: Option<&[Rect]>,
            info: Option<&AcceleratedPaintInfo>,
        ) {
            // PET_POPUP isn't composited, see on_paint.
            let Some(info) = info.filter(|_| type_ == PaintElementType::VIEW) else { return };
            if crate::overlay_gpu::paint(info, dirty_rects.unwrap_or_default())
                && !PAINT_QUEUED.swap(true, Ordering::SeqCst)
            {
                crate::send_cmd(Cmd::OverlayPainted);
            }
        }

        fn on_paint(
            &self,
            _browser: Option<&mut Browser>,
            type_: PaintElementType,
            dirty_rects: Option<&[Rect]>,
            buffer: *const u8,
            width: c_int,
            height: c_int,
        ) {
            // PET_POPUP (native <select> dropdowns) isn't composited; the overlay doesn't use them.
            if type_ != PaintElementType::VIEW || buffer.is_null() || width <= 0 || height <= 0 {
                return;
            }
            let len = width as usize * height as usize * 4;
            // SAFETY: CEF passes a width*height BGRA buffer valid for this call.
            let src = unsafe { std::slice::from_raw_parts(buffer, len) };
            let mut frame = FRAME.lock().unwrap();
            if frame.width != width || frame.height != height {
                frame.width = width;
                frame.height = height;
                frame.data.clear();
                frame.data.extend_from_slice(src);
                frame.damage = Some(Rect { x: 0, y: 0, width, height });
            } else {
                let stride = width as usize * 4;
                for r in dirty_rects.unwrap_or_default() {
                    let x0 = r.x.clamp(0, width) as usize;
                    let x1 = (r.x + r.width).clamp(0, width) as usize;
                    let y0 = r.y.clamp(0, height) as usize;
                    let y1 = (r.y + r.height).clamp(0, height) as usize;
                    if x0 >= x1 || y0 >= y1 {
                        continue;
                    }
                    for y in y0..y1 {
                        let range = y * stride + x0 * 4..y * stride + x1 * 4;
                        frame.data[range.clone()].copy_from_slice(&src[range]);
                    }
                    let clipped = Rect { x: x0 as i32, y: y0 as i32, width: (x1 - x0) as i32, height: (y1 - y0) as i32 };
                    frame.damage = Some(union(frame.damage.clone(), clipped));
                }
            }
            drop(frame);
            if !PAINT_QUEUED.swap(true, Ordering::SeqCst) {
                crate::send_cmd(Cmd::OverlayPainted);
            }
        }
    }
}

wrap_life_span_handler! {
    struct OverlayLifeSpanHandler;

    impl LifeSpanHandler {
        fn on_after_created(&self, browser: Option<&mut Browser>) {
            let Some(host) = browser.and_then(|b| b.host()) else { return };
            *HOST.lock().unwrap() = Some(HostRef(host.clone()));
            // The window may have been resized or closed while the browser was being created.
            host.was_resized();
            if CLOSING.load(Ordering::SeqCst) {
                host.close_browser(1);
            } else {
                crate::send_cmd(Cmd::BrowserCreated);
            }
        }

        fn on_before_close(&self, _browser: Option<&mut Browser>) {
            *HOST.lock().unwrap() = None;
            info!("Overlay browser closed");
            crate::send_cmd(Cmd::BrowserClosed);
        }

        fn on_before_popup(
            &self,
            _browser: Option<&mut Browser>,
            _frame: Option<&mut Frame>,
            _popup_id: c_int,
            _target_url: Option<&CefString>,
            _target_frame_name: Option<&CefString>,
            _target_disposition: WindowOpenDisposition,
            _user_gesture: c_int,
            _popup_features: Option<&PopupFeatures>,
            _window_info: Option<&mut WindowInfo>,
            _client: Option<&mut Option<Client>>,
            _settings: Option<&mut BrowserSettings>,
            _extra_info: Option<&mut Option<DictionaryValue>>,
            _no_javascript_access: Option<&mut c_int>,
        ) -> c_int {
            1
        }
    }
}

wrap_display_handler! {
    struct OverlayDisplayHandler;

    impl DisplayHandler {
        fn on_cursor_change(
            &self,
            _browser: Option<&mut Browser>,
            _cursor: ::std::os::raw::c_ulong,
            type_: CursorType,
            _custom_cursor_info: Option<&CursorInfo>,
        ) -> c_int {
            crate::send_cmd(Cmd::Cursor(type_.get_raw()));
            1
        }

        fn on_fullscreen_mode_change(&self, _browser: Option<&mut Browser>, fullscreen: c_int) {
            crate::send_cmd(Cmd::SetFullscreen(fullscreen != 0));
        }

        fn on_console_message(
            &self,
            _browser: Option<&mut Browser>,
            level: LogSeverity,
            message: Option<&CefString>,
            source: Option<&CefString>,
            line: c_int,
        ) -> c_int {
            let message = message.map(CefString::to_string).unwrap_or_default();
            let source = source.map(CefString::to_string).unwrap_or_default();
            let text = format!("[overlay] {message} ({source}:{line})");
            if level == LogSeverity::ERROR || level == LogSeverity::FATAL {
                warning!("{text}");
            } else {
                debug!("{text}");
            }
            0
        }
    }
}

wrap_load_handler! {
    struct OverlayLoadHandler;

    impl LoadHandler {
        fn on_load_error(
            &self,
            _browser: Option<&mut Browser>,
            frame: Option<&mut Frame>,
            error_code: Errorcode,
            error_text: Option<&CefString>,
            failed_url: Option<&CefString>,
        ) {
            if !frame.is_some_and(|f| f.is_main() == 1)
                || sys::cef_errorcode_t::from(error_code) == sys::cef_errorcode_t::ERR_ABORTED
            {
                return;
            }
            error!(
                "Overlay failed to load {}: {}",
                failed_url.map(CefString::to_string).unwrap_or_default(),
                error_text.map(CefString::to_string).unwrap_or_default()
            );
        }
    }
}

// ---------------------------------------------------------------------------------------------
// http://app.local/ -> overlay bundle on disk
// ---------------------------------------------------------------------------------------------

wrap_scheme_handler_factory! {
    struct OverlaySchemeFactory;

    impl SchemeHandlerFactory {
        fn create(
            &self,
            _browser: Option<&mut Browser>,
            _frame: Option<&mut Frame>,
            _scheme_name: Option<&CefString>,
            request: Option<&mut Request>,
        ) -> Option<ResourceHandler> {
            let url = CefString::from(&request?.url()).to_string();
            Some(serve(&url))
        }
    }
}

fn serve(url: &str) -> ResourceHandler {
    let cfg = crate::config();
    let path = url.strip_prefix(OVERLAY_ORIGIN).unwrap_or("");
    let path = path.split(['?', '#']).next().unwrap_or("");
    let path = percent_decode(path);
    let path = if path.is_empty() { cfg.overlay_page.clone() } else { path };

    // Stay inside the bundle directory.
    let relative = Path::new(&path);
    if relative.components().any(|c| !matches!(c, Component::Normal(_))) {
        warning!("Rejected overlay request {url}");
        return BytesResource::new(Arc::new(Vec::new()), "text/plain".into(), 403, Arc::default());
    }
    let file = cfg.overlay_dir.join(relative);
    match std::fs::read(&file) {
        Ok(mut data) => {
            let mime = mime_type(&file);
            if mime == "text/html" && !cfg.inject_script.is_empty() {
                data = inject_script(data, &cfg.inject_script);
            }
            BytesResource::new(Arc::new(data), mime.into(), 200, Arc::default())
        }
        Err(e) => {
            warning!("Overlay file {} not found: {e}", file.display());
            BytesResource::new(Arc::new(Vec::new()), "text/plain".into(), 404, Arc::default())
        }
    }
}

/// Puts `<script>` first in `<head>` so it runs before the bundle's own scripts.
fn inject_script(html: Vec<u8>, script: &str) -> Vec<u8> {
    let tag = format!("<script>{}</script>", script.replace("</", "<\\/"));
    let text = String::from_utf8_lossy(&html);
    match text.find("<head>") {
        Some(pos) => {
            let at = pos + "<head>".len();
            format!("{}{tag}{}", &text[..at], &text[at..]).into_bytes()
        }
        None => format!("{tag}{text}").into_bytes(),
    }
}

fn percent_decode(s: &str) -> String {
    let bytes = s.as_bytes();
    let mut out = Vec::with_capacity(bytes.len());
    let mut i = 0;
    while i < bytes.len() {
        if bytes[i] == b'%'
            && i + 2 < bytes.len()
            && let Ok(hex) = std::str::from_utf8(&bytes[i + 1..i + 3])
            && let Ok(v) = u8::from_str_radix(hex, 16)
        {
            out.push(v);
            i += 3;
        } else {
            out.push(bytes[i]);
            i += 1;
        }
    }
    String::from_utf8_lossy(&out).into_owned()
}

fn mime_type(path: &Path) -> &'static str {
    match path.extension().and_then(|e| e.to_str()).unwrap_or("") {
        "html" | "htm" => "text/html",
        "js" | "mjs" => "text/javascript",
        "css" => "text/css",
        "json" | "map" => "application/json",
        "svg" => "image/svg+xml",
        "png" => "image/png",
        "jpg" | "jpeg" => "image/jpeg",
        "gif" => "image/gif",
        "webp" => "image/webp",
        "ico" => "image/x-icon",
        "woff" => "font/woff",
        "woff2" => "font/woff2",
        "ttf" => "font/ttf",
        "txt" => "text/plain",
        _ => "application/octet-stream",
    }
}

wrap_resource_handler! {
    struct BytesResource {
        data: Arc<Vec<u8>>,
        mime: String,
        status: i32,
        offset: Arc<AtomicUsize>,
    }

    impl ResourceHandler {
        fn open(
            &self,
            _request: Option<&mut Request>,
            handle_request: Option<&mut c_int>,
            _callback: Option<&mut Callback>,
        ) -> c_int {
            if let Some(handle) = handle_request {
                *handle = 1;
            }
            1
        }

        fn response_headers(
            &self,
            response: Option<&mut Response>,
            response_length: Option<&mut i64>,
            _redirect_url: Option<&mut CefString>,
        ) {
            if let Some(response) = response {
                response.set_status(self.status);
                response.set_mime_type(Some(&CefString::from(self.mime.as_str())));
                // Always serve the current bundle (it's rebuilt in place during development).
                response.set_header_by_name(
                    Some(&CefString::from("Cache-Control")),
                    Some(&CefString::from("no-store")),
                    1,
                );
            }
            if let Some(length) = response_length {
                *length = self.data.len() as i64;
            }
        }

        fn read(
            &self,
            data_out: *mut u8,
            bytes_to_read: c_int,
            bytes_read: Option<&mut c_int>,
            _callback: Option<&mut ResourceReadCallback>,
        ) -> c_int {
            let offset = self.offset.load(Ordering::SeqCst);
            let n = self.data.len().saturating_sub(offset).min(bytes_to_read.max(0) as usize);
            if n > 0 {
                // SAFETY: CEF provides a buffer of at least bytes_to_read bytes.
                unsafe { std::ptr::copy_nonoverlapping(self.data.as_ptr().add(offset), data_out, n) };
                self.offset.store(offset + n, Ordering::SeqCst);
            }
            if let Some(read) = bytes_read {
                *read = n as c_int;
            }
            (n > 0) as c_int
        }
    }
}
