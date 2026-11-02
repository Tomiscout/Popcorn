# Popcorn

Personal media library + video player. .NET 10 backend (`Popcorn.Server`), React web UI (`Popcorn.Web`), native MPV-based players for Linux and Windows. Primary dev platform is Linux (Arch); Windows support lags behind.

## Products (what actually ships)

Everything is built around `Popcorn.Server`:

| Variant | Entry project | What it is |
|---|---|---|
| Server + browser UI | `Popcorn.Server` | ASP.NET Core host. Serves `Popcorn.Web` bundle from `web/`, WebSocket RPC at `/ws`, REST video streaming at `api/video`. Runs headless / as systemd user service. Browser playback uses the web player in `Popcorn.Web/src/player`. |
| Linux desktop app | `Popcorn.Linux` | **Rust + CEF** (not .NET, not in `Popcorn.sln`). Single CEF Views window showing `{server}/index.html`. Finds the server via `POPCORN_SERVER_URL` or `localhost:$POPCORN_PORT`; if unreachable runs `systemctl --user start popcorn.service` (falls back to spawning `Popcorn.Server` from its own dir with `popcorn.env` if the unit is missing or crash-looping) and shows a waiting page until it answers. Never hosts the server in-process. Adds `PopcornDesktop/<ver>` to the User-Agent → web UI `isDesktopApp` (`Popcorn.Web/src/utils/platform.ts`) hides Float/Dock and sends `FileOpen` (server → native player / default app) instead of using the web player. `-service` is accepted and ignored (old desktop files). |
| Linux player | `Popcorn.Player.Linux` + `Popcorn.Player.Native` | **Wayland only.** .NET executable (player logic: DB, navigation, mpv control via `LibMpvClient`, overlay WebSocket bridge, server socket) that P/Invokes `libpopcorn_player.so` (Rust): raw Wayland window (smithay-client-toolkit); mpv core rendering into a video subsurface placed *below* the main surface — with a libmpv that has the DMA-BUF render API (`render_dmabuf.h`, our mpv patch) mpv renders with its own Vulkan device into GBM buffers we attach as linux-dmabuf `wl_buffer`s, otherwise libmpv OpenGL render API on EGL; CEF off-screen overlay (the shared player UI from `Popcorn.Web`, `player.html` entry, served as `http://app.local/index-chrome.html` from `web-player/`) on the main surface — CEF's shared-texture DMA-BUFs copied on the GPU (EGL/GLES blit) into our own dmabuf buffers, or wl_shm from software paint as fallback; input is forwarded to CEF. Works standalone without the server. Launched by server via `LinuxPlayerService`. |
| Windows player | `Popcorn.Player.Windows` | WPF + WebView2 counterpart of Linux player. Launched by server via `WindowsPlayerService` from `<server dir>\Player\Popcorn.Player.Windows.exe`. |

The old Windows Blazor/WebView app (`Popcorn/`, `Popcorn.WebView`, `Popcorn.Installer`) was removed — do not reintroduce references to it.

## Project map

```
Popcorn.Server          ASP.NET host, WebSocket services, VideoController, DI root (ServiceRegister.cs)
Popcorn.Application     Business services: FolderService, MediaItemService, ItemHistoryService, MediaItemRefreshService, FileService
Popcorn.Domain          Models, interfaces (IPlayerService, IMediaItemRepository, INative*), MediaLoader (folder scanning)
Popcorn.Persistence     SQLite via Dapper + Dapper.FluentMap + FluentMigrator; repositories; Migrations/
Popcorn.Shared          FFmpeg helpers, DirectoryUtils (data paths), INativeProcessRunner + Linux/Windows impls
Popcorn.SearchProviders TMDb / OMDb metadata + torrent search scrapers (PirateBay, YTS, Rarbg, Linkomanija)
Popcorn.Torrents        MonoTorrent wrapper (currently disabled in DI)
Popcorn.SegmentFinder   Audio fingerprint intro/outro detection (not referenced by anything currently)
Popcorn.PlayerServer    Server side of server<->player IPC: Linux/ (unix socket, NetCoreServer), Windows/ (named pipes, PipeMethodCalls)
PopcornPlayer.Shared    IPC contracts (IPipeClient/IPipeServer)
Popcorn.Player.Core     Shared player logic: MpvPlayer (on IMpvClient), LibMpvClient (libmpv P/Invoke), PlayerCore, AudioSubtitlesService, ThumbnailService, web bridges, DI root
Popcorn.Player.Linux    Linux player host (.NET): Program.cs + Native/PlayerNative.cs (C ABI of libpopcorn_player.so)
Popcorn.Player.Native   Rust crate (not in Popcorn.sln): cdylib libpopcorn_player.so + `popcorn-player-cef` (CEF subprocess exe). src/lib.rs (C ABI), window.rs (Wayland, input), video.rs (mpv render thread: DMA-BUF or EGL output), overlay_gpu.rs (CEF shared texture -> our dmabufs), dmabuf.rs (linux-dmabuf feedback, GBM, wl_buffers), cef_app.rs (OSR browser, app.local scheme), keys.rs, ffi.rs
Popcorn.Player.Windows  WPF player app (see above)
Popcorn.Linux           Rust + CEF desktop shell (Cargo crate, see above): src/main.rs (CEF subprocess dispatch), browser.rs (window/handlers), server.rs, log.rs
Popcorn.Linux.Tray      StatusNotifier/DBus tray icon used by server on Linux (ServerTrayHostedService)
Popcorn.Domain.Tests    NUnit tests (tiny)
Popcorn.Web             Main UI: React 19 + TypeScript + Vite (SWC) + Mantine 8 + TanStack Query + zustand. Also the native players' overlay (player.html, `npm run build:player`)
MPVSharp/src            git submodule (Tomiscout/MPVSharp fork); built DLL referenced only by Popcorn.Player.Windows (MpvSharpClient adapter)
```

Dependency direction: `Server → Application/PlayerServer/SearchProviders → Persistence → Domain → Shared`. `Player.* → Player.Core → Application/Persistence`.

## Communication

1. **Web UI ↔ Server** — WebSocket `/ws`, custom RPC.
   - Text frames: JSON `{ type, payload }`, camelCase. Binary frames: UTF-8 prefix `"type,param1,param2;"` followed by bytes (images/thumbnails).
   - Server handlers: classes marked `[WebSocketService]`, methods marked `[WebSocketEvent(SocketRequests.X)]` / `[WebSocketBinaryEvent]`, discovered by reflection in `WebSocketServices.RegisterWebServices`. Handlers are resolved per-message from a DI scope.
   - Event names: `Popcorn.Server/Models/SocketRequests.cs` must stay in sync with `Popcorn.Web/src/socket/socket-requests.ts`.
   - Client: `Popcorn.Web/src/socket/websocket-client.ts` (auto-reconnect, send queue), hooks `useSocketEvents` / `useSocketBlobEvents`.
2. **Web UI ↔ Server (HTTP)** — `VideoController` (`api/video/{fileId}`, `/metadata`, `/audio/{trackId}`, `/subtitles/{trackId}`) for in-browser playback.
3. **Server ↔ native player** — Linux: unix socket `/tmp/popcorn_bridge.sock` (`PlayerBridgeServer`, JSON events like `LoadMediaEvent`). Windows: named pipes, see `Docs/Video player connection.md`.
4. **Player app ↔ its overlay UI** — `Popcorn.Player.Core/Web/` bridges (`IWebBridge`, socket/Chromium variants). Linux: Fleck WebSocket on 127.0.0.1, address injected into the overlay HTML (`window.wsAddress`) by the native scheme handler.
5. **Linux player .NET ↔ native** — C ABI in `Popcorn.Player.Native/src/lib.rs` (`pn_init`, `pn_mpv_core`, `pn_run`, `pn_quit`, `pn_set_fullscreen`, `pn_set_title`, `pn_destroy`; callbacks for log + window events incl. `Ready`). C# attaches its own mpv client handle (`mpv_create_client`) to the core the native layer owns.

## Build & run

Prereqs: .NET SDK 10 (`global.json`), Node/npm, Rust toolchain + cmake (Popcorn.Linux, Popcorn.Player.Native), libmpv, ffmpeg, libwayland + libEGL + libgbm + libdrm + libxkbcommon (Linux player). Optional for the player's zero-copy video: a libmpv built with the DMA-BUF render API (see below). `make` / `build_windows.ps1` run `npm ci` in `Popcorn.Web/` before building (`make npm-ci` to run it alone).

```sh
make mpvsharp                  # init submodule (Tomiscout fork, pinned commit) + build MPVSharp DLL if missing (Windows player only)
make update-mpvsharp           # move submodule to latest fork main and rebuild the DLL
make all                       # clean + build both web UIs + Popcorn.Server + Popcorn.Linux (cargo) + Popcorn.Player.Native (cargo) + Popcorn.Player.Linux, bundle into build/Linux/ (incl. CEF runtime, stripped libcef.so)
make build-popcorn-player-native  # player native layer only (cargo, same CEF_PATH/CEF version as Popcorn.Linux)
make all MPV_LIB=../mpv/build/libmpv.so.2   # also bundle a libmpv with the DMA-BUF render API (next to libpopcorn_player.so)
make install-linux             # stop running instances (make kill-popcorn), copy bundle to ~/.local/bin/Popcorn, install + enable systemd user service, add ~/.local/share/applications/popcorn.desktop if missing
make build-popcorn-web         # Popcorn.Web only (post-build copies dist/ to Popcorn.Server/web)
make build-popcorn-linux       # desktop shell only: cargo build --release with CEF_PATH (default ~/.cache/popcorn/cef; first build downloads ~1.5 GB CEF)
make build-player-web          # player overlay only: Popcorn.Web `npm run build:player` -> dist-player/, copied to Popcorn.Player.Linux/web-player

dotnet run --project Popcorn.Server       # dev server, port from POPCORN_PORT (default 5000), listens on all interfaces
cd Popcorn.Web && npm run dev             # Vite dev server on :5173 against running Popcorn.Server
make build-popcorn-linux bundle-linux && build/Linux/Popcorn.Linux   # desktop shell against running server (needs libcef.so + CEF resources next to it, so run from the bundle)
build/Linux/Popcorn.Player.Linux <file|fileId>   # player, also only from the bundle (libpopcorn_player.so, popcorn-player-cef, libcef.so side by side)
dotnet test Popcorn.Domain.Tests
cd Popcorn.Web && npm run lint            # eslint; prettier config in .prettierrc
```

Windows: `build_windows.ps1` builds Popcorn.Web (UI + `build:player` overlay) + Popcorn.Server + Popcorn.Player.Windows into `Bundle/Windows/` (player in `Player/` subfolder).

Web bundles (`*/web`, `*/web-player`) are gitignored build artifacts — rebuild the web project after UI changes, the .NET projects only copy whatever is there.

## Runtime data

- Data folder: `DirectoryUtils.DataFolder` = `LocalApplicationData/Popcorn/` (`~/.local/share/Popcorn/` on Linux): SQLite DB, `logs/`, `ThumbnailsCache/`, `StreamCache/`.
- Logging: Serilog everywhere, daily rolling files in `logs/`.
- Desktop entry: `Popcorn.Linux/popcorn.desktop` template (`@INSTALL_DIR@` substituted), installed only if missing; launches `Popcorn.Linux`. Window app_id / WM_CLASS is `popcorn` so it matches this file.
- Popcorn.Linux: CEF profile (localStorage, cookies; also the single-instance lock) in `<data>/cef/`, logs in `logs/Popcorn.Linux-YYYYMMDD.txt` + `logs/cef.log`. Env: `POPCORN_SERVER_URL`, `POPCORN_PORT`, `POPCORN_OZONE=x11|wayland`, `POPCORN_NO_SANDBOX=1`.
- Linux player: logs in `logs/Popcorn.Player-YYYYMMDD.txt` (native messages prefixed `[native]`, overlay console `[overlay]`) + `logs/cef-player.log`; throwaway CEF profile per process in `$XDG_RUNTIME_DIR/popcorn-player-cef-<pid>`. Env: `POPCORN_PLAYER_DEVTOOLS_PORT` (chrome://inspect), `POPCORN_NO_SANDBOX=1`, `POPCORN_PLAYER_VIDEO=opengl` (force the EGL video output), `POPCORN_PLAYER_OVERLAY=shm` (force software overlay). F12 opens DevTools, F11 toggles fullscreen. The log says which outputs were picked (`Video: mpv DMA-BUF output ...` / `Video: mpv OpenGL output`, `Overlay: GPU copy ...`).
- systemd: `Popcorn.Server/systemd/popcorn.service` (template, `@INSTALL_DIR@`/`@ENV_FILE@` substituted by make) + `popcorn.env` (copied to `~/.config/popcorn/popcorn.env` only if missing).

## Conventions

- `net10.0`, nullable + implicit usings enabled. Primary constructors common in newer code.
- Platform-specific DI via `RuntimeInformation.IsOSPlatform` in `Popcorn.Server/ServiceRegister.cs` and `Popcorn.Player.Core/ServiceRegister.cs`. Native abstractions live in `Popcorn.Domain/Interfaces` (`IPlayerService`, `INativeFolderService`, `INativeFileAssosiation` — note spelling) with Linux/Windows impls in `Popcorn.PlayerServer` / `Popcorn.Shared`.
- Native players: `SettingsModel.Players` is an ordered `PlayerOption` list (built-ins `Popcorn` + `Default`, user-added `Program` = executable gets the file path as its argument, `Script` = shell line with `%args` → quoted path). `PlayerLaunchService.OpenAsync` (used by `FileOpen`) tries enabled entries in order and falls through on failure. `SettingsModel.NormalizePlayers()` (run on load/save) keeps the built-ins present; legacy `UseInternalPlayer` only seeds the initial order.
- Repositories and most services are singletons; `FileStreamService`, `VideoService` scoped.
- DB schema changes: add a new FluentMigrator class in `Popcorn.Persistence/Migrations/` named `<Name>_<yyyyMMdd>.cs`, plus Dapper map updates in `Persistence/Dapper/`. Migrations run on startup only when environment != Development.
- New WebSocket feature: add constant to `SocketRequests.cs` + `socket-requests.ts`, request model under `Popcorn.Server/Models/Requests`, handler method on a `[WebSocketService]` class in `Popcorn.Server/Services`, TS contract under `Popcorn.Web/src/model`.
- The web player (`src/player`) is a lazy chunk (`React.lazy` in `App.tsx`): keep it out of static imports from non-player code (type-only `import type` is fine) so the desktop app never downloads it.
- `Popcorn.Web` path alias `@` → `src/`. Pages in `src/pages/*`, state in `src/state/*` (zustand stores + contexts).
- `Popcorn.Web/post-build.js` also emits `index-chrome.html` / `index-linux.html` (unused leftovers); `post-build-player.js` turns `dist-player/player.html` into `index.html` + `index-chrome.html` (`<base href="http://app.local/">`) and replaces `Popcorn.Player.Linux/web-player`.
- **One player UI, several hosts.** `src/player/PlayerOverlay.tsx` (controls, top bar, progress/thumbnails/intro-outro skip, tracks, episodes, errors) is shared. Components only read the zustand player store and post commands on the local bus (`utils/events`: `play_status_changed`, `set_playback_time`, `skip_seconds`, `volume`, `audio_track_selected`, `subtitle_track_selected`, `folder_open`, `thumb_requested`, `player_close`, ...); they must not talk to the server socket or a `<video>` directly. Hosts: `PopcornPlayer.tsx` (browser: HTML5 video, server WebSocket, floating/PiP) and `native/NativePlayer.tsx` (native players: maps the C# `WebEventHandler` events to the store and forwards commands; transport `native/native-bridge.ts` = `window.wsAddress` WebSocket on Linux, `chrome.webview` on Windows). Host-specific UI is switched via `player-features.ts` (`floating`, `fileDrop`, `addSubtitle`, `subtitleDelay`) instead of forking components. New overlay command → handle it in both hosts and, for native, in `WebEventHandler.OnMessage` (unknown names throw there).
- `npm run dev:player` serves the overlay from Vite; open `player.html?ws=ws://127.0.0.1:<port>` against a running Linux player (port = Fleck server, usually 8181). When the overlay (re)connects, C# replays the current file's state (`PlayerCore.ResendState`).

## Known issues / gotchas

- **Windows server is untested**: `ServiceRegister.cs` now registers `WindowsPlayerService`, `WindowsFolderService` (pickers via STA PowerShell WinForms dialogs), `WindowsFileAssociation`, `WindowsProcessRunner`, but the Windows server path has not been run end to end; the Linux tray registrations have no Windows counterpart.
- **`Popcorn.Player.Windows` targets `net8.0-windows`** while `Popcorn.Player.Core` is `net10.0` — it will not build until bumped to `net10.0-windows10.0.19041.0` (also update path in `build_windows.ps1`). Its post-build uses `xcopy` (Windows only).
- `Popcorn.Player.Windows` fails to compile until `make mpvsharp` has produced `MPVSharp/Nickvision.MPVSharp.dll`. Everything else (incl. the Linux player) builds without it.
- **Linux player: CEF inside a .NET process.** Chromium's start-up resets SIGCHLD/SIGSEGV/SIGTERM/... to SIG_DFL; `cef_app.rs` (`signals`) snapshots and restores .NET's handlers. Child processes started before that would never be reaped (`Process.WaitForExit` hangs), so the C# side starts loading (FFProbe) and the server link only on `PlayerNative.Ready`, which also waits for the mpv render context (vo=libmpv refuses files opened before it exists).
- Linux player CEF disables `LocalNetworkAccessChecks*`: otherwise Chromium cancels the overlay's WebSocket to 127.0.0.1 after connecting.
- Linux player CEF disables `WaylandFractionalScaleV1` (hack, see comment in `cef_app.rs`): with it on, OSR ignores the device scale at fractional scales (1.5) -> page at dpr 1, DIP-sized frames upscaled by the compositor -> blurry overlay. `--force-device-scale-factor` does not help. Recheck after CEF updates: overlay `devicePixelRatio` must equal the window scale.
- `index-linux.html` (app.local:// base for WebKit) is no longer used by any player.
- **Player DMA-BUF video needs a patched libmpv**: `MPV_RENDER_API_TYPE_DMABUF` (`include/mpv/render_dmabuf.h`, `video/out/vulkan/libmpv_dmabuf.c`, client API 2.7) lives in the local mpv checkout (`~/code/mpv`, uncommitted, written as an upstream-style render backend next to OpenGL/D3D11). Building it: meson (venv) + libplacebo >= 7.387 as `subprojects/libplacebo` (system one is older), `meson setup build -Dlibmpv=true -Dvulkan=enabled -Dwayland=disabled` (wayland-protocols not installed; not needed for libmpv). Stock libmpv returns MPV_ERROR_NOT_IMPLEMENTED and the player uses OpenGL.
- NVIDIA GBM cannot allocate XRGB2101010; the video buffer negotiation (mpv formats ∩ compositor feedback, mpv's 10-bit-first order) ends up on ARGB2101010 there. The video plane stays opaque (mpv writes alpha 1, opaque region set).
- mpv's vo_gpu renderer (used by every libmpv render backend) dereferences missing textures when Vulkan allocations fail (e.g. VRAM nearly full while a game runs: libplacebo `VK_ERROR_OUT_OF_DEVICE_MEMORY` -> segfault in `gl_sc_uniform_texture`). Same with stock `--vf=gpu=api=vulkan`; not specific to the DMA-BUF backend.
- `Popcorn.Domain.csproj` (Dapper) and `Popcorn.Application.csproj` (Serilog) contain stale `<Reference HintPath="..\..\..\.nuget\...">` entries; they produce MSB3245 warnings and should be replaced by PackageReferences.
- `WindowsPlayerService.GetPlayerExePath` dev fallback walks up to a directory literally named `Popcorn` — depends on repo checkout folder name.
- `Directory.Build.targets` pins net10.0 targeting/apphost packs to 10.0.0 and uses the portable `linux-x64` apphost: distro SDKs (Arch) otherwise fail restore with `NU1101 Microsoft.NETCore.App.Host.arch-x64` whenever the SDK patch and `dotnet-targeting-pack` patch differ. Runtime requirement stays 10.0.0 + roll-forward.
- Build emits ~200 nullable warnings; not treated as errors.
- `README.md` holds a personal TODO list and a SQL snippet for migrating a Windows DB to Linux paths.

## Other files

- `.github/copilot-instructions.md`, `.opencode/AGENTS.md`, `.clinerules/`, `.windsurf/`, `.agents/skills/caveman*` — instructions for other AI tools (caveman terse-style skill). Not relevant to the build.
- `Scripts/dirCompare.js`, `misc/bench_thumbs.py` — ad-hoc utilities.
