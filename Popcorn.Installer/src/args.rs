use std::ffi::OsString;
use std::path::PathBuf;

use anyhow::{Context, bail};

pub const USAGE: &str = "\
Popcorn.Installer [options]

Installs Popcorn when it is not installed yet, else looks for a newer release on GitHub and
updates the installed one.

Options:
  --confirm            start installing / updating / uninstalling without asking (alias --yes)
  --from-app           started by Popcorn (tray, Settings): --confirm, and the finish screen has no
                       buttons to open Popcorn (it restarts what ran)
  --update-check       print {\"version\", \"notes\"} of a newer release as JSON ({} = up to date)
                       and exit, no window
  --uninstall          remove Popcorn (Windows: what Settings > Apps runs; Linux: the app
                       menu entry's Uninstall Popcorn action)
  --keep-data          with --uninstall: keep the data folder (library, settings, plugins)
  --package <zip>      install this package instead of a GitHub release (testing; the release
                       script's Popcorn-<version>-<platform>.zip)
  --install-dir <dir>  Popcorn folder (default: the installer's own folder when it is in one,
                       else the installed one, else %LOCALAPPDATA%\\Programs\\Popcorn on Windows,
                       ~/.local/bin/Popcorn on Linux)
  --repo <owner/name>  GitHub repository (default: POPCORN_UPDATE_REPO or the built-in one)
  --prerelease         also offer pre-releases
  --force              offer the latest release even when it is not newer (reinstall, downgrade)
  --no-restart         do not start Popcorn again afterwards
  --no-autostart       fresh install: do not start Popcorn at sign-in (the window asks)
  --no-relocate        run in place instead of from a copy outside the install folder
  --help               this text

Single-dash spellings work too (-update-check, -confirm, -uninstall).
Environment: POPCORN_GITHUB_TOKEN (or GITHUB_TOKEN) for a private repository.";

#[derive(Clone, Default, Debug)]
pub struct Args {
    pub package: Option<PathBuf>,
    pub install_dir: Option<PathBuf>,
    pub repo: Option<String>,
    pub prerelease: bool,
    pub force: bool,
    pub confirm: bool,
    pub from_app: bool,
    pub no_restart: bool,
    pub no_autostart: bool,
    pub no_relocate: bool,
    pub uninstall: bool,
    pub keep_data: bool,
    pub update_check: bool,
    pub help: bool,
}

impl Args {
    pub fn parse(args: impl IntoIterator<Item = OsString>) -> anyhow::Result<Self> {
        let mut parsed = Args::default();
        let mut args = args.into_iter();
        while let Some(arg) = args.next() {
            let name = arg.to_string_lossy().into_owned();
            // `-update-check` = `--update-check` (Windows programs are often called that way).
            let long = match name.strip_prefix('-') {
                Some(rest) if rest.len() > 1 && !rest.starts_with('-') => format!("-{name}"),
                _ => name.clone(),
            };
            let mut value = || args.next().with_context(|| format!("{name} needs a value"));
            match long.as_str() {
                "--package" => parsed.package = Some(value()?.into()),
                "--install-dir" => parsed.install_dir = Some(value()?.into()),
                "--repo" => parsed.repo = Some(value()?.to_string_lossy().into_owned()),
                "--prerelease" => parsed.prerelease = true,
                "--force" => parsed.force = true,
                "--confirm" | "--yes" | "-y" => parsed.confirm = true,
                "--from-app" => parsed.from_app = true,
                "--no-restart" => parsed.no_restart = true,
                "--no-autostart" => parsed.no_autostart = true,
                "--no-relocate" => parsed.no_relocate = true,
                "--uninstall" => parsed.uninstall = true,
                "--keep-data" => parsed.keep_data = true,
                "--update-check" => parsed.update_check = true,
                "--help" | "-h" | "/?" => parsed.help = true,
                _ => bail!("Unknown option: {name}"),
            }
        }
        Ok(parsed)
    }

    /// `--package` / `--install-dir` relative to the current directory → absolute.
    pub fn absolute(mut self) -> anyhow::Result<Self> {
        for path in [&mut self.package, &mut self.install_dir]
            .into_iter()
            .flatten()
        {
            *path = std::path::absolute(&*path)
                .with_context(|| format!("Bad path: {}", path.display()))?;
        }
        Ok(self)
    }

    /// The command line that parses back into these options (for the relocated copy).
    pub fn to_args(&self) -> Vec<OsString> {
        let mut args: Vec<OsString> = Vec::new();
        let mut push = |name: &str, value: Option<OsString>| {
            args.push(name.into());
            args.extend(value);
        };
        if let Some(package) = &self.package {
            push("--package", Some(package.into()));
        }
        if let Some(dir) = &self.install_dir {
            push("--install-dir", Some(dir.into()));
        }
        if let Some(repo) = &self.repo {
            push("--repo", Some(repo.into()));
        }
        for (set, name) in [
            (self.prerelease, "--prerelease"),
            (self.force, "--force"),
            (self.confirm, "--confirm"),
            (self.from_app, "--from-app"),
            (self.no_restart, "--no-restart"),
            (self.no_autostart, "--no-autostart"),
            (self.no_relocate, "--no-relocate"),
            (self.uninstall, "--uninstall"),
            (self.keep_data, "--keep-data"),
            (self.update_check, "--update-check"),
        ] {
            if set {
                push(name, None);
            }
        }
        args
    }
}
