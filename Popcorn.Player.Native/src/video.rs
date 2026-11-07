//! Video layer: a dedicated thread that renders mpv into the video subsurface.
//!
//! Two outputs, picked at start-up:
//! - **DMA-BUF** (mpv with the `MPV_RENDER_API_TYPE_DMABUF` render backend, render_dmabuf.h):
//!   mpv renders with its own Vulkan device (libplacebo; hwdec interop, 10-bit where the GPU and
//!   compositor allow it) straight into GBM buffers on the compositor's GPU, which we attach to
//!   the subsurface as linux-dmabuf `wl_buffer`s. No GPU API on our side at all.
//! - **OpenGL** (stock libmpv): EGL context on a wl_egl_window, libmpv OpenGL render API;
//!   eglSwapBuffers hands the frame to the compositor.

use std::ffi::{CString, c_char, c_void};
use std::sync::atomic::{AtomicBool, Ordering};
use std::sync::{Arc, Condvar, Mutex};
use std::thread::JoinHandle;

use wayland_client::protocol::{wl_buffer::WlBuffer, wl_surface::WlSurface};
use wayland_client::{Connection, QueueHandle};
use wayland_protocols::wp::linux_dmabuf::zv1::client::zwp_linux_dmabuf_v1::ZwpLinuxDmabufV1;

use crate::dmabuf::{self, Bo, Feedback, Gbm};
use crate::ffi::*;
use crate::log::{error, info, warning};
use crate::window::{App, Cmd};

/// On screen, queued in the compositor, being rendered, spare.
const MAX_DMABUF_SLOTS: usize = 4;

#[derive(Default)]
struct State {
    width: i32,
    height: i32,
    resized: bool,
    mpv_update: bool,
    /// The compositor released a buffer.
    released: bool,
    quit: bool,
}

struct Shared {
    state: Mutex<State>,
    cv: Condvar,
}

impl Shared {
    fn update(&self, f: impl FnOnce(&mut State)) {
        f(&mut self.state.lock().unwrap());
        self.cv.notify_one();
    }
}

pub struct Video {
    shared: Arc<Shared>,
    thread: Option<JoinHandle<()>>,
}

/// Where the video goes.
pub struct Target {
    pub conn: Connection,
    pub qh: QueueHandle<App>,
    pub surface: WlSurface,
    /// linux-dmabuf global and the compositor's feedback, if available.
    pub dmabuf: Option<(ZwpLinuxDmabufV1, Feedback)>,
    pub mpv: *mut mpv_handle,
}

// SAFETY: the Wayland proxies are thread-safe; libmpv is thread-safe for the calls made with
// the core handle, which outlives the thread (see `Video::stop`).
unsafe impl Send for Target {}

impl Video {
    /// The surface must outlive this object (`stop` joins the thread before it's destroyed).
    pub fn start(target: Target, width: i32, height: i32) -> Video {
        let shared = Arc::new(Shared {
            state: Mutex::new(State {
                width: width.max(1),
                height: height.max(1),
                ..Default::default()
            }),
            cv: Condvar::new(),
        });
        let thread_shared = shared.clone();
        let thread = std::thread::Builder::new()
            .name("mpv-render".into())
            .spawn(move || {
                let target = target;
                if let Err(e) = render_thread(&target, &thread_shared) {
                    error!("Video output failed: {e}");
                    crate::send_cmd(Cmd::VideoReady);
                }
            })
            .map_err(|e| error!("Cannot start render thread: {e}"))
            .ok();
        Video { shared, thread }
    }

    /// Physical (buffer) size of the video surface.
    pub fn resize(&self, width: i32, height: i32) {
        self.shared.update(|s| {
            if s.width != width || s.height != height {
                s.width = width.max(1);
                s.height = height.max(1);
                s.resized = true;
            }
        });
    }

    /// Frees the mpv render context and the buffers; must run before the surface is destroyed
    /// and before the mpv core is terminated.
    pub fn stop(&mut self) {
        self.shared.update(|s| s.quit = true);
        if let Some(t) = self.thread.take() {
            let _ = t.join();
        }
    }
}

impl Drop for Video {
    fn drop(&mut self) {
        self.stop();
    }
}

unsafe extern "C" fn on_mpv_update(ctx: *mut c_void) {
    // SAFETY: ctx is the `Shared` kept alive by the render thread until the callback is cleared.
    let shared = unsafe { &*(ctx as *const Shared) };
    shared.update(|s| s.mpv_update = true);
}

fn render_thread(target: &Target, shared: &Arc<Shared>) -> Result<(), String> {
    let force_gl = std::env::var("POPCORN_PLAYER_VIDEO").is_ok_and(|v| v == "opengl");
    if let (Some((dmabuf, feedback)), false) = (&target.dmabuf, force_gl) {
        match DmabufOutput::new(target, dmabuf, feedback) {
            Ok(output) => return output.run(target, shared),
            Err(e) => info!("DMA-BUF video output unavailable ({e}), using OpenGL"),
        }
    }
    // SAFETY: valid display/surface pointers for the thread's lifetime.
    unsafe { run_gl(target, shared) }
}

/// Waits for something to do. Returns (resized, mpv_update, released) or None on quit.
fn wait(shared: &Shared, blocked_on_buffer: bool, idle: bool, size: &mut (i32, i32)) -> Option<(bool, bool, bool)> {
    let mut s = shared
        .cv
        .wait_while(shared.state.lock().unwrap(), |s| {
            !(s.quit || s.resized || s.mpv_update || (blocked_on_buffer && s.released) || !idle)
        })
        .unwrap();
    if s.quit {
        return None;
    }
    let flags = (s.resized, s.mpv_update, s.released);
    s.resized = false;
    s.mpv_update = false;
    s.released = false;
    *size = (s.width, s.height);
    Some(flags)
}

// ---------------------------------------------------------------------------------------------
// DMA-BUF output
// ---------------------------------------------------------------------------------------------

struct DmabufOutput {
    render: *mut mpv_render_context,
    gbm: Arc<Gbm>,
    format: u32,
    modifiers: Vec<u64>,
    dmabuf: ZwpLinuxDmabufV1,
}

struct Slot {
    bo: Bo,
    buffer: WlBuffer,
    /// Attached and not released by the compositor yet.
    busy: Arc<AtomicBool>,
}

impl DmabufOutput {
    fn new(target: &Target, dmabuf: &ZwpLinuxDmabufV1, feedback: &Feedback) -> Result<DmabufOutput, String> {
        let node = dmabuf::render_node(feedback.main_device).ok_or("compositor GPU has no render node")?;
        let mut init = mpv_dmabuf_init_params { drm_device: node.as_ptr() };
        let mut params = [
            mpv_render_param { type_: MPV_RENDER_PARAM_API_TYPE, data: c"dmabuf".as_ptr() as *mut c_void },
            mpv_render_param { type_: MPV_RENDER_PARAM_DMABUF_INIT_PARAMS, data: &mut init as *mut _ as *mut c_void },
            // vaapi interop on the compositor's display connection.
            mpv_render_param { type_: MPV_RENDER_PARAM_WL_DISPLAY, data: target.conn.backend().display_ptr() as *mut c_void },
            mpv_render_param { type_: MPV_RENDER_PARAM_INVALID, data: std::ptr::null_mut() },
        ];
        let mut render: *mut mpv_render_context = std::ptr::null_mut();
        // SAFETY: valid core handle and parameter list.
        let rc = unsafe { mpv_render_context_create(&mut render, target.mpv, params.as_mut_ptr()) };
        if rc < 0 {
            return Err(if rc == MPV_ERROR_NOT_IMPLEMENTED {
                "libmpv has no DMA-BUF render API".into()
            } else {
                format!("mpv_render_context_create: {}", mpv_error(rc))
            });
        }
        let free = |e: String| {
            // SAFETY: the context created above, not used elsewhere.
            unsafe { mpv_render_context_free(render) };
            e
        };

        let mut formats = mpv_dmabuf_formats { formats: std::ptr::null(), num_formats: 0 };
        let param = mpv_render_param { type_: MPV_RENDER_PARAM_DMABUF_FORMATS, data: &mut formats as *mut _ as *mut c_void };
        // SAFETY: valid context; the list is owned by it and constant.
        if unsafe { mpv_render_context_get_info(render, param) } < 0 || formats.formats.is_null() {
            return Err(free("mpv returned no DMA-BUF formats".into()));
        }
        // SAFETY: see above.
        let mpv_formats = unsafe { std::slice::from_raw_parts(formats.formats, formats.num_formats.max(0) as usize) };

        // mpv's order of preference (10-bit first), restricted to what the compositor imports.
        let mut candidates: Vec<(u32, Vec<u64>)> = Vec::new();
        for f in mpv_formats {
            if !feedback.formats.contains(&(f.format, f.modifier)) {
                continue;
            }
            match candidates.iter_mut().find(|(format, _)| *format == f.format) {
                Some((_, mods)) => mods.push(f.modifier),
                None => candidates.push((f.format, vec![f.modifier])),
            }
        }
        let gbm = Gbm::open(&node).map_err(free)?;
        let Some((format, modifiers)) = dmabuf::first_allocatable(&gbm, &candidates) else {
            return Err(free(format!("no buffer format both mpv and the compositor support ({} candidates)", candidates.len())));
        };
        info!(
            "Video: mpv DMA-BUF output on {}, {} ({} modifier(s))",
            node.to_string_lossy(),
            dmabuf::describe(format, modifiers[0]),
            modifiers.len()
        );
        Ok(DmabufOutput { render, gbm, format, modifiers, dmabuf: dmabuf.clone() })
    }

    fn run(self, target: &Target, shared: &Arc<Shared>) -> Result<(), String> {
        let render = self.render;
        // SAFETY: valid context; `shared` outlives it (callback cleared before freeing).
        unsafe { mpv_render_context_set_update_callback(render, Some(on_mpv_update), Arc::as_ptr(shared) as *mut c_void) };
        crate::send_cmd(Cmd::VideoReady);

        let mut slots: Vec<Slot> = Vec::new();
        let mut size = {
            let s = shared.state.lock().unwrap();
            (s.width, s.height)
        };
        // Draw right away: black until there's video, so the window isn't see-through.
        let mut redraw = true;
        let mut blocked = false;
        let mut frames: u64 = 0;
        loop {
            let Some((resized, mpv_update, released)) = wait(shared, blocked, !redraw || blocked, &mut size) else {
                break;
            };
            if resized {
                for slot in slots.drain(..) {
                    slot.buffer.destroy();
                }
                redraw = true;
                blocked = false;
            }
            // SAFETY: valid context, render thread.
            if mpv_update && unsafe { mpv_render_context_update(render) } & MPV_RENDER_UPDATE_FRAME != 0 {
                redraw = true;
            }
            if released {
                blocked = false;
            }
            if !redraw {
                continue;
            }

            let index = match slots.iter().position(|s| !s.busy.load(Ordering::SeqCst)) {
                Some(i) => i,
                None if slots.len() < MAX_DMABUF_SLOTS => match self.new_slot(target, shared, size) {
                    Some(slot) => {
                        slots.push(slot);
                        slots.len() - 1
                    }
                    None => {
                        error!("Cannot allocate a {}x{} video buffer", size.0, size.1);
                        redraw = false;
                        continue;
                    }
                },
                None => {
                    // All buffers are with the compositor: render once one comes back.
                    blocked = true;
                    continue;
                }
            };
            let slot = &slots[index];
            let mut fbo = mpv_dmabuf_fbo {
                w: slot.bo.width,
                h: slot.bo.height,
                format: slot.bo.format,
                modifier: slot.bo.modifier,
                num_planes: 1,
                planes: [mpv_dmabuf_plane { fd: slot.bo.raw_fd(), offset: slot.bo.offset, stride: slot.bo.stride }, Default::default(), Default::default(), Default::default()],
            };
            let mut params = [
                mpv_render_param { type_: MPV_RENDER_PARAM_DMABUF_FBO, data: &mut fbo as *mut _ as *mut c_void },
                mpv_render_param { type_: MPV_RENDER_PARAM_INVALID, data: std::ptr::null_mut() },
            ];
            // SAFETY: valid context; returns after the GPU finished (render_dmabuf.h).
            let rc = unsafe { mpv_render_context_render(render, params.as_mut_ptr()) };
            redraw = false;
            if rc < 0 {
                warning!("mpv_render_context_render: {}", mpv_error(rc));
                continue;
            }
            slot.busy.store(true, Ordering::SeqCst);
            target.surface.attach(Some(&slot.buffer), 0, 0);
            target.surface.damage_buffer(0, 0, i32::MAX, i32::MAX);
            target.surface.commit();
            let _ = target.conn.flush();
            // SAFETY: valid context.
            unsafe { mpv_render_context_report_swap(render) };
            if frames == 0 {
                info!("Video: first frame presented ({}x{}, {} buffer(s))", slot.bo.width, slot.bo.height, slots.len());
            }
            frames += 1;
        }

        // SAFETY: valid context; the callback must be cleared before `shared` can go away.
        unsafe {
            mpv_render_context_set_update_callback(render, None, std::ptr::null_mut());
            mpv_render_context_free(render);
        }
        for slot in slots {
            slot.buffer.destroy();
        }
        let _ = target.conn.flush();
        info!("Video output stopped ({frames} frames presented)");
        Ok(())
    }

    fn new_slot(&self, target: &Target, shared: &Arc<Shared>, (w, h): (i32, i32)) -> Option<Slot> {
        let bo = self.gbm.alloc(w, h, self.format, &self.modifiers)?;
        let busy = Arc::new(AtomicBool::new(false));
        let (flag, shared) = (busy.clone(), shared.clone());
        let buffer = dmabuf::create_wl_buffer(&self.dmabuf, &target.qh, &bo, move || {
            flag.store(false, Ordering::SeqCst);
            shared.update(|s| s.released = true);
        });
        Some(Slot { bo, buffer, busy })
    }
}

// ---------------------------------------------------------------------------------------------
// OpenGL output (stock libmpv)
// ---------------------------------------------------------------------------------------------

unsafe extern "C" fn get_proc_address(_ctx: *mut c_void, name: *const c_char) -> *mut c_void {
    // SAFETY: name is a NUL-terminated GL function name from mpv.
    unsafe { eglGetProcAddress(name) }
}

unsafe fn run_gl(target: &Target, shared: &Arc<Shared>) -> Result<(), String> {
    let wl_display = target.conn.backend().display_ptr() as *mut c_void;
    let wl_surface = wayland_client::Proxy::id(&target.surface).as_ptr() as *mut c_void;
    unsafe {
        let dpy = eglGetPlatformDisplay(EGL_PLATFORM_WAYLAND_KHR, wl_display, std::ptr::null());
        if dpy.is_null() || eglInitialize(dpy, std::ptr::null_mut(), std::ptr::null_mut()) == EGL_FALSE {
            return Err(format!("EGL initialisation failed (0x{:x})", eglGetError()));
        }
        if eglBindAPI(EGL_OPENGL_API) == EGL_FALSE {
            return Err("eglBindAPI(OpenGL) failed".into());
        }

        // EGL sorts by buffer size first, so an XRGB config (opaque video) comes before ARGB.
        let attribs = [
            EGL_SURFACE_TYPE, EGL_WINDOW_BIT,
            EGL_RENDERABLE_TYPE, EGL_OPENGL_BIT,
            EGL_RED_SIZE, 8, EGL_GREEN_SIZE, 8, EGL_BLUE_SIZE, 8,
            EGL_ALPHA_SIZE, 0,
            EGL_NONE,
        ];
        let mut config: EGLConfig = std::ptr::null_mut();
        let mut count = 0;
        if eglChooseConfig(dpy, attribs.as_ptr(), &mut config, 1, &mut count) == EGL_FALSE || count < 1 {
            return Err("No suitable EGL config".into());
        }
        let context = eglCreateContext(dpy, config, std::ptr::null_mut(), [EGL_NONE].as_ptr());
        if context.is_null() {
            return Err(format!("eglCreateContext failed (0x{:x})", eglGetError()));
        }

        let mut size = {
            let s = shared.state.lock().unwrap();
            (s.width, s.height)
        };
        let egl_window = wl_egl_window_create(wl_surface, size.0, size.1);
        if egl_window.is_null() {
            eglDestroyContext(dpy, context);
            return Err("wl_egl_window_create failed".into());
        }
        let surface = eglCreateWindowSurface(dpy, config, egl_window, std::ptr::null());
        if surface.is_null() {
            wl_egl_window_destroy(egl_window);
            eglDestroyContext(dpy, context);
            return Err(format!("eglCreateWindowSurface failed (0x{:x})", eglGetError()));
        }
        eglMakeCurrent(dpy, surface, surface, context);
        // mpv does its own frame timing; don't let a hidden window block this thread forever.
        eglSwapInterval(dpy, 0);

        // Black frame right away, so the window isn't see-through until mpv draws.
        let clear_color: Option<GlClearColor> = std::mem::transmute(eglGetProcAddress(c"glClearColor".as_ptr()));
        let clear: Option<GlClear> = std::mem::transmute(eglGetProcAddress(c"glClear".as_ptr()));
        if let (Some(clear_color), Some(clear)) = (clear_color, clear) {
            clear_color(0.0, 0.0, 0.0, 1.0);
            clear(GL_COLOR_BUFFER_BIT);
            eglSwapBuffers(dpy, surface);
        }

        let mut gl_init = mpv_opengl_init_params {
            get_proc_address: Some(get_proc_address),
            get_proc_address_ctx: std::ptr::null_mut(),
        };
        let api = CString::new("opengl").unwrap();
        let mut create_params = [
            mpv_render_param { type_: MPV_RENDER_PARAM_API_TYPE, data: api.as_ptr() as *mut c_void },
            mpv_render_param { type_: MPV_RENDER_PARAM_OPENGL_INIT_PARAMS, data: &mut gl_init as *mut _ as *mut c_void },
            // Lets mpv use VA-API zero-copy interop on this display.
            mpv_render_param { type_: MPV_RENDER_PARAM_WL_DISPLAY, data: wl_display },
            mpv_render_param { type_: MPV_RENDER_PARAM_INVALID, data: std::ptr::null_mut() },
        ];
        let mut render: *mut mpv_render_context = std::ptr::null_mut();
        let rc = mpv_render_context_create(&mut render, target.mpv, create_params.as_mut_ptr());
        if rc < 0 {
            warning!("mpv_render_context_create: {}", mpv_error(rc));
        } else {
            mpv_render_context_set_update_callback(render, Some(on_mpv_update), Arc::as_ptr(shared) as *mut c_void);
            info!("Video: mpv OpenGL output ({}x{})", size.0, size.1);
        }
        crate::send_cmd(Cmd::VideoReady);

        while let Some((resized, mpv_update, _)) = wait(shared, false, true, &mut size) {
            if render.is_null() {
                continue;
            }
            if resized {
                wl_egl_window_resize(egl_window, size.0, size.1, 0, 0);
            }
            let frame = mpv_update && (mpv_render_context_update(render) & MPV_RENDER_UPDATE_FRAME) != 0;
            if frame || resized {
                let mut fbo = mpv_opengl_fbo { fbo: 0, w: size.0, h: size.1, internal_format: 0 };
                let mut flip_y: i32 = 1;
                let mut params = [
                    mpv_render_param { type_: MPV_RENDER_PARAM_OPENGL_FBO, data: &mut fbo as *mut _ as *mut c_void },
                    mpv_render_param { type_: MPV_RENDER_PARAM_FLIP_Y, data: &mut flip_y as *mut _ as *mut c_void },
                    mpv_render_param { type_: MPV_RENDER_PARAM_INVALID, data: std::ptr::null_mut() },
                ];
                mpv_render_context_render(render, params.as_mut_ptr());
                eglSwapBuffers(dpy, surface);
                mpv_render_context_report_swap(render);
            }
        }

        if !render.is_null() {
            mpv_render_context_set_update_callback(render, None, std::ptr::null_mut());
            mpv_render_context_free(render);
        }
        eglMakeCurrent(dpy, std::ptr::null_mut(), std::ptr::null_mut(), std::ptr::null_mut());
        eglDestroySurface(dpy, surface);
        wl_egl_window_destroy(egl_window);
        eglDestroyContext(dpy, context);
        info!("Video output stopped");
        Ok(())
    }
}
