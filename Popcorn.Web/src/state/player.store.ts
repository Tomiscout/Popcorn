import { create } from 'zustand';
import { AudioTrackDto, MediaFileMetadata, SubtitleTrackDto } from '../query/models/video.models';
import type { AdditionalAudioFile } from '../player/Components/VideoPlayer';
import { FolderLoadedNavigation, MediaFile } from '../model/media.contract';

interface PlayerState {
  mediaId: string | null;
  fileId: string | null; // If set - renders the player
  isPlaying: boolean;
  isFloating: boolean;
  isFullscreen: boolean;
  isAudioLoading: boolean;
  currentTime: number;
  duration: number;
  currentExtraTrack: number;
  metadata: MediaFileMetadata | null;
  currentSubtitle: SubtitleTrackDto | null;
  currentAudio: AudioTrackDto | null;
  subtitleFiles: { [key: string]: string };
  thumbnailFiles: { [key: string]: string };
  additionalAudioFiles: AdditionalAudioFile[];
  currentPlaylist: MediaFile[] | null;
  currentPlaylistNavigation: FolderLoadedNavigation[] | null;
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
  addSubtitleTrack: (newTrack: SubtitleTrackDto) => void;
  setIsAudioLoading: (v: boolean) => void;
  addSubtitleFile: (key: string, file: string) => void;
  getSubtitleFile: (v: string) => string | null;
  getExtraTrackKey: () => string;
  addThumbnailFile: (key: string, fileUrl: string) => void;
  getThumbnailFile: (key: string) => string | null;
  addAdditionalAudioFile: (file: AdditionalAudioFile) => void;
  resetState: () => void;
  closePlayer: () => void;
}

export const usePlayerStore = create<PlayerState>((set) => ({
  mediaId: null,
  fileId: null,
  isPlaying: false,
  isFloating: false,
  isFullscreen: false,
  isAudioLoading: false,
  currentTime: -1,
  duration: -1,
  currentExtraTrack: 1000,
  metadata: null,
  currentSubtitle: null,
  subtitleFiles: {},
  thumbnailFiles: {},
  additionalAudioFiles: [],
  currentAudio: null,
  currentPlaylist: [],
  currentPlaylistNavigation: [],
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
  addAdditionalAudioFile: (file) =>
    set((state) => ({
      additionalAudioFiles: [...state.additionalAudioFiles, file],
    })),
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
      additionalAudioFiles: [],
    });
  },
  closePlayer: () => {
    set({
      fileId: null,
    });
  },
}));
