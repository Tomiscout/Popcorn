import { create } from 'zustand';
import { MediaFileMetadata } from '../query/models/video.models';

interface PlayerState {
  fileId: string | null;
  isPlaying: boolean;
  isFloating: boolean;
  isFullscreen: boolean;
  currentTime: number;
  duration: number;
  metadata: MediaFileMetadata | null;
  setFileId: (v: string | null) => void;
  setIsPlaying: (v: boolean) => void;
  setIsFloating: (v: boolean) => void;
  setIsFullscreen: (v: boolean) => void;
  setCurrentTime: (v: number) => void;
  setDuration: (v: number) => void;
  setMetadata: (v: MediaFileMetadata) => void;
}

export const usePlayerStore = create<PlayerState>((set) => ({
  fileId: null,
  isPlaying: false,
  isFloating: false,
  isFullscreen: false,
  currentTime: -1,
  duration: -1,
  metadata: null,
  setFileId: (v) => set({ fileId: v }),
  setIsPlaying: (v) => set({ isPlaying: v }),
  setIsFloating: (v) => set({ isFloating: v }),
  setIsFullscreen: (v) => set({ isFullscreen: v }),
  setCurrentTime: (v) => set({ currentTime: v }),
  setDuration: (v) => set({ duration: v }),
  setMetadata: (v) => set({ metadata: v }),
}));
