//! Windows: processes are found by their executable's folder (only this install's, never a dev build elsewhere),
//! the server is asked to stop through a named event (`Local\Popcorn.Server.Shutdown`, Program.cs) before anything
//! gets killed. Also the Settings → Apps entry, Start menu shortcut and autostart of an install.

use std::ffi::OsStr;
use std::os::windows::ffi::OsStrExt;
use std::os::windows::process::CommandExt;
use std::path::{Path, PathBuf};
use std::process::Command;
use std::ptr::{null, null_mut};
use std::time::{Duration, Instant};

use anyhow::bail;
use windows_sys::Win32::Foundation::{
    CloseHandle, ERROR_ACCESS_DENIED, ERROR_ALREADY_EXISTS, ERROR_SUCCESS, GetLastError, HANDLE,
    INVALID_HANDLE_VALUE, WAIT_OBJECT_0,
};
use windows_sys::Win32::System::Console::{
    ATTACH_PARENT_PROCESS, AttachConsole, GetStdHandle, STD_OUTPUT_HANDLE,
};
use windows_sys::Win32::System::Diagnostics::ToolHelp::{
    CreateToolhelp32Snapshot, PROCESSENTRY32W, Process32FirstW, Process32NextW, TH32CS_SNAPPROCESS,
};
use windows_sys::Win32::System::Registry::{
    HKEY, HKEY_CURRENT_USER, KEY_SET_VALUE, REG_BINARY, REG_DWORD, REG_OPTION_NON_VOLATILE, REG_SZ,
    RRF_RT_REG_SZ, RegCloseKey, RegCreateKeyExW, RegDeleteKeyValueW, RegDeleteKeyW, RegDeleteTreeW,
    RegGetValueW, RegSetValueExW,
};
use windows_sys::Win32::System::Threading::{
    CREATE_NEW_PROCESS_GROUP, CREATE_NO_WINDOW, CreateMutexW, EVENT_MODIFY_STATE, OpenEventW,
    OpenProcess, PROCESS_NAME_WIN32, PROCESS_QUERY_LIMITED_INFORMATION, PROCESS_SYNCHRONIZE,
    PROCESS_TERMINATE, QueryFullProcessImageNameW, SetEvent, TerminateProcess, WaitForSingleObject,
};
use windows_sys::Win32::UI::Shell::ShellExecuteW;
use windows_sys::Win32::UI::WindowsAndMessaging::{
    MB_ICONERROR, MB_ICONINFORMATION, MB_OK, MessageBoxW, SW_SHOWNORMAL,
};

use super::{Running, Texts};
use crate::log::{info, warning};
use crate::paths;

pub const SERVER_EXE: &str = "Popcorn.Server.exe";
const APP_EXE: &str = "Popcorn.Windows.exe";
const INSTALLER_EXE: &str = "Popcorn.Installer.exe";
const SHUTDOWN_EVENT: &str = r"Local\Popcorn.Server.Shutdown";
const INSTANCE_MUTEX: &str = r"Local\Popcorn.Installer";
const UNINSTALL_KEY: &str = r"Software\Microsoft\Windows\CurrentVersion\Uninstall\Popcorn";
const RUN_KEY: &str = r"Software\Microsoft\Windows\CurrentVersion\Run";
/// Task Manager's Startup apps state of the Run values (and Settings → Apps → Startup): 12 bytes, first byte 2 =
/// enabled, 3 = disabled, then the FILETIME of the change. Popcorn.Server (`WindowsAutostart`) reads and writes the
/// same value, so all three places agree.
const STARTUP_APPROVED_KEY: &str =
    r"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
const RUN_VALUE: &str = "Popcorn";
/// How long the server gets to save its state (database, torrent engine) after the shutdown request.
const SERVER_STOP_TIMEOUT: Duration = Duration::from_secs(20);
/// A new server still running after this long counts as started.
const SERVER_START_GRACE: Duration = Duration::from_secs(5);

pub const TEXTS: Texts = Texts {
    register_step: "Adding the Start menu shortcut and autostart…",
    refresh_step: "Updating the Settings → Apps entry…",
    unregister_step: "Removing the Start menu shortcut, autostart and Settings → Apps entry…",
    uninstall_removes: "its Start menu shortcut and autostart",
    autostart_hint: "Change it later in Popcorn's settings or Task Manager → Startup apps.",
    launcher: "the Start menu",
    move_aside_hint: "A program still uses a file in it (Explorer window, terminal, a player started from it?); \
                      close it and retry",
};

/// A running exe (or a working directory) keeps Windows from renaming its folder: the installer runs from a copy
/// outside the install folder (main.rs `relocate`).
pub const RELOCATE_EXE: bool = true;

/// Only needed on Linux (systemd would stop the installer together with the server).
pub fn leave_server_service(_args: &[std::ffi::OsString]) -> anyhow::Result<bool> {
    Ok(false)
}

/// Data outside `paths::data_dir()` that an uninstall without `--keep-data` deletes too.
pub fn extra_data_dirs() -> Vec<PathBuf> {
    Vec::new()
}

pub fn runtime_message(missing: &[&str]) -> String {
    format!(
        "Popcorn needs the {} from Microsoft. Install it, then retry.",
        missing.join(" and the ")
    )
}

pub fn default_install_dir() -> PathBuf {
    let local = std::env::var_os("LOCALAPPDATA").map_or_else(std::env::temp_dir, PathBuf::from);
    local.join("Programs").join("Popcorn")
}

/// Console when started from one (`--help` from a terminal), else a message box.
pub fn show_text(text: &str, error: bool) {
    // SAFETY: plain Win32 calls; the strings are NUL-terminated and outlive the call.
    unsafe {
        if AttachConsole(ATTACH_PARENT_PROCESS) != 0 || GetLastError() == ERROR_ACCESS_DENIED {
            if error {
                eprintln!("{text}")
            } else {
                println!("{text}")
            }
            return;
        }
        let icon = if error {
            MB_ICONERROR
        } else {
            MB_ICONINFORMATION
        };
        MessageBoxW(
            null_mut(),
            wide(text).as_ptr(),
            wide("Popcorn Installer").as_ptr(),
            MB_OK | icon,
        );
    }
}

pub struct InstanceGuard(HANDLE);

impl Drop for InstanceGuard {
    fn drop(&mut self) {
        // SAFETY: our own mutex handle, closed once.
        unsafe { CloseHandle(self.0) };
    }
}

pub fn single_instance() -> Option<InstanceGuard> {
    // SAFETY: plain Win32 calls with a NUL-terminated name.
    unsafe {
        let handle = CreateMutexW(null(), 0, wide(INSTANCE_MUTEX).as_ptr());
        if handle.is_null() {
            return Some(InstanceGuard(handle)); // cannot tell, don't block the update
        }
        if GetLastError() == ERROR_ALREADY_EXISTS {
            CloseHandle(handle);
            return None;
        }
        Some(InstanceGuard(handle))
    }
}

struct ProcessInfo {
    pid: u32,
    name: String,
}

/// Every process whose executable is inside `install_dir` (except this one).
fn processes_in(install_dir: &Path) -> Vec<ProcessInfo> {
    let own_pid = std::process::id();
    let mut found = Vec::new();
    // SAFETY: snapshot walk with a correctly sized PROCESSENTRY32W; the handle is closed at the end.
    unsafe {
        let snapshot = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0);
        if snapshot == INVALID_HANDLE_VALUE {
            warning!("CreateToolhelp32Snapshot failed ({})", GetLastError());
            return found;
        }
        let mut entry: PROCESSENTRY32W = std::mem::zeroed();
        entry.dwSize = size_of::<PROCESSENTRY32W>() as u32;
        let mut more = Process32FirstW(snapshot, &mut entry) != 0;
        while more {
            let pid = entry.th32ProcessID;
            if pid != own_pid
                && pid != 0
                && let Some(path) = process_path(pid)
                && paths::is_inside(&path, install_dir)
            {
                let name = path
                    .file_name()
                    .map(|n| n.to_string_lossy().into_owned())
                    .unwrap_or_default();
                found.push(ProcessInfo { pid, name });
            }
            more = Process32NextW(snapshot, &mut entry) != 0;
        }
        CloseHandle(snapshot);
    }
    found
}

fn process_path(pid: u32) -> Option<PathBuf> {
    // SAFETY: the buffer length is passed along; the handle is closed before returning.
    unsafe {
        let handle = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, 0, pid);
        if handle.is_null() {
            return None; // system / other users' processes
        }
        let mut buffer = vec![0u16; 32 * 1024];
        let mut size = buffer.len() as u32;
        let ok =
            QueryFullProcessImageNameW(handle, PROCESS_NAME_WIN32, buffer.as_mut_ptr(), &mut size)
                != 0;
        CloseHandle(handle);
        ok.then(|| PathBuf::from(String::from_utf16_lossy(&buffer[..size as usize])))
    }
}

fn terminate(process: &ProcessInfo) {
    // SAFETY: plain Win32 calls; the handle is closed before returning.
    unsafe {
        let handle = OpenProcess(PROCESS_TERMINATE | PROCESS_SYNCHRONIZE, 0, process.pid);
        if handle.is_null() {
            warning!(
                "Cannot open {} ({}) to stop it ({})",
                process.name,
                process.pid,
                GetLastError()
            );
            return;
        }
        info!("Stopping {} ({})", process.name, process.pid);
        if TerminateProcess(handle, 1) != 0 && WaitForSingleObject(handle, 5_000) != WAIT_OBJECT_0 {
            warning!("{} ({}) has not exited yet", process.name, process.pid);
        }
        CloseHandle(handle);
    }
}

/// Asks a running server (one that knows the event) to shut down cleanly. False = no server listens for it.
fn request_server_shutdown() -> bool {
    // SAFETY: plain Win32 calls. The handle is closed right away: the event is auto-reset and must not outlive the
    // old server, else the next server could open it still signalled.
    unsafe {
        let event = OpenEventW(EVENT_MODIFY_STATE, 0, wide(SHUTDOWN_EVENT).as_ptr());
        if event.is_null() {
            return false;
        }
        let ok = SetEvent(event) != 0;
        CloseHandle(event);
        ok
    }
}

fn is_named(process: &ProcessInfo, name: &str) -> bool {
    process.name.eq_ignore_ascii_case(name)
}

/// Stops every process of this install: the app window and players first (the app would start the server again
/// when it disappears), then the server, politely if it can, killed after `SERVER_STOP_TIMEOUT`.
pub fn stop_popcorn(install_dir: &Path, step: &dyn Fn(&str)) -> anyhow::Result<Running> {
    let processes = processes_in(install_dir);
    let running = Running {
        server: processes.iter().any(|p| is_named(p, SERVER_EXE)),
        app: processes.iter().any(|p| is_named(p, APP_EXE)),
    };

    if processes.iter().any(|p| !is_named(p, SERVER_EXE)) {
        step("Closing the Popcorn window and players…");
    }
    for process in processes.iter().filter(|p| !is_named(p, SERVER_EXE)) {
        terminate(process);
    }

    if running.server {
        if request_server_shutdown() {
            info!("Asked the server to shut down");
            step("Waiting for the Popcorn server to save and close…");
            let server_gone = wait_until(SERVER_STOP_TIMEOUT, || {
                !processes_in(install_dir)
                    .iter()
                    .any(|p| is_named(p, SERVER_EXE))
            });
            if !server_gone {
                warning!("Server still running after {SERVER_STOP_TIMEOUT:?}, killing it");
            }
        } else {
            info!("Server has no shutdown event (older version), killing it");
            step("Stopping the Popcorn server…");
        }
    }

    // Whatever is left (a server that did not stop in time, players started meanwhile).
    for process in processes_in(install_dir) {
        terminate(&process);
    }
    if !wait_until(Duration::from_secs(10), || {
        processes_in(install_dir).is_empty()
    }) {
        let names: Vec<_> = processes_in(install_dir)
            .into_iter()
            .map(|p| format!("{} ({})", p.name, p.pid))
            .collect();
        bail!("Cannot stop {}", names.join(", "));
    }
    Ok(running)
}

fn wait_until(timeout: Duration, mut done: impl FnMut() -> bool) -> bool {
    let start = Instant::now();
    loop {
        if done() {
            return true;
        }
        if start.elapsed() >= timeout {
            return false;
        }
        std::thread::sleep(Duration::from_millis(250));
    }
}

fn command(install_dir: &Path, exe: &str) -> Command {
    let mut command = Command::new(install_dir.join(exe));
    // Not in this console's Ctrl+C group (debug builds of the installer have a console).
    command
        .current_dir(install_dir)
        .creation_flags(CREATE_NEW_PROCESS_GROUP);
    command
}

pub fn start_server(install_dir: &Path) {
    match command(install_dir, SERVER_EXE).spawn() {
        Ok(_) => info!("Started {SERVER_EXE}"),
        Err(e) => warning!("Cannot start {SERVER_EXE}: {e}"),
    }
}

/// Starts the server and fails if it exits with an error within `SERVER_START_GRACE` (missing runtime, crash on
/// start-up). Exit code 0 is fine: another server already runs (single-instance mutex).
pub fn start_server_checked(install_dir: &Path) -> anyhow::Result<()> {
    let mut child = command(install_dir, SERVER_EXE).spawn()?;
    info!("Started {SERVER_EXE} ({})", child.id());
    let start = Instant::now();
    while start.elapsed() < SERVER_START_GRACE {
        if let Some(status) = child.try_wait()? {
            if status.success() {
                info!("{SERVER_EXE} exited right away with 0 (another one is running)");
                return Ok(());
            }
            bail!("{SERVER_EXE} exited with {status}");
        }
        std::thread::sleep(Duration::from_millis(200));
    }
    Ok(())
}

pub fn start_app(install_dir: &Path) {
    if !install_dir.join(APP_EXE).is_file() {
        return;
    }
    match command(install_dir, APP_EXE).spawn() {
        Ok(_) => info!("Started {APP_EXE}"),
        Err(e) => warning!("Cannot start {APP_EXE}: {e}"),
    }
}

/// Settings → Apps entry (HKCU Uninstall\Popcorn: also what Windows search lists under Apps), and for a new
/// install the Start menu shortcut and the server's autostart (HKCU Run, on or off = `autostart`). An update
/// only refreshes the entry, and only one that belongs to this folder (or none at all).
pub fn register(install_dir: &Path, version: &str, fresh: bool, autostart: bool) {
    let existing = registered_install_dir();
    if !fresh
        && let Some(existing) = &existing
        && paths::normalize(existing) != paths::normalize(install_dir)
    {
        info!(
            "Settings > Apps entry is for {}, not updating it",
            existing.display()
        );
        return;
    }
    let dir = install_dir.display().to_string();
    let app_exe = install_dir.join(APP_EXE).display().to_string();
    let installer = install_dir.join(INSTALLER_EXE);
    let (uninstall, quiet) = if installer.is_file() {
        let exe = format!("\"{}\" --uninstall", installer.display());
        (exe.clone(), format!("{exe} --confirm"))
    } else {
        // Bundles built without cargo have no installer, only the script.
        let script = format!(
            "powershell.exe -NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"{}\"",
            install_dir.join("uninstall.ps1").display()
        );
        (script.clone(), script)
    };

    let Some(key) = Key::create(UNINSTALL_KEY) else {
        warning!("Cannot create HKCU\\{UNINSTALL_KEY}");
        return;
    };
    key.set_str("DisplayName", "Popcorn");
    key.set_str("DisplayVersion", version);
    key.set_str("Publisher", "Popcorn");
    key.set_str("DisplayIcon", &format!("{app_exe},0"));
    key.set_str("InstallLocation", &dir);
    key.set_str("UninstallString", &uninstall);
    key.set_str("QuietUninstallString", &quiet);
    if fresh || existing.is_none() {
        key.set_str("InstallDate", &today());
    }
    key.set_dword("NoModify", 1);
    key.set_dword("NoRepair", 1);
    key.set_dword(
        "EstimatedSize",
        u32::try_from(dir_size(install_dir) / 1024).unwrap_or(u32::MAX),
    );
    info!("Settings > Apps entry: Popcorn {version} in {dir}");

    if fresh {
        set_autostart(install_dir, autostart);
        create_shortcut(install_dir);
    }
}

/// The server's Run value, enabled or disabled the way Task Manager does it: the value stays either way, so
/// Popcorn is listed under Startup apps and can be switched on there later.
fn set_autostart(install_dir: &Path, enabled: bool) {
    if let Some(run) = Key::create(RUN_KEY) {
        run.set_str(
            RUN_VALUE,
            &format!("\"{}\"", install_dir.join(SERVER_EXE).display()),
        );
    }
    if let Some(approved) = Key::create(STARTUP_APPROVED_KEY) {
        let mut state = [0u8; 12];
        if enabled {
            state[0] = 2;
        } else {
            state[0] = 3;
            let mut now = windows_sys::Win32::Foundation::FILETIME {
                dwLowDateTime: 0,
                dwHighDateTime: 0,
            };
            // SAFETY: writes into the local struct.
            unsafe {
                windows_sys::Win32::System::SystemInformation::GetSystemTimeAsFileTime(&mut now)
            };
            state[4..8].copy_from_slice(&now.dwLowDateTime.to_le_bytes());
            state[8..12].copy_from_slice(&now.dwHighDateTime.to_le_bytes());
        }
        approved.set_binary(RUN_VALUE, &state);
    }
    info!(
        "Popcorn.Server autostart at login: {}",
        if enabled { "on" } else { "off" }
    );
}

/// Removes what `register` (or install_windows.ps1) added for this install folder.
pub fn unregister(install_dir: &Path) {
    let ours = |location: Option<PathBuf>| {
        location.is_none_or(|l| paths::normalize(&l) == paths::normalize(install_dir))
    };
    if ours(registered_install_dir()) {
        delete_tree(UNINSTALL_KEY);
    }
    let server = get_str(RUN_KEY, RUN_VALUE).map(|v| PathBuf::from(v.trim_matches('"')));
    if server.is_some()
        && ours(
            server
                .as_deref()
                .and_then(Path::parent)
                .map(Path::to_path_buf),
        )
    {
        delete_value(RUN_KEY, RUN_VALUE);
        delete_value(STARTUP_APPROVED_KEY, RUN_VALUE);
    }
    if let Some(shortcut) = shortcut_path() {
        let _ = std::fs::remove_file(shortcut);
    }
    info!("Removed the Settings > Apps entry, autostart and Start menu shortcut");
}

/// InstallLocation of the Settings → Apps entry.
pub fn registered_install_dir() -> Option<PathBuf> {
    get_str(UNINSTALL_KEY, "InstallLocation")
        .filter(|l| !l.trim().is_empty())
        .map(PathBuf::from)
}

fn shortcut_path() -> Option<PathBuf> {
    let appdata = std::env::var_os("APPDATA")?;
    Some(Path::new(&appdata).join(r"Microsoft\Windows\Start Menu\Programs\Popcorn.lnk"))
}

/// Start menu shortcut to the app window (makes Popcorn show up in the Start menu search). Through WScript.Shell
/// in a hidden PowerShell: writing .lnk files from here would take the COM IShellLink interfaces. The paths go in
/// environment variables, so nothing needs quoting.
fn create_shortcut(install_dir: &Path) {
    let Some(shortcut) = shortcut_path() else {
        return;
    };
    if let Some(folder) = shortcut.parent() {
        let _ = std::fs::create_dir_all(folder);
    }
    let script = "$l = (New-Object -ComObject WScript.Shell).CreateShortcut($env:POPCORN_LNK); \
                  $l.TargetPath = $env:POPCORN_TARGET; $l.WorkingDirectory = $env:POPCORN_DIR; \
                  $l.IconLocation = \"$env:POPCORN_TARGET,0\"; $l.Description = 'Popcorn'; $l.Save()";
    let status = Command::new("powershell.exe")
        .args([
            "-NoProfile",
            "-NonInteractive",
            "-ExecutionPolicy",
            "Bypass",
        ])
        .args(["-Command", script])
        .env("POPCORN_LNK", &shortcut)
        .env("POPCORN_TARGET", install_dir.join(APP_EXE))
        .env("POPCORN_DIR", install_dir)
        .creation_flags(CREATE_NO_WINDOW)
        .status();
    match status {
        Ok(s) if s.success() => info!("Start menu shortcut: {}", shortcut.display()),
        Ok(s) => warning!("Creating the Start menu shortcut failed ({s})"),
        Err(e) => warning!("Cannot run PowerShell for the Start menu shortcut: {e}"),
    }
}

/// .NET runtimes the bundle needs (framework-dependent builds) and the machine lacks.
pub fn missing_runtimes() -> Vec<&'static str> {
    let program_files = std::env::var_os("ProgramFiles").map(PathBuf::from);
    let candidates = std::iter::once(PathBuf::from("dotnet"))
        .chain(program_files.map(|p| p.join(r"dotnet\dotnet.exe")));
    let mut listed = String::new();
    for dotnet in candidates {
        if let Ok(output) = Command::new(dotnet)
            .arg("--list-runtimes")
            .creation_flags(CREATE_NO_WINDOW)
            .output()
        {
            listed = String::from_utf8_lossy(&output.stdout).into_owned();
            break;
        }
    }
    [
        ("Microsoft.AspNetCore.App 10.", "ASP.NET Core Runtime 10"),
        (
            "Microsoft.WindowsDesktop.App 10.",
            ".NET Desktop Runtime 10",
        ),
    ]
    .into_iter()
    .filter(|(prefix, _)| !listed.lines().any(|line| line.starts_with(prefix)))
    .map(|(_, name)| name)
    .collect()
}

pub const RUNTIME_DOWNLOAD: &str = "https://dotnet.microsoft.com/download/dotnet/10.0";

/// The server's web UI in the default browser: `localhost:$POPCORN_PORT`, default 5000 (like the tray's Open in browser).
pub fn open_browser() {
    let port = std::env::var("POPCORN_PORT")
        .ok()
        .and_then(|p| p.parse::<u16>().ok())
        .filter(|&p| p > 0)
        .unwrap_or(5000);
    let url = format!("http://localhost:{port}/");
    info!("Opening {url}");
    let (verb, url) = (wide("open"), wide(&url));
    // SAFETY: null-terminated UTF-16 strings that outlive the call.
    unsafe {
        ShellExecuteW(
            null_mut(),
            verb.as_ptr(),
            url.as_ptr(),
            null(),
            null(),
            SW_SHOWNORMAL,
        )
    };
}

/// stdout for `--update-check`: the caller's pipe if it redirected it, else its console (this is a GUI program).
pub fn attach_stdout() {
    // SAFETY: plain Win32 calls.
    unsafe {
        let handle = GetStdHandle(STD_OUTPUT_HANDLE);
        if handle.is_null() || handle == INVALID_HANDLE_VALUE {
            AttachConsole(ATTACH_PARENT_PROCESS);
        }
    }
}

/// A running exe cannot delete itself: a hidden cmd does it once this process is gone.
pub fn delete_after_exit(path: &Path) {
    let command = format!(
        "/c ping -n 3 127.0.0.1 >nul & del /f /q \"{}\"",
        path.display()
    );
    let _ = Command::new("cmd.exe")
        .raw_arg(command)
        .current_dir(std::env::temp_dir())
        .creation_flags(CREATE_NO_WINDOW)
        .spawn();
}

fn today() -> String {
    // SAFETY: fills a stack-allocated SYSTEMTIME.
    let t = unsafe {
        let mut t = std::mem::zeroed();
        windows_sys::Win32::System::SystemInformation::GetLocalTime(&mut t);
        t
    };
    format!("{:04}{:02}{:02}", t.wYear, t.wMonth, t.wDay)
}

/// An open HKCU key.
struct Key(HKEY);

impl Key {
    fn create(path: &str) -> Option<Self> {
        let mut key: HKEY = null_mut();
        // SAFETY: NUL-terminated path, out pointer to a local.
        let status = unsafe {
            RegCreateKeyExW(
                HKEY_CURRENT_USER,
                wide(path).as_ptr(),
                0,
                null(),
                REG_OPTION_NON_VOLATILE,
                KEY_SET_VALUE,
                null(),
                &mut key,
                null_mut(),
            )
        };
        (status == ERROR_SUCCESS).then_some(Key(key))
    }

    fn set_str(&self, name: &str, value: &str) {
        let value = wide(value);
        // SAFETY: the byte length matches the NUL-terminated UTF-16 buffer.
        unsafe {
            RegSetValueExW(
                self.0,
                wide(name).as_ptr(),
                0,
                REG_SZ,
                value.as_ptr().cast(),
                (value.len() * 2) as u32,
            );
        }
    }

    fn set_dword(&self, name: &str, value: u32) {
        // SAFETY: 4 bytes of a local u32.
        unsafe {
            RegSetValueExW(
                self.0,
                wide(name).as_ptr(),
                0,
                REG_DWORD,
                (&raw const value).cast(),
                4,
            );
        }
    }

    fn set_binary(&self, name: &str, value: &[u8]) {
        // SAFETY: the slice and its length.
        unsafe {
            RegSetValueExW(
                self.0,
                wide(name).as_ptr(),
                0,
                REG_BINARY,
                value.as_ptr(),
                value.len() as u32,
            );
        }
    }
}

impl Drop for Key {
    fn drop(&mut self) {
        // SAFETY: our own open key, closed once.
        unsafe { RegCloseKey(self.0) };
    }
}

fn get_str(path: &str, name: &str) -> Option<String> {
    let mut buffer = vec![0u16; 4096];
    let mut size = (buffer.len() * 2) as u32;
    // SAFETY: NUL-terminated strings, the buffer size in bytes is passed along.
    let status = unsafe {
        RegGetValueW(
            HKEY_CURRENT_USER,
            wide(path).as_ptr(),
            wide(name).as_ptr(),
            RRF_RT_REG_SZ,
            null_mut(),
            buffer.as_mut_ptr().cast(),
            &mut size,
        )
    };
    (status == ERROR_SUCCESS)
        .then(|| String::from_utf16_lossy(&buffer[..(size as usize / 2).saturating_sub(1)]))
}

fn delete_value(path: &str, name: &str) {
    // SAFETY: NUL-terminated strings.
    unsafe { RegDeleteKeyValueW(HKEY_CURRENT_USER, wide(path).as_ptr(), wide(name).as_ptr()) };
}

fn delete_tree(path: &str) {
    // SAFETY: NUL-terminated string; then the (now empty) key itself.
    unsafe {
        RegDeleteTreeW(HKEY_CURRENT_USER, wide(path).as_ptr());
        RegDeleteKeyW(HKEY_CURRENT_USER, wide(path).as_ptr());
    }
}

fn dir_size(dir: &Path) -> u64 {
    let Ok(entries) = std::fs::read_dir(dir) else {
        return 0;
    };
    entries
        .flatten()
        .map(|entry| match entry.file_type() {
            Ok(t) if t.is_dir() => dir_size(&entry.path()),
            Ok(_) => entry.metadata().map_or(0, |m| m.len()),
            Err(_) => 0,
        })
        .sum()
}

fn wide(text: &str) -> Vec<u16> {
    OsStr::new(text).encode_wide().chain(Some(0)).collect()
}
