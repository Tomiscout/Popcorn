//! Locating (and if needed starting) Popcorn.Server. The shell never hosts the server itself.

use std::net::{TcpStream, ToSocketAddrs};
use std::os::unix::process::CommandExt;
use std::path::PathBuf;
use std::process::{Child, Command, Stdio};
use std::time::Duration;

const SERVICE_NAME: &str = "popcorn.service";
const DEFAULT_PORT: u16 = 5000;

pub struct Target {
    /// Base URL without trailing slash, e.g. `http://localhost:5000`.
    pub url: String,
    /// Set via `POPCORN_SERVER_URL` (e.g. by the tray). We don't try to start anything then.
    pub explicit: bool,
}

impl Target {
    pub fn from_env() -> Self {
        if let Ok(url) = std::env::var("POPCORN_SERVER_URL") {
            let url = url.trim().trim_end_matches('/');
            if !url.is_empty() {
                return Self {
                    url: url.to_owned(),
                    explicit: true,
                };
            }
        }

        // Same lookup order as the server under systemd: process env, then popcorn.env.
        let port = std::env::var("POPCORN_PORT")
            .ok()
            .or_else(|| {
                service_env()
                    .into_iter()
                    .find(|(k, _)| k == "POPCORN_PORT")
                    .map(|(_, v)| v)
            })
            .and_then(|p| p.trim().parse::<u16>().ok())
            .filter(|p| *p > 0)
            .unwrap_or(DEFAULT_PORT);

        Self {
            url: format!("http://localhost:{port}"),
            explicit: false,
        }
    }

    pub fn app_url(&self) -> String {
        format!("{}/index.html", self.url)
    }

    /// TCP connect check against the server's host:port.
    pub fn is_reachable(&self) -> bool {
        let Some((host, port)) = host_port(&self.url) else {
            return false;
        };
        let host = if host == "localhost" {
            "127.0.0.1"
        } else {
            host
        };
        let Ok(addrs) = (host, port).to_socket_addrs() else {
            return false;
        };
        addrs
            .into_iter()
            .any(|addr| TcpStream::connect_timeout(&addr, Duration::from_millis(750)).is_ok())
    }
}

/// `systemctl --user start popcorn.service`
pub fn start_systemd_service() -> Result<(), String> {
    systemctl(&["start", SERVICE_NAME])
}

pub fn stop_systemd_service() -> Result<(), String> {
    systemctl(&["stop", SERVICE_NAME])
}

fn systemctl(args: &[&str]) -> Result<(), String> {
    let output = Command::new("systemctl")
        .arg("--user")
        .args(args)
        .stdin(Stdio::null())
        .output()
        .map_err(|e| format!("cannot run systemctl: {e}"))?;

    if output.status.success() {
        Ok(())
    } else {
        Err(String::from_utf8_lossy(&output.stderr).trim().to_owned())
    }
}

/// `Some(reason)` when popcorn.service is not installed or keeps failing (e.g. crash loop, missing binary).
/// `None` while it is running / starting normally (or systemd can't be queried).
pub fn systemd_service_problem() -> Option<String> {
    let output = Command::new("systemctl")
        .args([
            "--user",
            "show",
            "-p",
            "LoadState,ActiveState,SubState,Result,ExecMainStatus",
            SERVICE_NAME,
        ])
        .stdin(Stdio::null())
        .output()
        .ok()?;
    let text = String::from_utf8_lossy(&output.stdout);
    let prop = |name: &str| {
        text.lines()
            .find_map(|l| l.strip_prefix(name)?.strip_prefix('='))
            .unwrap_or("")
            .to_owned()
    };

    let (load, active, sub) = (prop("LoadState"), prop("ActiveState"), prop("SubState"));
    if load == "not-found" {
        return Some(format!("{SERVICE_NAME} is not installed"));
    }
    if active == "failed" || sub == "auto-restart" {
        let status = prop("ExecMainStatus");
        let detail = match status.as_str() {
            "203" => "server executable missing (status 203/EXEC)".to_owned(),
            "" | "0" => prop("Result"),
            code => format!("{}, exit status {code}", prop("Result")),
        };
        return Some(format!("{SERVICE_NAME} is failing: {detail}"));
    }
    None
}

/// Starts Popcorn.Server from the directory of this executable (the install/bundle dir), detached
/// from our process group so it outlives the window. Prefers the apphost, falls back to `dotnet <dll>`.
pub fn launch_server_process() -> Result<Child, String> {
    let dir = install_dir().ok_or("cannot determine install directory")?;
    let exe = dir.join("Popcorn.Server");
    let dll = dir.join("Popcorn.Server.dll");

    let mut command = if exe.is_file() {
        Command::new(&exe)
    } else if dll.is_file() {
        let mut c = Command::new("dotnet");
        c.arg(&dll);
        c
    } else {
        return Err(format!("Popcorn.Server not found in {}", dir.display()));
    };

    command
        .envs(service_env())
        .current_dir(&dir)
        .stdin(Stdio::null())
        .stdout(Stdio::null())
        .stderr(Stdio::null())
        .process_group(0)
        .spawn()
        .map_err(|e| format!("cannot start {}: {e}", dir.join("Popcorn.Server").display()))
}

/// `~/.config/popcorn/popcorn.env` (the service's `EnvironmentFile`), as KEY=VALUE pairs.
fn service_env() -> Vec<(String, String)> {
    let config = std::env::var_os("XDG_CONFIG_HOME")
        .map(PathBuf::from)
        .filter(|p| p.is_absolute())
        .or_else(|| std::env::var_os("HOME").map(|h| PathBuf::from(h).join(".config")));
    let Some(text) =
        config.and_then(|c| std::fs::read_to_string(c.join("popcorn/popcorn.env")).ok())
    else {
        return Vec::new();
    };
    parse_env_file(&text)
}

fn parse_env_file(text: &str) -> Vec<(String, String)> {
    text.lines()
        .map(str::trim)
        .filter(|l| !l.is_empty() && !l.starts_with('#') && !l.starts_with(';'))
        .filter_map(|l| {
            let (k, v) = l.split_once('=')?;
            let v = v.trim();
            let v = v
                .strip_prefix('"')
                .and_then(|v| v.strip_suffix('"'))
                .or_else(|| v.strip_prefix('\'').and_then(|v| v.strip_suffix('\'')))
                .unwrap_or(v);
            Some((k.trim().to_owned(), v.to_owned()))
        })
        .collect()
}

fn install_dir() -> Option<PathBuf> {
    std::env::current_exe().ok()?.parent().map(PathBuf::from)
}

fn host_port(url: &str) -> Option<(&str, u16)> {
    let (scheme, rest) = url.split_once("://")?;
    let authority = rest.split(['/', '?', '#']).next()?;
    let authority = authority.rsplit_once('@').map_or(authority, |(_, a)| a);
    let default_port = if scheme.eq_ignore_ascii_case("https") {
        443
    } else {
        80
    };

    if let Some(v6) = authority.strip_prefix('[') {
        // [::1]:5000
        let (host, after) = v6.split_once(']')?;
        let port = after
            .strip_prefix(':')
            .map_or(Some(default_port), |p| p.parse().ok())?;
        return Some((host, port));
    }

    match authority.rsplit_once(':') {
        Some((host, port)) => Some((host, port.parse().ok()?)),
        None => Some((authority, default_port)),
    }
}

#[cfg(test)]
mod tests {
    use super::{host_port, parse_env_file};

    #[test]
    fn parses_env_file() {
        let env =
            parse_env_file("# c\nASPNETCORE_ENVIRONMENT=Production\n\n POPCORN_PORT = \"5001\"\n");
        assert_eq!(
            env,
            vec![
                ("ASPNETCORE_ENVIRONMENT".to_owned(), "Production".to_owned()),
                ("POPCORN_PORT".to_owned(), "5001".to_owned())
            ]
        );
    }

    #[test]
    fn parses_host_and_port() {
        assert_eq!(
            host_port("http://localhost:5000"),
            Some(("localhost", 5000))
        );
        assert_eq!(
            host_port("http://localhost:5000/index.html"),
            Some(("localhost", 5000))
        );
        assert_eq!(host_port("http://media.lan"), Some(("media.lan", 80)));
        assert_eq!(host_port("https://media.lan/x"), Some(("media.lan", 443)));
        assert_eq!(host_port("http://[::1]:8080/"), Some(("::1", 8080)));
        assert_eq!(host_port("localhost:5000"), None);
    }
}
