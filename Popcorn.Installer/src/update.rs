//! What to install and getting it there, without the window: the check (installed version against the latest
//! release or a package file), the downloads (package + FFmpeg) and handing both to `install`. ui.rs shows it,
//! `--update-check` prints the check.

use std::cell::Cell;
use std::path::{Path, PathBuf};
use std::sync::atomic::AtomicBool;
use std::time::Instant;

use anyhow::{Context, bail};

use crate::ffmpeg;
use crate::github::{self, Asset, GitHub};
use crate::install::{self, Report};
use crate::log::{info, warning};
use crate::package::{self, Package};
use crate::paths;

pub struct Config {
    pub install_dir: PathBuf,
    /// Install this zip instead of looking on GitHub.
    pub package: Option<PathBuf>,
    pub repo: String,
    pub token: Option<String>,
    pub prerelease: bool,
    pub force: bool,
    /// `--confirm`: go ahead without asking.
    pub confirm: bool,
    /// `--from-app`: started by Popcorn itself, which gets restarted; the finish screen offers no way to open it.
    pub from_app: bool,
    pub restart: bool,
    /// A fresh install starts the server at login (`--no-autostart` = off; the window asks).
    pub autostart: bool,
    pub uninstall: bool,
    pub keep_data: bool,
}

#[derive(Clone)]
pub struct Offer {
    /// Installed version; None = unknown (an install from before release.json) or nothing installed.
    pub installed: Option<String>,
    /// Nothing installed yet.
    pub fresh: bool,
    pub version: String,
    pub title: String,
    /// Release notes (markdown as written on GitHub).
    pub notes: String,
    pub size: Option<u64>,
    source: Source,
}

#[derive(Clone)]
enum Source {
    Release { package: Asset },
    File(PathBuf),
}

impl Offer {
    pub fn is_file(&self) -> bool {
        matches!(self.source, Source::File(_))
    }
}

pub enum Check {
    UpToDate { version: String },
    Offer(Box<Offer>),
}

pub fn check(config: &Config) -> anyhow::Result<Check> {
    let fresh = !install::is_installed(&config.install_dir);
    let installed = if fresh {
        None
    } else {
        package::installed(&config.install_dir).map(|m| m.version)
    };

    if let Some(path) = &config.package {
        let package = Package::open(path)?;
        info!(
            "Package {} = {}",
            path.display(),
            package.manifest.describe()
        );
        return Ok(Check::Offer(Box::new(Offer {
            installed,
            fresh,
            version: package.manifest.version.clone(),
            title: format!("Package file {}", path.display()),
            notes: String::new(),
            size: std::fs::metadata(path).ok().map(|m| m.len()),
            source: Source::File(path.clone()),
        })));
    }

    info!("Checking github.com/{} for releases", config.repo);
    let github = GitHub::new(&config.repo, config.token.clone());
    let release = github.latest(config.prerelease)?;
    let version = release.version().to_string();
    info!("Latest release: {} ({version})", release.tag_name);

    if !fresh && !config.force && installed.as_deref().is_some_and(|i| !is_newer(&version, i)) {
        return Ok(Check::UpToDate {
            version: installed.unwrap_or_default(),
        });
    }
    let package = release
        .package()
        .with_context(|| {
            format!(
                "Release {} has no {} package",
                release.tag_name,
                crate::platform::PLATFORM
            )
        })?
        .clone();
    if package.sha256().is_none() {
        // GitHub computes it on upload (assets since mid 2025): the only check the download gets.
        bail!(
            "{} of release {} has no SHA-256 digest on GitHub; upload it again",
            package.name,
            release.tag_name
        );
    }
    let mut title = release
        .name
        .clone()
        .filter(|n| !n.trim().is_empty())
        .unwrap_or(release.tag_name.clone());
    if release.prerelease {
        title.push_str(" (pre-release)");
    }
    Ok(Check::Offer(Box::new(Offer {
        installed,
        fresh,
        version,
        title,
        notes: release.body.clone().unwrap_or_default().trim().to_string(),
        size: Some(package.size),
        source: Source::Release { package },
    })))
}

/// Share of the progress bar the downloads get (by bytes), when there are any; unpacking and the swap get the rest.
const DOWNLOAD_SHARE: f32 = 0.7;

/// Downloads what `offer` needs and installs it (`autostart`: see `install::install`). Returns warnings for the user.
/// Cancelled (the window was closed): what it downloaded is deleted too.
pub fn run(
    config: &Config,
    offer: &Offer,
    autostart: bool,
    report: Report,
    cancel: &AtomicBool,
) -> anyhow::Result<Vec<String>> {
    let dir = paths::updates_dir();
    std::fs::create_dir_all(&dir)?;

    report("Looking for FFmpeg…", 0.0, true);
    let ffmpeg_asset = ffmpeg_to_download(config);

    let result = download_and_install(
        config,
        offer,
        autostart,
        report,
        cancel,
        &dir,
        ffmpeg_asset.as_ref(),
    );
    if result.as_ref().is_err_and(install::is_cancelled) {
        let package = match &offer.source {
            Source::Release { package, .. } => Some(package),
            Source::File(_) => None,
        };
        for asset in package.into_iter().chain(&ffmpeg_asset) {
            let path = dir.join(&asset.name);
            if path.is_file() {
                info!("Deleting {}", path.display());
                let _ = std::fs::remove_file(path);
            }
        }
    }
    result
}

fn download_and_install(
    config: &Config,
    offer: &Offer,
    autostart: bool,
    report: Report,
    cancel: &AtomicBool,
    dir: &Path,
    ffmpeg_asset: Option<&Asset>,
) -> anyhow::Result<Vec<String>> {
    let package_bytes = match &offer.source {
        Source::Release { package, .. } => package.size,
        Source::File(_) => 0,
    };
    let ffmpeg_bytes = ffmpeg_asset.map_or(0, |a| a.size);
    let total = package_bytes + ffmpeg_bytes;
    let share = if total > 0 { DOWNLOAD_SHARE } else { 0.0 };

    let (path, downloaded) = match &offer.source {
        Source::File(path) => (path.clone(), false),
        Source::Release { package } => {
            let github = GitHub::new(&config.repo, config.token.clone());
            let label = format!("Downloading Popcorn {}", offer.version);
            let progress = download_progress(&label, 0, total, share, report);
            (
                download_checked(&github, package, package.sha256(), dir, &progress, cancel)?,
                true,
            )
        }
    };

    let mut ffmpeg_zip = None;
    let ffmpeg_source = match ffmpeg_asset {
        Some(asset) => {
            let github = GitHub::new(ffmpeg::REPO, config.token.clone());
            let progress =
                download_progress("Downloading FFmpeg", package_bytes, total, share, report);
            match download_checked(&github, asset, asset.sha256(), dir, &progress, cancel) {
                Ok(path) => {
                    ffmpeg_zip = Some(path.clone());
                    ffmpeg::Source::Archive {
                        path,
                        id: ffmpeg::id(asset),
                    }
                }
                Err(e) if install::is_cancelled(&e) => return Err(e),
                Err(e) => {
                    warning!("FFmpeg download failed: {e:#}; keeping the installed one");
                    ffmpeg::Source::Current
                }
            }
        }
        None => ffmpeg::Source::Current,
    };

    let package = Package::open(&path)?;
    let install_report = |step: &str, f: f32, cancellable: bool| {
        report(step, share + (1.0 - share) * f, cancellable)
    };
    let warnings = install::install(
        &config.install_dir,
        &package,
        &ffmpeg_source,
        config.restart,
        autostart,
        &install_report,
        cancel,
    )?;
    if downloaded {
        let _ = std::fs::remove_file(&path);
    }
    if let Some(zip) = ffmpeg_zip {
        let _ = std::fs::remove_file(zip);
    }
    Ok(warnings)
}

/// The FFmpeg build to download; None = keep the installed files (same build, or GitHub not reachable).
fn ffmpeg_to_download(config: &Config) -> Option<Asset> {
    let github = GitHub::new(ffmpeg::REPO, config.token.clone());
    match ffmpeg::latest(&github) {
        Ok(asset) if ffmpeg::installed_id(&config.install_dir) == Some(ffmpeg::id(&asset)) => {
            info!("FFmpeg {} is installed already", asset.name);
            None
        }
        Ok(asset) => {
            info!("FFmpeg: {} ({} bytes)", asset.name, asset.size);
            Some(asset)
        }
        Err(e) => {
            warning!("Cannot look up FFmpeg: {e:#}; keeping the installed one");
            None
        }
    }
}

/// Downloads `asset` into `dir`, checked against `expected` (SHA-256 hex: the digest GitHub shows for the asset) when
/// there is one.
fn download_checked(
    github: &GitHub,
    asset: &Asset,
    expected: Option<String>,
    dir: &Path,
    progress: &dyn Fn(u64, u64),
    cancel: &AtomicBool,
) -> anyhow::Result<PathBuf> {
    let path = dir.join(&asset.name);
    // Same file from an earlier, interrupted attempt.
    if let Some(expected) = &expected
        && path.is_file()
        && github::sha256_file(&path).is_ok_and(|hash| &hash == expected)
    {
        info!("Using the already downloaded {}", path.display());
        progress(asset.size, asset.size);
        return Ok(path);
    }

    info!("Downloading {} ({} bytes)", asset.name, asset.size);
    let hash = github.download(asset, &path, progress, cancel)?;
    if let Some(expected) = expected
        && hash != expected
    {
        let _ = std::fs::remove_file(&path);
        bail!(
            "{} is damaged: SHA-256 {hash}, expected {expected}",
            asset.name
        );
    }
    Ok(path)
}

/// Progress callback of one download: `<label>… 12.3 of 45.6 MB, 5.4 MB/s`, the bar over all downloads
/// (`offset` = bytes of the downloads before this one).
fn download_progress<'a>(
    label: &'a str,
    offset: u64,
    total: u64,
    share: f32,
    report: Report<'a>,
) -> impl Fn(u64, u64) + 'a {
    let started = Instant::now();
    let sample = Cell::new((started, 0u64));
    let speed = Cell::new(None::<f64>);
    move |done: u64, size: u64| {
        // Smoothed over half-second samples, so the number can be read.
        let (at, bytes) = sample.get();
        let elapsed = at.elapsed().as_secs_f64();
        if elapsed >= 0.5 {
            let now = (done - bytes.min(done)) as f64 / elapsed;
            speed.set(Some(speed.get().map_or(now, |s| 0.6 * s + 0.4 * now)));
            sample.set((Instant::now(), done));
        }
        let rate = speed
            .get()
            .map(|s| format!(", {}/s", megabytes(s as u64)))
            .unwrap_or_default();
        let step = format!("{label}… {} of {}{rate}", megabytes(done), megabytes(size));
        report(
            &step,
            share * (offset + done) as f32 / total.max(1) as f32,
            true,
        );
    }
}

/// Semver when both parse, else "different = newer" so odd tags still get offered.
fn is_newer(candidate: &str, installed: &str) -> bool {
    match (
        semver::Version::parse(candidate),
        semver::Version::parse(installed),
    ) {
        (Ok(candidate), Ok(installed)) => candidate > installed,
        _ => candidate != installed,
    }
}

pub fn megabytes(bytes: u64) -> String {
    format!("{:.1} MB", bytes as f64 / 1_000_000.0)
}
