import type { PlayerSegmentsDto } from '../../model/segments.contract';
export interface AudioTrackDto {
  index: number;
  label: string;
  language?: string;
  /** English name of `language` ("eng" → "English"). */
  languageName?: string | null;
  codec: string;
}

export interface SubtitleTrackDto {
  index: number;
  label: string;
  language?: string;
  /** English name of `language` ("eng" → "English"). */
  languageName?: string | null;
  format: string;
}

export interface MediaFileMetadata {
  fileId: string;
  filePath: string;
  fullTitle: string;
  shortTitle: string;
  /** Parsed from the file name; null when unknown. */
  season: number | null;
  episode: number | null;

  audioTracks: AudioTrackDto[];
  subtitleTracks: SubtitleTrackDto[];

  videoDefaultCodec: string | null;
  /** Audio track inside the video stream; any other is streamed separately. */
  audioDefaultIndex: number | null;
  /** Audio track playback starts with (preferred language, else the file's default). */
  audioStartIndex?: number | null;
  subtitleDefaultIndex: number | null;
  formatNames: string[];
  /** Intro/outro locations in seconds; a negative start counts from the end of the file. */
  segments?: PlayerSegmentsDto | null; // browser player only, native hosts get segments_update
  /** Chapters stored in the file. */
  chapters?: ChapterInfo[];
  /** Technical details (ffprobe) for the settings menu's Media info tab. */
  mediaInfo?: MediaInfo | null;
  /** Saved position (seconds) playback starts at; null = from the beginning. */
  resumePosition?: number | null;
  /** What the next button plays (its tooltip); null outside the library. */
  nextFile?: NextFilePlan | null;
}

export interface ChapterInfo {
  title: string;
  startSeconds: number;
  endSeconds: number;
}

export interface MediaInfo {
  sections: { title: string; rows: { label: string; value: string }[] }[];
}

/** `NextFilePlan`: what comes after (and before) a file (socket `PlayerNextFile`, native `next_file_plan`). */
export interface NextFilePlan {
  currentFileId: string;
  itemId?: string | null;
  itemTitle?: string | null;
  nextFileId?: string | null;
  nextLabel?: string | null;
  /** Episode name in the next file's name, if it has one. */
  nextEpisodeTitle?: string | null;
  /** Name of the next file without the item's title, for files without season/episode numbers. */
  nextTitle?: string | null;
  /** First episode (S01E02) or season (S02) absent between the current file and the next one. */
  missingLabel?: string | null;
  previousFileId?: string | null;
  previousLabel?: string | null;
  previousEpisodeTitle?: string | null;
  previousTitle?: string | null;
}
