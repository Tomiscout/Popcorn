import type { SegmentAutoSkip } from './segments.contract';

export type PlayerOptionType = 'Popcorn' | 'Default' | 'Program' | 'Script';

/** Placeholder in a custom script command line, replaced with the quoted file path. */
export const PLAYER_ARGS_PLACEHOLDER = '%args';

export interface PlayerOption {
  id: string;
  type: PlayerOptionType;
  name?: string | null;
  /** Executable path (`Program`). */
  path?: string | null;
  /** Shell command line (`Script`). */
  command?: string | null;
  enabled: boolean;
}

/** Built-in torrent client + direct downloader (`DownloadSettings`). */
export interface DownloadSettings {
  /** Empty = ~/Downloads/Popcorn. Each download gets a folder of its own inside. */
  downloadFolder: string;
  useInternalDirectDownloader: boolean;
  /** KiB/s, 0 = unlimited. */
  maxDownloadKiBps: number;
  maxUploadKiBps: number;
  /** Stop seeding at uploaded / size; 0 (with no time limit) = stop when complete. */
  seedRatioLimit: number;
  /** 0 = no time limit. */
  seedTimeLimitMinutes: number;
  listenPort: number;
  maxConnections: number;
}

export interface Settings {
  useInternalPlayer: boolean;
  /** Magnet / .torrent links go to the built-in torrent client by default (else the system's torrent program). */
  useInternalTorrentClient: boolean;
  downloads: DownloadSettings;
  /** Native players in order of preference; first enabled one that starts is used. */
  players: PlayerOption[];
  /** When players skip intros/outros on their own; library items can override it in the player's settings menu. */
  segmentAutoSkip: SegmentAutoSkip;
  /** Skipping an outro that ends within 10 seconds of the end of the file skips those seconds too. */
  skipOutroTail: boolean;
  /** Auto-skip at once instead of after the skip button's count-down. */
  skipSegmentsWithoutDelay: boolean;
  /** ISO 639-2 codes in order of preference: the players start with the first audio track in one of them. */
  prefferedAudioLanguages: string[];
  /** Same for subtitles: a track in one of them is turned on at the start. */
  prefferedSubtitleLanguages: string[];
}

/** `LanguageList` entry (server `LanguageOption`). */
export interface LanguageOption {
  /** ISO 639-2/T. */
  code: string;
  /** English name. */
  name: string;
}

/** `Autostart` / `AutostartSet` answer (server `AutostartState`): the OS setting, not part of `Settings`. */
export interface AutostartState {
  /** False = not managed on this platform (yet). */
  supported: boolean;
  enabled: boolean;
}
