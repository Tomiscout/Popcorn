Respond terse like smart caveman. All technical substance stay. Only fluff die.

Rules:

- Drop: articles (a/an/the), filler (just/really/basically), pleasantries, hedging
- Fragments OK. Short synonyms. Technical terms exact. Code unchanged.
- Pattern: [thing] [action] [reason]. [next step].
- Not: "Sure! I'd be happy to help you with that."
- Yes: "Bug in auth middleware. Fix:"

Switch level: /caveman lite|full|ultra|wenyan
Stop: "stop caveman" or "normal mode"

Auto-Clarity: drop caveman for security warnings, irreversible actions, user confused. Resume after.

Boundaries: code/commits/PRs written normal.

# Popcorn - Copilot Instructions

Media server + player. .NET 10 backend, React frontends, cross-platform (Windows/Linux).

## Architecture

- **`Popcorn.Server`** — ASP.NET Core host. Serves static `web/` bundle, exposes REST controllers + WebSocket API at `/ws`.
- **`Popcorn.Web`** — React 19 + Vite + Mantine v8 + TanStack Query. Main UI. Connects to server WebSocket.
- **`Popcorn.Linux`** — Rust + CEF desktop shell (not .NET). One window showing the UI served by `Popcorn.Server`; starts `popcorn.service` via systemd if the server isn't running. Never hosts the server itself.
- **`Popcorn.Player.Linux`** — Separate GTK app. Renders video via MPV (OpenGL in GTK `GLArea`) with WebKit overlay for player controls (`web-player/`).
- **`Popcorn.Player.Core`** — Shared player logic: `MpvPlayer`, `PlayerCore`, `AudioSubtitlesService`, `ThumbnailService`, pipe/socket event handlers.
- **`Popcorn.Player.Windows`** — WPF player counterpart.
- **`Popcorn.PlayerServer`** — Platform-specific launchers for the internal player (named pipes on Windows, Unix domain socket on Linux).

### Communication Flows

1. Web UI ↔ Server: WebSocket (`/ws`) — custom attribute-driven RPC.
2. Server ↔ Player: Linux uses Unix domain socket at `/tmp/popcorn_bridge.sock` (`NetCoreServer`). Windows uses named pipes (`PipeMethodCalls`). See `Docs/Video player connection.md`.

## Build & Run

- `make all` — builds web UIs + server + desktop shell (cargo, downloads CEF to `~/.cache/popcorn/cef`) + Linux player and bundles into `build/Linux/`.
- `make install-linux` — copies to `~/.local/bin/Popcorn`, installs/enables systemd user service.
- `make build-popcorn-web` / `make build-player-web` — npm builds only.
- `make update-mpvsharp` — pulls git submodule and builds `Nickvision.MPVSharp` (archived upstream, referenced as local DLL).
- `make clean` — wipes build outputs.

Run dev server: `dotnet run --project Popcorn.Server` (port from `POPCORN_PORT` or 5000).  
Run Linux desktop shell: `make build-popcorn-linux bundle-linux && build/Linux/Popcorn.Linux` (CEF files must sit next to the exe; needs a running server, `POPCORN_SERVER_URL` overrides the URL).

## Key Patterns

### WebSocket Services (Server)

Register handlers via reflection. Example in `Popcorn.Server/Services/MediaItemWebService.cs`:

```csharp
[WebSocketService]
public class MediaItemWebService(WebSocketBus bus, ...) {
    [WebSocketEvent(SocketRequests.MediaItem)]
    public async Task SendMediaItems(MediaItemRequest item) { ... }
}
```

Scanned by `WebSocketServices.RegisterWebServices(bus, typeof(Startup).Assembly)` in `Startup.cs`.

### Post-Build Patching

Both frontends use `post-build.js` to inject `<base href="http://app.local/">` for WebKit local scheme. Linux variant rewrites to `app.local:/`.

### DI Registration

Platform-specific services registered conditionally in `Popcorn.Server/ServiceRegister.cs` and `Popcorn.Player.Core/ServiceRegister.cs` via `RuntimeInformation.IsOSPlatform`.

### Data Layer

SQLite via Dapper + FluentMigrator. `DapperContext` holds connection string and type mappings. Repositories are singletons. Migrations run automatically in non-Development env.

### Logging

Serilog everywhere. Logs roll daily to `~/.local/share/Popcorn/logs/` (Linux) or equivalent LocalApplicationData path.

## Project Boundaries

| Project                | References                                                                                                                              | Role                         |
| ---------------------- | --------------------------------------------------------------------------------------------------------------------------------------- | ---------------------------- |
| `Popcorn.Server`       | `Popcorn.Application`, `Popcorn.Domain`, `Popcorn.Persistence`, `Popcorn.PlayerServer`, `Popcorn.SearchProviders`, `Popcorn.Linux.Tray` | Host + API                   |
| `Popcorn.Application`  | `Popcorn.Persistence`                                                                                                                   | Business services            |
| `Popcorn.Domain`       | `Popcorn.Shared`                                                                                                                        | Models, interfaces, enums    |
| `Popcorn.Persistence`  | `Popcorn.Domain`                                                                                                                        | Dapper + SQLite + migrations |
| `Popcorn.Player.Core`  | `Popcorn.Persistence`, `Popcorn.Shared`                                                                                                 | Player logic                 |
| `Popcorn.Shared`       | —                                                                                                                                       | Common utils, interfaces     |
| `PopcornPlayer.Shared` | —                                                                                                                                       | Pipe event contracts         |

## Important Files

- `Popcorn.Server/Startup.cs` — Server pipeline, WebSocket endpoint, static files, CORS.
- `Popcorn.Server/WebSocketBus.cs` / `WebSocketServices.cs` — Custom WebSocket RPC infra.
- `Popcorn.Server/ServiceRegister.cs` — DI composition root for server services.
- `Popcorn.Player.Core/ServiceRegister.cs` — DI composition root for player.
- `Popcorn.Domain/MediaLoader.cs` — Folder scanning logic for video/subtitle formats.
- `makefile` — Primary build orchestration.

## Conventions

- Target framework: `net10.0`.
- Nullable enabled, implicit usings enabled.
- `Popcorn.Web` uses Vite + SWC; `Popcorn.Player.Web` still uses Create React App (legacy).
- MPVSharp is a local DLL reference (`MPVSharp/Nickvision.MPVSharp.dll`), not a NuGet package.
