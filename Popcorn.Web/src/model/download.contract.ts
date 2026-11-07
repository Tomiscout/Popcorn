export type DownloadKind = 'Torrent' | 'Direct';

export type DownloadState = 'Metadata' | 'Hashing' | 'Downloading' | 'Seeding' | 'Paused' | 'Completed' | 'Error';

/** Bandwidth share while other downloads run; a download being streamed ranks above all. */
export type DownloadPriority = 'Low' | 'Normal' | 'High';

/** Order of the files inside a torrent; the file being streamed goes first. */
export type FilePriority = 'Skip' | 'Low' | 'Normal' | 'High';

export interface DownloadFile {
  index: number;
  /** Relative to the download folder. */
  path: string;
  length: number;
  progress: number;
  priority: FilePriority;
  /** Not skipped. */
  wanted: boolean;
  /** A player reads it right now. */
  streaming: boolean;
  isVideo: boolean;
  /** Library file id once the download is linked to a media item. */
  mediaFileId: string | null;
}

export interface Download {
  id: string;
  kind: DownloadKind;
  /** Release name as downloaded (torrent / file name). */
  name: string;
  /** `name` run through the server's file name parser (title, episodes, resolution). */
  displayName: string;
  state: DownloadState;
  error: string | null;
  savePath: string;
  /** Bytes, -1 = unknown yet. */
  size: number;
  downloaded: number;
  progress: number;
  downloadRate: number;
  uploadRate: number;
  uploaded: number;
  ratio: number;
  /** Seconds. */
  eta: number | null;
  peers: number;
  seeds: number;
  addedAt: string;
  /** Library item the files are (or will be) in. */
  mediaItemId: string | null;
  mediaItemTitle: string | null;
  /** Added to an existing item: deleting the files keeps the item. */
  attachedToItem: boolean;
  priority: DownloadPriority;
  /** A player reads from it right now (ranks above every priority). */
  streaming: boolean;
  files: DownloadFile[];
}

export interface DownloadStats {
  downloadRate: number;
  uploadRate: number;
  active: number;
}

export interface DownloadAddRequest {
  /** Torrent = magnet: or .torrent URL. */
  kind: DownloadKind;
  url: string;
  addToLibrary: boolean;
  mediaTitle?: string | null;
  posterUrl?: string | null;
  /** Existing library item to add the files to (saved inside its folder). */
  mediaItemId?: string | null;
}

/** Several downloads for one library item: `mediaItemId`, or a new item titled `mediaTitle`. */
export interface DownloadAddBatchRequest {
  links: { kind: DownloadKind; url: string }[];
  mediaItemId?: string | null;
  mediaTitle?: string | null;
  posterUrl?: string | null;
}

export interface DownloadAddedResponse {
  id: string | null;
  name: string | null;
  error: string | null;
}

/** `api/downloads/playback/{fileId}`: a file that is (or was) downloaded by Popcorn. */
export interface DownloadPlayback {
  downloadId: string;
  fileIndex: number;
  complete: boolean;
  streamPath: string;
  length: number;
  /** Downloaded parts as [start, end] fractions of the playback time. */
  ranges: [number, number][];
}
