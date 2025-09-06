import { create } from 'zustand';
import { MediaFileMetadata, SubtitleTrackDto } from '../query/models/video.models';

interface PlayerState {
  fileId: string | null;
  isPlaying: boolean;
  isFloating: boolean;
  isFullscreen: boolean;
  currentTime: number;
  duration: number;
  currentExtraTrack: number;
  metadata: MediaFileMetadata | null;
  currentSubtitle: SubtitleTrackDto | null;
  subtitleFiles: { [key: string]: string };
  setFileId: (v: string | null) => void;
  setIsPlaying: (v: boolean) => void;
  setIsFloating: (v: boolean) => void;
  setIsFullscreen: (v: boolean) => void;
  setCurrentTime: (v: number) => void;
  setDuration: (v: number) => void;
  setMetadata: (v: MediaFileMetadata) => void;
  setCurrentSubtitle: (v: SubtitleTrackDto | null) => void;
  addSubtitleTrack: (newTrack: SubtitleTrackDto) => void;
  addSubtitleFile: (key: string, file: string) => void;
  getSubtitleFile: (v: string) => string | null;
  resetState: () => void;
  getExtraTrackKey: () => string;
}

export const usePlayerStore = create<PlayerState>((set) => ({
  fileId: null,
  isPlaying: false,
  isFloating: false,
  isFullscreen: false,
  currentTime: -1,
  duration: -1,
  currentExtraTrack: 1000,
  metadata: null,
  currentSubtitle: null,
  subtitleFiles: {},
  setFileId: (v) => set({ fileId: v }),
  setIsPlaying: (v) => set({ isPlaying: v }),
  setIsFloating: (v) => set({ isFloating: v }),
  setIsFullscreen: (v) => set({ isFullscreen: v }),
  setCurrentTime: (v) => set({ currentTime: v }),
  setDuration: (v) => set({ duration: v }),
  setMetadata: (v) => set({ metadata: v }),
  setCurrentSubtitle: (v) => set({ currentSubtitle: v }),
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
  getExtraTrackKey: () => {
    const state: PlayerState = usePlayerStore.getState();
    const newKey = `extra-${state.currentExtraTrack}`;
    set({ currentExtraTrack: state.currentExtraTrack + 1 });
    return newKey;
  },
  resetState: () => {
    //Need to revoke blob URLs
    const state: PlayerState = usePlayerStore.getState();
    Object.values(state.subtitleFiles).forEach((file) => {
      if (file.startsWith('blob:')) {
        URL.revokeObjectURL(file);
      }
    });

    set({
      fileId: null,
      isPlaying: false,
      isFloating: false,
      isFullscreen: false,
      currentTime: -1,
      duration: -1,
      metadata: null,
      currentSubtitle: null,
      subtitleFiles: {},
    });
  },
}));
