//! Linux: the server runs as the systemd user unit `popcorn.service` (what `make install-linux` sets up too), the
//! app is found in the app menu through `~/.local/share/applications/popcorn.desktop` (with an "Uninstall Popcorn"
//! action). Processes are found by their executable (`/proc/<pid>/exe` inside the install folder: never a dev build
//! elsewhere) and stopped with SIGTERM (.NET's host shuts down cleanly on it), SIGKILL after a timeout.
//!
//! Unlike Windows, files and folders in use can be renamed and deleted, so the installer runs in place (no copy).
//! What it has to avoid instead: being started by the server (Settings → Updates) puts it in the server's cgroup,
//! and `systemctl stop popcorn.service` kills the whole cgroup. `leave_server_service` moves it into its own scope.
//!
//! The unit, env file and desktop entry are written from the templates in the bundle's `setup/` folder (copied
//! there by `cargo xtask bundle`), else from the copies built into this program.

use std::ffi::OsString;
use std::io::IsTerminal;
use std::os::unix::process::CommandExt;
use std::path::{Path, PathBuf};
use std::process::{Command, Stdio};
use std::time::{Duration, Instant};

use anyhow::{Context, bail};

use super::{Running, Texts};
use crate::log::{info, warning};
use crate::paths;

pub const SERVER_EXE: &str = "Popcorn.Server";
const APP_EXE: &str = "Popcorn.Linux";
const SERVICE: &str = "popcorn.service";
/// Bundle folder with the templates (`cargo xtask bundle`).
const SETUP_DIR: &str = "setup";
const UNIT_TEMPLATE: &str = include_str!("../../../Popcorn.Server/systemd/popcorn.service");
const ENV_TEMPLATE: &str = include_str!("../../../Popcorn.Server/systemd/popcorn.env");
const DESKTOP_TEMPLATE: &str = include_str!("../../../Popcorn.Linux/popcorn.desktop");
/// How long the server gets to save its state (database, torrent engine) after SIGTERM.
const SERVER_STOP_TIMEOUT: Duration = Duration::from_secs(20);
/// The window, players and their CEF processes.
const APP_STOP_TIMEOUT: Duration = Duration::from_secs(5);
/// A new server still running after this long counts as started.
const SERVER_START_GRACE: Duration = Duration::from_secs(5);

pub const TEXTS: Texts = Texts {
    register_step: "Adding the app menu entry and the systemd service…",
    refresh_step: "Updating the systemd service and the app menu entry…",
    unregister_step: "Removing the app menu entry and the systemd service…",
    uninstall_removes: "its app menu entry and systemd user service",
    autostart_hint: "Change it later in Popcorn's settings, or with systemctl --user enable / disable popcorn.service.",
    launcher: "your app menu",
    move_aside_hint: "Check that its parent folder is writable",
};

pub const RUNTIME_DOWNLOAD: &str = "https://learn.microsoft.com/dotnet/core/install/linux";

/// Files in use do not keep a folder from being renamed here: the installer runs in place.
pub const RELOCATE_EXE: bool = false;

/// Same place as `make install-linux`, so installs made by make get updated too.
pub fn default_install_dir() -> PathBuf {
    home().join(".local/bin/Popcorn")
}

fn home() -> PathBuf {
    std::env::var_os("HOME").map_or_else(std::env::temp_dir, PathBuf::from)
}

fn config_home() -> PathBuf {
    std::env::var_os("XDG_CONFIG_HOME")
        .filter(|d| !d.is_empty())
        .map_or_else(|| home().join(".config"), PathBuf::from)
}

fn data_home() -> PathBuf {
    std::env::var_os("XDG_DATA_HOME")
        .filter(|d| !d.is_empty())
        .map_or_else(|| home().join(".local/share"), PathBuf::from)
}

fn unit_path() -> PathBuf {
    config_home().join("systemd/user").join(SERVICE)
}

/// The unit's `EnvironmentFile` (port, environment), the user's to edit: only written when missing.
fn env_path() -> PathBuf {
    config_home().join("popcorn/popcorn.env")
}

fn desktop_path() -> PathBuf {
    data_home().join("applications/popcorn.desktop")
}

/// Terminal: stderr / stdout. Started from the app menu or a file manager: a zenity / kdialog box when there is one.
pub fn show_text(text: &str, error: bool) {
    let display = ["WAYLAND_DISPLAY", "DISPLAY"]
        .iter()
        .any(|name| std::env::var_os(name).is_some_and(|v| !v.is_empty()));
    if std::io::stderr().is_terminal() || !display {
        if error {
            eprintln!("{text}");
        } else {
            println!("{text}");
        }
        return;
    }
    let zenity = Command::new("zenity")
        .args([if error { "--error" } else { "--info" }, "--no-markup"])
        .args(["--title", "Popcorn Installer", "--text", text])
        .status();
    if zenity.is_ok_and(|s| s.success() || s.code() == Some(1)) {
        return; // 1 = closed with the window's X
    }
    let kdialog = Command::new("kdialog")
        .args(["--title", "Popcorn Installer"])
        .args([if error { "--error" } else { "--msgbox" }, text])
        .status();
    if !kdialog.is_ok_and(|s| s.success()) {
        eprintln!("{text}");
    }
}

/// An flock on a file in the runtime folder, released when the process ends (also when it crashes).
pub struct InstanceGuard(#[allow(dead_code)] Option<std::fs::File>);

pub fn single_instance() -> Option<InstanceGuard> {
    let dir = std::env::var_os("XDG_RUNTIME_DIR")
        .filter(|d| !d.is_empty())
        .map_or_else(std::env::temp_dir, PathBuf::from);
    let Ok(file) = std::fs::OpenOptions::new()
        .create(true)
        .truncate(false)
        .write(true)
        .open(dir.join("popcorn-installer.lock"))
    else {
        return Some(InstanceGuard(None)); // cannot tell, don't block the update
    };
    match file.try_lock() {
        Ok(()) => Some(InstanceGuard(Some(file))),
        Err(std::fs::TryLockError::WouldBlock) => None,
        Err(_) => Some(InstanceGuard(None)),
    }
}

/// Started by the server of a systemd unit (Settings → Updates): runs itself again in its own transient scope, so
/// that stopping the unit does not kill it along with the server. True = the copy runs (this process should exit).
pub fn leave_server_service(args: &[OsString]) -> anyhow::Result<bool> {
    let cgroup = std::fs::read_to_string("/proc/self/cgroup").unwrap_or_default();
    if !cgroup
        .lines()
        .any(|line| line.ends_with(&format!("/{SERVICE}")))
    {
        return Ok(false);
    }
    let exe = std::env::current_exe()?;
    Command::new("systemd-run")
        .args(["--user", "--scope", "--quiet", "--collect"])
        .arg(format!("--unit=popcorn-installer-{}", std::process::id()))
        .arg("--")
        .arg(&exe)
        .args(args)
        .stdin(Stdio::null())
        .process_group(0)
        .spawn()
        .context("Cannot run systemd-run")?;
    info!("Continuing outside {SERVICE} (systemd-run --scope)");
    Ok(true)
}

struct ProcessInfo {
    pid: i32,
    name: String,
}

/// Every process whose executable is inside `install_dir` (except this one).
fn processes_in(install_dir: &Path) -> Vec<ProcessInfo> {
    let own_pid = std::process::id() as i32;
    let Ok(entries) = std::fs::read_dir("/proc") else {
        warning!("Cannot list /proc");
        return Vec::new();
    };
    entries
        .flatten()
        .filter_map(|entry| {
            let pid: i32 = entry.file_name().to_str()?.parse().ok()?;
            if pid == own_pid {
                return None;
            }
            // Other users' processes cannot be read: skipped.
            let exe = std::fs::read_link(entry.path().join("exe")).ok()?;
            let exe = PathBuf::from(
                exe.to_string_lossy()
                    .trim_end_matches(" (deleted)")
                    .to_string(),
            );
            paths::is_inside(&exe, install_dir).then(|| ProcessInfo {
                pid,
                name: exe
                    .file_name()
                    .map(|n| n.to_string_lossy().into_owned())
                    .unwrap_or_default(),
            })
        })
        .collect()
}

fn signal(process: &ProcessInfo, signal: i32) {
    // SAFETY: plain syscall; a pid that is gone meanwhile only gives ESRCH.
    if unsafe { libc::kill(process.pid, signal) } != 0 {
        let e = std::io::Error::last_os_error();
        if e.raw_os_error() != Some(libc::ESRCH) {
            warning!("Cannot signal {} ({}): {e}", process.name, process.pid);
        }
    }
}

fn is_server(process: &ProcessInfo) -> bool {
    process.name == SERVER_EXE
}

fn stop(
    install_dir: &Path,
    which: impl Fn(&ProcessInfo) -> bool + Copy,
    timeout: Duration,
) -> bool {
    let gone = wait_until(timeout, || !processes_in(install_dir).iter().any(which));
    if !gone {
        for process in processes_in(install_dir).iter().filter(|p| which(p)) {
            warning!(
                "{} ({}) still running after {timeout:?}, killing it",
                process.name,
                process.pid
            );
            signal(process, libc::SIGKILL);
        }
    }
    gone
}

/// Stops every process of this install: the app window and players first (the app would start the server again
/// when it disappears), then the server (through systemd when the unit runs it, so it is not restarted), killed
/// after `SERVER_STOP_TIMEOUT`.
pub fn stop_popcorn(install_dir: &Path, step: &dyn Fn(&str)) -> anyhow::Result<Running> {
    let processes = processes_in(install_dir);
    let running = Running {
        server: processes.iter().any(is_server),
        app: processes.iter().any(|p| p.name == APP_EXE),
    };

    if processes.iter().any(|p| !is_server(p)) {
        step("Closing the Popcorn window and players…");
        for process in processes.iter().filter(|p| !is_server(p)) {
            info!("Stopping {} ({})", process.name, process.pid);
            signal(process, libc::SIGTERM);
        }
        stop(install_dir, |p| !is_server(p), APP_STOP_TIMEOUT);
    }

    if running.server {
        step("Waiting for the Popcorn server to save and close…");
        let servers: Vec<i32> = processes
            .iter()
            .filter(|p| is_server(p))
            .map(|p| p.pid)
            .collect();
        let unit_pid = service_property("MainPID").and_then(|p| p.parse::<i32>().ok());
        // systemd sends the SIGTERM itself (and then does not restart it); --no-block: the wait and the kill after
        // it are ours, systemd's own stop timeout is 90 s.
        let by_unit = unit_pid.is_some_and(|pid| servers.contains(&pid))
            && systemctl(&["stop", "--no-block", SERVICE]).is_ok();
        if by_unit {
            info!("Stopping {SERVICE}");
        }
        for process in processes.iter().filter(|p| is_server(p)) {
            if !by_unit || Some(process.pid) != unit_pid {
                info!("Stopping {} ({})", process.name, process.pid);
                signal(process, libc::SIGTERM);
            }
        }
        stop(install_dir, is_server, SERVER_STOP_TIMEOUT);
    }

    // Whatever is left (players started meanwhile).
    for process in processes_in(install_dir) {
        signal(&process, libc::SIGKILL);
    }
    if !wait_until(Duration::from_secs(5), || {
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

fn systemctl(args: &[&str]) -> anyhow::Result<String> {
    let output = Command::new("systemctl")
        .arg("--user")
        .args(args)
        .stdin(Stdio::null())
        .output()
        .context("Cannot run systemctl")?;
    if !output.status.success() {
        bail!(
            "systemctl --user {} failed: {}",
            args.join(" "),
            String::from_utf8_lossy(&output.stderr).trim()
        );
    }
    Ok(String::from_utf8_lossy(&output.stdout).trim().to_string())
}

fn service_property(name: &str) -> Option<String> {
    systemctl(&["show", SERVICE, "--property", name, "--value"]).ok()
}

/// The server only runs as the systemd user unit. Another install's unit (`--install-dir` on a test copy) is not
/// started for this one.
fn unit_is_for(install_dir: &Path) -> bool {
    let ours = registered_install_dir()
        .is_some_and(|dir| paths::normalize(&dir) == paths::normalize(install_dir));
    if !ours {
        warning!(
            "{} does not run {}, not starting it",
            unit_path().display(),
            install_dir.display()
        );
    }
    ours
}

pub fn start_server(install_dir: &Path) {
    if !unit_is_for(install_dir) {
        return;
    }
    match systemctl(&["start", SERVICE]) {
        Ok(_) => info!("Started {SERVICE}"),
        Err(e) => warning!("{e:#}"),
    }
}

/// Starts the unit and fails if the server exits with an error within `SERVER_START_GRACE` (missing runtime, crash
/// on start-up): the unit failed or restarted meanwhile. It is stopped then, so it does not keep restarting a broken
/// version while the old one is put back.
pub fn start_server_checked(install_dir: &Path) -> anyhow::Result<()> {
    if !unit_is_for(install_dir) {
        return Ok(());
    }
    let _ = systemctl(&["reset-failed", SERVICE]);
    let restarts = || {
        service_property("NRestarts")
            .and_then(|n| n.parse::<u32>().ok())
            .unwrap_or(0)
    };
    let before = restarts();
    systemctl(&["start", SERVICE])?;
    info!("Started {SERVICE}");
    let start = Instant::now();
    while start.elapsed() < SERVER_START_GRACE {
        std::thread::sleep(Duration::from_millis(250));
        let state = service_property("ActiveState").unwrap_or_default();
        let sub = service_property("SubState").unwrap_or_default();
        if state == "failed" || sub == "auto-restart" || restarts() > before {
            let status = service_property("ExecMainStatus").unwrap_or_default();
            let _ = systemctl(&["stop", SERVICE]);
            bail!(
                "{SERVER_EXE} exited with status {status} (journalctl --user -u {SERVICE} has its output)"
            );
        }
    }
    Ok(())
}

pub fn start_app(install_dir: &Path) {
    if !install_dir.join(APP_EXE).is_file() {
        return;
    }
    let spawned = Command::new(install_dir.join(APP_EXE))
        .current_dir(install_dir)
        .stdin(Stdio::null())
        .stdout(Stdio::null())
        .stderr(Stdio::null())
        .process_group(0)
        .spawn();
    match spawned {
        Ok(_) => info!("Started {APP_EXE}"),
        Err(e) => warning!("Cannot start {APP_EXE}: {e}"),
    }
}

/// `<install>/setup/<name>`, else the built-in copy.
fn template(install_dir: &Path, name: &str, built_in: &str) -> String {
    std::fs::read_to_string(install_dir.join(SETUP_DIR).join(name))
        .unwrap_or_else(|_| built_in.to_string())
}

/// Template placeholders filled in; `Exec*=` values quoted when the path has spaces (systemd and desktop entries
/// both split those lines at spaces, the other keys take the rest of the line as is).
fn fill(template: &str, install_dir: &Path, env_file: &Path) -> String {
    let dir = install_dir.display().to_string();
    let mut text = String::new();
    for line in template.lines() {
        let filled = line
            .replace("@INSTALL_DIR@", &dir)
            .replace("@ENV_FILE@", &env_file.display().to_string());
        let quoted = match filled.split_once('=') {
            Some((key, value))
                if (key == "ExecStart" || key == "Exec")
                    && line.contains("@INSTALL_DIR@")
                    && dir.contains(char::is_whitespace) =>
            {
                let (program, rest) = match value.find(" --") {
                    Some(i) => value.split_at(i),
                    None => (value, ""),
                };
                format!("{key}=\"{program}\"{rest}")
            }
            _ => filled,
        };
        text.push_str(&quoted);
        text.push('\n');
    }
    text
}

fn write_file(path: &Path, text: &str) -> bool {
    if let Some(parent) = path.parent() {
        let _ = std::fs::create_dir_all(parent);
    }
    match std::fs::write(path, text) {
        Ok(()) => true,
        Err(e) => {
            warning!("Cannot write {}: {e}", path.display());
            false
        }
    }
}

/// The systemd user unit (server; enabled or not = `autostart`, on a fresh install only: updates keep the user's
/// choice), its env file (only when missing) and the app menu entry. An update only rewrites them when the unit
/// belongs to this folder (or there is none).
pub fn register(install_dir: &Path, _version: &str, fresh: bool, autostart: bool) {
    let existing = registered_install_dir();
    if !fresh
        && let Some(existing) = &existing
        && paths::normalize(existing) != paths::normalize(install_dir)
    {
        info!(
            "{} is for {}, not updating it",
            unit_path().display(),
            existing.display()
        );
        return;
    }

    let env_file = env_path();
    if !env_file.is_file() {
        write_file(
            &env_file,
            &template(install_dir, "popcorn.env", ENV_TEMPLATE),
        );
    }
    let unit = fill(
        &template(install_dir, SERVICE, UNIT_TEMPLATE),
        install_dir,
        &env_file,
    );
    if write_file(&unit_path(), &unit) {
        info!("systemd unit: {}", unit_path().display());
        if let Err(e) = systemctl(&["daemon-reload"]) {
            warning!("{e:#}");
        }
        if fresh {
            let action = if autostart { "enable" } else { "disable" };
            match systemctl(&[action, SERVICE]) {
                Ok(_) => info!("Popcorn.Server autostart at login: {action}d"),
                Err(e) => warning!("{e:#}"),
            }
        }
    }

    let desktop = fill(
        &template(install_dir, "popcorn.desktop", DESKTOP_TEMPLATE),
        install_dir,
        &env_file,
    );
    if write_file(&desktop_path(), &desktop) {
        info!("App menu entry: {}", desktop_path().display());
    }
}

/// Removes what `register` (or `make install-linux`) added for this install folder. The env file is the user's
/// configuration: it goes with the data (`extra_data_dirs`).
pub fn unregister(install_dir: &Path) {
    let ours = |dir: Option<PathBuf>| {
        dir.is_some_and(|d| paths::normalize(&d) == paths::normalize(install_dir))
    };
    if ours(registered_install_dir()) {
        let _ = systemctl(&["disable", SERVICE]);
        let _ = std::fs::remove_file(unit_path());
        let _ = systemctl(&["daemon-reload"]);
        let _ = systemctl(&["reset-failed", SERVICE]);
        info!("Removed {}", unit_path().display());
    }
    if ours(desktop_install_dir()) {
        let _ = std::fs::remove_file(desktop_path());
        info!("Removed {}", desktop_path().display());
    }
}

/// `WorkingDirectory=` of the systemd unit.
pub fn registered_install_dir() -> Option<PathBuf> {
    key_value(
        &std::fs::read_to_string(unit_path()).ok()?,
        "WorkingDirectory",
    )
}

/// `Path=` of the desktop entry.
fn desktop_install_dir() -> Option<PathBuf> {
    key_value(&std::fs::read_to_string(desktop_path()).ok()?, "Path")
}

fn key_value(text: &str, key: &str) -> Option<PathBuf> {
    text.lines()
        .find_map(|line| line.trim().strip_prefix(key)?.strip_prefix('='))
        .map(|v| v.trim().trim_matches('"'))
        .filter(|v| !v.is_empty())
        .map(PathBuf::from)
}

/// `~/.config/popcorn` (popcorn.env).
pub fn extra_data_dirs() -> Vec<PathBuf> {
    env_path()
        .parent()
        .map(Path::to_path_buf)
        .into_iter()
        .collect()
}

/// KEY=VALUE lines of the env file (comments and blank lines skipped, quotes around values removed).
fn env_file_vars() -> Vec<(String, String)> {
    let text = std::fs::read_to_string(env_path()).unwrap_or_default();
    text.lines()
        .map(str::trim)
        .filter(|line| !line.is_empty() && !line.starts_with('#'))
        .filter_map(|line| {
            let (key, value) = line.split_once('=')?;
            let value = value.trim();
            let value = value
                .strip_prefix('"')
                .and_then(|v| v.strip_suffix('"'))
                .unwrap_or(value);
            Some((key.trim().to_string(), value.to_string()))
        })
        .collect()
}

/// What a fresh install needs and the machine lacks: the .NET runtime of the (framework-dependent) bundle, looked
/// for where the apphost would (DOTNET_ROOT, PATH, the distro and Microsoft install folders, ~/.dotnet), and systemd.
pub fn missing_runtimes() -> Vec<&'static str> {
    let mut candidates: Vec<PathBuf> = Vec::new();
    if let Some(root) = std::env::var_os("DOTNET_ROOT") {
        candidates.push(Path::new(&root).join("dotnet"));
    }
    candidates.push("dotnet".into());
    if let Ok(location) = std::fs::read_to_string("/etc/dotnet/install_location") {
        candidates.push(Path::new(location.trim()).join("dotnet"));
    }
    candidates.extend(
        ["/usr/share/dotnet", "/usr/lib/dotnet", "/usr/lib64/dotnet"]
            .iter()
            .map(|dir| Path::new(dir).join("dotnet")),
    );
    candidates.push(home().join(".dotnet/dotnet"));

    let found = candidates.into_iter().any(|dotnet| {
        Command::new(dotnet)
            .arg("--list-runtimes")
            .stdin(Stdio::null())
            .output()
            .is_ok_and(|output| {
                String::from_utf8_lossy(&output.stdout)
                    .lines()
                    .any(|line| line.starts_with("Microsoft.AspNetCore.App 10."))
            })
    });
    let mut missing = Vec::new();
    if !found {
        missing.push(DOTNET_RUNTIME);
    }
    // The server only runs as the systemd user unit.
    if systemctl(&["show-environment"]).is_err() {
        missing.push(SYSTEMD);
    }
    missing
}

const DOTNET_RUNTIME: &str = "ASP.NET Core Runtime 10";
const SYSTEMD: &str = "systemd user session";

pub fn runtime_message(missing: &[&str]) -> String {
    let mut text = format!("Popcorn needs the {}.", missing.join(" and a "));
    if missing.contains(&DOTNET_RUNTIME) {
        text.push_str(
            " Install the runtime with your package manager (Arch: aspnet-runtime, Debian / Ubuntu / Fedora: \
             aspnetcore-runtime-10.0).",
        );
    }
    if missing.contains(&SYSTEMD) {
        text.push_str(
            " Its server runs as a systemd user service (systemctl --user), which this session does not have.",
        );
    }
    text.push_str(" Then retry.");
    text
}

/// The server's web UI in the default browser: `localhost:$POPCORN_PORT` (environment, else the env file), 5000.
pub fn open_browser() {
    let port = std::env::var("POPCORN_PORT")
        .ok()
        .or_else(|| {
            env_file_vars()
                .into_iter()
                .find_map(|(key, value)| (key == "POPCORN_PORT").then_some(value))
        })
        .and_then(|p| p.parse::<u16>().ok())
        .filter(|&p| p > 0)
        .unwrap_or(5000);
    let url = format!("http://localhost:{port}/");
    info!("Opening {url}");
    if let Err(e) = Command::new("xdg-open")
        .arg(&url)
        .stdin(Stdio::null())
        .stdout(Stdio::null())
        .stderr(Stdio::null())
        .process_group(0)
        .spawn()
    {
        warning!("Cannot run xdg-open: {e}");
    }
}

/// stdout is a plain pipe / terminal here.
pub fn attach_stdout() {}

/// A running program can delete its own file on Linux.
pub fn delete_after_exit(path: &Path) {
    let _ = std::fs::remove_file(path);
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn fills_templates() {
        let unit = fill(
            UNIT_TEMPLATE,
            Path::new("/opt/Pop corn"),
            Path::new("/e/popcorn.env"),
        );
        assert!(unit.contains("WorkingDirectory=/opt/Pop corn\n"));
        assert!(unit.contains("ExecStart=\"/opt/Pop corn/Popcorn.Server\"\n"));
        assert!(unit.contains("EnvironmentFile=/e/popcorn.env\n"));
        assert_eq!(
            key_value(&unit, "WorkingDirectory"),
            Some(PathBuf::from("/opt/Pop corn"))
        );

        let desktop = fill(
            DESKTOP_TEMPLATE,
            Path::new("/home/u/Popcorn"),
            Path::new("/e"),
        );
        assert!(desktop.contains("Exec=/home/u/Popcorn/Popcorn.Linux\n"));
        assert!(desktop.contains("Exec=/home/u/Popcorn/Popcorn.Installer.Linux --uninstall\n"));
        assert_eq!(
            key_value(&desktop, "Path"),
            Some(PathBuf::from("/home/u/Popcorn"))
        );
    }
}
