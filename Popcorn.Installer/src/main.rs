//! Popcorn.Installer: a small egui window that installs Popcorn, updates an installed Popcorn bundle to a newer
//! GitHub release (or to a package zip given on the command line, for testing) and uninstalls it. `--update-check`
//! only prints whether there is an update. See README.md for the steps.
// Release builds are GUI programs (no console window); debug builds keep the console for the log.
#![cfg_attr(not(debug_assertions), windows_subsystem = "windows")]

mod args;
mod ffmpeg;
mod github;
mod install;
mod log;
mod package;
mod paths;
mod platform;
mod popcorn;
mod ui;
mod uninstall;
mod update;

use std::path::{Path, PathBuf};
use std::process::{Command, ExitCode};

use args::Args;
use log::{error, info, warning};

/// Name prefix of `--uninstall`'s copy in %TEMP% (deletes itself afterwards).
const UNINSTALL_COPY: &str = "Popcorn.Installer-uninstall-";

fn main() -> ExitCode {
    // Paths are made absolute right away: the working directory changes before they are used.
    let args = match Args::parse(std::env::args_os().skip(1)).and_then(Args::absolute) {
        Ok(args) => args,
        Err(e) => {
            platform::show_text(&format!("{e}\n\n{}", args::USAGE), true);
            return ExitCode::from(2);
        }
    };
    if args.help {
        platform::show_text(args::USAGE, false);
        return ExitCode::SUCCESS;
    }

    // Uninstalling deletes the data folder, log folder included.
    log::init(&if args.uninstall {
        std::env::temp_dir()
    } else {
        paths::logs_dir()
    });
    info!(
        "Popcorn.Installer {} {:?}",
        env!("CARGO_PKG_VERSION"),
        std::env::args().skip(1).collect::<Vec<_>>()
    );

    let install_dir = args
        .install_dir
        .clone()
        .unwrap_or_else(paths::default_install_dir);
    let config = update::Config {
        install_dir: install_dir.clone(),
        package: args.package.clone(),
        repo: args.repo.clone().unwrap_or_else(github::default_repo),
        token: github::token_from_env(),
        prerelease: args.prerelease,
        force: args.force,
        confirm: args.confirm || args.from_app,
        from_app: args.from_app,
        restart: !args.no_restart,
        autostart: !args.no_autostart,
        uninstall: args.uninstall,
        keep_data: args.keep_data,
    };

    if args.update_check {
        return update_check(&config);
    }

    if !args.no_relocate {
        match relocate(&args, &install_dir) {
            Ok(true) => return ExitCode::SUCCESS,
            Ok(false) => {}
            Err(e) => warning!(
                "Cannot run from a copy outside {}, staying in place: {e:#}",
                install_dir.display()
            ),
        }
        let mut relaunched = args.clone();
        relaunched.install_dir = Some(install_dir.clone());
        relaunched.no_relocate = true;
        match platform::leave_server_service(&relaunched.to_args()) {
            Ok(true) => return ExitCode::SUCCESS,
            Ok(false) => {}
            Err(e) => warning!("Cannot leave the server's service, staying in it: {e:#}"),
        }
    }

    let Some(_instance) = platform::single_instance() else {
        info!("Another installer is running, exiting.");
        platform::show_text("Popcorn Installer is already running.", false);
        return ExitCode::SUCCESS;
    };

    // The working directory must not be inside the install folder either: it would keep it from being renamed.
    let work_dir = if args.uninstall {
        std::env::temp_dir()
    } else {
        let updates_dir = paths::updates_dir();
        let _ = std::fs::create_dir_all(&updates_dir);
        cleanup_old_copies(&updates_dir);
        updates_dir
    };
    let _ = std::env::set_current_dir(&work_dir);

    let result = ui::run(config);
    if args.uninstall
        && let Ok(exe) = std::env::current_exe()
        && exe
            .file_name()
            .is_some_and(|n| n.to_string_lossy().starts_with(UNINSTALL_COPY))
    {
        platform::delete_after_exit(&exe);
    }
    if let Err(e) = result {
        error!("Installer window failed: {e:#}");
        platform::show_text(
            &format!(
                "Cannot open the installer window: {e:#}\n\nLog: {}",
                log::file_path().map_or_else(|| "-".into(), |p| p.display().to_string())
            ),
            true,
        );
        return ExitCode::FAILURE;
    }
    ExitCode::SUCCESS
}

/// `--update-check`: `{"version": "1.2.0", "notes": "<release notes>"}` when a newer release (or, with nothing
/// installed, any release) exists, `{}` when not; `{"error": "..."}` and exit code 1 when the check fails.
fn update_check(config: &update::Config) -> ExitCode {
    platform::attach_stdout();
    let (json, code) = match update::check(config) {
        Ok(update::Check::Offer(offer)) => (
            serde_json::json!({ "version": offer.version, "notes": offer.notes }),
            ExitCode::SUCCESS,
        ),
        Ok(update::Check::UpToDate { .. }) => (serde_json::json!({}), ExitCode::SUCCESS),
        Err(e) => {
            error!("Update check failed: {e:#}");
            (
                serde_json::json!({ "error": format!("{e:#}") }),
                ExitCode::FAILURE,
            )
        }
    };
    println!("{json}");
    code
}

/// The installer ships inside the folder it replaces, and a running exe (or a working directory) keeps Windows from
/// renaming that folder. So when started from there it copies itself to `<data>/updates` and runs the copy instead,
/// with every path made absolute. Returns true when the copy was started (this process should exit).
/// `--uninstall` deletes the data folder too, so its copy goes to %TEMP%. Not needed on Linux (`RELOCATE_EXE`).
fn relocate(args: &Args, install_dir: &Path) -> anyhow::Result<bool> {
    if !platform::RELOCATE_EXE {
        return Ok(false);
    }
    let exe = std::env::current_exe()?;
    let inside_data = args.uninstall && paths::is_inside(&exe, &paths::data_dir());
    if !paths::is_inside(&exe, install_dir) && !inside_data {
        return Ok(false);
    }

    let (dir, mut copy): (PathBuf, PathBuf) = if args.uninstall {
        let dir = std::env::temp_dir();
        let name = format!(
            "{UNINSTALL_COPY}{}{}",
            std::process::id(),
            std::env::consts::EXE_SUFFIX
        );
        (dir.clone(), dir.join(name))
    } else {
        let dir = paths::updates_dir();
        let file_name = exe.file_name().unwrap_or("Popcorn.Installer.exe".as_ref());
        (dir.clone(), dir.join(file_name))
    };
    std::fs::create_dir_all(&dir)?;
    if std::fs::copy(&exe, &copy).is_err() {
        // Still running (another installer): next to it under its own name.
        copy = dir.join(format!(
            "Popcorn.Installer-{}{}",
            std::process::id(),
            std::env::consts::EXE_SUFFIX
        ));
        std::fs::copy(&exe, &copy)?;
    }

    let mut relaunched = args.clone();
    relaunched.install_dir = Some(std::path::absolute(install_dir)?);
    relaunched.no_relocate = true;
    Command::new(&copy)
        .args(relaunched.to_args())
        .current_dir(&dir)
        .spawn()?;
    info!("Continuing in {}", copy.display());
    Ok(true)
}

/// Copies left by earlier runs (the current one is in use and stays).
fn cleanup_old_copies(updates_dir: &Path) {
    let Ok(current) = std::env::current_exe() else {
        return;
    };
    let Ok(entries) = std::fs::read_dir(updates_dir) else {
        return;
    };
    for entry in entries.flatten() {
        let path = entry.path();
        let name = entry.file_name().to_string_lossy().to_lowercase();
        if name.starts_with("popcorn.installer-")
            && name.ends_with(std::env::consts::EXE_SUFFIX)
            && path != current
        {
            let _ = std::fs::remove_file(path);
        }
    }
}
