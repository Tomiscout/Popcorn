# Popcorn

Personal media library + video player. .NET 10 backend (`Popcorn.Server`), React web UI (`Popcorn.Web`), native
MPV-based players for Linux and Windows. Primary dev platform is Linux (Arch); Windows support lags behind.

This file is the map. Details live next to the code they describe; keep them there, not here:

| Doc | What's in it |
|---|---|
| [Docs/Architecture.md](Docs/Architecture.md) | Products in detail, every communication path (socket RPC, HTTP, player link, overlay bridge, plugin host, native C ABIs), runtime data, logging |
| [Docs/Features.md](Docs/Features.md) | How library/player features behave: thumbnails, next episode, intro/outro skip, subtitles, languages, sync, downloading-file playback |
| [Docs/Building.md](Docs/Building.md) | Cargo workspace, bundles/ReadyToRun, Linux + Windows builds, libmpv for Windows, releases, versions |
| [Docs/Plugins.md](Docs/Plugins.md) | Plugin API (for plugin authors) |
| [Popcorn.Downloads/README.md](Popcorn.Downloads/README.md) | Built-in torrent/direct downloader |
| [Popcorn.SegmentFinder/README.md](Popcorn.SegmentFinder/README.md) | Intro/outro detection |
| [Popcorn.Installer/README.md](Popcorn.Installer/README.md) | Installer/updater (Windows + Linux) |
| [Popcorn.FilenameParser/README.md](Popcorn.FilenameParser/README.md) | Release name parser (Sonarr/Radarr derived, GPL-3.0) |
| [Popcorn.Web/README.md](Popcorn.Web/README.md) | Web UI builds and layout |

When a change alters behaviour described in one of these, update that file in the same change.

## Products

| Variant | Entry project | What it is |
|---|---|---|
| Server + browser UI | `Popcorn.Server` | ASP.NET Core host: serves the web bundle, WebSocket RPC at `/ws`, video streaming at `api/video`. Headless / systemd user service. |
| Linux desktop app | `Popcorn.Linux` | Rust + CEF window on `{server}/index.html` (not in `Popcorn.sln`). Starts the server (`popcorn.service`) if needed, never hosts it. `PopcornDesktop/<ver>` UA → web UI opens files in the native player. |
| Linux player | `Popcorn.Player.Linux` + `Popcorn.Player.Native` | **Wayland only.** .NET player logic P/Invoking a Rust layer: Wayland window, mpv video subsurface (DMA-BUF with our libmpv patch, else EGL), CEF off-screen overlay. Works without the server. |
| Windows desktop app | `Popcorn.Windows` | WinForms + WebView2 counterpart of `Popcorn.Linux`; the server runs as a background process with a tray icon (`Popcorn.Platform/Windows/WindowsTrayManager`). |
| Windows player | `Popcorn.Player.Windows` + `Popcorn.Player.Windows.Native` | Same split: Win32 window with mpv's D3D11 output + a separate owned WebView2 overlay window. |

The old Windows Blazor/WebView app (`Popcorn/`, `Popcorn.WebView`, its old .NET `Popcorn.Installer`) was removed — do
not reintroduce references to it. Today's `Popcorn.Installer` is the unrelated Rust installer/updater.

## Project map

```
Popcorn.Server          ASP.NET host, [WebSocketService] handlers (Services/), controllers, PluginHost/, Push/, DI root (ServiceRegister.cs)
Popcorn.Core            One assembly, old layers as folders (namespaces Popcorn.Domain/.Persistence/.Application/.Shared, RootNamespace Popcorn):
                        Shared/ (FFmpeg helpers, DirectoryUtils, Languages, Logging, PlayerLink), Domain/ (models, interfaces, MediaLoader),
                        Persistence/ (SQLite: Dapper + FluentMap + FluentMigrator, Migrations/), Application/ (services)
Popcorn.Platform        OS-specific implementations: Linux/ (tray via DBus, autostart, folders, LinuxPlayerService), Windows/ (Win32 tray, ...), NativePlayerService
Popcorn.FilenameParser  Scene/release name parsing (Sonarr episodes + Radarr movies, GPL-3.0); MediaNameParser.Parse(path)
Popcorn.Plugins.Abstractions  Plugin contract; the only thing plugins reference
Plugins/*               Bundled plugins (loaded at runtime, not referenced): Popcorn.Plugin.Tmdb (+ ui/ npm project)
Popcorn.Downloads       Built-in downloader (MonoTorrent + HTTP), DownloadManager
Popcorn.SegmentFinder   Intro/outro detection (SoundFingerprinting)
Popcorn.Player.Core     Shared player logic: MpvPlayer, LibMpvClient, PlayerCore, overlay web bridge (Web/), ServerLink, DI root
Popcorn.Player.Linux    Linux player host: Program.cs + Native/PlayerNative.cs (C ABI of libpopcorn_player.so)
Popcorn.Player.Native   Rust cdylib libpopcorn_player.so + popcorn-player-cef: lib.rs (C ABI), window.rs, video.rs, overlay_gpu.rs, dmabuf.rs, cef_app.rs
Popcorn.Player.Windows  Windows player host: Program.cs + Native/PlayerNative.cs + Native/FileDialog.cs
Popcorn.Player.Windows.Native  Rust cdylib popcorn_player.dll: lib.rs (C ABI), window.rs, overlay.rs (WebView2), mpv.rs (libmpv-2.dll loaded at run time)
Popcorn.Linux           Rust + CEF desktop shell: main.rs, browser.rs, server.rs, log.rs
Popcorn.Windows         Windows desktop app (WinForms + WebView2)
Popcorn.Installer       Rust + egui installer/updater, one crate for Windows and Linux (src/platform/{windows,linux}.rs)
Popcorn.Core.Tests      NUnit tests of Popcorn.Core (incl. PlayerLink)
Popcorn.FilenameParser.Tests  Sonarr/Radarr parser fixtures (~2.6k cases)
Popcorn.Web             React UI, also the native players' overlay (player.html, `npm run build:player`)
MPV/src                 git submodule: Tomiscout/mpv fork with the DMA-BUF render API
Cargo.toml              Root Cargo workspace of the four Rust crates (one Cargo.lock, one target/)
```

Dependency direction: `Server → Core + Platform + Downloads + SegmentFinder + Plugins.Abstractions`;
`Platform/Downloads/SegmentFinder → Core → FilenameParser`. `Player.* → Player.Core → Core + Platform`. Inside Core the
folders keep the old layering (Application → Persistence → Domain → Shared) by convention only.

## Communication (details: Docs/Architecture.md)

1. **Web UI ↔ Server, WebSocket `/ws`**: JSON `{ type, payload }` events (binary frames `"type,params;"` + bytes).
   Handlers = `[WebSocketEvent(SocketRequests.X)]` methods on `[WebSocketService]` classes, found by reflection.
   `socket-requests.ts` + `socket-payloads.ts` are generated by every server build — never edit them. Live data is
   pushed (`Subscribe` topics, `useSubscription`), never polled; live HTTP endpoints stream NDJSON.
2. **Web UI ↔ Server, HTTP**: `VideoController` (`api/video/...`, audio conversion/streaming), `DownloadsController`
   (waiting range streams of downloads in progress).
3. **Server → native player**: `PlayerLink`, one JSON line over a unix socket in `$XDG_RUNTIME_DIR` (Windows too),
   one-way.
4. **Player ↔ its overlay**: Fleck WebSocket on 127.0.0.1, address injected as `window.wsAddress`.
5. **Plugins**: everything search-related comes from plugins (`PluginManager`, own `AssemblyLoadContext`); Popcorn
   handles links generically — never integrate a specific site into the app.
6. / 7. **Player .NET ↔ Rust**: C ABI (`pn_*` in each native crate's `lib.rs`); C# attaches its own mpv client to the
   core the native layer owns.

## Build & run

Prereqs: .NET SDK 10 (`global.json`), Node/npm, Rust toolchain + cmake, libmpv, ffmpeg, libwayland + libEGL + libgbm +
libdrm + libxkbcommon (Linux player). `make` / `build_windows.ps1` run `npm ci` in `Popcorn.Web/` first.

```sh
make all                       # clean + build everything + bundle into Bundle/Linux/ (incl. CEF runtime)
make install-linux             # stop running instances, copy bundle to ~/.local/bin/Popcorn, systemd user service, desktop entry
make mpv                       # build MPV/libmpv.so.2 with the DMA-BUF render API (submodule, pinned fork commit)
make update-mpv                # move submodule to latest fork master and rebuild libmpv
make build-popcorn-web         # Popcorn.Web only (post-build replaces Popcorn.Server/web)
make build-player-web          # player overlay only -> Popcorn.Player.Linux/web-player
make build-plugins             # Plugins/* -> Popcorn.Server/plugins/<id>/
make build-popcorn-linux       # desktop shell (cargo -p popcorn-linux, CEF_PATH default ~/.cache/popcorn/cef)
make build-popcorn-player-native  # player native layer (cargo -p popcorn-player-native)
make build-popcorn-installer   # installer (cargo --profile installer -p popcorn-installer -> target/installer/)
./release_linux.sh             # make all + release zip; --skip-build, --version, --publish (gh)

dotnet run --project Popcorn.Server       # dev server, port POPCORN_PORT (default 5000), all interfaces
cd Popcorn.Web && npm run dev             # Vite dev server on :5173 against running Popcorn.Server
make build-popcorn-linux bundle-linux && Bundle/Linux/Popcorn.Linux   # desktop shell (needs the CEF runtime of the bundle)
Bundle/Linux/Popcorn.Player.Linux <file|fileId>   # player, also only from the bundle
dotnet test Popcorn.Core.Tests
dotnet test Popcorn.FilenameParser.Tests
cd Popcorn.Web && npm run lint            # eslint; prettier config in .prettierrc
```

Windows: `build_windows.ps1` (→ `Bundle/Windows/`), `release_windows.ps1`, `install_windows.ps1`. Versions:
`node set_version.mjs <semver>`. Rust: one workspace, always build a single crate with `-p`. All in
[Docs/Building.md](Docs/Building.md).

## Conventions

- `net10.0`, nullable + implicit usings enabled. Primary constructors common in newer code.
- FFmpeg: everything (FFMpegCore, `FFmpegWrapper`, `FFmpegService`) runs the binaries
  `Popcorn.Core/Shared/Ffmpeg/FfmpegBinaries.Configure()` picks at start-up: ffmpeg + ffprobe in the program's own
  folder (Windows player: also `..`), else from PATH. Never add Popcorn to PATH (no env changes, not even for child
  processes).
- Hardware decoding: pick decoders by the GPUs present (`GpuAdapters`, PCI vendor ids), never by `ffmpeg -hwaccels`
  (it lists what ffmpeg was built with), and measure rather than assume it is faster (Docs/Features.md, thumbnails).
- Platform-specific DI via `RuntimeInformation.IsOSPlatform` in `Popcorn.Server/ServiceRegister.cs` and
  `Popcorn.Player.Core/ServiceRegister.cs`. Native abstractions in `Popcorn.Core/Domain/Interfaces` (`IPlayerService`,
  `INativeFolderService`, `INativeFileAssosiation` — note spelling) with Linux/Windows impls in `Popcorn.Platform`.
- Repositories and most services are singletons; `FileStreamService`, `VideoService` scoped.
- DB schema changes: a new FluentMigrator class in `Popcorn.Core/Persistence/Migrations/` named
  `<Name>_<yyyyMMdd>.cs`, plus Dapper map updates in `Popcorn.Core/Persistence/Dapper/`. The server migrates on every
  start (`MigrateDatabase`, Development too); the players never migrate.
- New WebSocket feature: constant in `SocketRequests.cs`, request model under `Popcorn.Server/Models/Requests`, handler
  method on a `[WebSocketService]` class in `Popcorn.Server/Services`, response model under `Popcorn.Web/src/model`.
  The TS event names and payload types come from the server build.
- Live state: push it (subscription topic or NDJSON stream), don't add polling.
- The web player (`src/player`) is a lazy chunk: keep it out of static imports from non-player code (`import type` is
  fine).
- One player UI, several hosts: overlay components only read the player store and post on the local event bus; never
  talk to the server socket or a `<video>` directly. A new overlay command is handled in both hosts and in
  `WebEventHandler.OnMessage` (unknown names throw there). Host differences go through `player-features.ts`.
- `Popcorn.Server/plugins` (runtime folder) vs `Popcorn.Server/PluginHost` (code): MSBuild globs are case-insensitive,
  so the code folder must not be named `Plugins`.
- Changing `plugin-sdk/shared.js` or the plugin contract is an API change (`PluginApi.Version`).
- Files made outside a .NET project (web bundles, native libraries) are added as `Content` in a `BeforeBuild` target,
  never copied `AfterTargets="Build"` (publish skips those).

## Known issues / gotchas

- **Windows is untested**: the server (`WindowsPlayerService`, `WindowsFolderService` with pickers via STA PowerShell
  WinForms dialogs, `WindowsFileAssociation`, `WindowsProcessRunner`, tray), `Popcorn.Windows` and the install script
  build but have not been run on Windows. Of the thumbnail hardware decoders only CUDA has been run.
- **Windows Smart App Control** blocks freshly compiled unsigned programs, so meson's sanity check fails on a native
  build (`WinError 4551`). `MPV/windows.cross` declares the build as a cross build, so nothing it compiles gets run;
  never `ldd` the DLLs there either (`MPV/build_windows.sh` reads import tables with objdump). It also blocks MSYS2's
  own unsigned binaries at random (Python `.pyd` modules, `bsdtar`, even `ls` after updates; Event Viewer →
  `Microsoft-Windows-CodeIntegrity/Operational`, event 3077), hence the signed Python for meson and `System32\tar.exe`.
  The built libmpv itself does load with it on.
- Windows player overlay: WebView2's SmartScreen check (`IsReputationCheckingRequired`, on by default) holds every
  `http://app.local` page ~2 s before parsing it; `overlay.rs` turns it off. Measure overlay start-up with
  `POPCORN_PLAYER_DEVTOOLS_PORT` + `performance.getEntriesByType('navigation')` (`domInteractive`).
- `Popcorn.Player.Windows` needs `cargo` on Windows (its build compiles Popcorn.Player.Windows.Native); on Linux it
  only compiles the C# part. The Rust workspace can't be built as a whole on either OS: use `-p`.
- **Linux player: CEF inside a .NET process.** Chromium's start-up resets SIGCHLD/SIGSEGV/SIGTERM/... to SIG_DFL;
  `cef_app.rs` (`signals`) snapshots and restores .NET's handlers. Child processes started before that would never be
  reaped (`Process.WaitForExit` hangs), so the C# side starts loading (FFProbe) and the server link only on
  `PlayerNative.Ready`, which also waits for the mpv render context (vo=libmpv refuses files opened before it exists).
- Linux player CEF disables `LocalNetworkAccessChecks*`: otherwise Chromium cancels the overlay's WebSocket to
  127.0.0.1 after connecting.
- Linux player CEF disables `WaylandFractionalScaleV1` (hack, see comment in `cef_app.rs`): with it on, OSR ignores the
  device scale at fractional scales (1.5) → page at dpr 1, DIP-sized frames upscaled by the compositor → blurry
  overlay. `--force-device-scale-factor` does not help. Recheck after CEF updates: overlay `devicePixelRatio` must
  equal the window scale.
- **Player DMA-BUF video needs a patched libmpv**: `MPV_RENDER_API_TYPE_DMABUF` (`include/mpv/render_dmabuf.h`,
  `video/out/vulkan/libmpv_dmabuf.c`, client API 2.7) lives in the mpv fork (`MPV/src`, written as an upstream-style
  render backend next to OpenGL/D3D11). `make mpv` builds it: meson + libplacebo >= 7.387 as `subprojects/libplacebo`
  (system one is older; commit pinned in the makefile), `-Dwayland=disabled`. Stock libmpv returns
  MPV_ERROR_NOT_IMPLEMENTED and the player uses OpenGL.
- NVIDIA GBM cannot allocate XRGB2101010; the video buffer negotiation (mpv formats ∩ compositor feedback, mpv's
  10-bit-first order) ends up on ARGB2101010 there. The video plane stays opaque (mpv writes alpha 1, opaque region
  set).
- mpv's vo_gpu renderer (used by every libmpv render backend) dereferences missing textures when Vulkan allocations
  fail (e.g. VRAM nearly full while a game runs: libplacebo `VK_ERROR_OUT_OF_DEVICE_MEMORY` → segfault in
  `gl_sc_uniform_texture`). Same with stock `--vf=gpu=api=vulkan`; not specific to the DMA-BUF backend.
- `Directory.Build.targets` pins net10.0 targeting/apphost packs to 10.0.0 and uses the portable `linux-x64` apphost:
  distro SDKs (Arch) otherwise fail restore with `NU1101 Microsoft.NETCore.App.Host.arch-x64` whenever the SDK patch
  and `dotnet-targeting-pack` patch differ. Runtime requirement stays 10.0.0 + roll-forward.
- Two processes share the SQLite DB and `settings.json` (server + player): `SettingsRepository` rereads the file when
  its write time changes.
- Build emits ~200 nullable warnings; not treated as errors.
- `Docs/NOTES.md` holds a personal TODO list and a SQL snippet for migrating a Windows DB to Linux paths.
