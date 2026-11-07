//! Native layer of the Linux player, loaded into Popcorn.Player.Linux (.NET) as
//! `libpopcorn_player.so`.
//!
//! What lives here: the Wayland window, the mpv core + video surface, and the CEF overlay
//! (off-screen rendered, composited by the Wayland compositor on top of the video). What stays
//! in C#: everything the player *does* (DB, file navigation, server link, overlay protocol, mpv
//! commands/properties through its own client handle, see `Popcorn.Player.Core/Player/LibMpvClient.cs`).
//!
//! Surface layout (Wayland only):
//! ```text
//!   xdg_toplevel  main wl_surface   CEF overlay (GPU-copied dmabufs, or wl_shm), all input
//!                 └ video subsurface (placed below)  mpv output: dmabufs mpv renders into
//!                                                    (render_dmabuf.h), or EGL (render_gl.h)
//! ```
//!
//! Threads: `pn_run` runs the Wayland event loop on the calling (main) thread; CEF runs its own
//! UI thread (`multi_threaded_message_loop`); mpv rendering has a dedicated thread (video.rs).
//!
//! C ABI (all strings UTF-8, NUL-terminated, copied by the callee):
//!   pn_init(config, callbacks) -> 0 | error
//!   pn_mpv_core() -> mpv_handle* | null          the mpv core (created + initialised on first call,
//!                                                independent of pn_init), for `mpv_create_client` in C#
//!   pn_run() -> exit code                        blocks until the window is closed
//!   pn_quit(), pn_set_fullscreen(on), pn_set_title(title)   any thread
//!   pn_destroy()                                 after pn_run: terminates mpv (waits for clients)

mod cef_app;
mod dmabuf;
mod ffi;
mod keys;
mod log;
mod overlay_gpu;
mod video;
mod window;

use std::ffi::{CStr, c_char, c_int, c_void};
use std::path::PathBuf;
use std::sync::atomic::{AtomicBool, AtomicPtr, Ordering};
use std::sync::{Mutex, OnceLock};

use crate::log::{error, info};
use crate::window::Cmd;

pub const EVENT_CLOSE_REQUESTED: c_int = 1;
pub const EVENT_FULLSCREEN_CHANGED: c_int = 2;
/// CEF has finished starting (overlay browser created). Chromium resets signal handlers while it
/// starts, so the host must not spawn child processes before this (their exit would go unnoticed).
pub const EVENT_READY: c_int = 3;

#[repr(C)]
pub struct PnConfig {
    /// Directory with the overlay web bundle (Popcorn.Player.Web build).
    pub overlay_dir: *const c_char,
    /// Entry page inside `overlay_dir`, served as `http://app.local/<page>`.
    pub overlay_page: *const c_char,
    /// JavaScript injected into every HTML page before its own scripts (e.g. `window.wsAddress=...`).
    pub inject_script: *const c_char,
    /// CEF root cache / profile directory. Must not be shared with another running CEF process.
    pub cache_dir: *const c_char,
    /// CEF log file (Chromium internals; our own messages go through `callbacks.log`).
    pub cef_log_file: *const c_char,
    /// The `popcorn-player-cef` helper executable.
    pub subprocess_path: *const c_char,
    pub title: *const c_char,
    /// Wayland app_id (matches a .desktop file for the icon).
    pub app_id: *const c_char,
    /// > 0: Chromium remote debugging port (chrome://inspect). F12 also opens DevTools.
    pub devtools_port: c_int,
}

#[repr(C)]
#[derive(Clone, Copy)]
pub struct PnCallbacks {
    pub user_data: *mut c_void,
    /// (user_data, level 0=debug 1=info 2=warning 3=error, utf8 ptr, len). Any thread.
    pub log: Option<extern "C" fn(*mut c_void, c_int, *const u8, usize)>,
    /// (user_data, EVENT_*, value). Called on the pn_run thread.
    pub window_event: Option<extern "C" fn(*mut c_void, c_int, c_int)>,
}

// SAFETY: user_data is an opaque GCHandle owned by C#; the callbacks are thread-safe by contract.
unsafe impl Send for PnCallbacks {}
unsafe impl Sync for PnCallbacks {}

pub struct Config {
    pub overlay_dir: PathBuf,
    pub overlay_page: String,
    pub inject_script: String,
    pub cache_dir: PathBuf,
    pub cef_log_file: PathBuf,
    pub subprocess_path: PathBuf,
    pub title: String,
    pub app_id: String,
    pub devtools_port: i32,
}

static CONFIG: OnceLock<Config> = OnceLock::new();
static CALLBACKS: OnceLock<PnCallbacks> = OnceLock::new();
static MPV_CORE: AtomicPtr<ffi::mpv_handle> = AtomicPtr::new(std::ptr::null_mut());
static MPV_CORE_INIT: Mutex<()> = Mutex::new(());
static RUNNING: AtomicBool = AtomicBool::new(false);
/// Commands for the Wayland loop; `None` while it isn't running.
static LOOP_TX: Mutex<Option<window::CmdSender>> = Mutex::new(None);

pub fn config() -> &'static Config {
    CONFIG.get().expect("pn_init not called")
}

/// The mpv core, created on first use. Null if mpv could not be initialised (logged).
pub fn mpv_core() -> *mut ffi::mpv_handle {
    let core = MPV_CORE.load(Ordering::SeqCst);
    if !core.is_null() {
        return core;
    }
    let _guard = MPV_CORE_INIT.lock().unwrap();
    let core = MPV_CORE.load(Ordering::SeqCst);
    if !core.is_null() {
        return core;
    }
    match create_mpv_core() {
        Ok(handle) => {
            MPV_CORE.store(handle, Ordering::SeqCst);
            handle
        }
        Err(e) => {
            error!("{e}");
            std::ptr::null_mut()
        }
    }
}

/// Hands a command to the Wayland loop thread. Dropped silently if the loop isn't running.
pub fn send_cmd(cmd: Cmd) {
    if let Some(tx) = LOOP_TX.lock().unwrap().as_ref() {
        let _ = tx.send(cmd);
    }
}

pub(crate) fn set_loop_sender(tx: Option<window::CmdSender>) {
    *LOOP_TX.lock().unwrap() = tx;
}

pub(crate) fn emit_window_event(event: c_int, value: c_int) {
    if let Some(cb) = CALLBACKS.get()
        && let Some(f) = cb.window_event
    {
        f(cb.user_data, event, value);
    }
}

pub(crate) fn emit_log(level: c_int, msg: &str) {
    match CALLBACKS.get().and_then(|cb| cb.log.map(|f| (f, cb.user_data))) {
        Some((f, ud)) => f(ud, level, msg.as_ptr(), msg.len()),
        None => eprintln!("[popcorn_player] {msg}"),
    }
}

unsafe fn str_arg(ptr: *const c_char) -> String {
    if ptr.is_null() {
        return String::new();
    }
    // SAFETY: caller passes a valid NUL-terminated string.
    unsafe { CStr::from_ptr(ptr) }.to_string_lossy().into_owned()
}

/// # Safety
/// `config` must point to a valid `PnConfig` whose strings are NUL-terminated (or null).
#[unsafe(no_mangle)]
pub unsafe extern "C" fn pn_init(config: *const PnConfig, callbacks: PnCallbacks) -> c_int {
    let _ = CALLBACKS.set(callbacks);
    if config.is_null() {
        return -1;
    }
    // SAFETY: see function contract.
    let c = unsafe { &*config };
    let cfg = unsafe {
        Config {
            overlay_dir: PathBuf::from(str_arg(c.overlay_dir)),
            overlay_page: str_arg(c.overlay_page),
            inject_script: str_arg(c.inject_script),
            cache_dir: PathBuf::from(str_arg(c.cache_dir)),
            cef_log_file: PathBuf::from(str_arg(c.cef_log_file)),
            subprocess_path: PathBuf::from(str_arg(c.subprocess_path)),
            title: str_arg(c.title),
            app_id: str_arg(c.app_id),
            devtools_port: c.devtools_port,
        }
    };
    if CONFIG.set(cfg).is_err() {
        error!("pn_init called twice");
        return -2;
    }
    0
}

/// The core handle owns the video output, so its pre-init options belong here; everything
/// else (hwdec, subtitles, ...) is set by C# at runtime through its own client handle.
fn create_mpv_core() -> Result<*mut ffi::mpv_handle, String> {
    // SAFETY: plain libmpv calls on a handle we own.
    unsafe {
        let handle = ffi::mpv_create();
        if handle.is_null() {
            return Err("mpv_create failed".into());
        }
        for (name, value) in [(c"vo", c"libmpv")] {
            let rc = ffi::mpv_set_option_string(handle, name.as_ptr(), value.as_ptr());
            if rc < 0 {
                ffi::mpv_terminate_destroy(handle);
                return Err(format!("mpv option {name:?}: {}", ffi::mpv_error(rc)));
            }
        }
        let rc = ffi::mpv_initialize(handle);
        if rc < 0 {
            ffi::mpv_terminate_destroy(handle);
            return Err(format!("mpv_initialize: {}", ffi::mpv_error(rc)));
        }
        info!("mpv core initialised");
        Ok(handle)
    }
}

#[unsafe(no_mangle)]
pub extern "C" fn pn_mpv_core() -> *mut c_void {
    mpv_core()
}

/// Runs the window until it is closed. Must be called from the process main thread.
#[unsafe(no_mangle)]
pub extern "C" fn pn_run() -> c_int {
    if CONFIG.get().is_none() || mpv_core().is_null() {
        error!("pn_run without successful pn_init");
        return 1;
    }
    if RUNNING.swap(true, Ordering::SeqCst) {
        error!("pn_run is already running");
        return 1;
    }
    let code = match window::run() {
        Ok(()) => 0,
        Err(e) => {
            error!("Player window failed: {e}");
            1
        }
    };
    RUNNING.store(false, Ordering::SeqCst);
    code
}

#[unsafe(no_mangle)]
pub extern "C" fn pn_quit() {
    send_cmd(Cmd::Close);
}

#[unsafe(no_mangle)]
pub extern "C" fn pn_set_fullscreen(fullscreen: c_int) {
    send_cmd(Cmd::SetFullscreen(fullscreen != 0));
}

/// # Safety
/// `title` must be a valid NUL-terminated string (or null).
#[unsafe(no_mangle)]
pub unsafe extern "C" fn pn_set_title(title: *const c_char) {
    // SAFETY: see function contract.
    let title = unsafe { str_arg(title) };
    send_cmd(Cmd::SetTitle(title));
}

/// Terminates the mpv core. Blocks until every client handle (C#'s) has been destroyed, which
/// they do on MPV_EVENT_SHUTDOWN. Call after pn_run returned.
#[unsafe(no_mangle)]
pub extern "C" fn pn_destroy() {
    let _guard = MPV_CORE_INIT.lock().unwrap();
    let handle = MPV_CORE.swap(std::ptr::null_mut(), Ordering::SeqCst);
    if !handle.is_null() {
        // SAFETY: the render context (video.rs) is freed before pn_run returns.
        unsafe { ffi::mpv_terminate_destroy(handle) };
        info!("mpv core destroyed");
    }
}
