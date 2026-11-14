# Popcorn.Installer

Small egui window (Rust, `eframe`) that installs Popcorn, updates it and uninstalls it, on Windows and Linux. One
crate for both: everything but `src/platform/windows.rs` / `src/platform/linux.rs` is shared (each has the same
functions and constants, picked with `cfg`); other systems build it but it refuses to install
(`src/platform/unsupported.rs`). The Linux specifics are in [Linux](#linux) below; the rest of this file describes
Windows where the two differ.
While it checks and works it shows the web UI's loading animation (`src/popcorn.rs`, port of
`Popcorn.Web/src/components/popcorn-box`: pulsing box, kernels popping out under gravity, here by themselves).
The window has no Windows frame: ✕ in the top right corner closes it (disabled while files are swapped or removed),
dragging anywhere moves it (text is not selectable for that reason).

## What it does when started

The install folder is `--install-dir`, else the installer's own folder when it sits in a bundle, else the
`InstallLocation` of the Settings → Apps entry, else `%LOCALAPPDATA%\Programs\Popcorn`. Popcorn counts as installed
when `Popcorn.Server.exe` is in it.

- **Not installed** (the standalone `Popcorn.Installer.exe` from the release page): offers the latest release
  ("Install"). First checks that the ASP.NET Core 10 and .NET Desktop 10 runtimes are there (else a link to
  Microsoft's download page). Asks whether Popcorn starts at sign-in ("Start Popcorn on system startup", ticked;
  `--no-autostart` unticks it). Installs like an update below (without stopping anything), then adds the Start menu
  shortcut (`Popcorn.lnk` → `Popcorn.Windows.exe`, also what Windows search finds), the server's autostart (HKCU
  `Run` value `Popcorn`, written either way; off = Task Manager's own disabled state in
  `Explorer\StartupApproved\Run`, so it is listed under Task Manager → Startup apps and the Settings page's Startup
  switch shows and changes the same thing) and the Settings → Apps entry (HKCU `Uninstall\Popcorn`, `UninstallString` = `Popcorn.Installer.exe
  --uninstall`, `QuietUninstallString` with `--confirm`), and starts the server. Only into a missing or empty folder.
- **Installed**: "Popcorn is up to date", or the newer release with its notes ("Update" / "Later").
- `--confirm` (alias `--yes`) skips the question. `--from-app` (passed by the tray and Settings → Updates) = `--confirm`, and the finish screen has no buttons; started on its own it shows Open Popcorn and Open in browser, which close the installer.
- `--uninstall`: asks (with "also delete the library data", ticked by default; `--keep-data` unticks it), then
  stops Popcorn, removes the shortcut, autostart and Settings → Apps entry, the install folder (only if it really is
  a Popcorn install) and the data folder. Downloaded media stay. Runs from a copy in `%TEMP%` that deletes itself,
  and logs there (the data folder goes).
- `--update-check`: no window; prints `{"version": "1.2.0", "notes": "<release notes>"}` when there is a newer
  release (with nothing installed: the latest one), `{}` when not; `{"error": "..."}` with exit code 1 when the check
  fails. Works from a pipe (`Process` with redirected stdout) and from a console.

Single-dash spellings work too (`-update-check`, `-confirm`, `-uninstall`).

## Started from Popcorn

The tray (Check for updates) and Settings → General → Updates (`UpdateService` + `UpdateWebService` in the server,
`pages/settings/update-section.tsx`; sockets `UpdateStatus`/`UpdateCheck`/`UpdateInstall`) use it: the check runs
`--update-check` hidden on the server (answer as its own event, not on the socket loop), only an available release
asks (version + notes modal), yes starts the installer window with `--from-app`. The server finds it next to itself
(`Popcorn.Installer.exe` / `Popcorn.Installer.Linux`).

## Releases it looks for

`GET api.github.com/repos/<repo>/releases/latest` (`--prerelease`: newest non-draft). Repo: `--repo`, else
`POPCORN_UPDATE_REPO` at runtime, else the one built in (`POPCORN_UPDATE_REPO` at compile time, default
`Tomiscout/Popcorn`). Private repository: `POPCORN_GITHUB_TOKEN` (or `GITHUB_TOKEN`), never built in.

Release layout, made by `cargo xtask release` on each platform (each uploads only its own files, so the
platforms can be built on different machines and published into the same release):

- tag `v<version>`
- `Popcorn-<version>-windows-x64.zip` / `Popcorn-<version>-linux-x64.zip`: the `Bundle/Windows` / `Bundle/Linux`
  contents with `release.json` at the root (`{ version, platform, commit, date }`, written by `cargo xtask
  bundle`)
- `Popcorn.Installer.exe` / `Popcorn.Installer.Linux`: the bundle's installer on its own, what new users download

The download is checked against the SHA-256 digest GitHub computes on upload (the asset's `digest`, shown on the
release page); a package without one is refused. No checksum file.

The installed version is `<install dir>/release.json`. Installs older than that file count as "unknown" and are
always offered the release.

## Update steps (`src/install.rs`)

1. When started from inside the install folder, copy itself to `<data>/updates/` and run the copy (a running exe
   or working directory inside the folder keeps Windows from renaming it).
2. Look up FFmpeg (below), download the package and the FFmpeg archive into `<data>/updates/` and verify their
   SHA-256 against the asset digests GitHub shows, or take `--package`.
   The label shows the step, MB done and the download speed; the bar is the whole update (downloads by bytes, then
   unpacking and the swap), its percentage under it. Steps that cannot be counted (closing Popcorn, the renames)
   say what is happening instead. When finished, the box stands still with the result under it.
3. Unpack into `<install>.new` (a sibling folder on the same volume), FFmpeg's `bin/` into it too.
4. Stop this install's processes (matched by executable path, so a dev build elsewhere is left alone): first the app
   window and players (the app would restart the server), then the server through the `Local\Popcorn.Server.Shutdown`
   event (`Popcorn.Server/Program.cs`, clean shutdown). After 20 s, or if the server is older and has no event,
   kill it.
5. Rename `<install>` to `<install>.old`, then `<install>.new` to `<install>`, retrying for ~10 s while files are
   still held. Any failure puts the old folder back.
6. Rewrite the Settings → Apps entry (version, size, uninstall command; only if its `InstallLocation` is this
   folder or there is none).
7. Start again whatever was running. If the new server exits with an error within 5 s, the old version is
   restored and started.
8. Delete `<install>.old` and the downloaded zips.

The data folder (`%LOCALAPPDATA%\Popcorn`: database, plugins, caches) is never touched by installs and updates.
Log: `<data>/logs/YYYY-MM-DD-Popcorn.Installer.txt` (`%TEMP%` for `--uninstall`).

## FFmpeg (`src/ffmpeg.rs`)

From [BtbN/FFmpeg-Builds](https://github.com/BtbN/FFmpeg-Builds/releases), release `latest` (rebuilt daily): the
asset `*-win64-gpl-shared*.zip` of the newest release branch (`ffmpeg-n9.0-latest-win64-gpl-shared-9.0.zip`), the
master build if there is none. Its `bin/` contents go next to `Popcorn.Server.exe`, without `ffplay.exe`; the server
and the players run them from there (`Popcorn.Core/Shared/Ffmpeg/FfmpegBinaries.cs`; PATH is never changed). `ffmpeg-files.txt` in the
install folder lists the files (the DLL names change with FFmpeg versions), first line `# <asset> <digest>`.

- Same build as installed (digest): not downloaded again, the listed files are copied from the current install.
- GitHub not reachable (rate limit: 60 anonymous API calls an hour), download or unpack failed: the same copy.
- Nothing to copy either (first install): installed without FFmpeg, with a warning on the last screen.

## Command line

```
Popcorn.Installer.exe [--confirm | --from-app] [--update-check] [--uninstall [--keep-data]] [--package <zip>]
                    [--install-dir <dir>] [--repo owner/name] [--prerelease] [--force] [--no-restart] [--no-autostart]
                    [--no-relocate]
```

Testing without GitHub: `cargo xtask release --skip-build` (or without it: a full build) →
`Popcorn.Installer.exe --package Bundle\Release\<version>\Popcorn-<version>-windows-x64.zip`; Linux:
the same → `Bundle/Linux/Popcorn.Installer.Linux --package Bundle/Release/<version>/Popcorn-<version>-linux-x64.zip`. `--confirm` starts
right away. The installer writes real HKCU entries (Settings → Apps, Run, Start menu): uninstall a test install
with `--uninstall` afterwards. `--install-dir` points it at another copy of the bundle (setting `LOCALAPPDATA` to a scratch folder keeps
the logs and downloads out of the real data folder too).

## Building

`cargo xtask bundle` builds it and copies it into the bundle as `Popcorn.Installer.exe`.
It needs the MSVC toolchain (Visual Studio Build Tools, "Desktop development with C++"), or `windows-gnu` with
MSYS2 binutils (`dlltool`) on PATH. TLS is the OS's (SChannel through `native-tls`), so no C compiler is needed
for it.

The exe icon is `Popcorn.Web/public/images/popcorn.ico`, compiled in by `build.rs` (`winresource`: the Windows
SDK's `rc.exe` on MSVC, `windres` on windows-gnu). Without a resource compiler the build only warns and the exe has
no icon (the window still sets its own).

## Linux

Same window, checks and update steps; what differs (`src/platform/linux.rs`):

- **Install folder**: default `~/.local/bin/Popcorn`, the same as `make install-linux` (installs made by make get
  updated too). The installed one is the systemd unit's `WorkingDirectory`. Popcorn counts as installed when
  `Popcorn.Server` is in it.
- **Requirements** (checked before a fresh install): the ASP.NET Core 10 runtime (`dotnet --list-runtimes` from
  `DOTNET_ROOT`, PATH, `/etc/dotnet/install_location`, `/usr/share|lib|lib64/dotnet`, `~/.dotnet`; else the package
  names to install) and a systemd user session (`systemctl --user`): the server only runs as `popcorn.service`, there
  is no other way of starting it or of autostart.
- **Entries** (instead of the registry and Start menu), written from the templates in the bundle's `setup/` folder
  (copied there by `make bundle-linux`; the copies built into the installer when missing):
  `~/.config/systemd/user/popcorn.service` (the server; a fresh install enables it or not = "Start Popcorn when I
  sign in", updates keep the state; Settings → General → Startup switches it too, `LinuxAutostart` in the server),
  `~/.config/popcorn/popcorn.env` (only when missing: the user's port etc.), `~/.local/share/applications/popcorn.desktop`
  (app menu entry, with an "Uninstall Popcorn" action = `Popcorn.Installer.Linux --uninstall`). Updates rewrite them only
  when the unit belongs to this install folder (or there is none).
- **Stopping**: processes whose `/proc/<pid>/exe` is inside the install folder. The app window and players get SIGTERM
  (SIGKILL after 5 s), then the server: through `systemctl --user stop --no-block popcorn.service` when the unit
  runs it (so systemd does not restart it), else SIGTERM (.NET shuts down cleanly on it); SIGKILL after 20 s.
- **Starting**: `systemctl --user start popcorn.service`, only when the unit is this install's (a test copy under
  `--install-dir` is not started); failing = the unit failed or restarted within 5 s (it is stopped then, before the
  old version is put back; `journalctl --user -u popcorn.service` has the output). The app: `Popcorn.Linux` in its
  own process group.
- **No relocation**: files in use can be renamed and deleted on Linux, so the installer runs in place (also
  `--uninstall`, which deletes its own folder). Instead, when the server started it (Settings → Updates) it sits in
  `popcorn.service`'s cgroup, which `systemctl stop` kills as a whole: it first runs itself again through
  `systemd-run --user --scope` (`leave_server_service`).
- **Package**: the zip's Unix modes are applied (permission bits only), so it has to be made by a tool that stores
  them: `cargo xtask release` writes them into the zip entries.
- **FFmpeg**: BtbN's static GPL build (`ffmpeg-n9.0-latest-linux64-gpl-9.0.tar.xz`; the shared one's binaries look
  for their libraries in `../lib`), `bin/ffmpeg` + `bin/ffprobe` next to `Popcorn.Server`, unpacked in Rust
  (`tar` + `lzma-rust2`, no C library), same `ffmpeg-files.txt` tracking.
- **Uninstall without `--keep-data`** also deletes `~/.config/popcorn`.
- **Dialogs** outside the window (`--help`, errors before it opens): the terminal, else zenity / kdialog.
- Single instance: an flock on `$XDG_RUNTIME_DIR/popcorn-installer.lock`. Log times are local time.

Built by `make all` / `cargo xtask bundle` (alone: `cargo xtask build installer`), copied into the bundle as `Popcorn.Installer.Linux` (mode 755). TLS is
OpenSSL (`native-tls`), so the build needs its headers (Arch: `openssl`, Debian: `libssl-dev`). The binaries of the
bundle link against the build machine's glibc: build releases on the oldest distro they should run on.
