//! Popcorn.Web: the browser UI (dist/, served by Popcorn.Server) and the native players' overlay (dist-player/).

use std::fs;
use std::path::Path;

use anyhow::{Context, Result};

use crate::util::{cmd, path, run, sha256_hex, step};

/// `npm ci` in `dir`, only when its package-lock.json changed since the last one (a stamp in node_modules).
pub fn deps(dir: &Path) -> Result<()> {
    let lock = fs::read(dir.join("package-lock.json")).with_context(|| format!("reading {}/package-lock.json", dir.display()))?;
    let key = sha256_hex(&lock);
    let stamp = dir.join("node_modules/.popcorn-package-lock.sha256");
    if fs::read_to_string(&stamp).is_ok_and(|s| s == key) {
        return Ok(());
    }
    run(cmd("npm").arg("ci").current_dir(dir))?;
    fs::write(&stamp, key)?;
    Ok(())
}

/// Browser UI: type-check + vite build into Popcorn.Web/dist (linked into Popcorn.Server's output as web/).
pub fn ui() -> Result<()> {
    step("Popcorn.Web (UI)");
    let dir = path("Popcorn.Web");
    deps(&dir)?;
    run(cmd("npm").args(["run", "build"]).current_dir(&dir))
}

/// Player overlay into Popcorn.Web/dist-player (player.html renamed to index.html), linked into both players' output
/// as web-player/. No type check of its own: `ui` and `check` type-check the whole project.
pub fn player() -> Result<()> {
    step("Popcorn.Web (player overlay)");
    let dir = path("Popcorn.Web");
    deps(&dir)?;
    run(cmd("npm").args(["run", "build:player"]).current_dir(&dir))
}
