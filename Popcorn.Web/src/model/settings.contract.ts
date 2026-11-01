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

export interface Settings {
  useInternalPlayer: boolean;
  useInternalTorrentClient: boolean;
  /** Native players in order of preference; first enabled one that starts is used. */
  players: PlayerOption[];
}
