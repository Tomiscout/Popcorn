// Popcorn plugin UI contract (API version 1). Mirrors Popcorn.Plugins.Abstractions (C#, camelCase JSON).
// See Docs/Plugins.md.
import type { ComponentType } from 'react';

export interface MediaBadge {
  label: string;
  /** Any CSS color or Mantine color name; null = theme default. */
  color?: string | null;
}

export interface MediaField {
  label: string;
  value: string;
}

export interface MediaCard<TData = unknown> {
  id: string;
  title: string;
  subtitle?: string | null;
  description?: string | null;
  imageUrl?: string | null;
  badges: MediaBadge[];
  /** False: no details page, the card is not clickable. */
  hasDetails: boolean;
  /** Plugin-defined, whatever the C# side put into `Data`. */
  data?: TData | null;
}

export interface MediaCardSection {
  title: string;
  cards: MediaCard[];
  /** Rendered as a collapsed, expandable row. */
  collapsed?: boolean;
}

export interface MediaDetails<TData = unknown> {
  id: string;
  title: string;
  tagline?: string | null;
  overview?: string | null;
  posterUrl?: string | null;
  backdropUrl?: string | null;
  badges: MediaBadge[];
  fields: MediaField[];
  sections: MediaCardSection[];
  /** How file sources should look this item up; null = by title. */
  fileQuery?: FileQuery | null;
  /** Plugin-defined, whatever the C# side put into `Data`. */
  data?: TData | null;
}

export type MediaKind = 'Movie' | 'Show';

/** What a file source searches for: the typed text on the search page, a media item's details elsewhere. */
export interface FileQuery {
  text: string;
  title?: string | null;
  year?: number | null;
  /** e.g. "tt0133093" */
  imdbId?: string | null;
  kind?: MediaKind | null;
}

/**
 * What a link is, which also decides the download action: Magnet / Torrent → torrent download (built-in torrent client
 * or the system's torrent program); Direct → direct download of the file itself (built-in downloader or the browser);
 * Page → a web page about it (always opened in the browser).
 */
export type FileLinkKind = 'Magnet' | 'Torrent' | 'Direct' | 'Page';

/**
 * Where a download link goes: `internal` = Popcorn's own torrent client / downloader (shows on the Downloads page and
 * is added to the library), `external` = the system's torrent program / the browser.
 */
export type LinkTarget = 'internal' | 'external';

export interface FileLink {
  kind: FileLinkKind;
  url: string;
  /** Button text; null = the app's default for the kind. */
  label?: string | null;
}

export interface FileResult<TData = unknown> {
  id: string;
  title: string;
  subtitle?: string | null;
  description?: string | null;
  badges: MediaBadge[];
  links: FileLink[];
  /** Values of the plugin's sort fields by key; a missing key sorts last. */
  sortValues: Record<string, number>;
  /** Plugin-defined, whatever the C# side put into `Data`. */
  data?: TData | null;
}

/** A numeric column file results can be sorted by (`IFileSourcePlugin.SortFields`). */
export interface FileSortField {
  /** Key in `FileResult.sortValues`. Shared keys: seeders, leechers, size (bytes), added (Unix seconds). */
  key: string;
  label: string;
  /** Default direction. */
  descending: boolean;
}

/** A file source result (search page Files tab, Files section of a details page). */
export interface FileCardProps<TData = unknown> {
  pluginId: string;
  /** Id of the plugin's file source that returned it (`FileSource.Id`); "" for a plugin that is itself the source. */
  sourceId: string;
  file: FileResult<TData>;
  /**
   * Handles a link the way the app does for its built-in buttons. `target` only matters for downloadable links
   * (Magnet / Torrent / Direct); omitted = the user's default (Settings → Downloads).
   */
  openLink: (link: FileLink, target?: LinkTarget) => void;
  /** Default target of a downloadable link of this kind (what `openLink(link)` does), null for Page links. */
  defaultTarget: (kind: FileLinkKind) => LinkTarget | null;
  /**
   * The card's main action: downloads the result with Popcorn's own client into the library (the open media item, or
   * a new one). Undefined when the result has no link Popcorn can download.
   */
  add?: { onAdd: () => void; hint: string };
}

/** A search result in the search page list. */
export interface SearchCardProps<TData = unknown> {
  pluginId: string;
  card: MediaCard<TData>;
  /** Opens the details page; undefined when `card.hasDetails` is false. */
  onOpen?: () => void;
}

/** A small card in a details page section. */
export interface DetailsCardProps<TData = unknown> {
  pluginId: string;
  card: MediaCard<TData>;
  /** Opens the card's details page; undefined when `card.hasDetails` is false. */
  onOpen?: () => void;
}

/** The details page body (below the app's Back button). */
export interface DetailsProps<TData = unknown> {
  pluginId: string;
  details: MediaDetails<TData>;
  /** Opens the details page of a card returned by the same plugin. */
  onOpenCard: (card: MediaCard) => void;
}

/** Every custom component also receives the app's built-in component, to wrap or extend it. */
export type WithDefault<P> = P & { Default: ComponentType<P> };

/**
 * Named exports of a plugin UI module (plugin.json `ui`). All optional: a missing one uses the built-in component.
 * A component that throws is replaced by the built-in one.
 */
export interface PluginUiModule {
  SearchCard?: ComponentType<WithDefault<SearchCardProps<any>>>;
  DetailsCard?: ComponentType<WithDefault<DetailsCardProps<any>>>;
  Details?: ComponentType<WithDefault<DetailsProps<any>>>;
  FileCard?: ComponentType<WithDefault<FileCardProps<any>>>;
}
