/** `api/logs/query` (Popcorn.Server/Logs/LogReader.cs). Days are local `YYYY-MM-DD`. */
export interface LogQuery {
  from?: string | null;
  to?: string | null;
  excludeApps?: string[];
  excludeLevels?: string[];
  search?: string;
  limit?: number;
  /** Bytes read per file from the previous answer: only newer lines come back. */
  cursor?: Record<string, number> | null;
}

export interface LogEntry {
  /** `YYYY-MM-DD HH:mm:ss.fff`, local time. */
  time: string;
  app: string;
  level: LogLevel;
  message: string;
  /** Following lines (stack traces). */
  detail?: string | null;
  /** CEF: source file and line. */
  source?: string | null;
}

export interface LogApp {
  name: string;
  label: string;
}

export interface LogQueryResult {
  entries: LogEntry[];
  cursor: Record<string, number>;
  apps: LogApp[];
  oldestDay?: string | null;
  levelCounts: Record<string, number>;
  appCounts: Record<string, number>;
  skipped: number;
}

export type LogLevel = 'VRB' | 'DBG' | 'INF' | 'WRN' | 'ERR' | 'FTL';

export const logLevels: { value: LogLevel; label: string }[] = [
  { value: 'VRB', label: 'Verbose' },
  { value: 'DBG', label: 'Debug' },
  { value: 'INF', label: 'Info' },
  { value: 'WRN', label: 'Warning' },
  { value: 'ERR', label: 'Error' },
  { value: 'FTL', label: 'Fatal' },
];
