import { useEffect, useMemo, useState } from 'react';
import { PlayerOverlay } from '../PlayerOverlay';
import { PlayerFeatures, PlayerFeaturesProvider } from '../player-features';
import { disableDefaultListeners } from '../Utlis/listener.utils';
import { postMessage, registerEventHandler, unregisterEventHandler } from '../../utils/events';
import { PlayerSegment, thumbnailKey, usePlayerStore } from '../../state/player.store';
import { AudioTrackDto, MediaFileMetadata, SubtitleTrackDto } from '../../query/models/video.models';
import { FileType, FolderLoadedNavigation, MediaFile } from '../../model/media.contract';
import { connectNativeBridge, NativeBridge, NativeEvent } from './native-bridge';
import '../PopcornPlayer.css';
import './native.css';

// ---- Payloads of Popcorn.Player.Core (C#, System.Text.Json defaults unless the contract sets names) ----

interface NativeTrackLoaded {
  filename: string;
  title?: string;
  shortTitle?: string;
  episodeTitle?: string;
  season?: number;
  episode?: number;
}

interface NativeMediaTrack {
  id: number;
  language: string;
  description: string;
  selected: boolean;
}

interface NativeSegment {
  StartSeconds: number;
  Length: number;
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
  const isTvShow = track.season != null && track.episode != null;
  return {
    fileId: track.filename,
    filePath: track.filename,
    fullTitle: track.title ?? basename(track.filename),
    shortTitle: track.shortTitle || track.title || basename(track.filename),
    isTvShow,
    season: track.season ?? 0,
    episode: track.episode ?? 0,
    audioTracks: [],
    subtitleTracks: [],
    videoDefaultCodec: null,
    audioDefaultIndex: null,
    subtitleDefaultIndex: null,
    formatNames: [],
  };
}

const toAudioTrack = (t: NativeMediaTrack): AudioTrackDto => ({
  index: t.id,
  label: t.description,
  language: t.language,
  codec: '',
});

const toSubtitleTrack = (t: NativeMediaTrack): SubtitleTrackDto => ({
  index: t.id,
  label: t.description,
  language: t.language,
  format: '',
});

const toSegments = (data: { intro?: NativeSegment[]; outro?: NativeSegment[] }): PlayerSegment[] =>
  [...(data.intro ?? []), ...(data.outro ?? [])].map((s) => ({
    start: Math.round(s.StartSeconds * 1000),
    length: Math.round(s.Length * 1000),
  }));

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

/**
 * Host for the native players' overlay (player.html): mpv renders the video in its own layer below this
 * transparent page, the C# player owns playback, and this component translates between its events and the shared
 * player UI (store + local event bus).
 */
export const NativePlayer = () => {
  const [bridge, setBridge] = useState<NativeBridge | null>(null);

  useEffect(() => {
    disableDefaultListeners();
    const store = usePlayerStore;
    let lastThumbnailMs: number | null = null;
    let nativeFullscreen = false;

    // ---- native -> UI ----
    const inbound: Record<string, (data: any) => void> = {
      current_time: (seconds: number | null) => store.getState().setCurrentTime((seconds ?? 0) * 1000),
      duration_update: (seconds: number) => store.getState().setDuration(seconds > 0 ? seconds * 1000 : -1),
      track_loaded: (track: NativeTrackLoaded) => {
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
      segments_update: (data) => store.getState().setSegments(toSegments(data ?? {})),
      folder_loaded: (data: { navigation?: { path: string; title: string }[]; files?: NativeMediaFile[] }) => {
        store.setState({
          currentPlaylist: (data.files ?? []).map(toMediaFile),
          currentPlaylistNavigation: (data.navigation ?? []).map(toNavigation),
        });
      },
      playback_reset: () => {
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
        });
      },
      playback_ended: () => store.getState().setIsPlaying(false),
      // Sent with the state replay after the overlay reconnects (C# PlayerCore.ResendState).
      play_state: (playing: boolean) => store.getState().setIsPlaying(playing),
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
    setBridge(nativeBridge);
    const send = nativeBridge.send;

    // ---- UI commands -> native (names/types of WebEventHandler.OnMessage) ----
    const outbound: Record<string, (data: any) => void> = {
      play_status_changed: (play: boolean) => {
        store.getState().setIsPlaying(play);
        send('play_status_changed', play);
      },
      skip_seconds: (seconds: number) => send('skip_seconds', Math.round(seconds)),
      set_playback_time: (ms: number) => send('set_playback_time', Math.round(ms)),
      volume: (volume: number) => send('volume', volume),
      play_next_title: () => send('play_next_title'),
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
      subtitle_file_select: () => send('subtitle_file_select'),
      play_file: (fileId: string) => send('file_open', fileId),
      folder_open: (folder: { path: string }) => send('folder_open', folder.path),
      folder_open_root: () => send('folder_open_root'),
      thumb_requested: (ms: number) => {
        lastThumbnailMs = ms;
        send('thumb_requested', Math.floor(ms / 1000));
      },
      player_close: () => send('player_close'),
    };
    for (const [name, handler] of Object.entries(outbound)) registerEventHandler(name, handler);

    // Fullscreen is UI state in the store (F key, double click, button); the window is the native side's.
    const unsubscribe = store.subscribe((state, prev) => {
      if (state.isFullscreen !== prev.isFullscreen && state.isFullscreen !== nativeFullscreen) {
        send('request_fullscreen', state.isFullscreen);
      }
    });

    return () => {
      unsubscribe();
      for (const [name, handler] of Object.entries(outbound)) unregisterEventHandler(name, handler);
      nativeBridge.close();
    };
  }, []);

  const features = useMemo<PlayerFeatures>(
    () => ({
      floating: false,
      fileDrop: false,
      addSubtitle: bridge?.kind === 'webview' ? 'host' : false,
      subtitleDelay: true,
    }),
    [bridge]
  );

  return (
    <PlayerFeaturesProvider value={features}>
      <PlayerOverlay className="native" />
    </PlayerFeaturesProvider>
  );
};
