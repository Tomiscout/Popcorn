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
  //history: ItemHistory[];
}

export interface MediaFile {
  id?: string;
  itemId: string;
  path: string;
  title: string;
  shortTitle?: string;
  type?: FileType;
  played?: boolean;
}

export interface MediaItemHistory {
  itemId: string;
  fileId: string;
  playbackTime: string;
}

export enum FileType {
  File = 'File',
  Folder = 'Folder',
}
