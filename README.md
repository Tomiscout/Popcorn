<p align="center">
  <img src="Docs/images/popcorn.png" alt="Popcorn" width="160">
</p>

<h1 align="center">Popcorn</h1>

<p align="center">
  <b>Media library server with native and browser video players.</b><br>
</p>

<p align="center">
  <a href="https://github.com/Tomiscout/Popcorn/releases/latest"><img src="https://img.shields.io/github/v/release/Tomiscout/Popcorn?label=release&color=e03131" alt="Latest release"></a>
  <img src="https://img.shields.io/badge/.NET-10-512BD4?logo=dotnet&logoColor=white" alt=".NET 10">
  <img src="https://img.shields.io/badge/React-19-61DAFB?logo=react&logoColor=black" alt="React 19">
  <img src="https://img.shields.io/badge/Rust-native%20shells-000000?logo=rust&logoColor=white" alt="Rust">
  <img src="https://img.shields.io/badge/platform-Linux%20%7C%20Windows-lightgrey" alt="Linux | Windows">
  <a href="LICENSE"><img src="https://img.shields.io/badge/license-GPL--3.0-blue" alt="GPL-3.0"></a>
</p>

---

## What it does

| | |
|---|---|
| 📚 **Library** | Scans your folders, groups files into movies and shows, recognises seasons/episodes from release names, keeps watch history. |
| ▶️ **Playback** | In the browser (HTML5 video, picture-in-picture) or in a native **mpv** player with the same overlay UI — zero-copy GPU video on Linux/Wayland. |
| ⏭️ **Intro / outro skip** | Finds intros and outros by audio fingerprinting across episodes and skips them for you. |
| ⬇️ **Downloads** | Built-in file downloader, where you source files from your own plugins. Supports watching while it downloads. |
| 🔌 **Plugins** | Search, metadata, posters and file sources all come from plugins (TMDb bundled). |
| 🖥️ **Apps** | Native windows and linux apps and browser UI for your library. You can also run it as a headless server and access it soley through the browser. |

## How it works

```
                ┌──────────────────────────────┐
 browser ──────►│        Popcorn.Server        │◄────── plugins/
 desktop app ──►│  .NET 10 · web UI · /ws API  │        (search, metadata,
                │  SQLite library · streaming  │         file sources)
                └──────────────┬───────────────┘
                               │ launches
                               ▼
                    native player (libmpv + web overlay)
                    Linux: Wayland + CEF · Windows: Win32 + WebView2
```

Everything is built around `Popcorn.Server`. The UI is the same in the browser, the desktop apps and the native players' overlay.

## Install

Grab the latest release from the [Releases page](https://github.com/Tomiscout/Popcorn/releases/latest):

- **Windows** — run `Popcorn.Installer.Windows.exe` installer.
- **Linux** — run `Popcorn.Installer.Linux` installer. Only supports systemd-based distros at the moment.

Then open the **Popcorn** app, or <http://localhost:5000> in a browser.

## Run from source

Requirements: **.NET SDK 10**, **Node/npm**, **ffmpeg**, **libmpv**.

```sh
dotnet run --project Popcorn.Server       # http://localhost:5000  (port: POPCORN_PORT)
cd Popcorn.Web && npm ci && npm run dev   # optional: Vite dev server with hot reload on :5173
```

Your library, database and logs live in `~/.local/share/Popcorn/` (Linux) or `%LOCALAPPDATA%\Popcorn\` (Windows).

## Build

### Linux

Extra requirements for the desktop app and player: Rust toolchain, cmake, and the Wayland / EGL / GBM / DRM / xkbcommon libraries.

```sh
make mpv              # build patched libmpv with the DMA-BUF render API (zero-copy video)
make all              # web UI + server + desktop app + player → Bundle/Linux/
make install-linux    # installs build to your system. copy to ~/.local/bin/Popcorn, systemd user service, desktop entry
```

The first build downloads CEF (~1.5 GB) into `~/.cache/popcorn/cef`.

### Windows

Needs the .NET 10 SDK, Node/npm, git and Rust (MSVC) on `PATH`.

```powershell
.\build_windows.ps1     # libmpv (first run) + web UI + server + app + player → Bundle\Windows\
.\install_windows.ps1   # install to %LOCALAPPDATA%\Programs\Popcorn
```

## Plugins

Popcorn has no file search of its own — **plugins provide it**. You have to build them yourself.
A plugin is a .NET assembly (plus an optional React UI module) that can be any of:

| Kind | Interface | Answers |
|---|---|---|
| 🎬 Media source | `IMediaSourcePlugin` | *What exists?* — search results and details pages |
| 📦 File source | `IFileSourcePlugin` | *Where to get it?* — magnet, torrent, direct or page links |
| 🖼️ Image provider | `IImageProvider` | Posters and backdrops for library items |

Popcorn decides what to do with a link by its kind (torrent → built-in downloader, direct → direct download);

```
<id>/
  plugin.json        id, name, apiVersion, assembly, optional UI module, icon and settings form
  MyPlugin.dll       references only Popcorn.Plugins.Abstractions
  web/index.js       optional React components (SearchCard, Details, FileCard…)
```

Install a plugin by uploading its zip in **Settings → Plugins**, where you can also enable, reorder and configure them. Plugins declare their settings in `plugin.json` and get a generated form — no UI code needed.

Start from the bundled TMDb plugin, [`Plugins/Popcorn.Plugin.Tmdb`](Plugins/Popcorn.Plugin.Tmdb) (`make build-plugins`), and read the full guide in **[Docs/Plugins.md](Docs/Plugins.md)**.

## Learn more

- [CLAUDE.md](CLAUDE.md) — project map, build commands, conventions and gotchas
- [Docs/Architecture.md](Docs/Architecture.md) — how the apps fit together and talk to each other
- [Docs/Features.md](Docs/Features.md) — how the library and player features work
- [Docs/Building.md](Docs/Building.md) — builds, bundles, releases
- [Docs/Plugins.md](Docs/Plugins.md) — plugin API
- [Popcorn.Installer/README.md](Popcorn.Installer/README.md) — installer and updater
- [Popcorn.FilenameParser/README.md](Popcorn.FilenameParser/README.md) — release name parser (derived from Sonarr/Radarr, GPL-3.0)

## License

Popcorn is free software under the [GNU General Public License v3.0](LICENSE). The bundled mpv fork (`MPV/src`) keeps its own license.
