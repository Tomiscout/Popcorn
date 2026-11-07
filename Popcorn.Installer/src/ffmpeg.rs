//! FFmpeg for the server and the players (probing, thumbnails, segment audio): BtbN's builds
//! (github.com/BtbN/FFmpeg-Builds, release `latest`, rebuilt daily) of the newest release branch, master when there
//! is none. Windows: the shared GPL zip (`ffmpeg-n9.0-latest-win64-gpl-shared-9.0.zip`, exes + DLLs). Linux: the
//! static GPL tar.xz (`ffmpeg-n9.0-latest-linux64-gpl-9.0.tar.xz`; the shared one's binaries look for their libraries
//! in `../lib`). Its `bin/` goes next to Popcorn.Server, without ffplay. `ffmpeg-files.txt` lists what came from it (the DLL names change with FFmpeg
//! versions) and which build it was, so the next update can carry the same files over: when GitHub cannot be reached
//! (rate limit) or the build has not changed since.

use std::fs::File;
use std::io::{Read, Write};
use std::path::{Path, PathBuf};
use std::sync::atomic::{AtomicBool, Ordering};

use anyhow::{Context, bail};

use crate::github::{Asset, GitHub};

pub const REPO: &str = "BtbN/FFmpeg-Builds";
const TAG: &str = "latest";
pub const FILES: &str = "ffmpeg-files.txt";
const EXE: &str = std::env::consts::EXE_SUFFIX;

/// Name tokens of the build: target, licence, linking, archive type.
#[cfg(all(windows, target_arch = "aarch64"))]
const TARGET: &str = "winarm64";
#[cfg(all(windows, not(target_arch = "aarch64")))]
const TARGET: &str = "win64";
#[cfg(all(not(windows), target_arch = "aarch64"))]
const TARGET: &str = "linuxarm64";
#[cfg(all(not(windows), not(target_arch = "aarch64")))]
const TARGET: &str = "linux64";
const SHARED: bool = cfg!(windows);
const ARCHIVE: &str = if cfg!(windows) { ".zip" } else { ".tar.xz" };

/// `ffmpeg-n9.0-latest-linux64-gpl-9.0.tar.xz`: `<TARGET>-gpl`, `-shared` only when `SHARED`.
fn is_wanted(name: &str) -> bool {
    let name = name.to_lowercase();
    let Some(stem) = name.strip_suffix(ARCHIVE) else {
        return false;
    };
    let tokens: Vec<&str> = stem.split('-').collect();
    tokens.windows(2).any(|pair| pair == [TARGET, "gpl"]) && tokens.contains(&"shared") == SHARED
}

/// Where the new install's FFmpeg comes from.
pub enum Source {
    /// A downloaded build archive; `id` = `<asset name> <digest>` for the tracking file.
    Archive { path: PathBuf, id: String },
    /// The files `ffmpeg-files.txt` lists in the current install.
    Current,
}

/// The build to install: the newest release branch's shared GPL zip, else master's.
pub fn latest(github: &GitHub) -> anyhow::Result<Asset> {
    let release = github.release(TAG)?;
    release
        .assets
        .iter()
        .filter(|a| is_wanted(&a.name))
        .max_by_key(|a| branch_version(&a.name))
        .cloned()
        .with_context(|| {
            format!(
                "github.com/{REPO} {TAG} has no {TARGET}-gpl{} build",
                if SHARED { "-shared" } else { "" }
            )
        })
}

/// `ffmpeg-n9.0-latest-…` → Some([9, 0]); master → None (ranks below every release branch).
fn branch_version(name: &str) -> Option<Vec<u32>> {
    let version = name.strip_prefix("ffmpeg-n")?.split('-').next()?;
    version.split('.').map(|part| part.parse().ok()).collect()
}

pub fn id(asset: &Asset) -> String {
    format!(
        "{} {}",
        asset.name,
        asset
            .digest
            .as_deref()
            .unwrap_or(asset.updated_at.as_deref().unwrap_or("-"))
    )
}

/// The build id recorded in `<dir>/ffmpeg-files.txt`.
pub fn installed_id(dir: &Path) -> Option<String> {
    let text = std::fs::read_to_string(dir.join(FILES)).ok()?;
    let first = text.lines().next()?;
    Some(first.strip_prefix('#')?.trim().to_string())
}

/// Puts FFmpeg into `dest` from `source`, from the current install (`current`) when a zip fails. Returns the files.
pub fn provide(
    source: &Source,
    current: &Path,
    dest: &Path,
    progress: &dyn Fn(f32),
    cancel: &AtomicBool,
) -> anyhow::Result<Vec<String>> {
    if let Source::Archive { path, id } = source {
        let unpacked = if cfg!(windows) {
            unpack_zip(path, dest, progress, cancel)
        } else {
            unpack_tar_xz(path, dest, progress, cancel)
        };
        match unpacked.and_then(|files| write_list(dest, id, &files).map(|()| files)) {
            Ok(files) => return Ok(files),
            Err(e) if crate::install::is_cancelled(&e) => return Err(e),
            Err(e) => crate::log::warning!(
                "Cannot unpack {}: {e:#}; keeping the installed FFmpeg",
                path.display()
            ),
        }
    }
    copy(current, dest)
}

/// File name of an archive entry that is taken: `<top folder>/bin/<name>`, not ffplay. Only file names are kept, so
/// nothing can land outside the target folder.
fn wanted_binary(entry_name: &str) -> Option<String> {
    let name = entry_name.replace('\\', "/");
    let parts: Vec<&str> = name.split('/').collect();
    match parts.as_slice() {
        [.., folder, file_name]
            if folder.eq_ignore_ascii_case("bin")
                && !file_name.is_empty()
                && *file_name != "."
                && *file_name != ".."
                && !file_name.eq_ignore_ascii_case(&format!("ffplay{EXE}")) =>
        {
            Some(file_name.to_string())
        }
        _ => None,
    }
}

fn check_has_ffmpeg(archive: &Path, files: &[String]) -> anyhow::Result<()> {
    let ffmpeg = format!("ffmpeg{EXE}");
    if !files.iter().any(|name| name.eq_ignore_ascii_case(&ffmpeg)) {
        bail!("{} has no bin/{ffmpeg}", archive.display());
    }
    Ok(())
}

/// `<top folder>/bin/*` of the build zip into `dest`, without ffplay.
fn unpack_zip(
    zip_path: &Path,
    dest: &Path,
    progress: &dyn Fn(f32),
    cancel: &AtomicBool,
) -> anyhow::Result<Vec<String>> {
    let file = File::open(zip_path)?;
    let mut zip = zip::ZipArchive::new(file)
        .with_context(|| format!("{} is not a zip file", zip_path.display()))?;
    let mut binaries = Vec::new();
    for i in 0..zip.len() {
        let entry = zip.by_index_raw(i)?;
        if entry.is_dir() {
            continue;
        }
        if let Some(file_name) = wanted_binary(&entry.name()?) {
            binaries.push((i, file_name, entry.size()));
        }
    }
    let names: Vec<String> = binaries.iter().map(|(_, name, _)| name.clone()).collect();
    check_has_ffmpeg(zip_path, &names)?;

    let total = binaries.iter().map(|(_, _, size)| size).sum::<u64>().max(1);
    let mut done = 0u64;
    let mut buffer = vec![0u8; 256 * 1024];
    for (index, file_name, _) in &binaries {
        let mut entry = zip.by_index(*index)?;
        let target = dest.join(file_name);
        let mut out =
            File::create(&target).with_context(|| format!("Cannot create {}", target.display()))?;
        loop {
            if cancel.load(Ordering::Relaxed) {
                bail!(crate::install::Cancelled);
            }
            let read = entry.read(&mut buffer)?;
            if read == 0 {
                break;
            }
            out.write_all(&buffer[..read])?;
            done += read as u64;
            progress(done as f32 / total as f32);
        }
    }
    Ok(names)
}

/// `<top folder>/bin/*` of the build tar.xz into `dest`, without ffplay, executable. Read as a stream (there is no
/// index), so `progress` follows the compressed bytes read.
#[cfg(target_os = "linux")]
fn unpack_tar_xz(
    archive: &Path,
    dest: &Path,
    progress: &dyn Fn(f32),
    cancel: &AtomicBool,
) -> anyhow::Result<Vec<String>> {
    use std::os::unix::fs::PermissionsExt;

    /// Counts what the decompressor read of the file.
    struct Counting<'a, R> {
        inner: R,
        read: u64,
        total: u64,
        progress: &'a dyn Fn(f32),
    }
    impl<R: Read> Read for Counting<'_, R> {
        fn read(&mut self, buf: &mut [u8]) -> std::io::Result<usize> {
            let n = self.inner.read(buf)?;
            self.read += n as u64;
            (self.progress)(self.read as f32 / self.total.max(1) as f32);
            Ok(n)
        }
    }

    let file = File::open(archive)?;
    let total = file.metadata()?.len();
    let counting = Counting {
        inner: std::io::BufReader::with_capacity(256 * 1024, file),
        read: 0,
        total,
        progress,
    };
    let xz = lzma_rust2::XzReader::new(counting, true);
    let mut tar = tar::Archive::new(xz);
    let mut files = Vec::new();
    let mut buffer = vec![0u8; 256 * 1024];
    for entry in tar
        .entries()
        .with_context(|| format!("{} is not a tar.xz file", archive.display()))?
    {
        let mut entry = entry.with_context(|| format!("Cannot read {}", archive.display()))?;
        if !entry.header().entry_type().is_file() {
            continue;
        }
        let path = entry.path()?.to_string_lossy().into_owned();
        let Some(file_name) = wanted_binary(&path) else {
            continue;
        };
        let target = dest.join(&file_name);
        let mut out =
            File::create(&target).with_context(|| format!("Cannot create {}", target.display()))?;
        loop {
            if cancel.load(Ordering::Relaxed) {
                bail!(crate::install::Cancelled);
            }
            let read = entry.read(&mut buffer)?;
            if read == 0 {
                break;
            }
            out.write_all(&buffer[..read])?;
        }
        out.set_permissions(std::fs::Permissions::from_mode(0o755))?;
        files.push(file_name);
    }
    check_has_ffmpeg(archive, &files)?;
    Ok(files)
}

#[cfg(not(target_os = "linux"))]
fn unpack_tar_xz(
    archive: &Path,
    _dest: &Path,
    _progress: &dyn Fn(f32),
    _cancel: &AtomicBool,
) -> anyhow::Result<Vec<String>> {
    bail!("Cannot unpack {} on this platform", archive.display())
}

/// The listed files of the install in `from`, with their list.
fn copy(from: &Path, dest: &Path) -> anyhow::Result<Vec<String>> {
    let Ok(text) = std::fs::read_to_string(from.join(FILES)) else {
        bail!("no FFmpeg in {} yet ({FILES} missing)", from.display());
    };
    let files: Vec<String> = text
        .lines()
        .map(str::trim)
        .filter(|line| !line.is_empty() && !line.starts_with('#'))
        // Plain names only: the list is a file anyone can edit.
        .filter(|line| !line.contains(['/', '\\', ':']) && *line != "." && *line != "..")
        .map(str::to_string)
        .collect();
    for file in &files {
        std::fs::copy(from.join(file), dest.join(file))
            .with_context(|| format!("Cannot copy {file} from {}", from.display()))?;
    }
    std::fs::copy(from.join(FILES), dest.join(FILES))?;
    Ok(files)
}

fn write_list(dest: &Path, id: &str, files: &[String]) -> anyhow::Result<()> {
    let mut text = format!("# {id}\n");
    for file in files {
        text.push_str(file);
        text.push('\n');
    }
    std::fs::write(dest.join(FILES), text)
        .with_context(|| format!("Cannot write {}", dest.join(FILES).display()))
}

#[cfg(test)]
mod tests {
    use super::{branch_version, is_wanted, wanted_binary};

    #[test]
    fn picks_the_platform_build() {
        let names = [
            "ffmpeg-master-latest-linux64-gpl.tar.xz",
            "ffmpeg-n9.0-latest-linux64-gpl-9.0.tar.xz",
            "ffmpeg-n9.0-latest-linux64-gpl-shared-9.0.tar.xz",
            "ffmpeg-n9.0-latest-linux64-lgpl-9.0.tar.xz",
            "ffmpeg-n9.0-latest-linuxarm64-gpl-9.0.tar.xz",
            "ffmpeg-n9.0-latest-win64-gpl-shared-9.0.zip",
            "ffmpeg-n9.0-latest-win64-gpl-9.0.zip",
            "ffmpeg-n9.0-latest-win64-lgpl-shared-9.0.zip",
        ];
        let wanted: Vec<_> = names.iter().filter(|n| is_wanted(n)).collect();
        if cfg!(windows) {
            assert_eq!(wanted, [&"ffmpeg-n9.0-latest-win64-gpl-shared-9.0.zip"]);
        } else if cfg!(target_arch = "x86_64") {
            assert_eq!(
                wanted,
                [
                    &"ffmpeg-master-latest-linux64-gpl.tar.xz",
                    &"ffmpeg-n9.0-latest-linux64-gpl-9.0.tar.xz"
                ]
            );
        }
    }

    #[test]
    fn takes_bin_files_without_ffplay() {
        let exe = std::env::consts::EXE_SUFFIX;
        assert_eq!(
            wanted_binary(&format!("ffmpeg-n9.0/bin/ffprobe{exe}")),
            Some(format!("ffprobe{exe}"))
        );
        assert_eq!(wanted_binary(&format!("ffmpeg-n9.0/bin/ffplay{exe}")), None);
        assert_eq!(wanted_binary("ffmpeg-n9.0/doc/ffmpeg.html"), None);
    }

    #[test]
    fn newest_branch_wins_over_master() {
        let mut names = [
            "ffmpeg-master-latest-win64-gpl-shared.zip",
            "ffmpeg-n9.0-latest-win64-gpl-shared-9.0.zip",
            "ffmpeg-n8.1-latest-win64-gpl-shared-8.1.zip",
        ];
        names.sort_by_key(|n| branch_version(n));
        assert_eq!(names[2], "ffmpeg-n9.0-latest-win64-gpl-shared-9.0.zip");
        assert_eq!(names[0], "ffmpeg-master-latest-win64-gpl-shared.zip");
    }
}
