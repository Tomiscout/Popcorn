# Popcorn.Downloads

Built-in downloader (Downloads page) of `Popcorn.Server`: torrents (MonoTorrent 3) and direct HTTP downloads.

## Pieces

- `DownloadManager`: singleton + hosted service, 1 s tick: seed limits, speed-limit split, library linking,
  persistence in `<data>/downloads/downloads.json`.
- `TorrentDownload`: MonoTorrent engine state + .torrent copies in `<data>/downloads/engine|torrents`. Pause/resume
  never wait for MonoTorrent (`ApplyRunState` queues engine starts/stops in the background; the page also shows
  clicks optimistically).
- `DirectDownload`: HTTP range chunks, 3 workers, sequential fallback without range support.
- `SequentialPieceRequester` steers MonoTorrent's streaming window: reader position (playback) → head + 8 MiB tail
  of wanted files → first missing piece. Direct downloads use the same order.
- `DownloadStream`: seekable stream that waits for missing data, served by `DownloadsController` (in the server) with
  range processing, so players seek into undownloaded parts and wait.

## Library

- Downloads with `AddToLibrary` become media items (`MediaItemService.CreateMediaItemFromScan`) as soon as the file
  list is known.
- Downloads attached to an existing item (`AddDownloadOptions.MediaItemId` → `DownloadRecord.AttachToItemId`; item
  page File sources tab, `pages/item-details/item-file-sources.tsx`; add dialog of the Downloads page, whose library
  field autocompletes existing items) are saved in a folder inside the item's folder and their videos added to it
  (`MediaItemService.AddFilesFromScan`). Deleting their files only drops those files from the item.
- `DownloadAddBatch` = several links into one item (given, or a new empty one from `CreateLibraryItemAsync`):
  multi-select / episode groups of `file-results.tsx` (`plugins/file-groups.ts`: episode token parsing, naming-scheme
  groups, duplicates merged to the most seeders).

## Priorities

- Per file: `FilePriority` Skip/Low/Normal/High → MonoTorrent priority, orders `SequentialPieceRequester`'s file
  walk.
- Per download: `DownloadPriority`. `DownloadManager.ApplyPrioritiesAsync` caps lower-ranked downloading ones to
  25 % of the top rank's rate per level, min 64 KiB/s. A download a player streams from ranks above High.

## Settings and UI

- Settings: `SettingsModel.Downloads` (`DownloadSettings`) + `UseInternalTorrentClient` (Settings → Downloads tab,
  `pages/settings/download-settings.tsx`). Speed limits: click the nav-bar speeds
  (`pages/downloads/speed-limit-dialog.tsx`).
- Socket (`DownloadsWebService`): `DownloadList`/`DownloadStats` pushed to subscribers (see
  [Architecture](../Docs/Architecture.md#1-web-ui--server-websocket-ws-custom-rpc)) — `pages/downloads/` page with
  Torrent/Direct tabs and the nav-bar ↓/↑ `network-stats.tsx` — `DownloadAdd`, binary `DownloadAddTorrentFile` (also
  from .torrent files dropped anywhere in the app: `useTorrentFileDrop`, opens the Downloads page; the player keeps
  its own drops), `DownloadAdded` (global notification), `DownloadPause`/`Resume`/`Remove`/`SetFilePriority`/
  `SetPriority`/`OpenFolder`, `PickDownloadFolder`.
- The page groups rows (`pages/downloads/download-groups.ts`): downloads of one library item, else unlinked ones of
  one episode naming scheme (`parseEpisodeName`); the group row links to the item.
