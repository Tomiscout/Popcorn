export interface AudioTrackDto {
  index: number;
  label: string;
  language?: string;
  codec: string;
}

export interface SubtitleTrackDto {
  index: number;
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

  videoDefaultCodec: string | null;
  audioDefaultIndex: number | null;
  subtitleDefaultIndex: number | null;
  formatNames: string[];
}
