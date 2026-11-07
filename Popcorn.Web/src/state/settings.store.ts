import { useEffect } from 'react';
import { create } from 'zustand';
import { Settings } from '../model/settings.contract';
import { SocketRequests } from '../socket/socket-requests';
import { useWebSocket } from './websocket-context';

/** Last settings the server sent (any `GetSettings` answer, also the echo after saving). */
export const useSettingsStore = create<{ settings: Settings | null }>(() => ({ settings: null }));

export const useSettings = () => useSettingsStore((s) => s.settings);

/** Keeps `useSettingsStore` filled; mounted once by the app. */
export function useSettingsSync() {
  const { client, isClientReady } = useWebSocket();
  useEffect(() => {
    if (!isClientReady) return;
    const unsub = client.onEvent<Settings>(SocketRequests.GetSettings, (settings) =>
      useSettingsStore.setState({ settings })
    );
    client.sendEvent(SocketRequests.GetSettings);
    return unsub;
  }, [client, isClientReady]);
}
