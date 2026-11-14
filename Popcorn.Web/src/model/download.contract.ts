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
  port: PortStatus;
}

/** Unknown = not enough time with peers yet; Unreachable = torrents had peers for minutes, none connected to us. */
export type PortReachability = 'Unknown' | 'Reachable' | 'Unreachable';
/** Automatic port forwarding on the router (UPnP / NAT-PMP). NoRouter = no router answered. */
export type RouterMapping = 'Pending' | 'Mapped' | 'Failed' | 'NoRouter';

export interface PortStatus {
  /** Listen port setting, 0 = random. */
  port: number;
  reachability: PortReachability;
  mapping: RouterMapping;
}

export interface DownloadAddRequest {
  /** Torrent = magnet: or .torrent URL. */
  kind: DownloadKind;
  url: string;
  addToLibrary: boolean;
  mediaTitle?: string | null;
  posterUrl?: string | null;
  backdropUrl?: string | null;
  /** Media page the download was started from, kept by the library item (finds its pictures and description). */
  mediaSource?: MediaSourceRef | null;
  /** Existing library item to add the files to (saved inside its folder). */
  mediaItemId?: string | null;
}

/** Several downloads for one library item: `mediaItemId`, or a new item titled `mediaTitle`. */
export interface DownloadAddBatchRequest {
  links: { kind: DownloadKind; url: string }[];
  mediaItemId?: string | null;
  mediaTitle?: string | null;
  posterUrl?: string | null;
  backdropUrl?: string | null;
  mediaSource?: MediaSourceRef | null;
}

/** Popcorn.Domain/Models/MediaSourceRef.cs: a media source plugin's media (its details page) and its IMDb id. */
export interface MediaSourceRef {
  pluginId?: string | null;
  mediaId?: string | null;
  imdbId?: string | null;
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

/** Downloads page bottom panel of one download: subscription `DownloadDetails` + download id. */
export interface DownloadDetails {
  id: string;
  kind: DownloadKind;
  name: string;
  savePath: string;
  /** Magnet link or URL; null for a .torrent file. */
  source: string | null;
  infoHash: string | null;
  addedAt: string;
  completedAt: string | null;
  /** All files, skipped ones too; -1 = unknown. */
  totalSize: number;
  /** Wanted files. */
  size: number;
  downloaded: number;
  uploaded: number;
  ratio: number;
  seedingSeconds: number;
  comment: string | null;
  createdBy: string | null;
  createdAt: string | null;
  private: boolean;
  hashFails: number;
  /** Full copies the connected peers and we have together, null = no peers. */
  availability: number | null;
  trackers: Tracker[];
  peers: Peer[];
  /** Downloaded parts of each file (by file index) as [start, end] fractions of the file. */
  fileRanges: [number, number][][];
  pieces: PieceMap | null;
}

export interface Tracker {
  tier: number;
  url: string;
  /** Ok, Offline, InvalidResponse, Connecting, Unknown. */
  status: string;
  message: string | null;
  seeds: number | null;
  leechers: number | null;
  completed: number | null;
  lastAnnounceSeconds: number | null;
  intervalSeconds: number | null;
}

export interface Peer {
  address: string;
  client: string;
  incoming: boolean;
  seeder: boolean;
  progress: number;
  downloadRate: number;
  uploadRate: number;
  downloaded: number;
  uploaded: number;
  encryption: string;
  /** D/d downloading / we want to, U/u uploading / they want to, E encrypted, I incoming, F fast peer, X extensions. */
  flags: string;
}

/** Pieces of a torrent (chunks of a direct download). */
export interface PieceMap {
  count: number;
  pieceLength: number;
  /** Bitfield, base64, most significant bit first. */
  have: string;
  /** Pieces being downloaded, same encoding; null = unknown. */
  active: string | null;
  /** Peers having each piece, one byte per piece (max 255), base64; null = no peers. */
  availability: string | null;
}
