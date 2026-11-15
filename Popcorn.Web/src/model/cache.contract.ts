/** What a cached file is for (`CacheKind` on the server). */
export type CacheKind = 'Thumbnails' | 'Remux' | 'Audio' | 'Subtitles';

/** Totals of one kind of cached data. */
export interface CacheKindSummary {
  kind: CacheKind;
  /** Folders it is kept in. */
  folders: string[];
  files: number;
  bytes: number;
}

/** The cached files of one kind made for one media file. */
export interface CacheEntry {
  kind: CacheKind;
  /** The media file they were made from; null when the name doesn't start with a file id. */
  fileId?: string | null;
  /** Its media item; null when the file is no longer in the library. */
  itemId?: string | null;
  itemTitle?: string | null;
  /** Path of the media file; null when it is no longer in the library. */
  filePath?: string | null;
  files: number;
  bytes: number;
  /** Latest write time of its files (UTC ISO). */
  lastWrite: string;
  /** A remux still being written. */
  inProgress: boolean;
}

/** `CacheList` answer. */
export interface CacheOverview {
  kinds: CacheKindSummary[];
  /** Largest first. */
  entries: CacheEntry[];
  /** Start-up cleanup: files older than this are deleted… */
  maxAgeDays: number;
  /** …and the stream cache is trimmed to this size (oldest first). */
  streamCacheLimitBytes: number;
  /** The log files of all apps (kept `logRetentionDays` in the settings). */
  logs: LogFolderSummary;
}

export interface LogFolderSummary {
  folder: string;
  files: number;
  bytes: number;
  /** Largest single file (each is capped at `maxFileBytes`). */
  largestFileBytes: number;
  maxFileBytes: number;
}

/** `CacheClear` answer (the new `CacheList` follows it). */
export interface CacheClearResult {
  files: number;
  bytes: number;
  /** Left in place: still being written, or could not be deleted. */
  skipped: number;
}

export const cacheKindLabels: Record<CacheKind, string> = {
  Thumbnails: 'Thumbnails',
  Remux: 'Remuxed video',
  Audio: 'Converted audio',
  Subtitles: 'Subtitles',
};
