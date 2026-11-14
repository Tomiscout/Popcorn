//! Popcorn desktop shell for Linux.
//!
//! A thin CEF window that shows the web UI served by `Popcorn.Server`. All application
//! logic (library, native dialogs, tray, player launching) lives in the server; this binary
//! only finds/starts the server and displays it.
//!
//! The same executable is re-launched by CEF for its helper processes (renderer, GPU, ...),
//! so `main` must hand over to CEF before doing anything else.

#[cfg(target_os = "linux")]
mod browser;
#[cfg(target_os = "linux")]
mod log;
#[cfg(target_os = "linux")]
mod server;

#[cfg(target_os = "linux")]
use std::path::PathBuf;
#[cfg(target_os = "linux")]
use std::time::Instant;

#[cfg(target_os = "linux")]
use cef::{CefString, ImplCommandLine, api_hash, args::Args, execute_process, sys};

#[cfg(target_os = "linux")]
fn main() {
    let started = Instant::now();

    let args = Args::new();
    let _ = api_hash(sys::CEF_API_VERSION_LAST, 0);
    let Some(cmd_line) = args.as_cmd_line() else {
        eprintln!("Failed to parse command line");
        std::process::exit(1);
    };

    // Helper processes carry `--type=<renderer|gpu-process|...>`; CEF runs them to completion here.
    let is_browser_process = cmd_line.has_switch(Some(&CefString::from("type"))) != 1;
    let exit_code = execute_process(Some(args.as_main_args()), None, std::ptr::null_mut());
    if !is_browser_process {
        std::process::exit(exit_code.max(0));
    }

    let data_dir = data_dir();
    log::init(&data_dir.join("logs"));
    log::info!("Popcorn.Linux {} starting", env!("CARGO_PKG_VERSION"));

    browser::set_requested_page(&cmd_line);
    let target = server::Target::from_env();
    let code = browser::run(&args, target, &data_dir, started);
    log::info!("Popcorn.Linux finished (exit code {code})");
    std::process::exit(code);
}

#[cfg(target_os = "linux")]
/// Same folder as .NET's `LocalApplicationData/Popcorn` used by the server (`~/.local/share/Popcorn`).
fn data_dir() -> PathBuf {
    std::env::var_os("XDG_DATA_HOME")
        .map(PathBuf::from)
        .filter(|p| p.is_absolute())
        .or_else(|| std::env::var_os("HOME").map(|home| PathBuf::from(home).join(".local/share")))
        .unwrap_or_else(std::env::temp_dir)
        .join("Popcorn")
}

// Workspace member on every OS (`cargo build --workspace`), the shell itself is Linux only.
#[cfg(not(target_os = "linux"))]
fn main() {
    eprintln!("popcorn-linux only runs on Linux");
    std::process::exit(1);
}
