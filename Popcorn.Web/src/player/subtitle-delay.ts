import { usePlayerStore } from '../state/player.store';
import { postMessage } from '../utils/events';

/** Rounded to the millisecond: steps of 0.05 s must not drift into 0.30000000000000004. */
const round = (seconds: number) => Math.round(seconds * 1000) / 1000;

/**
 * Sets the subtitle delay (seconds, positive = subtitles later) in the store and tells the host
 * (`subtitle_delay_set`: browser = its subtitle renderers read the store, native = mpv `sub-delay`).
 */
export function setSubtitleDelay(seconds: number) {
  const delay = round(seconds);
  if (delay === usePlayerStore.getState().subtitleDelay) return;
  usePlayerStore.setState({ subtitleDelay: delay });
  postMessage('subtitle_delay_set', delay);
}

export const nudgeSubtitleDelay = (seconds: number) =>
  setSubtitleDelay(usePlayerStore.getState().subtitleDelay + seconds);

/** `+0.350 s`, `−1.200 s`. */
export function formatDelay(seconds: number) {
  const value = round(seconds);
  if (value === 0) return '0.000 s';
  return `${value > 0 ? '+' : '−'}${Math.abs(value).toFixed(3)} s`;
}
