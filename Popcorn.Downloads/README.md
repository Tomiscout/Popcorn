# Popcorn.Downloads

Built-in downloader (Downloads page) of `Popcorn.Server`: torrents (MonoTorrent 3) and direct HTTP downloads.

## Pieces

- `DownloadManager`: singleton + hosted service, 1 s tick: seed limits, speed-limit split, library linking,
  persistence in `<data>/downloads/downloads.json`.
- `TorrentDownload`: MonoTorrent engine state + .torrent copies in `<data>/downloads/engine|torrents`. Pause/resume
  never wait for MonoTorrent (`ApplyRunState` queues engine starts/stops in the background; the page also shows
  clicks optimistically).
- Server stop is a fast stop (`DownloadManager.StopAsync`, at most 8 s): direct downloads are cancelled, torrents
  stopped without waiting for the trackers' answer (`TorrentDownload.ShutdownAsync`, queued pauses/resumes dropped).
  Partly downloaded chunks/pieces are lost; MonoTorrent still writes its cache, closes the files and writes the fast
  resume data, so nothing is hashed again. Its engine teardown (UPnP/NAT-PMP unmapping, DHT) has no timeout of its
  own, hence the budget; `downloads.json` is saved either way.
- Peer discovery workarounds for MonoTorrent 3.0.2 (without them new magnets could sit on "getting metadata"
  forever):
  - Trackers: `TorrentDownload` gives every tracker its own tier (non-private torrents), so all are announced to at
    once. MonoTorrent tries a tier's trackers one at a time and stops at the first answer, even one with 0 peers, and
    magnets put all their trackers in a single tier.
  - DHT: `DhtBootstrap.PrepareAsync` runs before the engine is created. MonoTorrent's only bootstrap router
    (router.bittorrent.com) doesn't answer, and it saves `engine/dht_nodes.cache` as a bencoded list but loads it as
    raw compact nodes. So the cache is rewritten in the raw format (saved nodes plus dht.transmissionbt.com,
    dht.libtorrent.org and the two old routers), and MonoTorrent's private bootstrap list is replaced by reflection
    (a warning is logged if a MonoTorrent update renamed it).
- `DirectDownload`: HTTP range chunks, 3 workers, sequential fallback without range support.
- `SequentialPieceRequester` streams one file at a time (pieces in order) and picks the rest the usual way (file
  priority, rarest first, randomised). The streamed file is the one a player reads, from its position; with no player
  reading, the file streamed last, else the first wanted video file (head, last 8 MiB for the container index, then
  in order). Five minutes before the end of the file a player watches (time from the container index, else the last
  10 % of bytes), the next video file in path order is streamed after it (`DownloadManager.UpdateNextEpisodeAsync`).
  Every peer's request queue is filled: MonoTorrent's own `StreamingPieceRequester` allowed 2–4 blocks per peer, which
  held torrents to ~1 MiB/s. Direct downloads use the same order.
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
- Port check (`DownloadManager.GetPortStatus`, part of `DownloadStats`): no outside service, no root. A peer
  connecting to us = reachable; torrents with peers for 5 minutes and no incoming connection = unreachable (firewall
  or no port forward). Also reports the router's UPnP / NAT-PMP mapping. The Torrent tab shows a warning banner
  (`pages/downloads/port-warning.tsx`) when unreachable. firewalld can't be asked without root (polkit), so the
  firewall is not checked directly.
- The page groups rows (`pages/downloads/download-groups.ts`): downloads of one library item, else unlinked ones of
  one episode naming scheme (`parseEpisodeName`); the group row links to the item.
