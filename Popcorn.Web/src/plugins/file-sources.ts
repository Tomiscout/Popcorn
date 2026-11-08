import { useCallback, useMemo } from 'react';
import type { FileLink, FileLinkKind, FileQuery, FileResult, FileSortField, LinkTarget } from '@popcorn/plugin-sdk';
import { notifications } from '@mantine/notifications';
import { DownloadAddBatchRequest, DownloadAddRequest, DownloadKind } from '../model/download.contract';
import { useSettings } from '../state/settings.store';
import {
  OpenLinkRequest,
  PluginFileSearchRequest,
  PluginFileSearchResponse,
  PluginResolveLinksRequest,
  PluginResolveLinksResponse,
} from '../model/plugin.contract';
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

/** The file source a result came from: fetches its `resolve` links. */
export type LinkSource = Pick<FileSource, 'pluginId' | 'sourceId'>;

const LINK_PREFERENCE: FileLinkKind[] = ['Magnet', 'Torrent', 'Direct'];

/** The link of a result Popcorn's own client downloads in a batch (magnet first), null if it has none. */
export function downloadableLink(file: FileResult): FileLink | null {
  for (const kind of LINK_PREFERENCE) {
    const link = file.links.find((l) => l.kind === kind && (l.resolve || INTERNAL_LINK.test(l.url)));
    if (link) return link;
  }
  return null;
}

const downloadKind = (kind: FileLinkKind): DownloadKind => (kind === 'Direct' ? 'Direct' : 'Torrent');

/** Server limit 30 s (FlareSolverr solving a challenge) plus some slack. */
const RESOLVE_TIMEOUT_MS = 40_000;

function requestResolve(client: WebSocketClient, source: LinkSource, links: FileLink[]) {
  const requestId = `${Date.now()}-${++requestCounter}`;
  return new Promise<PluginResolveLinksResponse>((resolve, reject) => {
    const timer = setTimeout(() => {
      off();
      reject(new Error('The server did not answer'));
    }, RESOLVE_TIMEOUT_MS);
    const off = client.onEvent<PluginResolveLinksResponse>(SocketRequests.PluginResolveLinks, (r) => {
      if (r.requestId !== requestId) return;
      clearTimeout(timer);
      off();
      resolve(r);
    });
    client.sendEvent<PluginResolveLinksRequest>(SocketRequests.PluginResolveLinks, {
      pluginId: source.pluginId,
      sourceId: source.sourceId,
      requestId,
      links,
    });
  });
}

type Resolved = { link: FileLink | null; error: string | null };

/** Links of one source, `resolve` ones fetched from the plugin (one request), the rest as they are. */
async function resolveLinks(client: WebSocketClient, source: LinkSource, links: FileLink[]): Promise<Resolved[]> {
  const pending = links.filter((l) => l.resolve);
  if (pending.length === 0) return links.map((link) => ({ link, error: null }));
  let response: PluginResolveLinksResponse;
  try {
    response = await requestResolve(client, source, pending);
  } catch (e) {
    const error = e instanceof Error ? e.message : String(e);
    return links.map((link) => (link.resolve ? { link: null, error } : { link, error: null }));
  }
  let i = 0;
  return links.map((link) => {
    if (!link.resolve) return { link, error: null };
    const k = i++;
    const resolved = response.links[k] ?? null;
    return { link: resolved, error: resolved ? null : (response.errors[k] ?? 'The plugin found no link') };
  });
}

/** Runs `work` with a "Getting the link…" notification when it has links to fetch; errors replace it. */
async function withFetchNotice<T>(needed: boolean, count: number, work: () => Promise<T>): Promise<T> {
  if (!needed) return work();
  const id = notifications.show({
    loading: true,
    autoClose: false,
    withCloseButton: false,
    message: count === 1 ? 'Getting the link…' : `Getting ${count} links…`,
  });
  try {
    return await work();
  } finally {
    notifications.hide(id);
  }
}

/**
 * Downloads several results with Popcorn's own client into one library item (`media.itemId`, else a new item named
 * after `media` / `fallbackTitle`), e.g. the episodes of a season. One link per result, see `downloadableLink`; links
 * the plugins fetch on use are fetched first (one request per source).
 */
export function useDownloadFiles() {
  const { client } = useWebSocket();
  return useCallback(
    async (items: { plugin: LinkSource; file: FileResult }[], media: LinkMedia | undefined, fallbackTitle: string) => {
      const wanted = items
        .map((i) => ({ source: i.plugin, link: downloadableLink(i.file) }))
        .filter((w): w is { source: LinkSource; link: FileLink } => w.link !== null);
      if (wanted.length === 0) {
        notifications.show({ color: 'red', message: 'None of these results has a link Popcorn can download' });
        return;
      }

      const bySource = new Map<string, { source: LinkSource; indexes: number[] }>();
      wanted.forEach((w, index) => {
        const key = sourceKey(w.source.pluginId, w.source.sourceId);
        const group = bySource.get(key) ?? { source: w.source, indexes: [] };
        group.indexes.push(index);
        bySource.set(key, group);
      });
      const toFetch = wanted.filter((w) => w.link.resolve).length;
      const resolved: Resolved[] = new Array(wanted.length);
      await withFetchNotice(toFetch > 0, toFetch, () =>
        Promise.all(
          [...bySource.values()].map(async ({ source, indexes }) => {
            const results = await resolveLinks(
              client,
              source,
              indexes.map((i) => wanted[i].link)
            );
            results.forEach((r, k) => (resolved[indexes[k]] = r));
          })
        )
      );

      const links = resolved.map((r) => r.link).filter((l): l is FileLink => l !== null && INTERNAL_LINK.test(l.url));
      const failed = resolved.filter((r) => r.error);
      if (failed.length > 0)
        notifications.show({
          color: 'red',
          autoClose: 8000,
          message: `${failed.length} of ${resolved.length} links could not be fetched: ${failed[0].error}`,
        });
      if (links.length === 0) return;
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
 * (Settings → Downloads). `resolve` links are fetched from their `source` first.
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

  const openResolved = useCallback(
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
      // after fetching a `resolve` link this is no longer the click, so a popup blocker may stop it
      else window.open(link.url, '_blank', 'noopener,noreferrer');
    },
    [client, defaultTarget]
  );

  const openLink = useCallback(
    async (link: FileLink, target?: LinkTarget, media?: LinkMedia, source?: LinkSource) => {
      if (!link.resolve) return openResolved(link, target, media);
      if (!source) {
        console.error('A link to fetch without its file source', link);
        return;
      }
      const [{ link: resolved, error }] = await withFetchNotice(true, 1, () => resolveLinks(client, source, [link]));
      if (resolved) openResolved(resolved, target, media);
      else notifications.show({ color: 'red', autoClose: 8000, message: `Could not get the link: ${error}` });
    },
    [client, openResolved]
  );

  return { openLink, defaultTarget };
}
