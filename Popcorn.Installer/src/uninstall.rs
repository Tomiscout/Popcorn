//! `--uninstall` (Windows: Settings → Apps → Popcorn → Uninstall; Linux: the app menu entry's Uninstall Popcorn
//! action): stops Popcorn, removes the OS entries (autostart, Start menu, Settings → Apps / systemd unit, desktop
//! entry) and the program folder, and the data folder (library database, settings, plugins, caches, download list;
//! Linux: also ~/.config/popcorn) unless `--keep-data`. Downloaded media (Settings → Downloads folder) are the
//! user's and stay. On Windows it runs from a copy in %TEMP% (see main.rs), which deletes itself after exiting.

use std::path::Path;

use anyhow::bail;

use crate::install::{Report, is_installed, remove_dir};
use crate::log::{info, warning};
use crate::package;
use crate::paths;
use crate::platform;

pub fn uninstall(install_dir: &Path, keep_data: bool, report: Report) -> anyhow::Result<()> {
    info!(
        "Uninstalling {} (data: {})",
        install_dir.display(),
        if keep_data { "kept" } else { "deleted" }
    );
    // Only a folder that is a Popcorn install gets deleted, whatever --install-dir says.
    let ours = is_installed(install_dir) || install_dir.join(package::MANIFEST).is_file();
    if install_dir.exists() && !ours {
        bail!(
            "{} does not look like a Popcorn install, not deleting it",
            install_dir.display()
        );
    }

    if ours {
        report("Closing Popcorn…", 0.1, false);
        platform::stop_popcorn(install_dir, &|step| report(step, 0.1, false))?;
    }

    report(platform::TEXTS.unregister_step, 0.35, false);
    platform::unregister(install_dir);

    report("Deleting the program files…", 0.5, false);
    remove_dir(install_dir)?;
    for suffix in [".new", ".old", ".failed"] {
        let leftover = paths::sibling(install_dir, suffix);
        if let Err(e) = remove_dir(&leftover) {
            warning!("{e:#}");
        }
    }

    if !keep_data {
        report("Deleting the library data…", 0.8, false);
        remove_dir(&paths::data_dir())?;
        for dir in platform::extra_data_dirs() {
            remove_dir(&dir)?;
        }
    }
    report("Done", 1.0, false);
    info!("Uninstalled");
    Ok(())
}
