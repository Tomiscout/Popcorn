//! Putting a package in place:
//! unpack to `<install>.new` + FFmpeg → stop Popcorn → rename `<install>` → `<install>.old` and `<install>.new` →
//! `<install>` → start what was running → delete `<install>.old`. Both renames are on the same volume, so the folder
//! is never half old, half new; a failure on the way (or a new server that dies right away) puts the old folder back.
//! A fresh install is the same without the old folder, plus the OS entries (Windows: Start menu, autostart,
//! Settings → Apps; Linux: systemd user unit, app menu entry).

use std::path::Path;
use std::sync::atomic::{AtomicBool, Ordering};
use std::time::Duration;

use anyhow::{Context, bail};

use crate::ffmpeg;
use crate::log::{info, warning};
use crate::package::{self, Manifest, Package};
use crate::paths;
use crate::platform::{self, Running};

#[derive(Debug)]
pub struct Cancelled;

impl std::fmt::Display for Cancelled {
    fn fmt(&self, f: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        f.write_str("Cancelled")
    }
}

impl std::error::Error for Cancelled {}

pub fn is_cancelled(e: &anyhow::Error) -> bool {
    e.is::<Cancelled>()
}

/// Progress: what happens now, how far the whole step is (0..1), whether it can still be cancelled.
pub type Report<'a> = &'a dyn Fn(&str, f32, bool);

pub fn is_installed(install_dir: &Path) -> bool {
    install_dir.join(platform::SERVER_EXE).is_file()
}

/// Installs `package` into `install_dir` (an existing install or a new one). `autostart`: whether a fresh install
/// starts the server at login (updates keep what the user set). Returns warnings for the user.
pub fn install(
    install_dir: &Path,
    package: &Package,
    ffmpeg_source: &ffmpeg::Source,
    restart: bool,
    autostart: bool,
    report: Report,
    cancel: &AtomicBool,
) -> anyhow::Result<Vec<String>> {
    let fresh = !is_installed(install_dir);
    let previous = package::installed(install_dir);
    let staging = paths::sibling(install_dir, ".new");
    let backup = paths::sibling(install_dir, ".old");
    let mut warnings = Vec::new();
    info!(
        "Installing {} ({}) into {}, installed: {}",
        package.manifest.describe(),
        package.path.display(),
        install_dir.display(),
        if fresh {
            "nothing".into()
        } else {
            previous
                .as_ref()
                .map_or("unknown".into(), Manifest::describe)
        }
    );
    if fresh {
        check_new_folder(install_dir)?;
    }

    report("Unpacking Popcorn…", 0.0, true);
    remove_dir(&staging).with_context(|| format!("Cannot clear {}", staging.display()))?;
    let unpacked = package
        .extract(
            &staging,
            &|f| report("Unpacking Popcorn…", 0.6 * f, true),
            cancel,
        )
        .and_then(|()| {
            if !staging.join(platform::SERVER_EXE).is_file() {
                bail!("The package has no {}", platform::SERVER_EXE);
            }
            report("Unpacking FFmpeg…", 0.6, true);
            let progress = |f| report("Unpacking FFmpeg…", 0.6 + 0.15 * f, true);
            match ffmpeg::provide(ffmpeg_source, install_dir, &staging, &progress, cancel) {
                Ok(files) => info!("FFmpeg: {}", files.join(", ")),
                Err(e) if is_cancelled(&e) => return Err(e),
                Err(e) => {
                    warning!("No FFmpeg for the new version: {e:#}");
                    warnings.push(format!(
                        "FFmpeg could not be installed ({e:#}). Popcorn needs ffmpeg and ffprobe on PATH \
                         until an update brings them."
                    ));
                }
            }
            if cancel.load(Ordering::Relaxed) {
                bail!(Cancelled);
            }
            Ok(())
        });
    if let Err(e) = unpacked {
        let _ = remove_dir(&staging);
        return Err(e);
    }

    if fresh {
        report("Moving the files into place…", 0.85, false);
        if let Some(parent) = install_dir.parent() {
            std::fs::create_dir_all(parent)?;
        }
        // An empty folder of that name is in the way of the rename.
        let _ = std::fs::remove_dir(install_dir);
        if let Err(e) = rename(&staging, install_dir) {
            let _ = remove_dir(&staging);
            return Err(e);
        }
        report(platform::TEXTS.register_step, 0.9, false);
        platform::register(install_dir, &package.manifest.version, true, autostart);
        if restart {
            report("Starting the server…", 0.92, false);
            if let Err(e) = platform::start_server_checked(install_dir) {
                warnings.push(format!(
                    "Popcorn was installed but its server did not start: {e:#}"
                ));
            }
        }
        report("Done", 1.0, false);
        info!("Installed {} (new)", package.manifest.describe());
        return Ok(warnings);
    }

    // From here on no cancelling: Popcorn is going down.
    report("Closing Popcorn…", 0.75, false);
    let running = platform::stop_popcorn(install_dir, &|step| report(step, 0.75, false))?;
    info!("Stopped: server {}, app {}", running.server, running.app);

    report("Moving the old version aside…", 0.85, false);
    if let Err(e) = remove_dir(&backup) {
        start(install_dir, running, restart);
        return Err(e.context(format!("Cannot remove the old backup {}", backup.display())));
    }
    if let Err(e) = rename(install_dir, &backup) {
        start(install_dir, running, restart);
        return Err(e.context(format!(
            "Cannot move {} aside. {}",
            install_dir.display(),
            platform::TEXTS.move_aside_hint
        )));
    }
    report("Moving the new version in…", 0.88, false);
    if let Err(e) = rename(&staging, install_dir) {
        let restored = rename(&backup, install_dir);
        start(install_dir, running, restart);
        restored.context("Restoring the previous version failed too")?;
        return Err(e.context(format!(
            "Cannot move the new version into {}",
            install_dir.display()
        )));
    }
    report(platform::TEXTS.refresh_step, 0.9, false);
    platform::register(install_dir, &package.manifest.version, false, false);

    // The server also when it was not running: the finish screen offers to open Popcorn, and the check catches a
    // new version that does not start.
    if restart {
        report("Starting the server…", 0.92, false);
        if let Err(e) = platform::start_server_checked(install_dir) {
            warning!("New version did not start: {e:#}; restoring the previous one");
            report("Restoring the previous version…", 0.95, false);
            let _ = platform::stop_popcorn(install_dir, &|_| {});
            let failed = paths::sibling(install_dir, ".failed");
            let restored = remove_dir(&failed)
                .and_then(|_| rename(install_dir, &failed))
                .and_then(|_| rename(&backup, install_dir));
            if let Some(previous) = &previous {
                platform::register(install_dir, &previous.version, false, false);
            }
            start(install_dir, running, restart);
            restored.context("Restoring the previous version failed")?;
            let _ = remove_dir(&failed);
            bail!(
                "Popcorn {} did not start ({e:#}). The previous version was restored.",
                package.manifest.version
            );
        }
        if running.app {
            report("Opening the Popcorn window…", 0.96, false);
            platform::start_app(install_dir);
        }
    }

    report("Deleting the old version…", 0.98, false);
    if let Err(e) = remove_dir(&backup) {
        warning!(
            "Could not delete {}: {e:#} (next update retries)",
            backup.display()
        );
    }
    report("Done", 1.0, false);
    info!("Installed {}", package.manifest.describe());
    Ok(warnings)
}

/// A new install goes into a missing or empty folder only: never move aside (and delete) other files because
/// `--install-dir` pointed at them.
fn check_new_folder(install_dir: &Path) -> anyhow::Result<()> {
    if std::fs::read_dir(install_dir).is_ok_and(|mut entries| entries.next().is_some()) {
        bail!(
            "{} is not empty and has no Popcorn in it; pick an empty or new folder with --install-dir",
            install_dir.display()
        );
    }
    Ok(())
}

fn start(install_dir: &Path, running: Running, restart: bool) {
    if !restart {
        return;
    }
    if running.server {
        platform::start_server(install_dir);
    }
    if running.app {
        platform::start_app(install_dir);
    }
}

/// Renames retry for a while: on Windows processes that just got killed (and WebView2 helpers, virus scanners) let
/// go of their files a moment later.
fn rename(from: &Path, to: &Path) -> anyhow::Result<()> {
    retry(|| std::fs::rename(from, to))
        .with_context(|| format!("Cannot rename {} to {}", from.display(), to.display()))
}

pub fn remove_dir(dir: &Path) -> anyhow::Result<()> {
    retry(|| match std::fs::remove_dir_all(dir) {
        Err(e) if e.kind() == std::io::ErrorKind::NotFound => Ok(()),
        result => result,
    })
    .with_context(|| format!("Cannot delete {}", dir.display()))
}

fn retry(mut action: impl FnMut() -> std::io::Result<()>) -> std::io::Result<()> {
    let mut attempt = 0;
    loop {
        match action() {
            Ok(()) => return Ok(()),
            Err(e) if attempt < 20 => {
                attempt += 1;
                if attempt == 1 {
                    warning!("{e}, retrying");
                }
                std::thread::sleep(Duration::from_millis(500));
            }
            Err(e) => return Err(e),
        }
    }
}
