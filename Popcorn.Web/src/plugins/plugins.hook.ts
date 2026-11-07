import { useEffect, useMemo } from 'react';
import { useQuery } from '@tanstack/react-query';
import type { PluginUiModule } from '@popcorn/plugin-sdk';
import { PluginInfo } from '../model/plugin.contract';
import { SocketRequests } from '../socket/socket-requests';
import { useSocketEvents } from '../socket/useSocketEvents.hook';
import { usePluginStore } from '../state/plugin.store';
import { useWebSocket } from '../state/websocket-context';
import { resolveServerHost } from '../utils/url-resolver';

/** All plugins incl. disabled and broken ones (settings); null while loading. */
export function usePluginEntries(): PluginInfo[] | null {
  const { client, isClientReady } = useWebSocket();
  const { plugins, setPlugins } = usePluginStore();

  useSocketEvents(useMemo(() => ({ [SocketRequests.PluginList]: setPlugins }), [setPlugins]));

  useEffect(() => {
    if (isClientReady && plugins === null) client.sendEvent(SocketRequests.PluginList);
  }, [client, isClientReady, plugins]);

  return plugins;
}

/** Plugins usable for search and details (enabled and loaded); null while loading. */
export function usePlugins(): PluginInfo[] | null {
  const plugins = usePluginEntries();
  return useMemo(() => plugins?.filter((p) => p.loaded) ?? null, [plugins]);
}

async function importPluginUi(plugin: PluginInfo): Promise<PluginUiModule> {
  if (!plugin.uiUrl) return {};
  try {
    // Absolute: in dev the page comes from the Vite server, plugins from Popcorn.Server
    return await import(/* @vite-ignore */ `${window.location.protocol}//${resolveServerHost()}${plugin.uiUrl}`);
  } catch (error) {
    console.error(`Failed to load UI of plugin ${plugin.id}, using the built-in UI`, error);
    return {};
  }
}

/** The plugin's UI module ({} = none or failed to load); undefined while loading. */
export function usePluginUi(pluginId: string): PluginUiModule | undefined {
  const plugins = usePlugins();
  const plugin = plugins?.find((p) => p.id === pluginId);
  const { data } = useQuery({
    queryKey: ['plugin-ui', pluginId, plugin?.uiUrl],
    queryFn: () => importPluginUi(plugin!),
    enabled: !!plugin,
    staleTime: Infinity,
    gcTime: Infinity,
  });
  return plugins && !plugin ? {} : data;
}

export const pluginDetailsPath = (pluginId: string, id: string) =>
  `/media-details/${encodeURIComponent(pluginId)}/${encodeURIComponent(id)}`;
