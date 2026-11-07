//! Platforms without an implementation (macOS, BSDs): the installer builds but refuses to install.

use std::path::{Path, PathBuf};

use anyhow::bail;

use super::{Running, Texts};

pub const SERVER_EXE: &str = "Popcorn.Server";
pub const RUNTIME_DOWNLOAD: &str = "https://dotnet.microsoft.com/download/dotnet/10.0";
pub const RELOCATE_EXE: bool = false;
pub const TEXTS: Texts = Texts {
    register_step: "Registering Popcorn…",
    refresh_step: "Registering Popcorn…",
    unregister_step: "Unregistering Popcorn…",
    uninstall_removes: "its entries",
    autostart_hint: "",
    launcher: "its folder",
    move_aside_hint: "Close the programs that use it and retry",
};

pub fn default_install_dir() -> PathBuf {
    std::env::var_os("HOME")
        .map_or_else(std::env::temp_dir, PathBuf::from)
        .join(".local/bin/Popcorn")
}

pub fn show_text(text: &str, _error: bool) {
    eprintln!("{text}");
}

pub struct InstanceGuard;

pub fn single_instance() -> Option<InstanceGuard> {
    Some(InstanceGuard)
}

pub fn leave_server_service(_args: &[std::ffi::OsString]) -> anyhow::Result<bool> {
    Ok(false)
}

pub fn stop_popcorn(_install_dir: &Path, _step: &dyn Fn(&str)) -> anyhow::Result<Running> {
    bail!("Installing is only supported on Windows and Linux")
}

pub fn start_server(_install_dir: &Path) {}

pub fn start_server_checked(_install_dir: &Path) -> anyhow::Result<()> {
    Ok(())
}

pub fn start_app(_install_dir: &Path) {}

pub fn register(_install_dir: &Path, _version: &str, _fresh: bool, _autostart: bool) {}

pub fn unregister(_install_dir: &Path) {}

pub fn registered_install_dir() -> Option<PathBuf> {
    None
}

pub fn extra_data_dirs() -> Vec<PathBuf> {
    Vec::new()
}

pub fn missing_runtimes() -> Vec<&'static str> {
    Vec::new()
}

pub fn runtime_message(missing: &[&str]) -> String {
    format!("Popcorn needs the {}.", missing.join(" and the "))
}

pub fn attach_stdout() {}

pub fn delete_after_exit(_path: &Path) {}

pub fn open_browser() {}
