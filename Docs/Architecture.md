# Architecture

How the products are put together and how they talk to each other. Short map: [CLAUDE.md](../CLAUDE.md). Feature
behaviour: [Features.md](Features.md). Building and releasing: [Building.md](Building.md).

## Products

Everything is built around `Popcorn.Server`.

### Server + browser UI (`Popcorn.Server`)

ASP.NET Core host. Serves the `Popcorn.Web` bundle from `web/`, WebSocket RPC at `/ws`, REST video streaming at
`api/video`. Runs headless / as systemd user service. Browser playback uses the web player in
`Popcorn.Web/src/player`.

Shutdown is meant to take well under a second: hosted services stop concurrently with a 10 s backstop
(`HostOptions` in `PopcornServerHost`; `TimeoutStopSec=20` in `popcorn.service`), open `/ws` sockets are closed from
the server on `ApplicationStopping` (1001, aborted after 2 s without an answer; otherwise Kestrel waits for them until
the timeout), downloads do a fast stop (`Popcorn.Downloads/README.md`) and plugins get 5 s (§5).

### Linux desktop app (`Popcorn.Linux`)

**Rust + CEF** (not .NET, not in `Popcorn.sln`). Single CEF Views window showing `{server}/index.html`.

- Finds the server via `POPCORN_SERVER_URL` or `localhost:$POPCORN_PORT`. If unreachable it runs
  `systemctl --user start popcorn.service` (again if the unit goes inactive while waiting, e.g. launched during a
  previous server's shutdown; falls back to spawning `Popcorn.Server` from its own dir with `popcorn.env` if the unit
  is missing or crash-looping) and shows a waiting page until it answers. Never hosts the server in-process.
- Adds `PopcornDesktop/<ver>` to the User-Agent → web UI `isDesktopApp` (`Popcorn.Web/src/utils/platform.ts`) hides
  Float/Dock and sends `FileOpen` (server → native player / default app) instead of using the web player.
- `-service` is accepted and ignored (old desktop files).

### Linux player (`Popcorn.Player.Linux` + `Popcorn.Player.Native`)

**Wayland only.** .NET executable (player logic: DB, navigation, mpv control via `LibMpvClient`, overlay WebSocket
bridge, server link = `PlayerLink` listener) that P/Invokes `libpopcorn_player.so` (Rust):

- Raw Wayland window (smithay-client-toolkit).
- mpv core rendering into a video subsurface placed *below* the main surface. With a libmpv that has the DMA-BUF
  render API (`render_dmabuf.h`, our mpv patch) mpv renders with its own Vulkan device into GBM buffers we attach as
  linux-dmabuf `wl_buffer`s; otherwise libmpv OpenGL render API on EGL.
- CEF off-screen overlay (the shared player UI from `Popcorn.Web`, `player.html` entry, served as
  `http://app.local/index.html` from `web-player/`) on the main surface. CEF's shared-texture DMA-BUFs are copied on
  the GPU (EGL/GLES blit) into our own dmabuf buffers, or wl_shm from software paint as fallback. Input is forwarded
  to CEF.

Works standalone without the server. Launched by the server via `LinuxPlayerService`.

### Windows desktop app (`Popcorn.Windows`, + `Popcorn.Platform/Windows/WindowsTrayManager` in the server)

WinForms + **WebView2** counterpart of `Popcorn.Linux` (no CEF on Windows: WebView2 is preinstalled and shared).

- One window on `{server}/index.html` (`POPCORN_SERVER_URL` or `localhost:$POPCORN_PORT`); if unreachable starts
  `Popcorn.Server.exe` from its own folder and shows the waiting page (`wait.html`, copy of Popcorn.Linux's).
- `PopcornDesktop/<ver>` UA, popups → default browser, F11 / HTML5 fullscreen, single instance per user (mutex +
  named pipe; a second launch's `--open-path` is handed over).
- No Windows service: the server is a plain background process (`WinExe` in Windows Release builds,
  `Local\Popcorn.Server` mutex = single instance). Task Manager shows the server's AssemblyTitle/Company "Popcorn".
- Autostart via HKCU Run key from the installer / `install_windows.ps1`, switched on and off like Task Manager →
  Startup apps does (`StartupApprovedRun` state) by `INativeAutostart` (`Popcorn.Platform/Windows/WindowsAutostart.cs`;
  `Popcorn.Platform/Linux/LinuxAutostart.cs` = `systemctl --user enable/disable popcorn.service`, supported when the
  unit file exists; `UnsupportedAutostart` elsewhere). Settings page General → Startup, sockets
  `Autostart`/`AutostartSet`; never stored in Popcorn's settings.
- Notification-area icon (`WindowsTrayManager`, raw Win32 so the server stays plain `net10.0`): left click / Open app
  → `Popcorn.Windows.exe`, Open in browser, Exit.

### Windows player (`Popcorn.Player.Windows` + `Popcorn.Player.Windows.Native`)

Same split as the Linux player: .NET executable (same player logic, `LibMpvClient`, overlay WebSocket bridge,
`PlayerLink` listener) that P/Invokes `popcorn_player.dll` (Rust):

- Win32 main window (native frame) with mpv's own video output inside it (`wid`, mpv's D3D11 swapchain).
- The overlay is a separate borderless window **owned** by it and kept over its client area: WebView2 (windowed
  hosting, transparent background, `WS_EX_NOREDIRECTIONBITMAP`) on `http://app.local/index.html` (virtual host →
  `web-player/`).
- Two top-level windows on purpose: DWM draws mpv's swapchain over anything else inside the same top-level window
  (child windows, DirectComposition visuals — the old WPF player's UI vanished as soon as video played); an owned
  window is always composed above its owner. The overlay is activated in place of the main window (which keeps
  painting its caption active). F11 fullscreen.

Launched by the server via `WindowsPlayerService` from `<server dir>\Player\Popcorn.Player.Windows.exe`; also
`Popcorn.Player.Windows.exe <file|fileId>`, no argument = "drop a file" screen.

## Communication

### 1. Web UI ↔ Server: WebSocket `/ws`, custom RPC

- Text frames: JSON `{ type, payload }`, camelCase. Binary frames: UTF-8 prefix `"type,param1,param2;"` followed by
  bytes (images/thumbnails).
- Server handlers: classes marked `[WebSocketService]`, methods marked `[WebSocketEvent(SocketRequests.X)]` /
  `[WebSocketBinaryEvent]`, discovered by reflection in `WebSocketServices.RegisterWebServices`. Handlers are
  resolved per message from a DI scope.
- Event names and payloads are generated: every server build (`WriteSocketContracts` target in
  `Popcorn.Server.csproj` → `Popcorn.Server --write-socket-contracts <dir>`, `Contracts/SocketContractWriter.cs`)
  writes `Popcorn.Web/src/socket/socket-requests.ts` (the `SocketRequests.cs` constants) and `socket-payloads.ts`
  (the payload type of every `[WebSocketEvent]` handler, with the types it uses and their XML doc comments; enums
  with a `[JsonConverter]` attribute as string unions, others numeric; value-type fields with a default optional).
  `client.sendEvent(type, payload)` is typed with them (events without a payload take no argument). Responses
  (`onEvent<T>`) still use the hand-written models under `src/model`. `-p:SkipSocketContracts=true` skips the step.
- Live data is pushed, never polled: `Subscribe` / `Unsubscribe` (`{ topic, key? }`, `SubscriptionWebService`,
  `Push/SocketSubscriptions`), topics `DownloadList`, `DownloadStats`, `SegmentsState` (key = item id). The current
  value is sent on subscribe, then `Push/LivePushService` sends on `DownloadManager.Changed` /
  `SegmentFinderService.StateChanged` (only when the JSON changed). Web: `useSubscription(topic, key?, enabled)`
  (`src/socket/useSubscription.hook.ts`; several components may watch one topic, only the last to unmount
  unsubscribes, as the server keeps one subscription per connection and topic).
- HTTP endpoints with live state stream NDJSON instead (`Push/JsonLines.cs`, web `utils/json-lines.ts`
  `readJsonLines`; aborting the request = unsubscribing): `api/downloads/playback/live?path=` /
  `playback/{fileId}/live`, `api/video/{id}/audio/{track}/prepare`, `POST api/logs/live` (FileSystemWatcher on the
  log folder).
- Client: `Popcorn.Web/src/socket/websocket-client.ts` (auto-reconnect, send queue), hooks `useSocketEvents` /
  `useSocketBlobEvents`.

### 2. Web UI ↔ Server: HTTP

- `VideoController` (`api/video/{fileId}`, `/metadata`, `/audio/{trackId}`, `/subtitles/{trackId}`) for in-browser
  playback. A file still downloading redirects to the `DownloadsController` stream; subtitle extraction answers 409
  until it is complete.
- Audio tracks the video has no playable version of (`AudioTrackService`, singleton):
  - `/audio/{trackId}/prepare` starts the full conversion into `StreamCache/<fileId>-<track>.m4a` (AAC copied, else
    transcoded; `.part` until done; cancelled 20 s after the player stopped asking, it asks every 3 s) and says
    `ready`/`converting`.
  - `/audio/{trackId}` = the converted file (ranges).
  - `/audio/{trackId}/stream?start=<s>` = live ADTS stream from that time that masks the start-up (no ranges, ends
    with the request; the only option for files still downloading).
  - `VideoPlayer.tsx` plays the stream offset by `start` and moves to the converted file at the next reload (seek
    outside its buffer) or pause, holding the video (`isAudioLoading`) while the audio loads.
- `DownloadsController`: `api/downloads/{id}/files/{index}` (waiting range stream), `api/downloads/playback?path=` /
  `playback/{fileId}` (download state + downloaded ranges as fractions of playback time via the container index,
  `MediaTimeline`: MKV/WebM Cues, MP4 sample tables, byte fractions when there is none). Used by both players: seek
  bar `Components/ProgressBar/SeekBar.tsx`; the Linux player plays the stream URL with mpv `network-timeout=0`
  instead of the incomplete file.

### 3. Server → native player

One-way, same code on Linux and Windows (AF_UNIX, Windows 10 1803+): `PlayerLink`
(`Popcorn.Core/Shared/PlayerLink.cs`).

- A running player listens on `$XDG_RUNTIME_DIR/popcorn-player.sock` (no XDG_RUNTIME_DIR:
  `<temp>/popcorn-player-<user>.sock`; Windows `%TEMP%\popcorn-player.sock`), from `PlayerNative.Ready` on
  (`Player.Core/Core/ServerLink.cs`).
- The server connects, writes one JSON line `{ FileId }` or `{ FilePath }` and closes. `NativePlayerService.OpenFile`
  sends to a running player, else starts one with the file as argument (and for 15 s after a start keeps retrying the
  socket instead of starting another).
- Nothing comes back: players write history to the DB themselves.

### 4. Player app ↔ its overlay UI

`Popcorn.Player.Core/Web/` (`IWebBridge` = `SocketWebBridge`): Fleck WebSocket on 127.0.0.1, address injected into
the overlay page (`window.wsAddress`) by the native layer (Linux: CEF scheme handler, Windows: WebView2
`AddScriptToExecuteOnDocumentCreated`). The overlay (`native-bridge.ts`) retries every 500 ms whenever the socket drops
(seen after system sleep/resume), shows one persistent toast until it is back and queues commands meanwhile; each connect sends `initialized`, which C# answers with
`PlayerCore.ResendState` on reconnects. Connects/closes are in the player log ("Overlay socket ...").

### 5. Plugins

Search is entirely plugin-provided; the plugin API is in [Plugins.md](Plugins.md). Popcorn only handles links
generically: never integrate a specific site into the app. Host side:

- **Loading**: `PluginManager` (`Popcorn.Server/PluginHost`) loads `plugins/<id>/plugin.json` + assembly (own
  `AssemblyLoadContext`; `Popcorn.Plugins.Abstractions` + `Microsoft.Extensions.*` always from the server) from
  `<data>/plugins` then `<server dir>/plugins` (collectible contexts; rescanned on every change; plugins without a
  search role implement `IServicePlugin`). Every loaded plugin is disposed at server shutdown, all at once and for at most 5 s
  (`PluginShutdownService` → `UnloadAllAsync`, each dispose logged with its duration; not on `ApplicationStopping`,
  whose callbacks run before the host's shutdown timeout applies), so plugins may own processes. `PluginsController` (`POST api/plugins`, zip) installs into
  `<data>/plugins/<id>`; disabled ids + order in `<data>/plugins/plugins.json`. Plugin `web/` is served at
  `/plugins/<id>/` (`PluginWebFileProvider`, follows the loaded set).
- **Sockets** (`PluginWebService`): `PluginList` (all folders in the user's order, incl. disabled/broken; `media`
  flag, `fileSources` with per-source name/icon/sort fields, `iconUrl` from manifest `icon`), `PluginSearch`,
  `PluginDetails`, `PluginFileSearch` (`pluginId` + `sourceId`, client `requestId` echoed), `OpenLink`,
  `PluginSetEnabled`, `PluginSetOrder`, `PluginUninstall`. Managed from Settings → Plugins
  (`pages/settings/plugin-list.tsx`).
- **Two search kinds** (a plugin class may implement both): media sources `IMediaSourcePlugin` (what exists: search +
  details; Search page Media tab) and file sources `IFileSourcePlugin`, or several per plugin via
  `IFileSourceProvider` → `FileSource`s (each its own provider in the UI, web key `pluginId/sourceId`, `""` = the
  plugin itself). File sources say where to get it: `FileResult`s with typed `FileLink`s — Magnet/Torrent/Direct/Page.
  Search page Files tab + Files section under media details, queried with `MediaDetails.FileQuery` = title/year/IMDb
  id.
- **Links**: `FileLink.Resolve` = fetched only when used (the file source's `IFileLinkResolver`, socket
  `PluginResolveLinks`, off the socket loop, 30 s), called by `useOpenLink` / `useDownloadFiles` before acting. A
  Torrent link may carry the file as `data:application/x-bittorrent;base64,` (`TorrentDataUrl` in Popcorn.Shared:
  .torrents behind the plugin's login; accepted by `DownloadManager`, `FileService.OpenUrl` = temp .torrent file,
  browser = saved .torrent). `src/plugins/file-sources.ts` `useOpenLink(link, target, media)`: the link kind picks the
  action (Magnet/Torrent = torrent, Direct = direct download); target `internal` → `DownloadAdd` (built-in downloader,
  added to the library with the details page's title/poster), `external` → browser: magnet to OS / new tab, desktop
  app: server `OpenLink` → `FileService.OpenUrl` = xdg-open; default target per kind from settings
  (`useInternalTorrentClient`, `downloads.useInternalDirectDownloader`).
- **Images**: providers `IImageProvider` (`ImageKinds` = Poster and/or Backdrop) feed the pictures of library items.
  `PluginWebService.ImageSearch` (`MediaImageSearch`, name parsed with `MediaNameParser`, all providers of the kind in
  parallel; `ImageQuery.ImdbId` = the item's or a `tt…` in the name, `MediaId` = the item's media id for the plugin it
  came from; results grouped per matched title by `ImageResult.Group`) → `components/image-picker/media-image-picker.tsx`
  (Add to Library modal, item page Edit pictures (sends `itemId`); provider chips = `SourceFilter`, excluded ids in
  `useExcludedImageProviders`). The pick (provider http(s) URL or uploaded file as data: URL) goes in
  `CreateMediaItem` (JSON) / `SetMediaItemImage` and is stored by `MediaImageService` (Application) in
  `<data>/images` as `MediaItem.ImagePath` (poster) / `BackdropPath` (unique file name per save, old file deleted,
  also on item removal).
- **Media source of items**: items added from a media details page (downloads started there: `LinkMedia.source` →
  `DownloadAddRequest.mediaSource` → `DownloadRecord.MediaSource`) keep `MediaItem.Source` (`MediaSourceRef` = plugin
  id + its media id + IMDb id; columns `media_plugin_id`/`media_id`/`imdb_id`) and get the page's backdrop too.
- **Descriptions**: new library items get `MediaItem.Description` in `MediaItemService.CreateMediaItemFromScan` via
  `IMediaDescriptionProvider` (`PluginHost/PluginMediaDescriptions`: the `Source`'s details overview, else
  `MediaDetails.Overview` of the best search match of the first media source plugin in the user's order that has
  one, 10 s limit).
- **Settings**: fields declared in plugin.json `settings` (`PluginSettingField`: text/password/number
  (whole)/float/boolean/select, `list` for text/password/number/float, optional `group` heading), form
  `pages/settings/plugin-settings-dialog.tsx`, sockets `PluginSettings`/`PluginSettingsSave`, stored by
  `PluginSettingsFile` in `settings.json` in the plugin folder (bundled plugins: `<data>/plugins/<id>/`, their folder
  is replaced on install; zip updates keep it); passwords never sent to the browser. Plugins read it via
  `IPluginSettings` (constructor); saving reloads the plugin.
- **Actions**: buttons in the settings dialog: `IPluginActions` on the plugin class (`PluginAction` list +
  `RunActionAsync`; sockets `PluginSettings` (`actions`) / `PluginAction`, run off the socket loop, 2 min limit). A
  plugin with actions gets the dialog without fields.
- **Variables**: plugins share string values by name through `IPluginVariables` (constructor;
  `PluginHost/PluginVariables.cs`: `PluginVariableStore` singleton = mediator, in memory, a value belongs to the
  plugin instance that set it last and is dropped when it unloads).
- **Events**: plugins send each other named string events through `IPluginEvents` (apiVersion 2; constructor;
  `PluginHost/PluginEvents.cs`: `PluginEventHub` singleton = mediator, every subscriber of a name gets the event on
  the thread pool, `Publish` returns the subscriber count, nothing queued, subscriptions end when the instance
  unloads). Servers load `apiVersion` `PluginApi.MinVersion`..`PluginApi.Version` (1..2).
- **Status messages**: `IPluginStatus` (constructor; `PluginHost/PluginStatus.cs`, per instance, keyed, cleared on
  unload; `PluginInfo.status`, shown by `pages/settings/plugin-status.tsx` on the plugin row and in the settings
  dialog). Changes raise `PluginManager.StatusChanged`, on which every open socket gets `PluginList`
  (`Startup.HandleWebSocket`, 200 ms coalescing).
- **Web** (`src/plugins/`): file sources are searched lazily (Files tab opened / details page button; `useFileSearch`
  in `file-sources.ts`, state owned by the caller). Only sources left on in `SourceFilter` (`plugins/source-filter.tsx`,
  chips under the search buttons and on the Files tab, excluded ids in localStorage via `excluded-sources.store.ts`;
  switching one on mid-search asks it too). Shown by `file-results.tsx`: combined list by default or per provider;
  combined grouping merges same-named results across providers only, never within one (`groupFiles`); Remove
  duplicates drops copies with the same `FileResult.Origin` + `OriginId` from different plugins, e.g. an
  aggregator's (`dedupeFiles`); the three switches sit in the View popover, sorted by plugin
  `SortFields`/`FileResult.SortValues`, refresh per provider; view prefs in localStorage. Result groups
  `provider-group.tsx` (collapsed state persisted in localStorage, first 10 + Show all).
- **UI slots** `PluginSearchCard`/`PluginDetails`/`PluginDetailsCard`/`PluginFileCard` render the plugin's UI module
  export (dynamic `import()`) or the built-in component (`default-components.tsx`). Plugin UI modules share
  react/react-dom/@mantine via an import map that `vite.config.ts` (`pluginSharedModules`) generates; the list is
  `plugin-sdk/shared.js` — changing it or the contract is an API change (`PluginApi.Version`).

Plugins outside the repo reference Popcorn.Plugins.Abstractions and are installed into `<data>/plugins/<id>/` like the
bundled ones.

### 6. Linux player .NET ↔ native

C ABI in `Popcorn.Player.Native/src/lib.rs` (`pn_init`, `pn_mpv_core`, `pn_run`, `pn_quit`, `pn_set_fullscreen`,
`pn_set_title`, `pn_destroy`; callbacks for log + window events incl. `Ready`). C# attaches its own mpv client
handle (`mpv_create_client`) to the core the native layer owns.

The player also talks to the desktop over the D-Bus session bus (Tmds.DBus.Protocol, `Popcorn.Platform/Linux`):
MPRIS server `LinuxMediaControls` and the sleep inhibition `LinuxSleepInhibitor` (Docs/Features.md, OS session).

### 7. Windows player .NET ↔ native

Same pattern, `Popcorn.Player.Windows.Native/src/lib.rs` (`pn_init` creates the hidden windows and must run on the
`pn_run` thread, `pn_mpv_core`, `pn_run`, `pn_quit`, `pn_set_fullscreen`, `pn_set_title`, `pn_window_handle` (dialog
owner), `pn_destroy` (terminates mpv while pumping messages: mpv's video window is a child of ours); callbacks for
log, window events (`Ready` = WebView2 created) and dropped file paths).

## Runtime data

- Data folder: `DirectoryUtils.DataFolder` = `LocalApplicationData/Popcorn/` (`~/.local/share/Popcorn/` on Linux):
  SQLite DB, `logs/`, `ThumbnailsCache/`, `StreamCache/`, `downloads/` (download list + torrent engine state; the
  files themselves go to `DownloadSettings.DownloadFolder`, default `~/Downloads/Popcorn`, one folder per download),
  `plugins/` (user-installed plugins, win over bundled ones; + `plugins.json` = disabled ids).
- Logging: Serilog everywhere, one file per app and day `logs/YYYY-MM-DD-<App>.txt` (date first so the folder sorts
  by day). .NET apps via `WriteTo.DailyFile` (`Popcorn.Core/Shared/Logging/DailyLogFileSink.cs`, names in
  `LogFileName.cs`, both linked into Popcorn.Windows; rolls at midnight, `_001` suffix when another process of the
  app holds the day's file — on Unix a POSIX record lock, `FileShare.Read` alone doesn't exclude writers there); Rust
  apps (`Popcorn.Linux`, `Popcorn.Installer` `src/log.rs`) the same names. The server renames old
  `<App>-YYYYMMDD.txt` files at start (`LogFileName.MigrateLegacyNames`).
- Log viewer: Settings → Logs (`pages/settings/logs/log-viewer.tsx`, lazy chunk; `@mantine/dates` range picker,
  `@tanstack/react-virtual` list) reads them merged through `LogsController` (`POST api/logs/query`,
  `Popcorn.Server/Logs/LogReader.cs`: daily files + CEF's `cef.log`/`cef-player.log`, filters = days/excluded
  apps/excluded levels/search, newest 5000; live = `POST api/logs/live` streams new entries (NDJSON) from the
  per-file byte `cursor` on, only while the range has no end day). Requests to `/api/logs` are filtered out of the
  server's own log (`Program.IsLogViewerRequest`).
- Desktop entry: `Popcorn.Linux/popcorn.desktop` template (`@INSTALL_DIR@` substituted; Uninstall Popcorn action =
  `Popcorn.Installer.Linux --uninstall`), installed only if missing by make, (re)written by Popcorn.Installer;
  launches `Popcorn.Linux`. Window app_id / WM_CLASS is `popcorn` so it matches this file.
- Popcorn.Linux: CEF profile (localStorage, cookies; also the single-instance lock) in `<data>/cef/`, logs in
  `logs/YYYY-MM-DD-Popcorn.Linux.txt` + `logs/cef.log`. Env: `POPCORN_SERVER_URL`, `POPCORN_PORT`,
  `POPCORN_OZONE=x11|wayland`, `POPCORN_NO_SANDBOX=1`.
- Linux player: logs in `logs/YYYY-MM-DD-Popcorn.Player.txt` (native messages prefixed `[native]`, overlay console
  `[overlay]`) + `logs/cef-player.log`; throwaway CEF profile per process in
  `$XDG_RUNTIME_DIR/popcorn-player-cef-<pid>`. Env: `POPCORN_PLAYER_DEVTOOLS_PORT` (chrome://inspect),
  `POPCORN_NO_SANDBOX=1`, `POPCORN_PLAYER_VIDEO=opengl` (force the EGL video output), `POPCORN_PLAYER_OVERLAY=shm`
  (force software overlay). F12 opens DevTools, F11 toggles fullscreen. The log says which outputs were picked
  (`Video: mpv DMA-BUF output ...` / `Video: mpv OpenGL output`, `Overlay: GPU copy ...`).
- Windows player: logs in `logs/YYYY-MM-DD-Popcorn.Player.txt` too (native messages `[native]`); WebView2 profile in
  `<data>/webview2-player`. Env: `POPCORN_PLAYER_DEVTOOLS_PORT` (edge://inspect); F12 DevTools, F11 fullscreen.
- systemd: `Popcorn.Server/systemd/popcorn.service` (template, `@INSTALL_DIR@`/`@ENV_FILE@` substituted by make) +
  `popcorn.env` (copied to `~/.config/popcorn/popcorn.env` only if missing).
