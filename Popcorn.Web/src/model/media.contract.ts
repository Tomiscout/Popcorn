export interface MediaItemRequest {
  id: string;
}

export interface MediaItem {
  id: string; // Guid
  title: string;
  shortTitle: string;
  image?: string;
  imagePath?: string;
  folderDirectory: string;
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
}

export interface MediaItemHistory {
  title: string;
  fileId: string;
  playbackTime: string;
}

export interface MediaListResponse {
  recentItems: MediaItem[];
  newItems: MediaItem[];
  removedItems: MediaItem[];
}

export interface NextFileResponse {
  fileId: string;
  title: string;
}

export enum FileType {
  File = 0,
  Folder = 1,
}
