import { create } from 'zustand';
import { AudioTrackDto, MediaFileMetadata, NextFilePlan, SubtitleTrackDto } from '../query/models/video.models';
import { FolderLoadedNavigation, MediaFile } from '../model/media.contract';
import type { PlayerSegmentsDto, SegmentAutoSkip } from '../model/segments.contract';

/** Intro/outro segment, in ms. A negative `start` counts from the end of the file. */
export interface PlayerSegment {
  /** Reference the location was matched against (shared by the episodes with the same intro). */
  segmentId: string;
  start: number;
  length: number;
  /** This segment was watched or skipped before. */
  seen: boolean;
}

export interface SegmentAutoSkipState {
  /** Null = file not in the library: no per-item setting. */
  itemId: string | null;
  globalMode: SegmentAutoSkip;
  itemMode: SegmentAutoSkip | null;
}

export const isOutro = (segment: PlayerSegment) => segment.start < 0;

export function shouldAutoSkip(mode: SegmentAutoSkip, segment: PlayerSegment) {
  switch (mode) {
    case 'Always':
      return true;
    case 'AfterFirstOccurrence':
      return segment.seen;
    case 'Never':
      return false;
  }
}

/** Store form of the server's `PlayerSegments`. */
export function fromPlayerSegmentsDto(dto: PlayerSegmentsDto | null | undefined) {
  return {
    segments: (dto?.segments ?? []).map<PlayerSegment>((s) => ({
      segmentId: s.segmentId,
      start: Math.round(s.startSeconds * 1000),
      length: Math.round(s.length * 1000),
      seen: s.seen,
    })),
    segmentAutoSkip: dto
      ? { itemId: dto.itemId ?? null, globalMode: dto.globalMode, itemMode: dto.itemMode ?? null }
      : null,
    skipOutroTail: dto?.skipOutroTail ?? true,
    skipSegmentsWithoutDelay: dto?.skipSegmentsWithoutDelay ?? false,
  };
}

/** What is playing at the current time: intro/outro if inside one, else the file's chapter. */
export function selectCurrentSegmentName(state: PlayerState): string | null {
  const { currentTime, duration, segments, metadata } = state;
  if (currentTime < 0) return null;
  const segment = segments.find((s) => {
    const start = isOutro(s) ? duration + s.start : s.start;
    return duration > 0 && currentTime >= start && currentTime < start + s.length;
  });
  if (segment) return isOutro(segment) ? 'Outro' : 'Intro';
  const seconds = currentTime / 1000;
  return metadata?.chapters?.find((c) => seconds >= c.startSeconds && seconds < c.endSeconds)?.title ?? null;
}

/** Key of a seek-bar thumbnail in `thumbnailFiles` (also the server's ThumbnailFile blob name). */
export const thumbnailKey = (fileId: string | null, timeMs: number) => `thumbnail_${fileId}_${timeMs / 1000}`;

/** Playing a file that is still downloading stops this far before the end of the downloaded data... */
const BUFFER_STOP_MS = 5000;
/** ...and goes on once this much is there, so that a slow download doesn't stop it every other second. */
const BUFFER_RESUME_MS = 10000;
/** A downloaded part reaching this far (fraction of the playback time) ends with the file: nothing to wait for. */
const FILE_END_FRACTION = 0.9999;

const downloadRangeAt = (ranges: [number, number][], fraction: number) =>
  ranges.find(([start, end]) => fraction >= start && fraction <= end);

/** Only downloaded parts of a file that is still downloading can be seeked to. */
export function canSeekTo(state: PlayerState, timeMs: number) {
  const { downloadRanges, duration } = state;
  if (!downloadRanges || duration <= 0) return true;
  return !!downloadRangeAt(downloadRanges, Math.max(0, Math.min(1, timeMs / duration)));
}

/** Whether playback has to wait for the download at the current position (next value of `isBuffering`). */
export function needsBuffering(state: PlayerState) {
  const { downloadRanges, duration, currentTime, isBuffering } = state;
  if (!downloadRanges || duration <= 0 || currentTime < 0) return false;
  const range = downloadRangeAt(downloadRanges, Math.min(1, currentTime / duration));
  if (!range) return true;
  if (range[1] >= FILE_END_FRACTION) return false;
  return range[1] * duration - currentTime < (isBuffering ? BUFFER_RESUME_MS : BUFFER_STOP_MS);
}

export interface PlayerState {
  mediaId: string | null;
  fileId: string | null; // If set - renders the player
  isPlaying: boolean;
  isFloating: boolean;
  isFullscreen: boolean;
  /** 0–1, kept while muted. Changed through `player/volume.ts`, which also tells the host. */
  volume: number;
  muted: boolean;
  isAudioLoading: boolean;
  currentTime: number;
  duration: number;
  currentExtraTrack: number;
  metadata: MediaFileMetadata | null;
  currentSubtitle: SubtitleTrackDto | null;
  currentAudio: AudioTrackDto | null;
  subtitleFiles: { [key: string]: string };
  thumbnailFiles: { [key: string]: string };
  currentPlaylist: MediaFile[] | null;
  currentPlaylistNavigation: FolderLoadedNavigation[] | null;
  segments: PlayerSegment[];
  segmentAutoSkip: SegmentAutoSkipState | null;
  skipOutroTail: boolean;
  /** Auto-skip at once instead of after the skip button's count-down. */
  skipSegmentsWithoutDelay: boolean;
  /** Set = the next episode is missing: the warning dialog is open. */
  nextFileWarning: NextFilePlan | null;
  /** The file played to its end (not the next button); cleared when something plays or seeks again. */
  playbackEnded: boolean;
  /** The ended file was the last one: the Fin image is up. */
  showFin: boolean;
  /** A menu that needs reading is open: the controls don't hide on their own. */
  holdControls: boolean;
  /** Downloaded parts ([start, end] fractions of the playback time) of a file that is still downloading; null = not a download. */
  downloadRanges: [number, number][] | null;
  /** Stopped for the download (`needsBuffering`). Hosts hold playback while set; `isPlaying` stays what the user asked for. */
  isBuffering: boolean;
  setFileId: (v: string | null) => void;
  setMediaId: (v: string | null) => void;
  setIsPlaying: (v: boolean) => void;
  setIsFloating: (v: boolean) => void;
  setIsFullscreen: (v: boolean) => void;
  setCurrentTime: (v: number) => void;
  setDuration: (v: number) => void;
  setMetadata: (v: MediaFileMetadata) => void;
  setCurrentSubtitle: (v: SubtitleTrackDto | null) => void;
  setCurrentAudio: (v: AudioTrackDto | null) => void;
  setCurrentPlaylist: (v: MediaFile[] | null) => void;
  setCurrentPlaylistNavigation: (v: FolderLoadedNavigation[] | null) => void;
  setSegments: (v: PlayerSegmentsDto | null | undefined) => void;
  /** A segment was watched or skipped: the "after first" auto-skip modes skip it (and its kind) from now on. */
  markSegmentSeen: (segment: PlayerSegment) => void;
  setItemAutoSkip: (mode: SegmentAutoSkip | null) => void;
  setDownloadRanges: (v: [number, number][] | null) => void;
  setIsBuffering: (v: boolean) => void;
  setNextFileWarning: (v: NextFilePlan | null) => void;
  setPlaybackEnded: (v: boolean) => void;
  setHoldControls: (v: boolean) => void;
  addSubtitleTrack: (newTrack: SubtitleTrackDto) => void;
  setIsAudioLoading: (v: boolean) => void;
  addSubtitleFile: (key: string, file: string) => void;
  getSubtitleFile: (v: string) => string | null;
  getExtraTrackKey: () => string;
  addThumbnailFile: (key: string, fileUrl: string) => void;
  getThumbnailFile: (key: string) => string | null;
  resetState: () => void;
  closePlayer: () => void;
}

export const usePlayerStore = create<PlayerState>((set) => ({
  mediaId: null,
  fileId: null,
  isPlaying: false,
  isFloating: false,
  isFullscreen: false,
  volume: 1,
  muted: false,
  isAudioLoading: false,
  currentTime: -1,
  duration: -1,
  currentExtraTrack: 1000,
  metadata: null,
  currentSubtitle: null,
  subtitleFiles: {},
  thumbnailFiles: {},
  currentAudio: null,
  currentPlaylist: [],
  currentPlaylistNavigation: [],
  segments: [],
  segmentAutoSkip: null,
  skipOutroTail: true,
  skipSegmentsWithoutDelay: false,
  nextFileWarning: null,
  playbackEnded: false,
  showFin: false,
  holdControls: false,
  downloadRanges: null,
  isBuffering: false,
  setMediaId: (v) => set({ mediaId: v }),
  setFileId: (v) => set({ fileId: v }),
  setIsPlaying: (v) => set({ isPlaying: v }),
  setIsFloating: (v) => set({ isFloating: v }),
  setIsFullscreen: (v) => set({ isFullscreen: v }),
  setIsAudioLoading: (v) => set({ isAudioLoading: v }),
  setCurrentTime: (v) => set({ currentTime: v }),
  setDuration: (v) => set({ duration: v }),
  setMetadata: (v) => set({ metadata: v }),
  setCurrentSubtitle: (v) => set({ currentSubtitle: v }),
  setCurrentAudio: (v) => set({ currentAudio: v }),
  setCurrentPlaylist: (v) => set({ currentPlaylist: v }),
  setCurrentPlaylistNavigation: (v) => set({ currentPlaylistNavigation: v }),
  setSegments: (v) => set(fromPlayerSegmentsDto(v)),
  markSegmentSeen: (segment) =>
    set((state) => ({
      segments: state.segments.map((s) => ({
        ...s,
        seen: s.seen || s.segmentId === segment.segmentId,
      })),
    })),
  setItemAutoSkip: (mode) =>
    set((state) => ({ segmentAutoSkip: state.segmentAutoSkip && { ...state.segmentAutoSkip, itemMode: mode } })),
  setDownloadRanges: (v) => set({ downloadRanges: v }),
  setIsBuffering: (v) => set({ isBuffering: v }),
  setNextFileWarning: (v) => set({ nextFileWarning: v }),
  setPlaybackEnded: (v) => set({ playbackEnded: v, showFin: false }),
  setHoldControls: (v) => set({ holdControls: v }),
  addSubtitleTrack: (newTrack) =>
    set((state) => {
      if (!state.metadata) {
        return state;
      }

      return {
        metadata: {
          ...state.metadata,
          subtitleTracks: [...(state.metadata.subtitleTracks ?? []), newTrack],
        },
      };
    }),
  addSubtitleFile: (key, file) =>
    set((state) => ({
      subtitleFiles: { ...state.subtitleFiles, [key]: file },
    })),
  getSubtitleFile: (trackId) => {
    const state: PlayerState = usePlayerStore.getState();
    return state.subtitleFiles[trackId] || null;
  },
  addThumbnailFile: (key, fileUrl) =>
    set((state) => ({
      thumbnailFiles: { ...state.thumbnailFiles, [key]: fileUrl },
    })),
  getThumbnailFile: (key) => {
    const state: PlayerState = usePlayerStore.getState();
    return state.thumbnailFiles[key] || null;
  },
  getExtraTrackKey: () => {
    const state: PlayerState = usePlayerStore.getState();
    const newKey = `extra-${state.currentExtraTrack}`;
    set({ currentExtraTrack: state.currentExtraTrack + 1 });
    return newKey;
  },
  resetState: () => {
    //Need to revoke blob URLs
    const state: PlayerState = usePlayerStore.getState();
    setTimeout(() => {
      for (const fileData of Object.values(state.subtitleFiles)) {
        if (fileData.startsWith('blob:')) {
          URL.revokeObjectURL(fileData);
        }
      }
      Object.values(state.thumbnailFiles).forEach(URL.revokeObjectURL);
    }, 100);

    set({
      isPlaying: false,
      isAudioLoading: false,
      currentTime: -1,
      duration: -1,
      currentExtraTrack: 1000,
      metadata: null,
      currentSubtitle: null,
      currentAudio: null,
      subtitleFiles: {},
      thumbnailFiles: {},
      segments: [],
      segmentAutoSkip: null,
      nextFileWarning: null,
      playbackEnded: false,
      showFin: false,
      holdControls: false,
      downloadRanges: null,
      isBuffering: false,
    });
  },
  closePlayer: () => {
    set({
      fileId: null,
    });
  },
}));
