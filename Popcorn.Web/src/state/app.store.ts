import { create } from 'zustand';

type AppState = {
  isAppLoading: boolean;
  isWebSocketConnecting: boolean;
  isWebSocketReconnecting: boolean;
  setAppLoading: (value: boolean) => void;
  setWebSocketConnecting: (value: boolean) => void;
  setWebSocketReconnecting: (value: boolean) => void;
};

export const useAppStore = create<AppState>((set) => ({
  isAppLoading: true,
  isWebSocketConnecting: false,
  isWebSocketReconnecting: false,
  setAppLoading: (value) => set({ isAppLoading: value }),
  setWebSocketConnecting: (value) => set({ isWebSocketConnecting: value }),
  setWebSocketReconnecting: (value) => set({ isWebSocketReconnecting: value }),
}));
