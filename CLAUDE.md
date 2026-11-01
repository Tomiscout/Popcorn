# Popcorn

Personal media library + video player. .NET 10 backend (`Popcorn.Server`), React web UI (`Popcorn.Web`), native MPV-based players for Linux and Windows. Primary dev platform is Linux (Arch); Windows support lags behind.

## Products (what actually ships)

Everything is built around `Popcorn.Server`:

| Variant | Entry project | What it is |
|---|---|---|
| Server + browser UI | `Popcorn.Server` | ASP.NET Core host. Serves `Popcorn.Web` bundle from `web/`, WebSocket RPC at `/ws`, REST video streaming at `api/video`. Runs headless / as systemd user service. Browser playback uses the web player in `Popcorn.Web/src/player`. |
| Linux desktop app | `Popcorn.Linux` | **Rust + CEF** (not .NET, not in `Popcorn.sln`). Single CEF Views window showing `{server}/index.html`. Finds the server via `POPCORN_SERVER_URL` or `localhost:$POPCORN_PORT`; if unreachable runs `systemctl --user start popcorn.service` (falls back to spawning `Popcorn.Server` from its own dir with `popcorn.env` if the unit is missing or crash-looping) and shows a waiting page until it answers. Never hosts the server in-process. Adds `PopcornDesktop/<ver>` to the User-Agent → web UI `isDesktopApp` (`Popcorn.Web/src/utils/platform.ts`) hides Float/Dock and sends `FileOpen` (server → native player / default app) instead of using the web player. `-service` is accepted and ignored (old desktop files). |
| Linux player | `Popcorn.Player.Linux` | Separate GTK process. MPV renders into GTK `GLArea` (OpenTK); WebKit overlay shows controls from `Popcorn.Player.Web` (`web-player/`). Launched by server via `LinuxPlayerService`. |
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
Popcorn.Player.Core     Shared player logic: MpvPlayer, PlayerCore, AudioSubtitlesService, ThumbnailService, web bridges, DI root
Popcorn.Player.Linux    GTK player app (see above)
Popcorn.Player.Windows  WPF player app (see above)
Popcorn.Linux           Rust + CEF desktop shell (Cargo crate, see above): src/main.rs (CEF subprocess dispatch), browser.rs (window/handlers), server.rs, log.rs
Popcorn.Linux.Tray      StatusNotifier/DBus tray icon used by server on Linux (ServerTrayHostedService)
Popcorn.Domain.Tests    NUnit tests (tiny)
Popcorn.Web             Main UI: React 19 + TypeScript + Vite (SWC) + Mantine 8 + TanStack Query + zustand
Popcorn.Player.Web      Player overlay UI: React (JS) + Create React App (legacy)
MPVSharp/src            git submodule (Tomiscout/MPVSharp fork); built DLL is referenced from MPVSharp/Nickvision.MPVSharp.dll
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
4. **Player app ↔ its overlay UI** — `Popcorn.Player.Core/Web/` bridges (`IWebBridge`, socket/Chromium variants).

## Build & run

Prereqs: .NET SDK 10 (`global.json`), Node/npm, Rust toolchain + cmake (Popcorn.Linux), libmpv, ffmpeg, GTK4 + WebKitGTK 6 (Linux player). `make` / `build_windows.ps1` run `npm ci` in `Popcorn.Web/` and `Popcorn.Player.Web/` before building (`make npm-ci` to run it alone).

```sh
make mpvsharp                  # init submodule (Tomiscout fork, pinned commit) + build MPVSharp DLL if missing; `all` does this automatically
make update-mpvsharp           # move submodule to latest fork main and rebuild the DLL
make all                       # clean + build both web UIs + Popcorn.Server + Popcorn.Linux (cargo) + Popcorn.Player.Linux, bundle into build/Linux/ (incl. CEF runtime, stripped libcef.so)
make install-linux             # copy bundle to ~/.local/bin/Popcorn, install + enable systemd user service, add ~/.local/share/applications/popcorn.desktop if missing
make build-popcorn-web         # Popcorn.Web only (post-build copies dist/ to Popcorn.Server/web)
make build-popcorn-linux       # desktop shell only: cargo build --release with CEF_PATH (default ~/.cache/popcorn/cef; first build downloads ~1.5 GB CEF)
make build-player-web          # Popcorn.Player.Web only (post-build copies to Popcorn.Player.Linux/web-player)

dotnet run --project Popcorn.Server       # dev server, port from POPCORN_PORT (default 5000), listens on all interfaces
cd Popcorn.Web && npm run dev             # Vite dev server on :5173 against running Popcorn.Server
make build-popcorn-linux bundle-linux && build/Linux/Popcorn.Linux   # desktop shell against running server (needs libcef.so + CEF resources next to it, so run from the bundle)
dotnet test Popcorn.Domain.Tests
cd Popcorn.Web && npm run lint            # eslint; prettier config in .prettierrc
```

Windows: `build_windows.ps1` builds Popcorn.Web + Popcorn.Player.Web + Popcorn.Server + Popcorn.Player.Windows into `Bundle/Windows/` (player in `Player/` subfolder).

Web bundles (`*/web`, `*/web-player`) are gitignored build artifacts — rebuild the web project after UI changes, the .NET projects only copy whatever is there.

## Runtime data

- Data folder: `DirectoryUtils.DataFolder` = `LocalApplicationData/Popcorn/` (`~/.local/share/Popcorn/` on Linux): SQLite DB, `logs/`, `ThumbnailsCache/`, `StreamCache/`.
- Logging: Serilog everywhere, daily rolling files in `logs/`.
- Desktop entry: `Popcorn.Linux/popcorn.desktop` template (`@INSTALL_DIR@` substituted), installed only if missing; launches `Popcorn.Linux`. Window app_id / WM_CLASS is `popcorn` so it matches this file.
- Popcorn.Linux: CEF profile (localStorage, cookies; also the single-instance lock) in `<data>/cef/`, logs in `logs/Popcorn.Linux-YYYYMMDD.txt` + `logs/cef.log`. Env: `POPCORN_SERVER_URL`, `POPCORN_PORT`, `POPCORN_OZONE=x11|wayland`, `POPCORN_NO_SANDBOX=1`.
- systemd: `Popcorn.Server/systemd/popcorn.service` (template, `@INSTALL_DIR@`/`@ENV_FILE@` substituted by make) + `popcorn.env` (copied to `~/.config/popcorn/popcorn.env` only if missing).

## Conventions

- `net10.0`, nullable + implicit usings enabled. Primary constructors common in newer code.
- Platform-specific DI via `RuntimeInformation.IsOSPlatform` in `Popcorn.Server/ServiceRegister.cs` and `Popcorn.Player.Core/ServiceRegister.cs`. Native abstractions live in `Popcorn.Domain/Interfaces` (`IPlayerService`, `INativeFolderService`, `INativeFileAssosiation` — note spelling) with Linux/Windows impls in `Popcorn.PlayerServer` / `Popcorn.Shared`.
- Repositories and most services are singletons; `FileStreamService`, `VideoService` scoped.
- DB schema changes: add a new FluentMigrator class in `Popcorn.Persistence/Migrations/` named `<Name>_<yyyyMMdd>.cs`, plus Dapper map updates in `Persistence/Dapper/`. Migrations run on startup only when environment != Development.
- New WebSocket feature: add constant to `SocketRequests.cs` + `socket-requests.ts`, request model under `Popcorn.Server/Models/Requests`, handler method on a `[WebSocketService]` class in `Popcorn.Server/Services`, TS contract under `Popcorn.Web/src/model`.
- The web player (`src/player`) is a lazy chunk (`React.lazy` in `App.tsx`): keep it out of static imports from non-player code (type-only `import type` is fine) so the desktop app never downloads it.
- `Popcorn.Web` path alias `@` → `src/`. Pages in `src/pages/*`, state in `src/state/*` (zustand stores + contexts).
- Both web projects' `post-build.js` also emit `index-chrome.html` / `index-linux.html` with `<base href>` for `app.local` custom-scheme loading in embedded webviews.

## Known issues / gotchas

- **Windows server DI is incomplete**: `Popcorn.Server/ServiceRegister.cs` registers only `INativeFileAssosiation` on Windows — `IPlayerService` (`WindowsPlayerService`), `INativeFolderService`, `INativeProcessRunner` are missing, so `FileWebService` cannot resolve on Windows.
- **`Popcorn.Player.Windows` targets `net8.0-windows`** while `Popcorn.Player.Core` is `net10.0` — it will not build until bumped to `net10.0-windows10.0.19041.0` (also update path in `build_windows.ps1`). Its post-build uses `xcopy` (Windows only).
- `Popcorn.Player.*` / `Popcorn.Player.Core` fail to compile (when built directly with dotnet) until `make mpvsharp` has produced `MPVSharp/Nickvision.MPVSharp.dll`. `Popcorn.Server` / `Popcorn.Linux` build without it.
- `Popcorn.Domain.csproj` (Dapper) and `Popcorn.Application.csproj` (Serilog) contain stale `<Reference HintPath="..\..\..\.nuget\...">` entries; they produce MSB3245 warnings and should be replaced by PackageReferences.
- `WindowsPlayerService.GetPlayerExePath` dev fallback walks up to a directory literally named `Popcorn` — depends on repo checkout folder name.
- `Directory.Build.targets` pins net10.0 targeting/apphost packs to 10.0.0 and uses the portable `linux-x64` apphost: distro SDKs (Arch) otherwise fail restore with `NU1101 Microsoft.NETCore.App.Host.arch-x64` whenever the SDK patch and `dotnet-targeting-pack` patch differ. Runtime requirement stays 10.0.0 + roll-forward.
- Build emits ~200 nullable warnings; not treated as errors.
- `README.md` holds a personal TODO list and a SQL snippet for migrating a Windows DB to Linux paths.

## Other files

- `.github/copilot-instructions.md`, `.opencode/AGENTS.md`, `.clinerules/`, `.windsurf/`, `.agents/skills/caveman*` — instructions for other AI tools (caveman terse-style skill). Not relevant to the build.
- `Scripts/dirCompare.js`, `misc/bench_thumbs.py` — ad-hoc utilities.
