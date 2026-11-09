# Building and releasing

The everyday commands are in [CLAUDE.md](../CLAUDE.md#build--run). This file has the rest. Installer and update
details: [Popcorn.Installer](../Popcorn.Installer/README.md).

## Rust crates

One Cargo workspace at the root (`Cargo.toml`, one `Cargo.lock`, one `target/`): `Popcorn.Installer`,
`Popcorn.Linux`, `Popcorn.Player.Native`, `Popcorn.Player.Windows.Native`. They share the version
(`[workspace.package]`), the `cef` / `libc` dependencies and the release profile. Always build one crate
(`cargo build --release -p popcorn-linux`): the Windows crate does not compile on Linux and the Linux ones not on
Windows. Outputs land in `target/release/`, except the installer's: its own profile `installer` (opt-level `s` for
all its dependencies, unwinding panics) → `target/installer/`. Popcorn.Linux and Popcorn.Player.Native use the same
CEF build (`CEF_PATH`, default `~/.cache/popcorn/cef`, downloaded by the `cef` crate's build script on first build,
~1.5 GB); the makefile reads the CEF version from `Cargo.lock`.

## Bundles

Bundles publish the apps (server, players, Windows app) instead of building them:
`dotnet publish -r <rid> --self-contained false -p:PublishReadyToRun=true` (`make`: `DOTNET_RID`, default
`linux-x64`/`linux-arm64` from `uname -m`, into `Bundle/Publish/<project>`; `build_windows.ps1`: `win-x64` into
`Bundle/Publish/<project>`). RID-specific output puts native libraries next to the DLLs instead of `runtimes/<rid>/`.

ReadyToRun = precompiled native code, no JIT at start-up. It matters for `Popcorn.FilenameParser`, whose ~230 regexes
are `[GeneratedRegex]` (~145k lines of generated code: ~250 ms of JIT on the first parse without R2R, ~50 ms with; the
old `RegexOptions.Compiled` took ~1.4 s).

Web bundles (`*/web`, `*/web-player`) and `Popcorn.Server/plugins` are gitignored build artifacts: rebuild the web
project after UI changes, the .NET projects only copy whatever is there (as `Content` added in a `BeforeBuild`
target, so plain builds and `dotnet publish` both get it; never as `AfterTargets="Build"` copies, publish skips those).
`post-build.js` replaces `Popcorn.Server/web` with `dist/`; `post-build-player.js` renames `dist-player/player.html`
to `index.html` (the page both players load) and replaces `Popcorn.Player.Linux/web-player`.

## Linux

`make all`: clean + both web UIs + Popcorn.Server + Popcorn.Linux + Popcorn.Player.Native + Popcorn.Player.Linux +
Popcorn.Installer, bundled into `Bundle/Linux/` (incl. the CEF runtime, stripped libcef.so; the installer as
`Popcorn.Installer.Linux` + `setup/` = unit, env, desktop templates it writes from + `release.json` = VERSION,
platform = RID, commit). `make install-linux` stops running instances (`make kill-popcorn`), copies the bundle to
`~/.local/bin/Popcorn`, installs + enables the systemd user service and adds `~/.local/share/applications/popcorn.desktop`
if missing.

libmpv: `make mpv` inits the `MPV/src` submodule (Tomiscout fork, pinned commit) and builds `MPV/libmpv.so.2` with
the DMA-BUF render API if missing (fetches libplacebo; meson from PATH or a venv in `~/.cache/popcorn`); see Known
issues in CLAUDE.md. `make all MPV_LIB=<path>` bundles another libmpv (next to libpopcorn_player.so); default:
`MPV/libmpv.so.2` if built, else none (system libmpv).

`./release_linux.sh`: `make all` + `Bundle/Release/<ver>/Popcorn-<ver>-linux-x64.zip` (Python zipfile: keeps Unix
modes) + `Popcorn.Installer.Linux`; `--skip-build`, `--version`, `--publish [--draft --prerelease --notes-file
--repo]` (gh; uploads only the Linux files, so Windows and Linux publish into the same release from different
machines).

## Windows

`build_windows.ps1` (needs the .NET 10 SDK, Node/npm, git and Rust (`cargo`, MSVC toolchain) on PATH; stops at the
first failure) initializes the MPV submodule and builds `MPV/windows/libmpv-2.dll` if missing, then builds
Popcorn.Web (UI + `build:player` overlay) + Popcorn.Server + Popcorn.Windows + Popcorn.Player.Windows (incl. its Rust
native layer) into `Bundle/Windows/` (server and app window side by side, player in `Player/` subfolder with libmpv
and popcorn_player.dll), plus `Popcorn.Installer.exe` and `release.json`.

- Player alone: `dotnet build Popcorn.Player.Windows` (after `npm run build:player` in Popcorn.Web), then
  `Popcorn.Player.Windows\bin\Debug\net10.0\Popcorn.Player.Windows.exe <file>`. Its build runs
  `cargo build --release -p popcorn-player-windows-native` (Windows only) and copies the DLL, libmpv-2.dll and
  web-player/ next to it.
- `release_windows.ps1` (optionally `-SkipBuild`) zips the bundle into
  `Bundle/Release/<ver>/Popcorn-<ver>-windows-x64.zip` (+ the standalone installer), and `-Publish`
  (`-Draft`/`-Prerelease`/`-NotesFile`/`-Repo`) creates or uploads to the `v<ver>` GitHub release with `gh`.
- `install_windows.ps1` = `make install-linux` counterpart: stops running instances, copies the bundle to
  `%LOCALAPPDATA%\Programs\Popcorn`, HKCU Run key for the server, Start menu shortcut to `Popcorn.Windows.exe`,
  Settings → Apps entry (HKCU `Uninstall\Popcorn`), starts the server. Uninstall runs `uninstall.ps1` (from
  `Popcorn.Windows/`, copied to the bundle) hidden: stops the processes, removes those entries, the install folder and
  the data folder (downloaded media stay).
- ffmpeg/ffprobe are not in the bundle: Popcorn.Installer downloads them into the install folder;
  `install_windows.ps1` installs need them on PATH.
- The Windows .NET projects also compile on Linux (`EnableWindowsTargeting`), useful as a build check.

### libmpv for Windows

The `MPV/src` fork built by `MPV/build_windows.sh` in a portable MSYS2 UCRT64 (`MPV/msys64`, download pinned by
checksum, packages from pacman are not pinned; all gitignored) as one DLL with its dependencies linked in statically
(no DMA-BUF there: it needs drm).

- Own FFmpeg (`MPV/build-windows-deps`): every video and subtitle decoder FFmpeg has for Windows, incl. external-library
  ones built from source there at pinned commits — davs2, uavs3d, xevd, SVT-JPEG-XS, libaribcaption; no image-only
  decoders (JPEG XL, SVG: thumbnails come from the separate ffmpeg), few encoders/muxers. MSYS2's FFmpeg has no usable
  static link. Bump `DEPS_REVISION` when changing these builds.
- Meson runs on python.org's signed embeddable Python (`MPV/build-windows-tools`, pinned by checksum).
- Auto-detected mpv features pick up whatever MSYS2 happens to have installed, so unneeded ones are disabled
  explicitly (libcurl, libarchive, lua, javascript). Only `vulkan-1.dll` stays external (from the GPU driver, no static
  version), so it does not load on a PC without one.
- Delete `MPV/windows` and `MPV/build-windows` to rebuild it (`MPV/build-windows-deps` too for FFmpeg).

## Versions

`node set_version.mjs <semver>` (both platforms) writes `VERSION`, the Cargo workspace version (root `Cargo.toml`) +
the crates' `Cargo.lock` entries and `Popcorn.Web/package(-lock).json`. `Directory.Build.props` makes `VERSION` every
.NET assembly's `Version` (`-p:Version` overrides), so `make` builds carry it too; `build_windows.ps1 -Version`
defaults to it and passes it as `-p:Version`. `make all VERSION=x.y.z` overrides the bundle's `release.json`. Plugin
and plugin-sdk versions are separate.
