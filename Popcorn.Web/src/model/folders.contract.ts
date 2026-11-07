export interface MediaScanResult {
  folderPath: string;
  title: string;
  files: string[];
}

export type LibraryFoldersData = Record<string, MediaScanResult[]>;
