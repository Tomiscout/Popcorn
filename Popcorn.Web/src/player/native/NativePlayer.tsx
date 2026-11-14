import { useEffect, useMemo, useState } from 'react';
import { IconMovie } from '@tabler/icons-react';
import { dismissToast, showToast } from '../Components/PlayerToasts/toasts.store';
import { PlayerOverlay } from '../PlayerOverlay';
import { PlayerFeatures, PlayerFeaturesProvider } from '../player-features';
import { disableDefaultListeners } from '../Utlis/listener.utils';
import { postMessage, registerEventHandler, unregisterEventHandler } from '../../utils/events';
import { perFileSyncState, thumbnailKey, usePlayerStore } from '../../state/player.store';
import type { PlayerSegmentsDto, SegmentAutoSkip } from '../../model/segments.contract';
import { AudioTrackDto, MediaFileMetadata, SubtitleTrackDto } from '../../query/models/video.models';
import { FileType, FolderLoadedNavigation, MediaFile } from '../../model/media.contract';
import type { ChapterInfo, MediaInfo, NextFilePlan } from '../../query/models/video.models';
import { applyNextFilePlan } from '../next-file';
import { connectNativeBridge, NativeBridge, NativeEvent } from './native-bridge';
import '../PopcornPlayer.css';
import './native.css';

const BRIDGE_TOAST = 'native-bridge';

// ---- Payloads of Popcorn.Player.Core (C#, System.Text.Json defaults unless the contract sets names) ----

interface NativeTrackLoaded {
  filename: string;
  title?: string;
  shortTitle?: string;
  episodeTitle?: string;
  season?: number;
  episode?: number;
  chapters?: ChapterInfo[];
  mediaInfo?: MediaInfo | null;
  nextFile?: NextFilePlan | null;
}

interface NativeMediaTrack {
  id: number;
  language: string;
  languageName?: string;
  description: string;
  selected: boolean;
  /** mpv codec name (subrip, ass, hdmv_pgs_subtitle...). */
  codec?: string;
}

/** Popcorn.Domain PlayerSegments. */
interface NativeSegments {
  ItemId?: string | null;
  GlobalMode: SegmentAutoSkip;
  ItemMode?: SegmentAutoSkip | null;
  SkipOutroTail?: boolean;
  SkipSegmentsWithoutDelay?: boolean;
  Segments?: { SegmentId: string; StartSeconds: number; Length: number; Seen: boolean }[];
}

/** Popcorn.Domain MediaFile (no JSON names, so PascalCase). */
interface NativeMediaFile {
  Id: string;
  ItemId?: string;
  Path: string;
  Title?: string;
  ShortTitle?: string;
  Type?: FileType;
  Played?: boolean;
  FormatTags?: string[];
}

const basename = (path: string) => path.split(/[\\/]/).pop() ?? path;

function metadataFromTrack(track: NativeTrackLoaded): MediaFileMetadata {
  return {
    fileId: track.filename,
    filePath: track.filename,
    fullTitle: track.title ?? basename(track.filename),
    shortTitle: track.shortTitle || track.title || basename(track.filename),
    season: track.season ?? null,
    episode: track.episode ?? null,
    audioTracks: [],
    subtitleTracks: [],
    videoDefaultCodec: null,
    audioDefaultIndex: null,
    subtitleDefaultIndex: null,
    formatNames: [],
    chapters: track.chapters ?? [],
    mediaInfo: track.mediaInfo ?? null,
    nextFile: track.nextFile ?? null,
  };
}

const toAudioTrack = (t: NativeMediaTrack): AudioTrackDto => ({
  index: t.id,
  label: t.description,
  language: t.language,
  languageName: t.languageName,
  codec: '',
});

const toSubtitleTrack = (t: NativeMediaTrack): SubtitleTrackDto => ({
  index: t.id,
  label: t.description,
  language: t.language,
  languageName: t.languageName,
  format: t.codec ?? '',
});

/** byte[] from System.Text.Json (base64). */
const fromBase64 = (data: string) => Uint8Array.from(atob(data), (c) => c.charCodeAt(0));

const toSegmentsDto = (data: NativeSegments | null): PlayerSegmentsDto | null =>
  data && {
    itemId: data.ItemId ?? null,
    globalMode: data.GlobalMode,
    itemMode: data.ItemMode ?? null,
    skipOutroTail: data.SkipOutroTail ?? true,
    skipSegmentsWithoutDelay: data.SkipSegmentsWithoutDelay ?? false,
    segments: (data.Segments ?? []).map((s) => ({
      segmentId: s.SegmentId,
      startSeconds: s.StartSeconds,
      length: s.Length,
      seen: s.Seen,
    })),
  };

const toMediaFile = (f: NativeMediaFile): MediaFile => ({
  id: f.Id,
  itemId: f.ItemId ?? '',
  path: f.Path,
  title: f.Title ?? basename(f.Path),
  shortTitle: f.ShortTitle,
  type: f.Type ?? FileType.File,
  played: f.Played ?? false,
  formatTags: f.FormatTags ?? [],
});

// The native side navigates folders by path.
const toNavigation = (n: { path: string; title: string }): FolderLoadedNavigation => ({
  fileId: n.path,
  path: n.path,
  title: n.title,
});

function revokeThumbnails() {
  Object.values(usePlayerStore.getState().thumbnailFiles).forEach(URL.revokeObjectURL);
}

/** Windows player (set by its native layer): file drops and the host's subtitle file picker. */
const windowsHost = window.playerHost === 'windows';

/**
 * Host for the native players' overlay (player.html): mpv renders the video in its own layer below this
 * transparent page, the C# player owns playback, and this component translates between its events and the shared
 * player UI (store + local event bus).
 */
export const NativePlayer = () => {
  // Started without a file (or it failed to load): nothing to play until one is dropped/opened.
  const [noFile, setNoFile] = useState(false);

  useEffect(() => {
    disableDefaultListeners();
    const store = usePlayerStore;
    let lastThumbnailMs: number | null = null;
    let nativeFullscreen = false;

    // ---- native -> UI ----
    const inbound: Record<string, (data: any) => void> = {
      current_time: (seconds: number | null) => store.getState().setCurrentTime((seconds ?? 0) * 1000),
      duration_update: (seconds: number) => store.getState().setDuration(seconds > 0 ? seconds * 1000 : -1),
      no_file: () => setNoFile(true),
      track_loaded: (track: NativeTrackLoaded) => {
        setNoFile(false);
        revokeThumbnails();
        lastThumbnailMs = null;
        // Duration arrives before track_loaded, keep it.
        store.setState({
          fileId: track.filename,
          metadata: metadataFromTrack(track),
          isPlaying: true,
          currentAudio: null,
          currentSubtitle: null,
          thumbnailFiles: {},
          segments: [],
          segmentAutoSkip: null,
          nextFileWarning: null,
          playbackEnded: false,
          showFin: false,
          subtitleFiles: {},
          ...perFileSyncState,
        });
      },
      audio_subtitles_loaded: (data: { audioTracks?: NativeMediaTrack[]; subtitleTracks?: NativeMediaTrack[] }) => {
        const audio = data.audioTracks ?? [];
        const subtitles = data.subtitleTracks ?? [];
        const selectedAudio = audio.find((t) => t.selected);
        const selectedSubtitle = subtitles.find((t) => t.selected);
        store.setState((s) => ({
          metadata: s.metadata && {
            ...s.metadata,
            audioTracks: audio.map(toAudioTrack),
            subtitleTracks: subtitles.map(toSubtitleTrack),
          },
          currentAudio: selectedAudio ? toAudioTrack(selectedAudio) : null,
          currentSubtitle: selectedSubtitle ? toSubtitleTrack(selectedSubtitle) : null,
        }));
      },
      segments_update: (data: NativeSegments | null) => store.getState().setSegments(toSegmentsDto(data)),
      download_ranges: (data: [number, number][] | null) => store.getState().setDownloadRanges(data ?? null),
      folder_loaded: (data: { navigation?: { path: string; title: string }[]; files?: NativeMediaFile[] }) => {
        store.setState({
          currentPlaylist: (data.files ?? []).map(toMediaFile),
          currentPlaylistNavigation: (data.navigation ?? []).map(toNavigation),
        });
      },
      playback_reset: () => {
        setNoFile(true);
        revokeThumbnails();
        store.setState({
          fileId: null,
          metadata: null,
          isPlaying: false,
          currentTime: -1,
          duration: -1,
          currentAudio: null,
          currentSubtitle: null,
          thumbnailFiles: {},
          segments: [],
          segmentAutoSkip: null,
          downloadRanges: null,
          playbackEnded: false,
          showFin: false,
          subtitleFiles: {},
          ...perFileSyncState,
        });
      },
      // Subtitle syncer: the text of a track (SubRip from ffmpeg, or the subtitle file as it is) and waveform windows.
      subtitle_text: (data: { track: number; text?: string; error?: string }) => {
        const key = data.track.toString();
        if (data.text != null) store.getState().addSubtitleFile(key, data.text);
        else store.getState().setSubtitleTextError(key, data.error ?? 'Could not read the subtitles');
      },
      waveform: (data: { key: string; peaks?: string; error?: string }) =>
        store.getState().setWaveform(data.key, data.peaks != null ? fromBase64(data.peaks) : null),
      // mpv stays on the last frame (keep-open): seeking back or playing again leaves the end (playback_resumed).
      playback_ended: () => store.setState({ isPlaying: false, playbackEnded: true, showFin: false }),
      playback_resumed: () => store.getState().setPlaybackEnded(false),
      next_file_plan: (plan: NextFilePlan) => applyNextFilePlan(plan),
      // Sent with the state replay after the overlay reconnects (C# PlayerCore.ResendState).
      play_state: (playing: boolean) => store.getState().setIsPlaying(playing),
      // OS media controls (MPRIS, media keys; C# MediaSessionService): run as if the overlay's own buttons were used.
      media_control: (action: 'play' | 'pause' | 'play_pause' | 'next') => {
        const { isPlaying } = store.getState();
        if (action === 'next') postMessage('play_next_title');
        else if (action === 'play_pause') postMessage('play_status_changed', !isPlaying);
        else if ((action === 'play') !== isPlaying) postMessage('play_status_changed', action === 'play');
      },
      fullscreen: (fullscreen: boolean) => {
        nativeFullscreen = fullscreen;
        store.getState().setIsFullscreen(fullscreen);
      },
      error: (error) => postMessage('player_error', error),
    };

    let nativeBridge: NativeBridge;
    try {
      nativeBridge = connectNativeBridge({
        onEvent: ({ name, data }: NativeEvent) => {
          const handler = inbound[name];
          if (handler) handler(data);
          else console.log('Unhandled native event', name, data);
        },
        // One persistent toast for the whole outage: retries don't add toasts, the reconnect removes it.
        onConnectionChange: (connected) =>
          connected
            ? dismissToast(BRIDGE_TOAST)
            : showToast({
                id: BRIDGE_TOAST,
                level: 'warning',
                title: 'Lost connection to the player',
                message: 'Controls do nothing until it is back. Reconnecting…',
                persistent: true,
              }),
        onThumbnail: (blobUrl) => {
          const { fileId, addThumbnailFile } = store.getState();
          if (lastThumbnailMs === null) {
            URL.revokeObjectURL(blobUrl);
            return;
          }
          addThumbnailFile(thumbnailKey(fileId, lastThumbnailMs), blobUrl);
        },
      });
    } catch (error) {
      postMessage('player_error', String(error));
      return;
    }
    const send = nativeBridge.send;

    // ---- UI commands -> native (names/types of WebEventHandler.OnMessage) ----
    const outbound: Record<string, (data: any) => void> = {
      play_status_changed: (play: boolean) => {
        store.getState().setIsPlaying(play);
        // While buffering this only records the wish; playback goes on when the data is there.
        if (!play || !store.getState().isBuffering) send('play_status_changed', play);
      },
      skip_seconds: (seconds: number) => send('skip_seconds', Math.round(seconds)),
      set_playback_time: (ms: number) => send('set_playback_time', Math.round(ms)),
      volume: (volume: number) => send('volume', volume),
      play_next_title: () => send('play_next_title'),
      next_file_refresh: () => send('next_file_refresh'),
      next_file_find: (path: string) => send('open_app_path', path),
      audio_track_selected: (id: string) => {
        const index = Number.parseInt(id);
        const track = store.getState().metadata?.audioTracks.find((t) => t.index === index);
        store.getState().setCurrentAudio(track ?? null);
        send('audio_track_selected', index);
      },
      subtitle_track_selected: (id: string) => {
        const index = Number.parseInt(id);
        const track = store.getState().metadata?.subtitleTracks.find((t) => t.index === index);
        store.getState().setCurrentSubtitle(track ?? null);
        send('subtitle_track_selected', index);
      },
      subtitle_delay_set: (delay: number) => send('subtitle_delay_set', delay),
      subtitle_text_requested: (track: number) => send('subtitle_text_requested', track),
      waveform_requested: (request: { track: number; start: number; duration: number; key: string }) =>
        send('waveform_requested', request),
      subtitle_file_select: () => send('subtitle_file_select'),
      play_file: (fileId: string) => send('file_open', fileId),
      folder_open: (folder: { path: string }) => send('folder_open', folder.path),
      folder_open_root: () => send('folder_open_root'),
      thumb_requested: (ms: number) => {
        lastThumbnailMs = ms;
        send('thumb_requested', Math.floor(ms / 1000));
      },
      player_close: () => send('player_close'),
      segment_seen: (segmentId: string) => send('segment_seen', segmentId),
      segment_auto_skip_set: (mode: SegmentAutoSkip | null) => send('segment_auto_skip_set', mode),
    };
    for (const [name, handler] of Object.entries(outbound)) registerEventHandler(name, handler);

    // Fullscreen is UI state in the store (F key, double click, button); the window is the native side's.
    const unsubscribe = store.subscribe((state, prev) => {
      if (state.isFullscreen !== prev.isFullscreen && state.isFullscreen !== nativeFullscreen) {
        send('request_fullscreen', state.isFullscreen);
      }
      if (state.isBuffering !== prev.isBuffering) {
        if (state.isBuffering) send('play_status_changed', false);
        else if (state.isPlaying) send('play_status_changed', true);
      }
    });

    return () => {
      unsubscribe();
      for (const [name, handler] of Object.entries(outbound)) unregisterEventHandler(name, handler);
      nativeBridge.close();
    };
  }, []);

  // Windows: dropped files go to the player by path (WebView2 hands the host the dropped Files); without this
  // WebView2 would navigate to the file.
  useEffect(() => {
    const webview = window.chrome?.webview;
    if (!windowsHost || !webview) return;
    const onDragOver = (event: DragEvent) => {
      event.preventDefault();
      if (event.dataTransfer) event.dataTransfer.dropEffect = 'copy';
    };
    const onDrop = (event: DragEvent) => {
      event.preventDefault();
      const files = event.dataTransfer?.files;
      if (files?.length) webview.postMessageWithAdditionalObjects('files_dropped', files);
    };
    window.addEventListener('dragover', onDragOver);
    window.addEventListener('drop', onDrop);
    return () => {
      window.removeEventListener('dragover', onDragOver);
      window.removeEventListener('drop', onDrop);
    };
  }, []);

  const features = useMemo<PlayerFeatures>(
    () => ({
      floating: false,
      close: false,
      fileDrop: false,
      addSubtitle: windowsHost ? 'host' : false,
      subtitleDelay: true,
    }),
    []
  );

  return (
    <PlayerFeaturesProvider value={features}>
      <PlayerOverlay className="native" />
      {noFile && (
        <div className="native-no-file">
          <IconMovie size={64} stroke={1.2} />
          {/* The Windows player takes files dropped on its window; the Linux one only opens what it is given. */}
          <span>{windowsHost ? 'Drag and drop a video file here to play it' : 'No file to play'}</span>
        </div>
      )}
    </PlayerFeaturesProvider>
  );
};
