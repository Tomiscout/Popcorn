//! Bundle/<platform>: every app of the platform in the layout Popcorn.Installer installs, plus release.json.

use std::collections::HashMap;
use std::fs;
use std::path::Path;

use anyhow::{Context as _, Result, bail};

use crate::build::{self, Context};
use crate::util::{bundle_dir, cmd, copy_dir, copy_file, files, output, publish_dir, release_platform, remove_dir, step, warn};

pub fn bundle(ctx: &Context) -> Result<()> {
    build::build(ctx, &[])?;
    step("Bundle");
    let dir = bundle_dir();
    remove_dir(&dir)?;
    assemble(ctx, &dir)?;
    write_manifest(&dir, &ctx.version)?;
    verify(&dir, &ctx.version)?;
    println!("Bundle {} in {}", ctx.version, dir.display());
    Ok(())
}

/// Apps published into one folder share their dependencies' files. Each copy must be the same: the same file, or the
/// same NuGet package version (ReadyToRun compiles a package's DLL per app, so the bytes can differ). Anything else
/// (a project DLL from a stale build, two package versions) breaks one of the apps at run time.
fn check_shared(dirs: &[&Path]) -> Result<()> {
    let owners: Vec<HashMap<String, String>> = dirs.iter().map(|d| package_files(d)).collect::<Result<_>>()?;
    let mut conflicts = Vec::new();
    for (i, a) in dirs.iter().enumerate() {
        for (j, b) in dirs.iter().enumerate().skip(i + 1) {
            for file in files(a)? {
                let other = b.join(&file);
                if !other.exists() || fs::read(a.join(&file))? == fs::read(&other)? {
                    continue;
                }
                let name = file.file_name().unwrap().to_string_lossy().into_owned();
                let (owner_a, owner_b) = (owners[i].get(&name), owners[j].get(&name));
                if owner_a.is_some() && owner_a == owner_b {
                    continue;
                }
                let what = |o: Option<&String>| o.map_or("not a package".to_string(), |o| o.clone());
                conflicts.push(format!(
                    "  {}: {} ({}) vs {} ({})",
                    file.display(),
                    a.file_name().unwrap().to_string_lossy(),
                    what(owner_a),
                    b.file_name().unwrap().to_string_lossy(),
                    what(owner_b)
                ));
            }
        }
    }
    if !conflicts.is_empty() {
        bail!("apps bundled into one folder ship different copies of the same files:\n{}", conflicts.join("\n"));
    }
    Ok(())
}

/// File name -> "Package/version" of every NuGet package file in a publish folder (from its *.deps.json).
fn package_files(dir: &Path) -> Result<HashMap<String, String>> {
    let mut map = HashMap::new();
    for entry in fs::read_dir(dir)? {
        let path = entry?.path();
        if !path.to_string_lossy().ends_with(".deps.json") {
            continue;
        }
        let deps: serde_json::Value =
            serde_json::from_slice(&fs::read(&path)?).with_context(|| format!("reading {}", path.display()))?;
        let libraries = &deps["libraries"];
        for target in deps["targets"].as_object().into_iter().flat_map(|t| t.values()) {
            for (library, assets) in target.as_object().into_iter().flatten() {
                if libraries[library]["type"] != "package" {
                    continue;
                }
                for kind in ["runtime", "native", "resources"] {
                    for asset in assets[kind].as_object().into_iter().flat_map(|a| a.keys()) {
                        let name = asset.rsplit('/').next().unwrap_or(asset);
                        map.insert(name.to_string(), library.clone());
                    }
                }
            }
        }
    }
    Ok(map)
}

/// release.json: the installed version for Popcorn.Installer, and the marker of a package zip's root.
fn write_manifest(dir: &Path, version: &str) -> Result<()> {
    let commit = output(cmd("git").args(["rev-parse", "--short", "HEAD"])).unwrap_or_default();
    let string = |s: &str| serde_json::Value::from(s).to_string();
    let manifest = format!(
        "{{\n  \"version\": {},\n  \"platform\": {},\n  \"commit\": {},\n  \"date\": {}\n}}\n",
        string(version),
        string(release_platform()),
        string(&commit),
        string(&crate::util::utc_now())
    );
    fs::write(dir.join("release.json"), manifest)?;
    Ok(())
}

/// Checks a bundle is what the installer expects to install: its release.json and the files every install needs.
pub fn verify(dir: &Path, version: &str) -> Result<()> {
    let manifest_path = dir.join("release.json");
    let manifest: serde_json::Value = serde_json::from_slice(
        &fs::read(&manifest_path).with_context(|| format!("{} missing, run `cargo xtask bundle`", manifest_path.display()))?,
    )?;
    if manifest["version"] != version || manifest["platform"] != release_platform() {
        bail!(
            "{} is {} {}, expected {version} {} (rebuild, or drop --skip-build)",
            manifest_path.display(),
            manifest["version"],
            manifest["platform"],
            release_platform()
        );
    }
    for file in platform::REQUIRED {
        // Without the installer in it, installs of this release could not update themselves any more.
        let path = dir.join(file);
        if !path.exists() {
            bail!("{file} missing from {}", dir.display());
        }
        #[cfg(unix)]
        {
            use std::os::unix::fs::PermissionsExt;
            if platform::EXECUTABLES.contains(file) && fs::metadata(&path)?.permissions().mode() & 0o111 == 0 {
                bail!("{} is not executable", path.display());
            }
        }
    }
    platform::warnings(dir);
    Ok(())
}

#[cfg(target_os = "linux")]
mod platform {
    use super::*;
    use crate::util::{cargo_release, path};
    use std::path::PathBuf;

    pub const REQUIRED: &[&str] = &[
        "Popcorn.Server",
        "Popcorn.Linux",
        "Popcorn.Player.Linux",
        "Popcorn.Installer.Linux",
        "popcorn-cli",
        "libpopcorn_player.so",
        "popcorn-player-cef",
        "libcef.so",
        "setup/popcorn.service",
        "setup/popcorn.env",
        "setup/popcorn.desktop",
    ];
    pub const EXECUTABLES: &[&str] =
        &["Popcorn.Server", "Popcorn.Linux", "Popcorn.Player.Linux", "Popcorn.Installer.Linux", "popcorn-cli", "popcorn-player-cef"];
    const CEF_RUNTIME_FILES: &[&str] = &[
        "libcef.so",
        "chrome-sandbox",
        "chrome_100_percent.pak",
        "chrome_200_percent.pak",
        "resources.pak",
        "icudtl.dat",
        "v8_context_snapshot.bin",
        "libvk_swiftshader.so",
        "libvulkan.so.1",
        "vk_swiftshader_icd.json",
    ];

    /// The CEF binary distribution the cef crate downloaded: <CEF_PATH>/<CEF version>/cef_linux_<arch>, the CEF
    /// version being cef-dll-sys's build metadata (156.3.0+156.0.15 -> 156.0.15).
    fn cef_dir(ctx: &Context) -> Result<PathBuf> {
        let metadata: serde_json::Value = serde_json::from_str(&output(cmd("cargo").args(["metadata", "--format-version", "1"]))?)?;
        let version = metadata["packages"]
            .as_array()
            .into_iter()
            .flatten()
            .find(|p| p["name"] == "cef-dll-sys")
            .and_then(|p| p["version"].as_str())
            .context("cef-dll-sys not in cargo metadata")?;
        let cef_version = version.split_once('+').map_or(version, |(_, build)| build);
        let arch = output(cmd("uname").arg("-m"))?;
        let dir = ctx.cef_path.join(cef_version).join(format!("cef_linux_{arch}"));
        if !dir.join("libcef.so").exists() {
            bail!("no CEF runtime in {} (built with another CEF_PATH?)", dir.display());
        }
        Ok(dir)
    }

    pub fn assemble(ctx: &Context, dir: &Path) -> Result<()> {
        let player = publish_dir("Popcorn.Player.Linux");
        let server = publish_dir("Popcorn.Server");
        check_shared(&[&player, &server])?;
        copy_dir(&player, dir)?;
        copy_dir(&server, dir)?;
        copy_file(&cargo_release("popcorn-linux"), &dir.join("Popcorn.Linux"))?;
        copy_file(&cargo_release("libpopcorn_player.so"), &dir.join("libpopcorn_player.so"))?;
        copy_file(&cargo_release("popcorn-player-cef"), &dir.join("popcorn-player-cef"))?;
        // libmpv with the DMA-BUF render API, found through libpopcorn_player.so's $ORIGIN rpath
        let mpv = ctx.mpv_lib.clone().or_else(|| Some(crate::mpv::linux_lib()).filter(|l| l.exists()));
        if let Some(mpv) = mpv {
            copy_file(&mpv, &dir.join("libmpv.so.2"))?;
        }
        let cef = cef_dir(ctx)?;
        for file in CEF_RUNTIME_FILES {
            copy_file(&cef.join(file), &dir.join(file))?;
        }
        copy_dir(&cef.join("locales"), &dir.join("locales"))?;
        crate::util::run(cmd("strip").arg("--strip-unneeded").arg(dir.join("libcef.so")))?;
        copy_file(&build::installer_exe(), &dir.join("Popcorn.Installer.Linux"))?;
        copy_file(&cargo_release("popcorn-cli"), &dir.join("popcorn-cli"))?;
        copy_file(&path("Popcorn.Linux/popcorn.png"), &dir.join("popcorn.png"))?;
        // Templates Popcorn.Installer writes the systemd unit / env file / desktop entry from (the installed
        // version's, not the ones built into an older installer)
        for file in ["Popcorn.Server/systemd/popcorn.service", "Popcorn.Server/systemd/popcorn.env", "Popcorn.Linux/popcorn.desktop"] {
            let source = path(file);
            copy_file(&source, &dir.join("setup").join(source.file_name().unwrap()))?;
        }
        Ok(())
    }

    pub fn warnings(dir: &Path) {
        if !dir.join("libmpv.so.2").exists() {
            warn("no libmpv.so.2 in the bundle (`cargo xtask mpv`): the player uses the system libmpv, without DMA-BUF video");
        }
    }
}

#[cfg(windows)]
mod platform {
    //! <bundle>\Popcorn.Server.exe and Popcorn.Windows.exe side by side (the tray and the app find each other there),
    //! <bundle>\Player\Popcorn.Player.Windows.exe (WindowsPlayerService; the player's "Find file" looks one folder up).

    use super::*;
    use crate::util::cargo_release;

    pub const REQUIRED: &[&str] = &[
        "Popcorn.Server.exe",
        "Popcorn.Windows.exe",
        "Popcorn.Installer.exe",
        "popcorn-cli.exe",
        "Player/Popcorn.Player.Windows.exe",
        // The player cannot play anything without libmpv and has no window without its native layer
        "Player/libmpv-2.dll",
        "Player/popcorn_player.dll",
    ];
    #[allow(dead_code)] // only checked on Unix
    pub const EXECUTABLES: &[&str] = &[];

    pub fn assemble(_ctx: &Context, dir: &Path) -> Result<()> {
        let app = publish_dir("Popcorn.Windows");
        let server = publish_dir("Popcorn.Server");
        check_shared(&[&app, &server])?;
        copy_dir(&app, dir)?;
        copy_dir(&server, dir)?;
        // Includes popcorn_player.dll (its native layer, built with the player)
        copy_dir(&publish_dir("Popcorn.Player.Windows"), &dir.join("Player"))?;
        for file in files(&crate::mpv::windows_dir())? {
            if !file.to_string_lossy().ends_with(".stamp") {
                copy_file(&crate::mpv::windows_dir().join(&file), &dir.join("Player").join(&file))?;
            }
        }
        copy_file(&build::installer_exe(), &dir.join("Popcorn.Installer.exe"))?;
        copy_file(&cargo_release("popcorn-cli.exe"), &dir.join("popcorn-cli.exe"))?;
        Ok(())
    }

    /// Things the bundle needs at run time but does not contain: Popcorn.Installer downloads FFmpeg into the install
    /// folder, install_windows.ps1 installs need it on PATH.
    pub fn warnings(_dir: &Path) {
        for tool in ["ffmpeg", "ffprobe"] {
            if std::process::Command::new(tool).arg("-version").output().is_err() {
                warn(&format!("{tool} not found on PATH, the server needs it at run time (install_windows.ps1 installs)"));
            }
        }
    }
}

use platform::assemble;
