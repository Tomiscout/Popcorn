//! Native layer of the Windows player, loaded into Popcorn.Player.Windows (.NET) as `popcorn_player.dll`.
//! Windows counterpart of Popcorn.Player.Native (Linux): same split, similar C ABI.
//!
//! What lives here: the window, the mpv core (rendering with its own video output into our window) and the
//! WebView2 overlay. What stays in C#: everything the player *does* (DB, file navigation, server link, overlay
//! protocol over the WebSocket bridge, mpv commands/properties through its own client handle, see
//! `Popcorn.Player.Core/Player/LibMpvClient.cs`).
//!
//! Window layout:
//! ```text
//!   main window     WS_OVERLAPPEDWINDOW, black   title bar + frame; mpv's own video window inside it (`wid`)
//!   overlay window  WS_POPUP owned by main       WebView2 with a transparent background, kept over main's client area
//! ```
//! Two top-level windows because mpv presents through a D3D11 swapchain in its child window, which DWM draws
//! over everything else inside the same top-level window (other child windows, DirectComposition visuals), so an
//! overlay in the main window is hidden as soon as video plays. DWM always composes an owned window above its
//! owner instead (like the video subsurface below the overlay surface on Linux).
//!
//! Threads: `pn_init` and `pn_run` must be called on the same thread (it owns the windows and runs their message
//! loop, COM single-threaded apartment); mpv renders on its own threads.
//!
//! C ABI (all strings UTF-8, NUL-terminated, copied by the callee):
//!   pn_init(config, callbacks) -> 0 | error     creates the (hidden) windows
//!   pn_mpv_core() -> mpv_handle* | null         the mpv core (created on first call, before or after pn_init), for
//!                                               `mpv_create_client` in C#
//!   pn_run() -> exit code                       shows the window, blocks until it is closed
//!   pn_quit(), pn_set_fullscreen(on), pn_set_title(title)   any thread
//!   pn_hide_splash()                            any thread: removes the logo shown while the overlay starts
//!   pn_window_handle() -> HWND                  owner for dialogs
//!   pn_destroy()                                after pn_run: terminates mpv (waits for clients), destroys the window

// Empty on other OSes, so the whole workspace builds everywhere (`cargo build --workspace`).
#![cfg(windows)]

mod log;
mod mpv;
mod overlay;
mod splash;
mod window;

use std::ffi::{CStr, c_char, c_int, c_void};
use std::path::PathBuf;
use std::sync::atomic::{AtomicBool, AtomicPtr, Ordering};
use std::sync::{Mutex, OnceLock};

use crate::log::{error, info};

pub const EVENT_CLOSE_REQUESTED: c_int = 1;
pub const EVENT_FULLSCREEN_CHANGED: c_int = 2;
/// The overlay's WebView2 has been created and is loading the page: the player can start loading files.
pub const EVENT_READY: c_int = 3;

#[repr(C)]
pub struct PnConfig {
    /// Directory with the overlay web bundle (Popcorn.Web `build:player`).
    pub overlay_dir: *const c_char,
    /// Entry page inside `overlay_dir`, served as `http://app.local/<page>`.
    pub overlay_page: *const c_char,
    /// JavaScript run in every page before its own scripts (e.g. `window.wsAddress=...`).
    pub inject_script: *const c_char,
    /// WebView2 user data folder (profile).
    pub user_data_dir: *const c_char,
    pub title: *const c_char,
    /// > 0: Chromium remote debugging port (edge://inspect). F12 also opens DevTools.
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
    /// (user_data, utf8 ptr, len): full paths of files dropped on the window, one per line. pn_run thread.
    pub files_dropped: Option<extern "C" fn(*mut c_void, *const u8, usize)>,
}

// SAFETY: user_data is an opaque handle owned by C#; the callbacks are thread-safe by contract.
unsafe impl Send for PnCallbacks {}
unsafe impl Sync for PnCallbacks {}

pub struct Config {
    pub overlay_dir: PathBuf,
    pub overlay_page: String,
    pub inject_script: String,
    pub user_data_dir: PathBuf,
    pub title: String,
    pub devtools_port: i32,
}

static CONFIG: OnceLock<Config> = OnceLock::new();
static CALLBACKS: OnceLock<PnCallbacks> = OnceLock::new();
static MPV_CORE: AtomicPtr<mpv::mpv_handle> = AtomicPtr::new(std::ptr::null_mut());
static MPV_CORE_INIT: Mutex<()> = Mutex::new(());
static RUNNING: AtomicBool = AtomicBool::new(false);

pub fn config() -> &'static Config {
    CONFIG.get().expect("pn_init not called")
}

pub(crate) fn emit_window_event(event: c_int, value: c_int) {
    if let Some(cb) = CALLBACKS.get()
        && let Some(f) = cb.window_event
    {
        f(cb.user_data, event, value);
    }
}

pub(crate) fn emit_files_dropped(paths: &[String]) {
    let joined = paths.join("\n");
    if let Some(cb) = CALLBACKS.get()
        && let Some(f) = cb.files_dropped
    {
        f(cb.user_data, joined.as_ptr(), joined.len());
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
            user_data_dir: PathBuf::from(str_arg(c.user_data_dir)),
            title: str_arg(c.title),
            devtools_port: c.devtools_port,
        }
    };
    if CONFIG.set(cfg).is_err() {
        error!("pn_init called twice");
        return -2;
    }
    match window::create() {
        Ok(()) => {
            let _guard = MPV_CORE_INIT.lock().unwrap();
            set_mpv_window(MPV_CORE.load(Ordering::SeqCst));
            0
        }
        Err(e) => {
            error!("Could not create the player window: {e}");
            -3
        }
    }
}

/// The mpv core, created on first use. Null if mpv could not be initialised (logged).
fn mpv_core() -> *mut mpv::mpv_handle {
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

/// The core handle owns the video output, so its pre-init options belong here; everything
/// else (hwdec, subtitles, ...) is set by C# at runtime through its own client handle.
fn create_mpv_core() -> Result<*mut mpv::mpv_handle, String> {
    let mpv = mpv::get()?;
    // SAFETY: plain libmpv calls on a handle we own.
    unsafe {
        let handle = (mpv.create)();
        if handle.is_null() {
            return Err("mpv_create failed".into());
        }
        // The overlay window takes all input; mpv's window (disabled by mpv anyway) is only a picture.
        for (name, value) in [
            (c"input-default-bindings", c"no"),
            (c"input-vo-keyboard", c"no"),
            (c"input-cursor", c"no"),
        ] {
            let rc = (mpv.set_option_string)(handle, name.as_ptr(), value.as_ptr());
            if rc < 0 {
                (mpv.terminate_destroy)(handle);
                return Err(format!("mpv option {name:?}: {}", mpv.error(rc)));
            }
        }
        // POPCORN_MPV_LOG=<file>: mpv's own verbose log, with timestamps (start-up, decoder, video output).
        if let Some(path) = std::env::var_os("POPCORN_MPV_LOG")
            && let Ok(path) = std::ffi::CString::new(path.to_string_lossy().as_bytes())
        {
            (mpv.set_option_string)(handle, c"log-file".as_ptr(), path.as_ptr());
        }
        let rc = (mpv.initialize)(handle);
        if rc < 0 {
            (mpv.terminate_destroy)(handle);
            return Err(format!("mpv_initialize: {}", mpv.error(rc)));
        }
        info!("mpv core initialised");
        set_mpv_window(handle);
        watch_first_frame(mpv, handle);
        Ok(handle)
    }
}

/// The splash goes with the first video frame. mpv tells every client (MPV_EVENT_PLAYBACK_RESTART), so we wait
/// on our own client handle: on C#'s, the event queues behind its slow handlers of earlier events.
fn watch_first_frame(mpv: &'static mpv::Mpv, core: *mut mpv::mpv_handle) {
    // SAFETY: valid core handle.
    let client = unsafe { (mpv.create_client)(core, c"popcorn-native".as_ptr()) };
    if client.is_null() {
        return;
    }
    let client = client as usize;
    std::thread::spawn(move || {
        let client = client as *mut mpv::mpv_handle;
        loop {
            // SAFETY: our client handle, only used on this thread; the event lives until the next wait.
            let id = unsafe { (*(mpv.wait_event)(client, -1.0)).event_id };
            match id {
                mpv::MPV_EVENT_PLAYBACK_RESTART => window::post_hide_splash(),
                // pn_destroy's mpv_terminate_destroy waits for this.
                mpv::MPV_EVENT_SHUTDOWN => break,
                _ => {}
            }
        }
        // SAFETY: as above, not used after this.
        unsafe { (mpv.destroy)(client) };
    });
}

/// mpv's video goes into our main window (`wid`). Set on whichever comes second, the core or the window; mpv reads
/// it when it creates its video output, i.e. when the first file plays.
fn set_mpv_window(core: *mut mpv::mpv_handle) {
    let wid = window::main_hwnd();
    if core.is_null() || wid == 0 {
        return;
    }
    let Ok(mpv) = mpv::get() else { return };
    let wid = std::ffi::CString::new(wid.to_string()).unwrap();
    // SAFETY: valid core handle; options can be set after mpv_initialize.
    let rc = unsafe { (mpv.set_option_string)(core, c"wid".as_ptr(), wid.as_ptr()) };
    if rc < 0 {
        error!("mpv option wid: {}", mpv.error(rc));
    }
}

#[unsafe(no_mangle)]
pub extern "C" fn pn_mpv_core() -> *mut c_void {
    mpv_core()
}

/// Shows the window and runs its message loop until it is closed. Same thread as pn_init.
#[unsafe(no_mangle)]
pub extern "C" fn pn_run() -> c_int {
    if CONFIG.get().is_none() || window::main_hwnd() == 0 {
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
    window::post_close();
}

#[unsafe(no_mangle)]
pub extern "C" fn pn_set_fullscreen(fullscreen: c_int) {
    window::post_fullscreen(fullscreen != 0);
}

/// # Safety
/// `title` must be a valid NUL-terminated string (or null).
#[unsafe(no_mangle)]
pub unsafe extern "C" fn pn_set_title(title: *const c_char) {
    // SAFETY: see function contract.
    let title = unsafe { str_arg(title) };
    window::post_title(title);
}

/// The overlay page is up: stop drawing the logo under it.
#[unsafe(no_mangle)]
pub extern "C" fn pn_hide_splash() {
    window::post_hide_splash();
}

#[unsafe(no_mangle)]
pub extern "C" fn pn_window_handle() -> isize {
    window::main_hwnd()
}

/// Terminates the mpv core (blocks until every client handle, C#'s, has been destroyed, which they do on
/// MPV_EVENT_SHUTDOWN), then destroys the window. Call after pn_run returned, on its thread.
#[unsafe(no_mangle)]
pub extern "C" fn pn_destroy() {
    let handle = {
        let _guard = MPV_CORE_INIT.lock().unwrap();
        MPV_CORE.swap(std::ptr::null_mut(), Ordering::SeqCst)
    };
    if !handle.is_null()
        && let Ok(mpv) = mpv::get()
    {
        let handle = handle as usize;
        // mpv's video window is a child of ours: its thread talks to our window while closing, so keep
        // the message loop going instead of blocking this thread.
        window::pump_until(move || {
            // SAFETY: nobody else uses the core handle any more.
            unsafe { (mpv.terminate_destroy)(handle as *mut mpv::mpv_handle) };
        });
        info!("mpv core destroyed");
    }
    window::destroy();
}
