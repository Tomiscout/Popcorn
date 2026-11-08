export interface MediaItemRequest {
  id: string;
}

export interface MediaItem {
  id: string; // Guid
  title: string;
  shortTitle: string;
  image?: string;
  /** Poster, in the server's images folder (load with useFileUrl). */
  imagePath?: string;
  /** Wide background picture, in the server's images folder. */
  backdropPath?: string;
  folderDirectory: string;
  /** Plot summary, from a media source plugin when the item was added. */
  description?: string | null;
  //files: MediaFile[];
  history: MediaItemHistory[];
}

export interface MediaFile {
  id: string;
  itemId: string;
  path: string;
  title: string;
  shortTitle?: string;
  type?: FileType;
  played?: boolean;
  otherTags?: string[];
  formatTags: string[];
  episodes?: string[];
  seasons?: string[];
}

/** Item page's flattened file list: every file in play order (same order as the next file solver). */
export interface MediaItemFlatFileList {
  itemId: string;
  files: MediaFile[];
  /** More than one download feeds the item: the list is flattened unless the user switched it off. */
  multipleDownloads: boolean;
}

/** Deletes one history entry of the item, or all of them without `historyId`. */
export interface DeleteMediaItemHistoryRequest {
  itemId: string;
  historyId?: number;
}

export interface RenameMediaItemRequest {
  id: string;
  title: string;
}

export interface MediaItemHistory {
  /** Id of the history entry (what `MediaItemHistoryDelete` deletes). */
  id: number;
  title: string;
  fileId: string;
  playbackTime: string;
}

export interface MediaListResponse {
  recentItems: MediaItem[];
  newItems: MediaItem[];
  removedItems: MediaItem[];
  /** Per item id: the file its card plays. */
  continue: Record<string, ContinueWatching>;
}

// Popcorn.Server/Models/Responses/MediaListResponse.cs
export interface ContinueWatching {
  fileId: string;
  /** "S01 E08" / "E05", null without an episode number. */
  label: string | null;
  episodeTitle: string | null;
  /** Seconds; null when the file is not started. */
  position: number | null;
  duration: number | null;
}

export interface PlayerPlaylistResponse {
  navigation: FolderLoadedNavigation[];
  files: MediaFile[];
}

export interface FolderLoadedNavigation {
  fileId: string;
  path: string;
  title: string;
}

export enum FileType {
  File = 0,
  Folder = 1,
}

// Popcorn.Domain/Enum/MediaImageKind.cs
export type MediaImageKind = 'Poster' | 'Backdrop';

// Popcorn.Server/Models/Requests/MediaItemRequest.cs
/** Images: http(s) URL (image provider) or data: URL (uploaded file). */
export interface CreateMediaItemRequest {
  folderPath: string;
  title: string;
  posterUrl?: string | null;
  backdropUrl?: string | null;
}

/** `url` null removes the image. */
export interface SetMediaItemImageRequest {
  id: string;
  kind: MediaImageKind;
  url: string | null;
}

// Popcorn.Server/Models/Requests/MediaItemRequest.cs
export interface RemoveMediaItemRequest {
  id: string;
  deleteFiles: boolean;
}
