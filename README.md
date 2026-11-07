# Popcorn

Personal media library and video player. A .NET 10 server scans your folders, serves a React web UI with plugin-provided search — media sources (TMDb bundled) and file sources such as torrent sites, see [Docs/Plugins.md](Docs/Plugins.md)); videos play in the browser or in a native MPV-based player.

## Components

- **Popcorn.Server** – ASP.NET Core host: web UI, WebSocket API (`/ws`), video streaming (`api/video`).
- **Popcorn.Web** – React + TypeScript + Vite UI, also used as the native player overlay.
- **Popcorn.Linux** – Rust + CEF desktop shell around the web UI.
- **Popcorn.Player.Linux** – Wayland player (libmpv + CEF overlay, native layer in `Popcorn.Player.Native`).
- **Popcorn.Player.Windows** – WPF + WebView2 player.

## Requirements

.NET SDK 10, Node/npm, ffmpeg, libmpv. For the Linux desktop app and player: Rust toolchain, cmake, Wayland/EGL/GBM/DRM/xkbcommon libraries.

## Quick start

```sh
# Dev
dotnet run --project Popcorn.Server     # http://localhost:5000 (POPCORN_PORT)
cd Popcorn.Web && npm ci && npm run dev  # Vite dev server on :5173

# Linux build + install (systemd user service + desktop entry)
make all
make install-linux
```

Windows: `build_windows.ps1` (output in `Bundle/Windows/`).

Data, database and logs live in `~/.local/share/Popcorn/` (Linux) or `%LOCALAPPDATA%\Popcorn\` (Windows).

See [CLAUDE.md](CLAUDE.md) for architecture and detailed build notes, [Docs/NOTES.md](Docs/NOTES.md) for TODOs.
