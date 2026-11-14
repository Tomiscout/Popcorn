//! Popcorn's build (`cargo xtask <command>`): one implementation for Linux and Windows. It always builds the apps of
//! the platform it runs on; the platform differences live in `bundle.rs` and `mpv.rs`, behind `cfg(target_os)`.
//!
//! Everything is incremental (dotnet, cargo, vite; `npm ci` only when a package-lock.json changed, libmpv only when
//! its sources or build options changed); `clean` removes the outputs. Layout, shared with Docs/Building.md:
//!   Bundle/<Linux|Windows>      the bundle (what the installer installs)
//!   Bundle/Publish/<project>    dotnet publish output of each app
//!   Bundle/Release/<version>    release files

mod build;
mod bundle;
mod check;
mod dotnet;
mod mpv;
mod release;
mod util;
mod web;

use std::path::PathBuf;

use anyhow::Result;
use clap::{Parser, Subcommand};

#[derive(Parser)]
#[command(about = "Builds, bundles and releases Popcorn for the host platform")]
struct Cli {
    #[command(subcommand)]
    command: Command,
}

#[derive(clap::Args, Clone)]
pub struct BuildOptions {
    /// Product version (major.minor.patch[-prerelease]); default: the VERSION file
    #[arg(long)]
    pub version: Option<String>,
    /// Linux: CEF binary distribution cache, downloaded by the cef crate on first build (~1.5 GB)
    #[arg(long, env = "CEF_PATH")]
    pub cef_path: Option<PathBuf>,
    /// Linux: libmpv to bundle next to libpopcorn_player.so; default MPV/libmpv.so.2 when `mpv` has built it
    /// (else none: the player uses the system libmpv, without DMA-BUF video)
    #[arg(long, env = "MPV_LIB")]
    pub mpv_lib: Option<PathBuf>,
}

#[derive(Subcommand)]
enum Command {
    /// Build every app and assemble Bundle/<platform> (+ release.json)
    Bundle {
        #[command(flatten)]
        options: BuildOptions,
    },
    /// Build some apps only (default: all), without bundling
    Build {
        parts: Vec<build::Part>,
        #[command(flatten)]
        options: BuildOptions,
    },
    /// Build libmpv from MPV/src (Linux: MPV/libmpv.so.2 with the DMA-BUF render API, Windows: MPV/windows/libmpv-2.dll)
    /// when its sources or build options changed since the last build
    Mpv {
        /// Move the MPV/src submodule to the fork's latest master first
        #[arg(long)]
        update: bool,
    },
    /// Build every project, run the tests, type-check + lint the web UI, clippy the Rust workspace
    Check,
    /// Run the .NET and Rust tests
    Test,
    /// Bundle (unless --skip-build) and pack Bundle/Release/<version>/ for a GitHub release; --publish uploads it
    Release(release::Options),
    /// Remove the bundle, publish and web outputs (not target/: `cargo clean`)
    Clean,
}

fn main() {
    if let Err(e) = run() {
        eprintln!("error: {e:#}");
        std::process::exit(1);
    }
}

fn run() -> Result<()> {
    match Cli::parse().command {
        Command::Bundle { options } => bundle::bundle(&build::Context::new(&options)?),
        Command::Build { parts, options } => build::build(&build::Context::new(&options)?, &parts),
        Command::Mpv { update } => mpv::build(update),
        Command::Check => check::check(),
        Command::Test => check::test(),
        Command::Release(options) => release::release(&options),
        Command::Clean => util::clean(),
    }
}
