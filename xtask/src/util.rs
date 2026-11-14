//! Paths, processes, files and the version: the helpers every step uses.

use std::fs;
use std::path::{Path, PathBuf};
use std::process::Command;
use std::sync::OnceLock;
use std::time::{SystemTime, UNIX_EPOCH};

use anyhow::{Context, Result, bail};
use sha2::{Digest, Sha256};

/// The repository root (xtask/..). Every relative path below is relative to it.
pub fn root() -> &'static Path {
    static ROOT: OnceLock<PathBuf> = OnceLock::new();
    ROOT.get_or_init(|| Path::new(env!("CARGO_MANIFEST_DIR")).parent().unwrap().to_path_buf())
}

pub fn path(relative: &str) -> PathBuf {
    root().join(relative)
}

/// .NET runtime identifier the apps are published for (RID-specific output: native libraries next to the DLLs).
/// Portable RID, like the apphost in Directory.Build.targets.
pub fn rid() -> &'static str {
    if cfg!(windows) {
        "win-x64"
    } else if std::env::consts::ARCH == "aarch64" {
        "linux-arm64"
    } else {
        "linux-x64"
    }
}

/// Package platform: release.json and the release zip's name, what Popcorn.Installer matches releases with.
pub fn release_platform() -> &'static str {
    if cfg!(windows) { "windows-x64" } else { rid() }
}

pub fn bundle_dir() -> PathBuf {
    path(if cfg!(windows) { "Bundle/Windows" } else { "Bundle/Linux" })
}

pub fn publish_dir(project: &str) -> PathBuf {
    path("Bundle/Publish").join(project)
}

/// Output of `cargo build --release` (the installer has its own profile: [`installer_exe`]).
pub fn cargo_release(file: &str) -> PathBuf {
    path("target/release").join(file)
}

pub fn exe(name: &str) -> String {
    format!("{name}{}", std::env::consts::EXE_SUFFIX)
}

/// The product version: `--version`, else the VERSION file (set_version.mjs).
pub fn version(arg: Option<&str>) -> Result<String> {
    let version = match arg {
        Some(v) => v.trim_start_matches('v').to_string(),
        None => fs::read_to_string(path("VERSION")).context("reading VERSION")?.trim().to_string(),
    };
    if !is_version(&version) {
        bail!("version '{version}' is not major.minor.patch[-prerelease]");
    }
    Ok(version)
}

fn is_version(v: &str) -> bool {
    let (core, pre) = match v.split_once('-') {
        Some((core, pre)) => (core, Some(pre)),
        None => (v, None),
    };
    let numbers: Vec<&str> = core.split('.').collect();
    numbers.len() == 3
        && numbers.iter().all(|n| !n.is_empty() && n.bytes().all(|b| b.is_ascii_digit()))
        && pre.is_none_or(|p| !p.is_empty() && p.bytes().all(|b| b.is_ascii_alphanumeric() || b == b'.' || b == b'-'))
}

/// A command run in the repository root. npm/npx are .cmd scripts on Windows, which `Command` only finds by full name.
pub fn cmd(program: &str) -> Command {
    let program = match program {
        "npm" | "npx" if cfg!(windows) => format!("{program}.cmd"),
        // The cargo running this xtask
        "cargo" => std::env::var("CARGO").unwrap_or_else(|_| "cargo".into()),
        _ => program.to_string(),
    };
    let mut command = Command::new(program);
    command.current_dir(root());
    command
}

fn describe(command: &Command) -> String {
    let mut text = command.get_program().to_string_lossy().into_owned();
    for arg in command.get_args() {
        text.push(' ');
        text.push_str(&arg.to_string_lossy());
    }
    if let Some(dir) = command.get_current_dir().filter(|d| *d != root()) {
        let dir = dir.strip_prefix(root()).unwrap_or(dir);
        text = format!("(in {}) {text}", dir.display());
    }
    text
}

/// Runs a command with its output on the console; fails when it exits with an error.
pub fn run(command: &mut Command) -> Result<()> {
    let text = describe(command);
    println!("> {text}");
    let status = command.status().with_context(|| format!("could not start `{text}` (is it installed and on PATH?)"))?;
    if !status.success() {
        bail!("`{text}` failed ({status})");
    }
    Ok(())
}

/// Runs a command and returns its trimmed stdout.
pub fn output(command: &mut Command) -> Result<String> {
    let text = describe(command);
    let out = command.output().with_context(|| format!("could not start `{text}`"))?;
    if !out.status.success() {
        bail!("`{text}` failed ({}): {}", out.status, String::from_utf8_lossy(&out.stderr).trim());
    }
    Ok(String::from_utf8_lossy(&out.stdout).trim().to_string())
}

/// Whether a program can be started (`<program> --version` succeeds).
pub fn has(program: &str) -> bool {
    cmd(program).arg("--version").output().is_ok_and(|o| o.status.success())
}

pub fn step(name: &str) {
    println!("\n== {name}");
}

pub fn warn(message: &str) {
    eprintln!("warning: {message}");
}

pub fn remove_dir(dir: &Path) -> Result<()> {
    if dir.exists() {
        fs::remove_dir_all(dir).with_context(|| format!("removing {}", dir.display()))?;
    }
    Ok(())
}

/// Copies a file, keeping its permissions (fs::copy does), creating the target's folder.
pub fn copy_file(from: &Path, to: &Path) -> Result<()> {
    if let Some(parent) = to.parent() {
        fs::create_dir_all(parent)?;
    }
    fs::copy(from, to).with_context(|| format!("copying {} to {}", from.display(), to.display()))?;
    Ok(())
}

/// Copies the contents of `from` into `to` (merged, files replaced); symlinks are copied as the files they point to.
pub fn copy_dir(from: &Path, to: &Path) -> Result<()> {
    for file in files(from)? {
        copy_file(&from.join(&file), &to.join(&file))?;
    }
    Ok(())
}

/// Every file below `dir`, relative to it, sorted; symlinks followed.
pub fn files(dir: &Path) -> Result<Vec<PathBuf>> {
    fn walk(root: &Path, dir: &Path, out: &mut Vec<PathBuf>) -> Result<()> {
        for entry in fs::read_dir(dir).with_context(|| format!("reading {}", dir.display()))? {
            let path = entry?.path();
            if fs::metadata(&path)?.is_dir() {
                walk(root, &path, out)?;
            } else {
                out.push(path.strip_prefix(root).unwrap().to_path_buf());
            }
        }
        Ok(())
    }
    let mut out = Vec::new();
    walk(dir, dir, &mut out)?;
    out.sort();
    Ok(out)
}

pub fn sha256_hex(bytes: &[u8]) -> String {
    Sha256::digest(bytes).iter().map(|b| format!("{b:02x}")).collect()
}

/// Reads a stamp file (what an output was built from); empty when missing.
pub fn read_stamp(path: &Path) -> String {
    fs::read_to_string(path).unwrap_or_default()
}

/// Current UTC time as yyyy-MM-ddTHH:mm:ssZ.
pub fn utc_now() -> String {
    let secs = SystemTime::now().duration_since(UNIX_EPOCH).map(|d| d.as_secs()).unwrap_or(0) as i64;
    let (days, rem) = (secs.div_euclid(86400), secs.rem_euclid(86400));
    // Civil date from days since 1970-01-01 (Howard Hinnant's algorithm)
    let z = days + 719_468;
    let era = z.div_euclid(146_097);
    let doe = z - era * 146_097;
    let yoe = (doe - doe / 1460 + doe / 36524 - doe / 146_096) / 365;
    let doy = doe - (365 * yoe + yoe / 4 - yoe / 100);
    let mp = (5 * doy + 2) / 153;
    let day = doy - (153 * mp + 2) / 5 + 1;
    let month = if mp < 10 { mp + 3 } else { mp - 9 };
    let year = yoe + era * 400 + i64::from(month <= 2);
    format!("{year:04}-{month:02}-{day:02}T{:02}:{:02}:{:02}Z", rem / 3600, rem % 3600 / 60, rem % 60)
}

/// `cargo xtask clean`: the bundle, publish and web outputs. Not target/ (CEF, every crate): `cargo clean` for that.
pub fn clean() -> Result<()> {
    let mut dirs = vec![bundle_dir(), path("Bundle/Publish"), path("Popcorn.Web/dist"), path("Popcorn.Web/dist-player")];
    for project in ["Popcorn.Server", "Popcorn.Player.Linux", "Popcorn.Player.Windows", "Popcorn.Windows"] {
        dirs.push(path(project).join("bin/Release"));
    }
    for dir in dirs {
        if dir.exists() {
            println!("Removing {}", dir.strip_prefix(root()).unwrap_or(&dir).display());
            remove_dir(&dir)?;
        }
    }
    Ok(())
}

#[cfg(test)]
mod tests {
    #[test]
    fn versions() {
        for ok in ["1.2.3", "10.0.0-beta.1", "1.0.0-rc-2"] {
            assert!(super::is_version(ok), "{ok}");
        }
        for bad in ["1.2", "1.2.x", "1.2.3-", "1.2.3-a_b", ""] {
            assert!(!super::is_version(bad), "{bad}");
        }
    }
}
