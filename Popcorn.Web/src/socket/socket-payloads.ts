// Generated from Popcorn.Server (Contracts/SocketContractWriter.cs) on build. Do not edit.
import { SocketRequests } from './socket-requests';

export interface SubscriptionRequest {
  /** The pushed event: DownloadList, DownloadStats, SegmentsState (+ item id as key). */
  topic: string;
  key?: string | null;
}

/** Web player position in a file, seconds. */
export interface PlaybackProgressRequest {
  fileId?: string;
  position?: number;
  duration?: number;
}

export interface MediaItemRequest {
  id?: string;
}

/** Removes an item from the library; `DeleteFiles` also deletes its downloads and files from disk. */
export interface RemoveMediaItemRequest {
  id?: string;
  deleteFiles?: boolean;
}

export interface MediaItemFileListRequest {
  directory: string;
  id?: string;
}

/** Deletes a history entry of an item (`HistoryId` = ItemHistory.Id), or all of them when null. */
export interface DeleteMediaItemHistoryRequest {
  itemId?: string;
  historyId?: number | null;
}

/** Renames a library item (item page title). */
export interface RenameMediaItemRequest {
  id?: string;
  title: string;
}

export interface ThumbnailRequest {
  fileId?: string;
  timeSeconds?: number;
}

export interface FileOpenWithRequest {
  /** File to open. When null, the next unwatched file of `MediaId` is opened. */
  fileId?: string | null;
  mediaId?: string | null;
  /** `Id` of the player to use. */
  playerId: string;
}

/** Adds a scanned folder to the library. Images: http(s) URL (image provider) or data: URL (upload). */
export interface CreateMediaItemRequest {
  folderPath: string;
  title: string;
  posterUrl?: string | null;
  backdropUrl?: string | null;
}

/** Replaces an item's poster or backdrop. `Url`: http(s) or data: URL; null/empty removes it. */
export interface SetMediaItemImageRequest {
  id?: string;
  kind?: MediaImageKind;
  url?: string | null;
}

export interface MediaImageSearchRequest {
  /** Item name as typed or the folder name; parsed for title/year. */
  name: string;
  kind?: MediaImageKind;
  /** Library item the images are for: its `Source` identifies the title. */
  itemId?: string | null;
  /** Echoed in the response so the client can drop answers to older searches. */
  requestId?: string | null;
}

export interface SettingsModel {
  /** Legacy (pre player list) switch: true = Popcorn player, false = default app. Only used to seed `Players`. */
  useInternalPlayer?: boolean;
  /** Magnet / .torrent links are downloaded by the built-in torrent client (true) or handed to the system's torrent program. */
  useInternalTorrentClient?: boolean;
  prefferedAudioLanguages: string[];
  prefferedSubtitleLanguages: string[];
  /** Native players in order of preference, used when the server opens a file (desktop app, FileOpen). */
  players: PlayerOption[];
  downloads: DownloadSettings;
  /** When players skip intros/outros on their own; library items can override it. */
  segmentAutoSkip?: SegmentAutoSkip;
  /** Skipping an outro that ends within 10 seconds of the end of the file skips those last seconds too. */
  skipOutroTail?: boolean;
  /** Auto-skip jumps past an intro/outro as soon as it starts instead of after the skip button's count-down. */
  skipSegmentsWithoutDelay?: boolean;
}

export interface AutostartSetRequest {
  enabled?: boolean;
}

export interface PluginSearchRequest {
  pluginId: string;
  query: string;
}

export interface PluginDetailsRequest {
  pluginId: string;
  id: string;
}

export interface PluginSetEnabledRequest {
  pluginId: string;
  enabled?: boolean;
}

export interface PluginUninstallRequest {
  pluginId: string;
}

export interface PluginSetOrderRequest {
  /** Plugin ids in the new order. */
  pluginIds: string[];
}

export interface PluginFileSearchRequest {
  pluginId: string;
  /** `Id`; "" for a plugin that is itself the file source. */
  sourceId: string;
  /** Client-chosen, echoed in the response so it can tell its searches apart. */
  requestId: string;
  query?: FileQuery | null;
}

export interface PluginSettingsRequest {
  pluginId: string;
}

export interface PluginSettingsSaveRequest {
  pluginId: string;
  /** Key → new value. A key left out stays as it is; null or blank removes the value. */
  values: Record<string, unknown>;
}

export interface PluginActionRequest {
  pluginId: string;
  actionId: string;
}

/** `Resolve` links of one file source to turn into usable ones. */
export interface PluginResolveLinksRequest {
  pluginId: string;
  sourceId: string;
  /** Echoed in the response. */
  requestId: string;
  links: FileLink[];
}

export interface OpenLinkRequest {
  url: string;
}

export interface SegmentsDetectRequest {
  itemId?: string;
  fileIds: string[];
}

export interface SegmentsSetTypeRequest {
  itemId?: string;
  segmentId?: string;
  type?: SegmentType;
}

export interface SegmentsDeleteRequest {
  itemId?: string;
  segmentId?: string;
}

/** Per file id the segments it should have (replacing its saved ones); files left out are not touched. */
export interface SegmentsApplyRequest {
  itemId?: string;
  files: Record<string, SegmentPick[]>;
}

export interface SegmentAutoSkipSetRequest {
  itemId?: string;
  /** Null = use the global setting. */
  mode?: SegmentAutoSkip | null;
}

export interface DownloadAddRequest {
  kind?: DownloadLinkKind;
  url: string;
  /** Create a library item for the downloaded videos (downloads started from Search). */
  addToLibrary?: boolean;
  /** Library item title, e.g. the media page's title; null = the download name. */
  mediaTitle?: string | null;
  posterUrl?: string | null;
  backdropUrl?: string | null;
  /** Media page the download was started from (plugin + media id, IMDb id), kept by the library item. */
  mediaSource?: MediaSourceRef | null;
  /** Existing library item to add the files to (saved inside its folder); overrides the title/poster. */
  mediaItemId?: string | null;
}

/**
 * Several downloads for one library item (e.g. the episodes of a season): `MediaItemId`, or a new item
 * titled `MediaTitle` that each download is added to.
 */
export interface DownloadAddBatchRequest {
  links: DownloadLinkRequest[];
  mediaItemId?: string | null;
  mediaTitle?: string | null;
  posterUrl?: string | null;
  backdropUrl?: string | null;
  mediaSource?: MediaSourceRef | null;
}

export interface DownloadIdRequest {
  id?: string;
}

export interface DownloadRemoveRequest {
  id?: string;
  deleteFiles?: boolean;
}

export interface DownloadSetFilePriorityRequest {
  id?: string;
  index?: number;
  priority?: FilePriority;
}

export interface DownloadSetPriorityRequest {
  id?: string;
  priority?: DownloadPriority;
}

/** The pictures of a library item: `ImagePath` / `BackdropPath`. */
export type MediaImageKind = 'Poster' | 'Backdrop';

/**
 * One entry of the ordered native player list. When a file is opened the first enabled entry that launches
 * successfully is used, the rest are fallbacks.
 */
export interface PlayerOption {
  id: string;
  type?: PlayerOptionType;
  name?: string | null;
  /** Executable path for `Program`. */
  path?: string | null;
  /** Command line for `Script`. */
  command?: string | null;
  enabled?: boolean;
}

/** Settings of the built-in torrent client and direct downloader (Settings → Downloads). */
export interface DownloadSettings {
  /** Downloads go to a folder per download inside this one. Empty = ~/Downloads/Popcorn. */
  downloadFolder: string;
  /** File links of kind Direct are downloaded by Popcorn (true) or opened externally (browser / system). */
  useInternalDirectDownloader?: boolean;
  /** Total download limit in KiB/s, 0 = unlimited. */
  maxDownloadKiBps?: number;
  /** Total upload limit in KiB/s, 0 = unlimited. */
  maxUploadKiBps?: number;
  /** A finished torrent stops seeding once uploaded / size reaches this. 0 = stop as soon as it is complete. */
  seedRatioLimit?: number;
  /** A finished torrent also stops after seeding this many minutes. 0 = no time limit (ratio only). */
  seedTimeLimitMinutes?: number;
  /** Incoming peer connections (TCP + uTP + DHT). 0 = random port chosen by the OS. */
  listenPort?: number;
  maxConnections?: number;
}

/**
 * When the player skips intros/outros on its own (count-down on the skip button). Global in
 * `SegmentAutoSkip`, overridable per library item.
 */
export type SegmentAutoSkip = 'Always' | 'AfterFirstOccurrence' | 'Never';

export interface FileQuery {
  text: string;
  title?: string | null;
  year?: number | null;
  imdbId?: string | null;
  kind?: MediaKind | null;
}

export interface FileLink {
  kind: FileLinkKind;
  url: string;
  label?: string | null;
  resolve?: boolean;
}

export enum SegmentType {
  Intro = 0,
  Outro = 1,
}

export interface SegmentPick {
  segmentId: string;
  start: number;
  length: number;
}

export type DownloadLinkKind = 'Torrent' | 'Direct';

/**
 * The media a library item was added from: a media source plugin's details page (its id) and the IMDb id it gave.
 * Lets image and description lookups ask for exactly that title instead of searching by name.
 */
export interface MediaSourceRef {
  /** Media source plugin id (plugin.json id). */
  pluginId?: string | null;
  /** That plugin's own id of the media (MediaDetails.Id, e.g. "tv:246" for TMDb). */
  mediaId?: string | null;
  /** "tt1234567". */
  imdbId?: string | null;
}

export interface DownloadLinkRequest {
  kind?: DownloadLinkKind;
  url: string;
}

export type FilePriority = 'Skip' | 'Low' | 'Normal' | 'High';

export type DownloadPriority = 'Low' | 'Normal' | 'High';

export type PlayerOptionType = 'Popcorn' | 'Default' | 'Program' | 'Script';

export type MediaKind = 'Movie' | 'Show';

export type FileLinkKind = 'Magnet' | 'Torrent' | 'Direct' | 'Page';

/** Payload the server's handler of each event takes (`sendEvent`). */
export interface SocketPayloads {
  [SocketRequests.Subscribe]: SubscriptionRequest;
  [SocketRequests.Unsubscribe]: SubscriptionRequest;
  [SocketRequests.MediaList]: void;
  [SocketRequests.PlaybackProgress]: PlaybackProgressRequest;
  [SocketRequests.MediaItem]: MediaItemRequest;
  [SocketRequests.RemoveMediaItem]: RemoveMediaItemRequest;
  [SocketRequests.RefreshMediaItem]: string;
  [SocketRequests.OpenMediaItemFolder]: string;
  [SocketRequests.MediaItemFileList]: MediaItemFileListRequest;
  [SocketRequests.MediaItemHistoryDelete]: DeleteMediaItemHistoryRequest;
  [SocketRequests.RenameMediaItem]: RenameMediaItemRequest;
  [SocketRequests.ImageFile]: string[];
  [SocketRequests.ThumbnailFile]: ThumbnailRequest;
  [SocketRequests.FileOpen]: string;
  [SocketRequests.FileOpenWith]: FileOpenWithRequest;
  [SocketRequests.FolderOpen]: string;
  [SocketRequests.FolderOpenPlayer]: string;
  [SocketRequests.NextFile]: string;
  [SocketRequests.PlayerNextFile]: string;
  [SocketRequests.PlayerRefreshFiles]: string;
  [SocketRequests.LibraryFolders]: void;
  [SocketRequests.LibraryFoldersData]: void;
  [SocketRequests.RemoveLibraryFolder]: string;
  [SocketRequests.AddLibraryFolderPicker]: void;
  [SocketRequests.CreateMediaItem]: CreateMediaItemRequest;
  [SocketRequests.SetMediaItemImage]: SetMediaItemImageRequest;
  [SocketRequests.MediaImageSearch]: MediaImageSearchRequest;
  [SocketRequests.GetSettings]: void;
  [SocketRequests.UpdateSettings]: SettingsModel;
  [SocketRequests.DefaultPlayerName]: void;
  [SocketRequests.PickPlayerProgram]: void;
  [SocketRequests.OpenProgramFolder]: void;
  [SocketRequests.Autostart]: void;
  [SocketRequests.AutostartSet]: AutostartSetRequest;
  [SocketRequests.LanguageList]: void;
  [SocketRequests.UpdateStatus]: void;
  [SocketRequests.UpdateCheck]: void;
  [SocketRequests.UpdateInstall]: void;
  [SocketRequests.PluginList]: void;
  [SocketRequests.PluginSearch]: PluginSearchRequest;
  [SocketRequests.PluginDetails]: PluginDetailsRequest;
  [SocketRequests.PluginSetEnabled]: PluginSetEnabledRequest;
  [SocketRequests.PluginUninstall]: PluginUninstallRequest;
  [SocketRequests.PluginSetOrder]: PluginSetOrderRequest;
  [SocketRequests.PluginFileSearch]: PluginFileSearchRequest;
  [SocketRequests.PluginSettings]: PluginSettingsRequest;
  [SocketRequests.PluginSettingsSave]: PluginSettingsSaveRequest;
  [SocketRequests.PluginAction]: PluginActionRequest;
  [SocketRequests.PluginResolveLinks]: PluginResolveLinksRequest;
  [SocketRequests.OpenLink]: OpenLinkRequest;
  [SocketRequests.SegmentsState]: string;
  [SocketRequests.SegmentsDetect]: SegmentsDetectRequest;
  [SocketRequests.SegmentsScan]: string;
  [SocketRequests.SegmentsSetType]: SegmentsSetTypeRequest;
  [SocketRequests.SegmentsDelete]: SegmentsDeleteRequest;
  [SocketRequests.SegmentsApply]: SegmentsApplyRequest;
  [SocketRequests.SegmentSeen]: string;
  [SocketRequests.SegmentAutoSkipSet]: SegmentAutoSkipSetRequest;
  [SocketRequests.DownloadList]: void;
  [SocketRequests.DownloadStats]: void;
  [SocketRequests.DownloadAdd]: DownloadAddRequest;
  [SocketRequests.DownloadAddBatch]: DownloadAddBatchRequest;
  [SocketRequests.DownloadPause]: DownloadIdRequest;
  [SocketRequests.DownloadResume]: DownloadIdRequest;
  [SocketRequests.DownloadRemove]: DownloadRemoveRequest;
  [SocketRequests.DownloadSetFilePriority]: DownloadSetFilePriorityRequest;
  [SocketRequests.DownloadSetPriority]: DownloadSetPriorityRequest;
  [SocketRequests.DownloadOpenFolder]: DownloadIdRequest;
  [SocketRequests.PickDownloadFolder]: void;
}
