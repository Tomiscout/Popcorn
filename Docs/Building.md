# Building and releasing

The everyday commands are in [CLAUDE.md](../CLAUDE.md#build--run). This file has the rest. Installer and update
details: [Popcorn.Installer](../Popcorn.Installer/README.md).

## cargo xtask

The build is one Rust program, `xtask/` (a member of the Cargo workspace, run through the alias in
`.cargo/config.toml`), the same on Linux and Windows. It builds the apps of the platform it runs on; the platform
differences are `cfg(target_os)` code in `xtask/src/bundle.rs` (layout) and `xtask/src/mpv.rs` (libmpv). The
makefile only gives it short names (`make all` = `cargo xtask bundle`) and adds the Linux install.

```sh
cargo xtask bundle [--version x.y.z] [--mpv-lib <path>] [--cef-path <dir>]   # everything -> Bundle/<platform>
cargo xtask build [web|player-web|server|desktop|player|installer|cli]...     # some apps, no bundle
cargo xtask mpv [--update]       # libmpv (Linux: optional, Windows: built by bundle)
cargo xtask check                # all .NET projects + tests, web type check + lint, clippy + cargo test
cargo xtask test                 # tests only
cargo xtask release [--skip-build] [--publish [--draft] [--prerelease] [--notes-file f] [--repo owner/name]]
cargo xtask clean                # Bundle/<platform>, Bundle/Publish, web dist folders, the apps' bin/Release
```

Everything is incremental: dotnet, cargo and vite rebuild what changed, `npm ci` runs when `package-lock.json`
changed (stamp in `node_modules`), libmpv when its sources or build options changed. `clean` for a fresh build;
`target/` (CEF, every crate) stays, `cargo clean` for that.

Outputs that could silently go stale are rebuilt or checked instead:

- **Socket contract**: the server build writes `Popcorn.Web/src/socket/socket-{requests,payloads}.ts`, so `build
  server` (and `check`) builds the server first, then the web UI, then publishes the server with it.
- **libmpv**: a stamp next to the library holds the `MPV/src` commit it was built from plus its build inputs (Linux:
  libplacebo commit, `OPTIONS_REVISION` of the meson options; Windows: hash of `MPV/build_windows.sh`); a submodule with
  local changes is recompiled every time (ninja: incremental). The build uses the checked-out commit; one that is not
  the commit this repository pins gives a warning.
- **Shared files in a bundle folder** (server + Linux player, server + Popcorn.Windows): every file both apps ship must
  be the same file or the same NuGet package version (ReadyToRun compiles package DLLs per app, so their bytes can
  differ); anything else fails the bundle. Central package management (`Directory.Packages.props`) keeps the versions
  in one place.
- **Bundle contents**: `release.json` and the files every install needs are checked after bundling and before
  packing a release.

## Rust crates

One Cargo workspace at the root (`Cargo.toml`, one `Cargo.lock`, one `target/`): `Popcorn.Cli`,
`Popcorn.Installer`, `Popcorn.Linux`, `Popcorn.Player.Native`, `Popcorn.Player.Windows.Native`, `xtask`. They share
the version (`[workspace.package]`, not xtask's), the `cef` / `libc` dependencies and the release profile. Every member
builds on every OS: the platform crates take their dependencies as `[target.'cfg(...)'.dependencies]` and their code
is `#![cfg(...)]`-gated (the libraries are empty elsewhere, the binaries print that they are Linux only), so `cargo
build --workspace`, `cargo clippy --workspace` and rust-analyzer work on Linux and Windows; CEF is not even downloaded
on Windows. The Windows player library is `popcorn_player_windows` (`target/release/popcorn_player_windows.dll`,
renamed to `popcorn_player.dll` by `Popcorn.Player.Windows.csproj`), so it does not collide with the Linux crate's
`popcorn_player`.

Outputs land in `target/release/`, except the installer's: its own profile `installer` (opt-level `s` for all its
dependencies, unwinding panics) → `target/installer/`. Popcorn.Linux and Popcorn.Player.Native use the same CEF build
(`CEF_PATH`, default `~/.cache/popcorn/cef`, downloaded by the `cef` crate's build script on first build, ~1.5 GB);
the bundle takes its runtime files from `<CEF_PATH>/<CEF version>/cef_linux_<arch>`, the version read from `cargo
metadata`.

## .NET

Bundles publish the apps (server, players, Windows app) instead of building them:
`dotnet publish -r <rid> --self-contained false -p:PublishReadyToRun=true` (`linux-x64` / `linux-arm64` / `win-x64`,
into `Bundle/Publish/<project>`). RID-specific output puts native libraries next to the DLLs instead of
`runtimes/<rid>/`.

ReadyToRun = precompiled native code, no JIT at start-up. It matters for `Popcorn.FilenameParser`, whose ~230 regexes
are `[GeneratedRegex]` (~145k lines of generated code: ~250 ms of JIT on the first parse without R2R, ~50 ms with; the
old `RegexOptions.Compiled` took ~1.4 s).

NuGet versions: `Directory.Packages.props` (central package management; projects reference packages without a
version). `NuGet.Config` maps packages to sources (FFMpegCore may also come from the Servarr feed).

The web bundles are linked in, not copied: Popcorn.Server's build takes `Popcorn.Web/dist` as `web/`, both players'
builds take `Popcorn.Web/dist-player` as `web-player/` (as `Content` added in a `BeforeBuild` target, so plain builds
and `dotnet publish` both get it; never as `AfterTargets="Build"` copies, publish skips those). Rebuild the web
project after UI changes. `post-build-player.js` renames `dist-player/player.html` to `index.html` (the page both
players load). `Popcorn.Server/plugins` (gitignored) is shipped the same way when it exists.

## Plugins

`Plugins/*` are separate products, not part of the app build or its central package versions
(`Plugins/Directory.Packages.props`). `make build-plugins` builds them into `Popcorn.Server/plugins/<id>/`; a bundle
made after that contains them. See [Plugins.md](Plugins.md).

## Linux

`cargo xtask bundle` (`make all`): both web UIs + Popcorn.Server + Popcorn.Linux + Popcorn.Player.Native +
Popcorn.Player.Linux + Popcorn.Installer + Popcorn.Cli, bundled into `Bundle/Linux/` (incl. the CEF runtime, stripped
libcef.so; the installer as `Popcorn.Installer.Linux` + `setup/` = unit, env, desktop templates it writes from +
`release.json` = version, platform = RID, commit, date). `make install-linux` stops running instances (`make
kill-popcorn`), copies the bundle to `~/.local/bin/Popcorn`, installs + enables the systemd user service and adds
`~/.local/share/applications/popcorn.desktop` if missing.

libmpv: `cargo xtask mpv` (`make mpv`) inits the `MPV/src` submodule if needed (Tomiscout fork) and builds
`MPV/libmpv.so.2` with the DMA-BUF render API (fetches libplacebo at a pinned commit into `MPV/src/subprojects`; meson
from PATH or a venv in `~/.cache/popcorn`); see Known issues in CLAUDE.md. Optional: once it exists, every bundle
rebuilds it when stale and ships it next to libpopcorn_player.so. `--mpv-lib <path>` (`make all MPV_LIB=<path>`)
bundles another libmpv; without either, none (system libmpv, OpenGL video). `--update` (`make update-mpv`) moves the
submodule to the fork's latest master first.

## Windows

Needs the .NET 10 SDK, Node/npm, git and Rust (`cargo`, MSVC toolchain) on PATH. `cargo xtask bundle` builds
`MPV/windows/libmpv-2.dll` when stale, then Popcorn.Web (UI + overlay) + Popcorn.Server + Popcorn.Windows +
Popcorn.Player.Windows (incl. its Rust native layer) + Popcorn.Installer + Popcorn.Cli into `Bundle/Windows/`
(server and app window side by side, player in `Player/` with libmpv and popcorn_player.dll, `Popcorn.Installer.exe`,
`popcorn-cli.exe`, `release.json`).

- Player alone: `cargo xtask build player-web`, `dotnet build Popcorn.Player.Windows`, then
  `Popcorn.Player.Windows\bin\Debug\net10.0\Popcorn.Player.Windows.exe <file>`. Its build runs
  `cargo build --release -p popcorn-player-windows-native` (Windows only) and copies the DLL, libmpv-2.dll and
  web-player/ next to it.
- `install_windows.ps1` = `make install-linux` counterpart: stops running instances, copies the bundle to
  `%LOCALAPPDATA%\Programs\Popcorn`, HKCU Run key for the server, Start menu shortcut to `Popcorn.Windows.exe`,
  Settings → Apps entry (HKCU `Uninstall\Popcorn`), starts the server. Uninstall runs `uninstall.ps1` (from
  `Popcorn.Windows/`, copied to the bundle) hidden: stops the processes, removes those entries, the install folder and
  the data folder (downloaded media stay).
- ffmpeg/ffprobe are not in the bundle: Popcorn.Installer downloads them into the install folder;
  `install_windows.ps1` installs need them on PATH (the bundle warns when they are not).
- The Windows .NET projects also compile on Linux (`EnableWindowsTargeting`); `cargo xtask check` builds them.

### libmpv for Windows

The `MPV/src` fork built by `MPV/build_windows.sh` in a portable MSYS2 UCRT64 (`MPV/msys64`, downloaded by
`xtask/src/mpv.rs` with `curl.exe`, pinned by checksum; packages from pacman are not pinned; all gitignored) as one
DLL with its dependencies linked in statically (no DMA-BUF there: it needs drm).

- Own FFmpeg (`MPV/build-windows-deps`): every video and subtitle decoder FFmpeg has for Windows, incl. external-library
  ones built from source there at pinned commits — davs2, uavs3d, xevd, SVT-JPEG-XS, libaribcaption; no image-only
  decoders (JPEG XL, SVG: thumbnails come from the separate ffmpeg), few encoders/muxers. MSYS2's FFmpeg has no usable
  static link. Bump `DEPS_REVISION` when changing these builds.
- Meson runs on python.org's signed embeddable Python (`MPV/build-windows-tools`, pinned by checksum).
- Auto-detected mpv features pick up whatever MSYS2 happens to have installed, so unneeded ones are disabled
  explicitly (libcurl, libarchive, lua, javascript). Only `vulkan-1.dll` stays external (from the GPU driver, no static
  version), so it does not load on a PC without one.
- A changed `build_windows.sh` deletes `MPV/build-windows` (fresh meson configure). Delete `MPV/windows` to force a
  rebuild (`MPV/build-windows-deps` too for FFmpeg).

## Releases

`cargo xtask release` (`make release ARGS=...`) bundles (unless `--skip-build`), checks the bundle and writes
`Bundle/Release/<ver>/Popcorn-<ver>-<platform>.zip` (forward slashes, sorted entries; Unix modes kept on Linux, the
installer restores them) + the standalone installer (`Popcorn.Installer.Linux` / `Popcorn.Installer.exe`).
`--publish` creates the `v<ver>` GitHub release with `gh` (or uploads into the existing one, `--clobber`); it uploads
only the host platform's files, so Windows and Linux publish into the same release from different machines. `--repo`
defaults to `POPCORN_UPDATE_REPO`, else the checkout's GitHub remote.

## Versions

`node set_version.mjs <semver>` (both platforms) writes `VERSION`, the Cargo workspace version (root `Cargo.toml`) +
the crates' `Cargo.lock` entries and `Popcorn.Web/package(-lock).json`. `Directory.Build.props` makes `VERSION` every
.NET assembly's `Version`; `cargo xtask` defaults to it and passes it as `-p:Version`, `--version x.y.z` (`make all
VERSION=x.y.z`) overrides it for one build (assemblies and `release.json`). Plugin and plugin-sdk versions are
separate.
