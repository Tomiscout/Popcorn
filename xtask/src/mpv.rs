//! libmpv from MPV/src (git submodule: the Tomiscout/mpv fork with the DMA-BUF render API).
//!
//! Rebuilt when what it was built from changes: a stamp next to the library holds the MPV/src commit and the build
//! inputs (Linux: libplacebo commit + the meson options' revision, Windows: MPV/build_windows.sh); a submodule with
//! local changes is always recompiled (incrementally). The build itself stays at the commit checked out in MPV/src:
//! a checkout that differs from the commit this repository pins only gets a warning (it may be on purpose).

use std::fs;
use std::path::PathBuf;

use anyhow::Result;

use crate::util::{cmd, output, path, read_stamp, run, step, warn};

const SUBMODULE: &str = "MPV/src";

fn ensure_submodule(update: bool) -> Result<()> {
    if update {
        run(cmd("git").args(["submodule", "update", "--init", "--remote", SUBMODULE]))?;
    } else if !path(SUBMODULE).join("meson.build").exists() {
        run(cmd("git").args(["submodule", "update", "--init", SUBMODULE]))?;
    } else {
        let pinned = output(cmd("git").args(["rev-parse", &format!("HEAD:{SUBMODULE}")]));
        let checked_out = output(cmd("git").args(["-C", SUBMODULE, "rev-parse", "HEAD"]));
        if let (Ok(pinned), Ok(checked_out)) = (pinned, checked_out)
            && pinned != checked_out
        {
            warn(&format!(
                "{SUBMODULE} is at {checked_out}, this repository pins {pinned}: building the checkout \
                 (`git submodule update {SUBMODULE}` to go back to the pinned commit)"
            ));
        }
    }
    Ok(())
}

/// Stamp text for the current MPV/src checkout plus the platform's build inputs; None when the checkout has local
/// changes (never up to date).
fn wanted_stamp(inputs: &str) -> Result<Option<String>> {
    let commit = output(cmd("git").args(["-C", SUBMODULE, "rev-parse", "HEAD"]))?;
    let changes = output(cmd("git").args(["-C", SUBMODULE, "status", "--porcelain", "--untracked-files=no"]))?;
    Ok(changes.is_empty().then(|| format!("mpv {commit}\n{inputs}")))
}

#[cfg(target_os = "linux")]
mod platform {
    use super::*;

    const LIBPLACEBO_URL: &str = "https://code.videolan.org/videolan/libplacebo.git";
    // libplacebo >= 7.387, newer than distros ship: built as a meson subproject inside MPV/src (gitignored there)
    const LIBPLACEBO_REF: &str = "c4de47cd94af6ce15573debe84306e01bca33996";
    const LIBPLACEBO_DIR: &str = "MPV/src/subprojects/libplacebo";
    const BUILD_DIR: &str = "MPV/build";
    // Bump when MESON_OPTIONS change: the next build reconfigures and rebuilds
    const OPTIONS_REVISION: u32 = 1;
    const MESON_OPTIONS: &[&str] = &[
        "-Dbuildtype=release",
        "-Dlibmpv=true",
        "-Dcplayer=false",
        "-Dvulkan=enabled",
        "-Dwayland=disabled",
        "-Dgl=enabled",
        "-Dlibplacebo:demos=false",
        "-Dlibplacebo:tests=false",
    ];

    pub fn lib() -> PathBuf {
        path("MPV/libmpv.so.2")
    }

    fn stamp() -> PathBuf {
        path("MPV/libmpv.so.2.stamp")
    }

    /// meson from PATH, else from a venv in ~/.cache/popcorn (created on first use)
    fn meson() -> Result<String> {
        if crate::util::has("meson") {
            return Ok("meson".into());
        }
        let venv = PathBuf::from(std::env::var_os("HOME").unwrap_or_default()).join(".cache/popcorn/meson-venv");
        let meson = venv.join("bin/meson");
        if !meson.exists() {
            run(cmd("python3").arg("-m").arg("venv").arg(&venv))?;
            run(cmd(&venv.join("bin/pip").to_string_lossy()).args(["install", "meson"]))?;
        }
        Ok(meson.to_string_lossy().into_owned())
    }

    pub fn build(update: bool) -> Result<()> {
        step("libmpv");
        ensure_submodule(update)?;
        let wanted = wanted_stamp(&format!("libplacebo {LIBPLACEBO_REF}\noptions {OPTIONS_REVISION}\n"))?;
        if lib().exists() && wanted.as_deref() == Some(read_stamp(&stamp()).as_str()) {
            println!("{} is up to date", lib().display());
            return Ok(());
        }
        if !path(LIBPLACEBO_DIR).exists() {
            run(cmd("git").args(["clone", LIBPLACEBO_URL, LIBPLACEBO_DIR]))?;
        }
        if output(cmd("git").args(["-C", LIBPLACEBO_DIR, "rev-parse", "HEAD"]))? != LIBPLACEBO_REF {
            run(cmd("git").args(["-C", LIBPLACEBO_DIR, "fetch", "origin", LIBPLACEBO_REF]))?;
            run(cmd("git").args(["-C", LIBPLACEBO_DIR, "checkout", "--detach", LIBPLACEBO_REF]))?;
            run(cmd("git").args(["-C", LIBPLACEBO_DIR, "submodule", "update", "--init", "--recursive"]))?;
        }
        let meson = meson()?;
        let mut setup = cmd(&meson);
        setup.arg("setup");
        if path(BUILD_DIR).join("build.ninja").exists() {
            setup.arg("--reconfigure");
        }
        run(setup.args([BUILD_DIR, SUBMODULE]).args(MESON_OPTIONS))?;
        run(cmd(&meson).args(["compile", "-C", BUILD_DIR]))?;
        // fs::copy follows the build's libmpv.so.2 -> libmpv.so.2.x symlink
        fs::copy(path(BUILD_DIR).join("libmpv.so.2"), lib())?;
        if let Some(wanted) = wanted {
            fs::write(stamp(), wanted)?;
        }
        println!("Built {}", lib().display());
        Ok(())
    }
}

#[cfg(windows)]
mod platform {
    //! One DLL with its dependencies linked in statically, built by MPV/build_windows.sh in a portable MSYS2 UCRT64
    //! (MPV/msys64: no install, no admin rights, nothing outside the repository).

    use super::*;
    use crate::util::{remove_dir, sha256_hex};
    use anyhow::bail;

    const MSYS2_URL: &str =
        "https://github.com/msys2/msys2-installer/releases/download/2026-09-27/msys2-base-x86_64-20260927.sfx.exe";
    const MSYS2_SHA256: &str = "ad336cccfda47758b5e15cda993fbba421115cb0b126697daef1ee4dfe37209f";
    const SCRIPT: &str = "MPV/build_windows.sh";

    pub fn out_dir() -> PathBuf {
        path("MPV/windows")
    }

    fn stamp() -> PathBuf {
        // build_windows.sh replaces MPV/windows, the stamp is written after it
        out_dir().join("libmpv-2.dll.stamp")
    }

    fn install_msys2() -> Result<()> {
        if path("MPV/msys64/usr/bin/bash.exe").exists() {
            return Ok(());
        }
        println!("Downloading MSYS2...");
        let archive = path("MPV/msys2-base.sfx.exe");
        // curl.exe comes with Windows 10 1803+
        run(cmd("curl.exe").args(["--fail", "--location", "--silent", "--show-error", "--output"]).arg(&archive).arg(MSYS2_URL))?;
        if sha256_hex(&fs::read(&archive)?) != MSYS2_SHA256 {
            fs::remove_file(&archive)?;
            bail!("MSYS2 download does not match its checksum: {MSYS2_URL}");
        }
        run(cmd(&archive.to_string_lossy()).arg("-y").arg(format!("-o{}", path("MPV").display())))?;
        fs::remove_file(&archive)?;
        Ok(())
    }

    /// A command in the MSYS2 UCRT64 login shell, in the repository root. The environment is the child's only.
    fn msys2(command: &str) -> std::process::Command {
        let mut bash = cmd(&path("MPV/msys64/usr/bin/bash.exe").to_string_lossy());
        bash.args(["-lc", command]).env("MSYSTEM", "UCRT64").env("CHERE_INVOKING", "1").env("MSYS2_PATH_TYPE", "minimal");
        bash
    }

    pub fn build(update: bool) -> Result<()> {
        step("libmpv");
        ensure_submodule(update)?;
        let script = sha256_hex(&fs::read(path(SCRIPT))?);
        let wanted = wanted_stamp(&format!("script {script}\n"))?;
        let previous = read_stamp(&stamp());
        let dll = out_dir().join("libmpv-2.dll");
        if dll.exists() && wanted.as_deref() == Some(previous.as_str()) {
            println!("{} is up to date", dll.display());
            return Ok(());
        }
        // build_windows.sh only configures a new build folder: a changed script (meson options) needs a fresh one.
        // Its dependency builds have their own revision (DEPS_REVISION).
        if !previous.contains(&format!("script {script}\n")) {
            remove_dir(&path("MPV/build-windows"))?;
        }
        install_msys2()?;
        // The first update of a new MSYS2 replaces its runtime and ends the shell on purpose; build_windows.sh
        // continues it.
        let _ = msys2("pacman -Syu --noconfirm").status();
        run(&mut msys2(&format!("./{SCRIPT}")))?;
        if !dll.exists() {
            bail!("{SCRIPT} finished without {}, see its output above", dll.display());
        }
        if let Some(wanted) = wanted {
            fs::write(stamp(), wanted)?;
        }
        Ok(())
    }
}

pub use platform::build;

#[cfg(target_os = "linux")]
pub fn linux_lib() -> PathBuf {
    platform::lib()
}

#[cfg(windows)]
pub fn windows_dir() -> PathBuf {
    platform::out_dir()
}
