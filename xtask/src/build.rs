//! The apps of the host platform, each built (and the .NET ones published) on its own.

use std::path::PathBuf;

use anyhow::{Result, bail};
use clap::ValueEnum;

use crate::util::{cmd, path, run, step};
use crate::{BuildOptions, dotnet, mpv, web};

#[derive(Clone, Copy, PartialEq, Eq, PartialOrd, Ord, ValueEnum)]
pub enum Part {
    /// Popcorn.Web browser UI only (dist/)
    Web,
    /// Popcorn.Web player overlay only (dist-player/)
    PlayerWeb,
    /// Popcorn.Server, with the UI (built in between, so it compiles against the socket contract the server writes)
    Server,
    /// Desktop app: Popcorn.Linux (Rust + CEF) / Popcorn.Windows (WinForms + WebView2)
    Desktop,
    /// Native player with its overlay: Popcorn.Player.Native + Popcorn.Player.Linux / Popcorn.Player.Windows (+ its
    /// Rust layer, built by its .NET build); on Windows also libmpv
    Player,
    /// Popcorn.Installer
    Installer,
    /// popcorn-cli
    Cli,
}

pub struct Context {
    pub version: String,
    pub cef_path: PathBuf,
    /// Linux: libmpv to bundle, if any
    pub mpv_lib: Option<PathBuf>,
}

impl Context {
    pub fn new(options: &BuildOptions) -> Result<Context> {
        let version = crate::util::version(options.version.as_deref())?;
        let cef_path = options.cef_path.clone().unwrap_or_else(|| {
            let home = std::env::var_os("HOME").or_else(|| std::env::var_os("USERPROFILE")).unwrap_or_default();
            PathBuf::from(home).join(".cache/popcorn/cef")
        });
        if let Some(lib) = &options.mpv_lib
            && !lib.is_file()
        {
            bail!("--mpv-lib {} does not exist", lib.display());
        }
        println!("Version {version}");
        Ok(Context { version, cef_path, mpv_lib: options.mpv_lib.clone() })
    }

    /// `cargo build` with the CEF cache the Linux crates download into (Popcorn.Linux and Popcorn.Player.Native share
    /// one CEF build).
    pub fn cargo(&self, profile: &str, package: &str) -> Result<()> {
        step(package);
        run(cmd("cargo").args(["build", "--profile", profile, "--package", package]).env("CEF_PATH", &self.cef_path))
    }
}

/// `--profile installer`: its own profile (root Cargo.toml), output in target/installer/.
pub fn installer_exe() -> PathBuf {
    path("target/installer").join(crate::util::exe("popcorn-installer"))
}

pub fn build(ctx: &Context, parts: &[Part]) -> Result<()> {
    let mut parts = if parts.is_empty() { Part::value_variants().to_vec() } else { parts.to_vec() };
    parts.sort();
    parts.dedup();
    // Included in the bigger steps
    if parts.contains(&Part::Server) {
        parts.retain(|p| *p != Part::Web);
    }
    if parts.contains(&Part::Player) {
        parts.retain(|p| *p != Part::PlayerWeb);
    }
    for part in parts {
        match part {
            Part::Web => web::ui()?,
            Part::PlayerWeb => web::player()?,
            Part::Server => {
                // The server build writes Popcorn.Web/src/socket/socket-{requests,payloads}.ts; the UI is built
                // after it and published with the server.
                dotnet::build("Popcorn.Server", &ctx.version)?;
                web::ui()?;
                dotnet::publish("Popcorn.Server", &ctx.version)?;
            }
            Part::Desktop => desktop(ctx)?,
            Part::Player => player(ctx)?,
            Part::Installer => ctx.cargo("installer", "popcorn-installer")?,
            Part::Cli => ctx.cargo("release", "popcorn-cli")?,
        }
    }
    Ok(())
}

#[cfg(target_os = "linux")]
fn desktop(ctx: &Context) -> Result<()> {
    ctx.cargo("release", "popcorn-linux")
}

#[cfg(windows)]
fn desktop(ctx: &Context) -> Result<()> {
    dotnet::publish("Popcorn.Windows", &ctx.version).map(drop)
}

#[cfg(target_os = "linux")]
fn player(ctx: &Context) -> Result<()> {
    web::player()?;
    // A libmpv built by `mpv` is kept current: rebuilt when MPV/src or its build options changed
    if ctx.mpv_lib.is_none() && mpv::linux_lib().exists() {
        mpv::build(false)?;
    }
    ctx.cargo("release", "popcorn-player-native")?;
    dotnet::publish("Popcorn.Player.Linux", &ctx.version).map(drop)
}

#[cfg(windows)]
fn player(ctx: &Context) -> Result<()> {
    // Required on Windows (no system libmpv); the player's build links it into its output
    mpv::build(false)?;
    web::player()?;
    // Its build runs `cargo build --release -p popcorn-player-windows-native` (Popcorn.Player.Windows.csproj)
    dotnet::publish("Popcorn.Player.Windows", &ctx.version).map(drop)
}
