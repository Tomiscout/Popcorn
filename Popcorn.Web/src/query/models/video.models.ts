export interface AudioTrackDto {
  key: string;
  label: string;
  language?: string;
  codec: string;
}

export interface SubtitleTrackDto {
  key: string;
  label: string;
  language?: string;
  format: string;
}

export interface MediaFileMetadata {
  fileId: string;
  filePath: string;
  fullTitle: string;
  shortTitle: string;
  isTvShow: boolean;
  season: number;
  episode: number;
  audioTracks: AudioTrackDto[];
  subtitleTracks: SubtitleTrackDto[];
}
