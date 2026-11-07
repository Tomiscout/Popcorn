//! The few libmpv functions the native layer needs: creating and destroying the core, and its own client handle
//! that waits for the first video frame (splash). libmpv-2.dll is loaded at run time: the MSVC linker would need
//! an import library, which the MSYS2 build doesn't make. Everything else goes through C#'s own client handle
//! (`Popcorn.Player.Core/Player/LibMpvClient.cs`).

use std::ffi::{CStr, c_char, c_int, c_void};
use std::sync::OnceLock;

use windows::Win32::System::LibraryLoader::{GetProcAddress, LoadLibraryW};
use windows::core::{PCSTR, w};

#[allow(non_camel_case_types)]
pub type mpv_handle = c_void;

pub const MPV_EVENT_SHUTDOWN: c_int = 1;
pub const MPV_EVENT_PLAYBACK_RESTART: c_int = 21;

/// The start of `mpv_event` (client.h); only the id is read.
#[repr(C)]
pub struct mpv_event {
    pub event_id: c_int,
}

pub struct Mpv {
    pub create: unsafe extern "C" fn() -> *mut mpv_handle,
    pub set_option_string: unsafe extern "C" fn(*mut mpv_handle, *const c_char, *const c_char) -> c_int,
    pub initialize: unsafe extern "C" fn(*mut mpv_handle) -> c_int,
    pub terminate_destroy: unsafe extern "C" fn(*mut mpv_handle),
    pub create_client: unsafe extern "C" fn(*mut mpv_handle, *const c_char) -> *mut mpv_handle,
    pub destroy: unsafe extern "C" fn(*mut mpv_handle),
    pub wait_event: unsafe extern "C" fn(*mut mpv_handle, f64) -> *mut mpv_event,
    error_string: unsafe extern "C" fn(c_int) -> *const c_char,
}

// SAFETY: plain function pointers into a DLL that stays loaded.
unsafe impl Send for Mpv {}
unsafe impl Sync for Mpv {}

static MPV: OnceLock<Result<Mpv, String>> = OnceLock::new();

pub fn get() -> Result<&'static Mpv, String> {
    MPV.get_or_init(load).as_ref().map_err(Clone::clone)
}

impl Mpv {
    pub fn error(&self, code: c_int) -> String {
        // SAFETY: mpv_error_string returns a static string for any code.
        unsafe { CStr::from_ptr((self.error_string)(code)) }.to_string_lossy().into_owned()
    }
}

fn load() -> Result<Mpv, String> {
    // SAFETY: the symbols are transmuted to their signatures from mpv/client.h.
    unsafe {
        // Found next to the executable (the player's folder), like C#'s DllImport finds it.
        let lib = LoadLibraryW(w!("libmpv-2.dll")).map_err(|e| format!("Could not load libmpv-2.dll: {e}"))?;
        macro_rules! sym {
            ($name:literal) => {{
                let f = GetProcAddress(lib, PCSTR(concat!($name, "\0").as_ptr()))
                    .ok_or_else(|| format!("libmpv-2.dll has no {}", $name))?;
                std::mem::transmute(f)
            }};
        }
        Ok(Mpv {
            create: sym!("mpv_create"),
            set_option_string: sym!("mpv_set_option_string"),
            initialize: sym!("mpv_initialize"),
            terminate_destroy: sym!("mpv_terminate_destroy"),
            create_client: sym!("mpv_create_client"),
            destroy: sym!("mpv_destroy"),
            wait_event: sym!("mpv_wait_event"),
            error_string: sym!("mpv_error_string"),
        })
    }
}
