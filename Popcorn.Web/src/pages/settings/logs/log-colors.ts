import { LogLevel } from '../../../model/logs.contract';

export const levelColors: Record<LogLevel, string> = {
  VRB: '#77706f',
  DBG: '#8e97ad',
  INF: '#5cc6e6',
  WRN: '#f4b740',
  ERR: '#ff5e5b',
  FTL: '#ff3d81',
};

export const levelColor = (level: string) => levelColors[level as LogLevel] ?? levelColors.INF;

const knownApps: Record<string, string> = {
  'Popcorn.Server': '#b493ff',
  'Popcorn.Player': '#4ad6a7',
  'Popcorn.Linux': '#68b5f5',
  'Popcorn.Windows': '#68b5f5',
  'Popcorn.Installer': '#f78fc0',
  CEF: '#f7a85a',
  'CEF.Player': '#e8d36a',
};

const fallback = ['#7fd4ff', '#c3e88d', '#ffcb6b', '#f78c6c', '#c792ea', '#89ddff', '#ff9cac', '#a6e3a1'];

export const appColor = (app: string) => {
  const known = knownApps[app];
  if (known) return known;
  let hash = 0;
  for (let i = 0; i < app.length; i++) hash = (hash * 31 + app.charCodeAt(i)) | 0;
  return fallback[Math.abs(hash) % fallback.length];
};
