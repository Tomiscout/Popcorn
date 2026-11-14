# Features: how they work

Behaviour of library and player features, with the code that implements it. Structure and protocols:
[Architecture.md](Architecture.md). Downloads: [Popcorn.Downloads](../Popcorn.Downloads/README.md). Intro/outro
detection: [Popcorn.SegmentFinder](../Popcorn.SegmentFinder/README.md).

## Library

- **Release name tags**: `Popcorn.FilenameParser` (see its README) via `MediaFile.ParseTags()`
  (`Application/Extensions/MediaFileTagExtensions.cs`): files are tagged when scanned (`MediaItemService`), old
  untagged rows lazily in `MediaItemWebService.SendMediaItems`.
- **Item page** (`pages/item-details/item-details.page.tsx`):
  - The title is renamed in place (pencil on hover, socket `RenameMediaItem` → `title` + `short_title`).
  - History tab: newest first, trash per row (on hover) + Delete all (confirm), socket `MediaItemHistoryDelete`
    (`historyId` = `ItemHistory.Id` = the table's SQLite `rowid`, read as `history_id`; none = all) → files left
    without history get `played = 0`, the item is resent.
  - Files tab "Flatten files" switch = every file in play order (`MediaItemExtensions.GetPlayOrder`: chains the
    next-file solver `FindNextFile` from the first episode / first file of the folder walk; files it passes over
    follow, other versions right after their episode), sent with the item as `MediaItemFlatFileList`
    (`multipleDownloads` = more than one download is attached to the item → switch on by default). The user's choice
    is per item in localStorage (`itemFlattenFiles:<id>`).

## Players

- **Native players setting**: `SettingsModel.Players` is an ordered `PlayerOption` list (built-ins `Popcorn` +
  `Default`, user-added `Program` = executable gets the file path as its argument, `Script` = shell line with `%args`
  → quoted path). `PlayerLaunchService.OpenAsync` (used by `FileOpen`) tries enabled entries in order and falls
  through on failure. `SettingsModel.NormalizePlayers()` (run on load/save) keeps the built-ins present; legacy
  `UseInternalPlayer` only seeds the initial order.
- **One player UI, several hosts.** `src/player/PlayerOverlay.tsx` (controls, top bar, progress/thumbnails/intro-outro
  skip, tracks, episodes, errors) is shared. Components only read the zustand player store and post commands on the
  local bus (`utils/events`: `play_status_changed`, `set_playback_time`, `skip_seconds`, `volume`,
  `audio_track_selected`, `subtitle_track_selected`, `folder_open`, `thumb_requested`, `player_close`, ...); they must
  not talk to the server socket or a `<video>` directly. Hosts:
  - `PopcornPlayer.tsx` (browser: HTML5 video, server WebSocket, floating/PiP).
  - `native/NativePlayer.tsx` (native players: maps the C# `WebEventHandler` events to the store and forwards
    commands; transport `native/native-bridge.ts` = `window.wsAddress` WebSocket on both). The Windows player also
    injects `window.playerHost = 'windows'` (file drops → `chrome.webview.postMessageWithAdditionalObjects('files_dropped')`
    → paths to C#, host subtitle picker).
  - Host-specific UI is switched via `player-features.ts` (`floating`, `fileDrop`, `addSubtitle`, `subtitleDelay`)
    instead of forking components. A new overlay command is handled in both hosts and, for native, in
    `WebEventHandler.OnMessage` (unknown names throw there).
- **Toasts**: the player has its own (no Mantine notifications in the overlay bundle):
  `player/Components/PlayerToasts/toasts.store.ts` → `showToast({ level: 'error' | 'warning' | 'info', message, title?,
  id?, persistent? })`, `dismissToast(id)`. Red/orange/gray, top centre, X button, gone after 4 s unless `persistent`;
  showing an existing `id` replaces it. Hosts call it; overlay components only render the store (`PlayerToasts`).
- **Overlay development**: `npm run dev:player` serves the overlay from Vite; open
  `player.html?ws=ws://127.0.0.1:<port>` against a running Linux player (port = Fleck server, usually 8181). When the
  overlay (re)connects, C# replays the current file's state (`PlayerCore.ResendState`).
- **Seek-bar thumbnails** (server `MediaThumbnailsService`, player `ThumbnailService`) go through
  `ThumbnailRenderer.Render` (`Popcorn.Core/Application/Utils`):
  - Hardware decoders come from the GPUs actually present (`Shared/Ffmpeg/GpuAdapters`: PCI vendor ids from
    `/sys/class/drm/renderD*` / the Windows display adapter registry class; never `ffmpeg -hwaccels`, which lists what
    ffmpeg was built with, e.g. qsv without an Intel GPU) → `ThumbnailUtils.HardwareMethods` (NVIDIA: `*_cuvid` +
    `-resize`; Linux AMD/Intel: VA-API on that GPU's render node; Windows Intel: QSV, AMD/Intel: D3D11VA).
  - Each is checked once per process before use (`ThumbnailUtils.BuildDeviceCheck`: `-init_hw_device` on a blank
    frame, ~40 ms when it fails): a GPU being there doesn't mean its driver for the API is (VA-API drivers are
    separate packages, nouveau has no CUDA). Logged as `Thumbnails: <method> available` / `not available (<ffmpeg
    error>)`.
  - Which one is used is measured, not assumed: per stream kind (`ThumbnailUtils.StreamKind` = codec + size bucket +
    10-bit) the first thumbnails alternate CPU and hardware, 3 runs each, then the fastest is kept; a hardware method
    that fails where the CPU works is dropped for that kind. In memory per process, decision logged as
    `Thumbnails of <kind>: using ...`.
  - ffmpeg sets the GPU up per thumbnail (~250 ms for CUDA), so a fast CPU wins at 1080p H.264 while CUDA wins ~2.5×
    at 4K HEVC 10-bit (RTX 4060 Ti / Ryzen 3800X). Hardware commands keep hardware output formats so an unsupported
    codec fails instead of silently decoding on the CPU. Only CUDA has been run; VA-API/QSV/D3D11VA are untested.
- **Player title and media info**: `GetPlayerTitle` (`MediaItemExtensions`) = library item's name without release
  tags, else the name parsed from the file. Second line of the top bar (`TopBar.tsx`): season / episode, else the
  full file name; hovering the title always shows the full file name (Mantine `Tooltip`: the Linux player's
  off-screen CEF does not show `title` attributes). `MediaInfoBuilder` (`Popcorn.Core/Shared/Ffmpeg`) turns the
  ffprobe analysis into `MediaInfo` rows (settings menu Media info tab, `PlayerSettings.tsx`) and `ChapterInfo`s
  (`selectCurrentSegmentName`: label between the left and right controls, intro/outro first); delivered in
  `MediaFileMetadata` (browser) / `TrackLoadedEvent` (native).
- **Next episode**: end of file and the next button ask for a `NextFilePlan` (`GetNextFilePlan` in
  `MediaItemExtensions`; browser socket `PlayerNextFile`, native `next_file_plan`), applied by
  `player/next-file.ts`: play it, or open `NextFileDialog` when season/episode numbers have a hole (not for season
  0), or, when the file played to its end and nothing is next, show `FinOverlay` (`src/player/fin.png`, imported
  because the player build has no `public/`). The previous button plays `NextFilePlan.PreviousFileId` from the
  metadata (`play_file`). Refresh files = `PlayerRefreshFiles` / `next_file_refresh` (rescan, new plan: plays the
  episode if it is now there). Find file = `/watch/<item>?tab=sources&q=<title> <SxxExx>`: browser floats the player
  and navigates, native `open_app_path` → `AppWindowLauncher` → `Popcorn.Linux --open-path=<path>` (a running
  instance takes it over in `on_already_running_app_relaunch`; no desktop app: default browser).
- **Files still downloading**: `downloadRanges` in the player store drives both rules (`player.store.ts`, shared by
  all hosts). `canSeekTo`: seek bar, skip buttons and arrow keys refuse targets outside the downloaded parts.
  `needsBuffering`: playback stops 5 s before the end of the downloaded part it is in (or at once outside any) and
  goes on at 10 s ahead or when that part reaches the end of the file; `PlayerOverlay` keeps `isBuffering` up to
  date and shows the "Buffering" spinner, the hosts hold the video on its changes (native: `play_status_changed`, so
  `isPlaying` stays the user's wish).
- **OS session (native players)**: `MediaSessionService` (`Popcorn.Player.Core/Services`, started on the hosts'
  start-up next to `ServerLink`) follows `IPlayer.PlayingChanged` (mpv `pause` + file loaded + not at the end):
  - Keeps the computer awake while a video plays, not while paused (`ISleepInhibitor`). The libmpv render API never
    inhibits by itself. Linux `LinuxSleepInhibitor`: XDG portal `Inhibit` (Suspend | Idle; KDE turns it into a
    PowerDevil inhibition, applied after a few seconds' delay, listed by `PolicyAgent.ListInhibitions`), else
    `org.freedesktop.ScreenSaver.Inhibit`; the player registers as `popcorn` with the portal host registry first,
    otherwise the inhibition has no app name (started by the systemd server, it has no app id). Windows
    `WindowsSleepInhibitor`: power request (system + display required, `powercfg /requests`), untested.
  - OS media controls (`ISystemMediaControls`). Linux `LinuxMediaControls`: MPRIS on the session bus as
    `org.mpris.MediaPlayer2.popcorn.instance<pid>` (media keys, Plasma's media widget, `playerctl`). Metadata = title
    (`SxxEyy · episode title` for episodes), item as album, length, poster as `artUrl`; `Seeked` on mpv's
    playback-restart. Play/pause/next go to the overlay as `media_control` (handled like its own buttons:
    buffering, missing-episode dialog, notification), seek/position/previous go straight to the player, `Quit`
    closes the window. No `Raise` (Wayland), volume/rate read-only. Windows: none yet (`NoSystemMediaControls`; SMTC
    would need `ISystemMediaTransportControlsInterop` in the Rust layer).

## Intro/outro skipping

- `SegmentAutoSkip` = `Always` / `AfterFirstOccurrence` / `Never` (removed `AfterFirstInItem` still reads as
  `AfterFirstOccurrence`, numbering kept for stored rows), global in `SettingsModel.SegmentAutoSkip` (Settings page),
  override per item in `MediaItem.segment_auto_skip` (null = global; player top-bar gear, `Components/PlayerSettings`).
- `SegmentSkipService` gives the players `PlayerSegments` (locations, modes, `SegmentInfo.seen`).
- The decision is in the overlay (`ProgressBar.tsx`, `shouldAutoSkip`): made when a segment is entered and only on
  its first entry per file playback (seeking back shows the button full without a count-down). Auto-skip = the skip
  button's 10 s count-down, or an immediate seek with `SettingsModel.SkipSegmentsWithoutDelay`
  (`PlayerSegments.SkipSegmentsWithoutDelay`, Settings page).
- Reaching a segment's end (watched, skipped, seeked past) posts `segment_seen`. Both hosts forward `segment_seen` /
  `segment_auto_skip_set`: browser to the `SegmentSeen` / `SegmentAutoSkipSet` socket events, native to
  `WebEventHandler`.
- `SettingsModel.SkipOutroTail` (default on, `PlayerSegments.SkipOutroTail`): skipping an outro that ends within
  10 s of the end of the file seeks to the end.

## Tracks and subtitles

- **External subtitles**: a video without subtitle streams of its own gets the subtitle files `ExternalSubtitles.Find`
  (`Popcorn.Core/Application/Utils`) picks in the item's folder (subfolders too; standalone files: the video's
  folder): same name (+ language/tag suffix), a folder named as the video (`Subs/<video>/2_English.srt`), every
  subtitle when the folder has only one video, else the same season/episode when only one video has it.
  - Native players: mpv `sub-auto=no`, `sub-add` on load (`PlayerCore.LoadMedia`/`PlayingStarted`), English or first
    turned on.
  - Browser: extra `SubtitleTrackDto`s from index `ExternalIndexBase` (10000, `ExternalPath` not serialized) in
    `FileStreamService.GetMediaFileMetadataAsync`, `SubtitleDefaultIndex` = the one turned on (`VideoPlayer.tsx`);
    `api/video/{id}/subtitles/{index}` serves .vtt/.ass/.ssa as they are, the rest converted to WebVTT (VobSub
    skipped), also while the video downloads.
- **Track languages**: `SettingsModel.PrefferedAudioLanguages` / `PrefferedSubtitleLanguages` (spelling kept: stored
  settings; ISO 639-2/T codes in order, Settings → General → Languages, `pages/settings/language-settings.tsx`, list
  from socket `LanguageList`). `LanguageCodes` (`Popcorn.Core/Shared/Languages`, .NET culture data + ISO 639-2/B
  aliases) normalizes tags (`en`/`eng`/`ger`/`pt-BR`), gives English names (`languageName` on the tracks, shown by
  `TrackControl.tsx`) and `PickPreferred`.
  - Browser: `FileStreamService` sets `AudioStartIndex` (preferred) + `AudioDefaultIndex` (track inside the stream:
    the remux `<id>-audio<index>.mp4` carries the preferred one; metadata cache key includes the preferences) and
    `SubtitleDefaultIndex` (preferred, else an external file).
  - Native: `AudioSubtitlesService.AutoSelectAudioAndSubtitles`; no match = mpv's default tracks.
  - `SettingsRepository` rereads `settings.json` when its write time changes (players are other processes).
- **Subtitle delay + syncer**: `subtitleDelay` (seconds, positive = later, mpv `sub-delay` sign) lives in the player
  store, changed only through `player/subtitle-delay.ts` (store + `subtitle_delay_set`; browser renderers
  `VttSubtitleOverlay` (checked per frame) / `AssSubtitleOverlay` read the store, native → `MpvPlayer.SubtitleSetDelay`);
  reset per file (`perFileSyncState`, mpv `sub-delay 0` in `LoadMedia`) and on track change.
  - Subtitle menu → Sync opens `Components/SubtitleSync/SubtitleSync.tsx` (`subtitleSyncOpen`): canvas waveform lane
    (drag/click = seek) over a subtitle-line lane (drag = delay, snaps a grabbed line onto the playhead, Alt = free;
    double-click / `S` = align line to playhead; `Z`/`X` ±50 ms, Shift ±10 ms; wheel = zoom), playhead fixed in the
    middle.
  - Lines: `subtitle-cues.ts` parses VTT/SRT/ASS text from `subtitleFiles` (native: `subtitle_text_requested` →
    `SubtitleSyncService` = ffmpeg → SRT, or the subtitle file itself → `subtitle_text`); bitmap subtitles (PGS/DVD,
    `isImageSubtitle`) only get the delay buttons.
  - Waveform: only windows of `WAVEFORM_WINDOW_SECONDS` around the view are read (`waveform_requested` → browser
    `api/video/{id}/audio/{track}/waveform?start&duration`, native `waveform` event with base64 peaks) by
    `Popcorn.Core/Shared/Ffmpeg/AudioWaveform` (input seek, mono speech band 150–4000 Hz, 100 peaks/s, sqrt-scaled
    bytes) into store `waveforms`.
