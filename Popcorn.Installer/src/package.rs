//! Release packages: a zip of the bundle folder (`cargo xtask release`) with `release.json` at its
//! root. A zip with the bundle inside one top folder works too (made by hand). On Linux the zip's Unix modes are
//! applied (executables), so it has to be made by a tool that stores them (`cargo xtask release` stores them).

use std::fs::File;
use std::io::{Read, Write};
use std::path::{Component, Path, PathBuf};
use std::sync::atomic::{AtomicBool, Ordering};

use anyhow::{Context, bail};
use serde::Deserialize;

use crate::platform;

pub const MANIFEST: &str = "release.json";

/// `release.json`, written into the bundle by `cargo xtask bundle`; the installed one says which version is
/// installed.
#[derive(Clone, Debug, Deserialize)]
pub struct Manifest {
    pub version: String,
    pub platform: String,
    #[serde(default)]
    pub commit: Option<String>,
}

impl Manifest {
    fn parse(text: &str) -> anyhow::Result<Self> {
        // PowerShell 5.1 likes to write a BOM.
        Ok(serde_json::from_str(text.trim_start_matches('\u{feff}'))?)
    }

    pub fn describe(&self) -> String {
        match &self.commit {
            Some(commit) if !commit.is_empty() => format!("{} ({commit})", self.version),
            _ => self.version.clone(),
        }
    }
}

/// The manifest of the installed bundle; None for bundles from before release.json.
pub fn installed(install_dir: &Path) -> Option<Manifest> {
    let text = std::fs::read_to_string(install_dir.join(MANIFEST)).ok()?;
    Manifest::parse(&text).ok()
}

pub struct Package {
    pub path: PathBuf,
    pub manifest: Manifest,
    /// Folder inside the zip that holds the bundle ("" = its root).
    prefix: String,
}

impl Package {
    pub fn open(path: &Path) -> anyhow::Result<Self> {
        let mut zip = open_zip(path)?;
        let manifest_name = zip
            .file_names()
            .filter_map(Result::ok)
            .filter(|name| {
                let name = name.replace('\\', "/");
                name == MANIFEST || name.ends_with(&format!("/{MANIFEST}"))
            })
            .min_by_key(|name| name.len())
            .map(|name| name.into_owned())
            .with_context(|| {
                format!(
                    "{} has no {MANIFEST}, not a Popcorn package",
                    path.display()
                )
            })?;
        let prefix =
            manifest_name.replace('\\', "/")[..manifest_name.len() - MANIFEST.len()].to_string();

        let mut text = String::new();
        zip.by_name(&manifest_name)?.read_to_string(&mut text)?;
        let manifest =
            Manifest::parse(&text).with_context(|| format!("{MANIFEST} in {}", path.display()))?;
        if manifest.platform != platform::PLATFORM {
            bail!(
                "{} is a {} package, this is {}",
                path.display(),
                manifest.platform,
                platform::PLATFORM
            );
        }
        Ok(Self {
            path: path.to_path_buf(),
            manifest,
            prefix,
        })
    }

    /// Unpacks the bundle into `dest` (must not exist). `progress` gets 0..1 by uncompressed bytes.
    pub fn extract(
        &self,
        dest: &Path,
        progress: &dyn Fn(f32),
        cancel: &AtomicBool,
    ) -> anyhow::Result<()> {
        let mut zip = open_zip(&self.path)?;
        let total: u64 = (0..zip.len())
            .filter_map(|i| zip.by_index_raw(i).ok().map(|entry| entry.size()))
            .sum::<u64>()
            .max(1);
        let mut done = 0u64;
        let mut buffer = vec![0u8; 256 * 1024];

        std::fs::create_dir_all(dest)?;
        for i in 0..zip.len() {
            if cancel.load(Ordering::Relaxed) {
                bail!(crate::install::Cancelled);
            }
            let mut entry = zip.by_index(i)?;
            let name = entry.name()?.replace('\\', "/");
            let Some(relative) = name.strip_prefix(&self.prefix).filter(|r| !r.is_empty()) else {
                continue;
            };
            let relative =
                safe_relative(relative).with_context(|| format!("Bad path in package: {name}"))?;
            let target = dest.join(relative);

            if entry.is_dir() {
                std::fs::create_dir_all(&target)?;
                continue;
            }
            if let Some(parent) = target.parent() {
                std::fs::create_dir_all(parent)?;
            }
            let mut file = File::create(&target)
                .with_context(|| format!("Cannot create {}", target.display()))?;
            loop {
                let read = entry
                    .read(&mut buffer)
                    .with_context(|| format!("Cannot unpack {name}"))?;
                if read == 0 {
                    break;
                }
                file.write_all(&buffer[..read])?;
                done += read as u64;
                progress(done as f32 / total as f32);
            }
            #[cfg(unix)]
            if let Some(mode) = entry.unix_mode() {
                use std::os::unix::fs::PermissionsExt;
                // Permission bits only: no setuid/setgid from a download.
                file.set_permissions(std::fs::Permissions::from_mode(mode & 0o777))?;
            }
        }
        Ok(())
    }
}

fn open_zip(path: &Path) -> anyhow::Result<zip::ZipArchive<File>> {
    let file = File::open(path).with_context(|| format!("Cannot open {}", path.display()))?;
    zip::ZipArchive::new(file).with_context(|| format!("{} is not a zip file", path.display()))
}

/// Only plain relative paths: no `..`, roots or drive letters that would write outside the target folder.
fn safe_relative(path: &str) -> Option<PathBuf> {
    let path = Path::new(path.trim_end_matches('/'));
    path.components()
        .all(|c| matches!(c, Component::Normal(_) | Component::CurDir))
        .then(|| path.to_path_buf())
}
