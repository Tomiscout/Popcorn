//! OS specifics: which processes are Popcorn, stopping and starting them, the installed-apps entries (Windows:
//! registry + Start menu, Linux: systemd user unit + desktop entry). Every module has the same functions and
//! constants; elsewhere (`unsupported.rs`) the installer builds but refuses to install.

#[cfg(windows)]
mod windows;
#[cfg(windows)]
pub use windows::*;

#[cfg(target_os = "linux")]
mod linux;
#[cfg(target_os = "linux")]
pub use linux::*;

#[cfg(not(any(windows, target_os = "linux")))]
mod unsupported;
#[cfg(not(any(windows, target_os = "linux")))]
pub use unsupported::*;

/// What `stop_popcorn` found running, i.e. what to start again.
#[derive(Clone, Copy, Debug, Default)]
pub struct Running {
    pub server: bool,
    pub app: bool,
}

/// The words the window and the progress steps use for the OS's own places.
pub struct Texts {
    /// Fresh install, after the files are in place.
    pub register_step: &'static str,
    /// Update, after the files are in place.
    pub refresh_step: &'static str,
    /// Uninstall, before the files go.
    pub unregister_step: &'static str,
    /// "Removes the program from <dir>, ...".
    pub uninstall_removes: &'static str,
    /// Tooltip of the "Start Popcorn on system startup" box.
    pub autostart_hint: &'static str,
    /// Where the user starts Popcorn from ("the Start menu").
    pub launcher: &'static str,
    /// Why moving the old folder aside can fail.
    pub move_aside_hint: &'static str,
}

/// Package platform name (`Popcorn-<version>-<platform>.zip`, `release.json` `platform`).
#[cfg(all(windows, target_arch = "x86_64"))]
pub const PLATFORM: &str = "windows-x64";
#[cfg(all(windows, target_arch = "aarch64"))]
pub const PLATFORM: &str = "windows-arm64";
#[cfg(all(target_os = "linux", target_arch = "x86_64"))]
pub const PLATFORM: &str = "linux-x64";
#[cfg(all(target_os = "linux", target_arch = "aarch64"))]
pub const PLATFORM: &str = "linux-arm64";
#[cfg(not(any(all(
    any(windows, target_os = "linux"),
    any(target_arch = "x86_64", target_arch = "aarch64")
))))]
pub const PLATFORM: &str = "unsupported";
