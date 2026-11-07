//! Where things live. The data folder is the server's `DirectoryUtils.DataFolder` (LocalApplicationData/Popcorn);
//! the program itself is installed separately (install_windows.ps1: %LOCALAPPDATA%\Programs\Popcorn), so replacing
//! it never touches the library database, plugins or caches.

use std::path::{Path, PathBuf};

use crate::platform;

pub fn data_dir() -> PathBuf {
    local_app_data().join("Popcorn")
}

pub fn logs_dir() -> PathBuf {
    data_dir().join("logs")
}

/// Downloaded packages and the installer's running copy (`--uninstall`'s copy goes to %TEMP%: this folder goes too).
pub fn updates_dir() -> PathBuf {
    data_dir().join("updates")
}

/// The installer's own folder when it is an installed bundle, else the installed one (Settings → Apps entry), else
/// the default install location (installed there or not).
pub fn default_install_dir() -> PathBuf {
    if let Ok(exe) = std::env::current_exe()
        && let Some(dir) = exe.parent()
        && dir.join(platform::SERVER_EXE).is_file()
    {
        return dir.to_path_buf();
    }
    if let Some(dir) = platform::registered_install_dir()
        && dir.join(platform::SERVER_EXE).is_file()
    {
        return dir;
    }
    platform::default_install_dir()
}

/// `<install dir><suffix>` next to the install folder (same volume, so renames are atomic).
pub fn sibling(dir: &Path, suffix: &str) -> PathBuf {
    let mut name = dir.file_name().unwrap_or_default().to_os_string();
    name.push(suffix);
    dir.with_file_name(name)
}

pub fn is_inside(path: &Path, dir: &Path) -> bool {
    let path = normalize(path);
    let dir = normalize(dir);
    path.len() > dir.len() && path.starts_with(&dir) && path[dir.len()..].starts_with(['/', '\\'])
}

/// Absolute, without `\\?\` and trailing separators, lower case on Windows: for comparing paths as text.
pub fn normalize(path: &Path) -> String {
    let path = std::fs::canonicalize(path)
        .or_else(|_| std::path::absolute(path))
        .unwrap_or_else(|_| path.to_path_buf());
    let text = path.to_string_lossy();
    let text = text
        .strip_prefix(r"\\?\")
        .unwrap_or(&text)
        .trim_end_matches(['/', '\\']);
    if cfg!(windows) {
        text.to_lowercase()
    } else {
        text.to_string()
    }
}

fn local_app_data() -> PathBuf {
    if cfg!(windows) {
        if let Some(dir) = std::env::var_os("LOCALAPPDATA") {
            return dir.into();
        }
    } else if let Some(dir) = std::env::var_os("XDG_DATA_HOME").filter(|d| !d.is_empty()) {
        return dir.into();
    } else if let Some(home) = std::env::var_os("HOME") {
        return Path::new(&home).join(".local/share");
    }
    std::env::temp_dir()
}
