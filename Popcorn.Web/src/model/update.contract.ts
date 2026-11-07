/** `UpdateStatus` answer: whether this install can update itself (Popcorn.Installer next to the server). */
export interface UpdateStatus {
  supported: boolean;
  currentVersion: string;
}

export type UpdateCheckStatus = 'Unsupported' | 'UpToDate' | 'Available' | 'Failed';

/** `UpdateCheck` answer. */
export interface UpdateCheckResult {
  status: UpdateCheckStatus;
  /** The newer release (`Available`). */
  version?: string | null;
  /** Its release notes, markdown as written on GitHub. */
  notes?: string | null;
  error?: string | null;
}
