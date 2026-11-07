import { useCallback, useMemo } from 'react';
import type { FileLink, FileLinkKind, FileQuery, FileResult, FileSortField, LinkTarget } from '@popcorn/plugin-sdk';
import { notifications } from '@mantine/notifications';
import { DownloadAddBatchRequest, DownloadAddRequest, DownloadKind } from '../model/download.contract';
import { useSettings } from '../state/settings.store';
import { OpenLinkRequest, PluginFileSearchRequest, PluginFileSearchResponse } from '../model/plugin.contract';
import { SocketRequests } from '../socket/socket-requests';
import { useSocketEvents } from '../socket/useSocketEvents.hook';
import { useWebSocket } from '../state/websocket-context';
import { WebSocketClient } from '../socket/websocket-client';
import { isDesktopApp } from '../utils/platform';
import { usePlugins } from './plugins.hook';

/** One file source as a provider of results; a plugin can have several. */
export type FileSource = {
  /** `pluginId`, or `pluginId/sourceId` for a source of a multi-source plugin: keys results and remembered UI state. */
  id: string;
  pluginId: string;
  sourceId: string;
  name: string;
  iconUrl: string | null;
  sortFields: FileSortField[];
};

const sourceKey = (pluginId: string, sourceId: string) => (sourceId ? `${pluginId}/${sourceId}` : pluginId);

/** Results of one provider for one search. */
export type ProviderResults<T> = { loading: boolean; results: T[]; error: string | null };

/** One file search (providers keyed by `FileSource.id`): the query, per provider the id of its latest request (a refresh re-asks one provider) and results. */
export type FileSearch = {
  query: FileQuery;
  requests: Record<string, string>;
  results: Record<string, ProviderResults<FileResult>>;
};

/** Setter of a `FileSearch` held by the caller (useState setter, store action). */
export type FileSearchUpdate = (update: (search: FileSearch | null) => FileSearch | null) => void;

export const loadingResults = (pluginIds: string[]) =>
  Object.fromEntries(pluginIds.map((id) => [id, { loading: true, results: [], error: null }]));

let requestCounter = 0;

/** Sends `query` to one file source; its response carries the returned request id. */
function sendFileSearch(client: WebSocketClient, source: FileSource, query: FileQuery): string {
  const requestId = `${Date.now()}-${++requestCounter}`;
  client.sendEvent<PluginFileSearchRequest>(SocketRequests.PluginFileSearch, {
    pluginId: source.pluginId,
    sourceId: source.sourceId,
    requestId,
    query,
  });
  return requestId;
}

/** File sources of all loaded plugins, in the user's plugin order; null while loading. */
export function useFileSources(): FileSource[] | null {
  const plugins = usePlugins();
  return useMemo(
    () =>
      plugins?.flatMap((p) =>
        (p.fileSources ?? []).map((s) => ({
          id: sourceKey(p.id, s.id),
          pluginId: p.id,
          sourceId: s.id,
          name: s.name,
          iconUrl: s.iconUrl,
          sortFields: s.sortFields,
        }))
      ) ?? null,
    [plugins]
  );
}

/**
 * File search over all file sources. The state lives with the caller (`search` / `setSearch`), so a page can keep it
 * across navigation; nothing is searched until `start` is called.
 */
export function useFileSearch(search: FileSearch | null, setSearch: FileSearchUpdate) {
  const { client, isClientReady } = useWebSocket();
  const sources = useFileSources();

  useSocketEvents(
    useMemo(
      () => ({
        [SocketRequests.PluginFileSearch]: (r: PluginFileSearchResponse) =>
          setSearch((s) => {
            const key = sourceKey(r.pluginId, r.sourceId ?? '');
            return s?.requests[key] !== r.requestId
              ? s
              : {
                  ...s,
                  results: { ...s.results, [key]: { loading: false, results: r.results ?? [], error: r.error } },
                };
          }),
      }),
      [setSearch]
    )
  );

  /** Searches every file source for `query`, replacing the previous search. */
  const start = useCallback(
    (query: FileQuery) => {
      if (!isClientReady || !sources?.length) return;
      const requests = Object.fromEntries(sources.map((s) => [s.id, sendFileSearch(client, s, query)]));
      const next = { query, requests, results: loadingResults(sources.map((p) => p.id)) };
      setSearch(() => next);
    },
    [client, isClientReady, sources, setSearch]
  );

  /** Searches one provider (`FileSource.id`) again with the current query. */
  const refresh = useCallback(
    (id: string) => {
      const source = sources?.find((s) => s.id === id);
      if (!isClientReady || !search || !source) return;
      const requestId = sendFileSearch(client, source, search.query);
      setSearch(
        (s) =>
          s && {
            ...s,
            requests: { ...s.requests, [id]: requestId },
            results: { ...s.results, ...loadingResults([id]) },
          }
      );
    },
    [client, isClientReady, search, sources, setSearch]
  );

  return { sources, start, refresh };
}

const SAFE_LINK = /^(magnet:|https?:|ftp:)/i;
/** What the built-in torrent client / downloader can fetch. */
const INTERNAL_LINK = /^(magnet:|https?:)/i;

/**
 * Media a download is for: on a search details page the library item it creates is named / pictured after it; with
 * `itemId` (a library item's page) the files are added to that item instead.
 */
export type LinkMedia = { title: string; posterUrl?: string | null; itemId?: string };

const LINK_PREFERENCE: FileLinkKind[] = ['Magnet', 'Torrent', 'Direct'];

/** The link of a result Popcorn's own client downloads in a batch (magnet first), null if it has none. */
export function downloadableLink(file: FileResult): FileLink | null {
  for (const kind of LINK_PREFERENCE) {
    const link = file.links.find((l) => l.kind === kind && INTERNAL_LINK.test(l.url));
    if (link) return link;
  }
  return null;
}

const downloadKind = (kind: FileLinkKind): DownloadKind => (kind === 'Direct' ? 'Direct' : 'Torrent');

/**
 * Downloads several results with Popcorn's own client into one library item (`media.itemId`, else a new item named
 * after `media` / `fallbackTitle`), e.g. the episodes of a season. One link per result, see `downloadableLink`.
 */
export function useDownloadFiles() {
  const { client } = useWebSocket();
  return useCallback(
    (files: FileResult[], media: LinkMedia | undefined, fallbackTitle: string) => {
      const links = files.map(downloadableLink).filter((l): l is FileLink => l !== null);
      if (links.length === 0) {
        notifications.show({ color: 'red', message: 'None of these results has a link Popcorn can download' });
        return;
      }
      client.sendEvent<DownloadAddBatchRequest>(SocketRequests.DownloadAddBatch, {
        links: links.map((l) => ({ kind: downloadKind(l.kind), url: l.url })),
        mediaItemId: media?.itemId ?? null,
        mediaTitle: media?.title ?? fallbackTitle,
        posterUrl: media?.posterUrl ?? null,
      });
    },
    [client]
  );
}

/**
 * Handles file links. Downloadable links (Magnet / Torrent → torrent, Direct → direct download) go to Popcorn's own
 * client (`internal`: Downloads page, added to the library) or out (`external`): in a browser magnet: goes to the OS
 * handler (torrent program) and the rest opens in a new tab; the desktop app has no such handoff, so the server opens
 * it with the system handler. Page links always open externally. Without a target the user's default applies
 * (Settings → Downloads).
 */
export function useOpenLink() {
  const { client } = useWebSocket();
  const settings = useSettings();

  const defaultTarget = useCallback(
    (kind: FileLinkKind): LinkTarget | null => {
      if (kind === 'Page') return null;
      const internal =
        kind === 'Direct' ? settings?.downloads?.useInternalDirectDownloader : settings?.useInternalTorrentClient;
      return (internal ?? true) ? 'internal' : 'external';
    },
    [settings]
  );

  const openLink = useCallback(
    (link: FileLink, target?: LinkTarget, media?: LinkMedia) => {
      if (!SAFE_LINK.test(link.url)) {
        console.error('Refusing to open link', link.url);
        return;
      }
      if (link.kind !== 'Page' && (target ?? defaultTarget(link.kind)) === 'internal') {
        if (!INTERNAL_LINK.test(link.url)) {
          notifications.show({ color: 'red', message: 'Popcorn can only download magnet and http(s) links' });
          return;
        }
        client.sendEvent<DownloadAddRequest>(SocketRequests.DownloadAdd, {
          kind: downloadKind(link.kind),
          url: link.url,
          addToLibrary: true,
          mediaTitle: media?.title ?? null,
          posterUrl: media?.posterUrl ?? null,
          mediaItemId: media?.itemId ?? null,
        });
        return;
      }
      if (isDesktopApp) client.sendEvent<OpenLinkRequest>(SocketRequests.OpenLink, { url: link.url });
      else if (link.kind === 'Magnet') window.location.href = link.url;
      else window.open(link.url, '_blank', 'noopener,noreferrer');
    },
    [client, defaultTarget]
  );

  return { openLink, defaultTarget };
}
