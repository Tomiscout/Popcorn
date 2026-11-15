/**
 * Waveform drawing shared by the player's subtitle syncer and the segment range editor. Peaks come from the server's
 * `AudioWaveform` (one byte per 1/`WAVEFORM_PEAKS_PER_SECOND` s, square-root scaled 0–255).
 */

/** Peaks per second (Popcorn.Shared `AudioWaveform.PeaksPerSecond`). */
export const WAVEFORM_PEAKS_PER_SECOND = 100;

/** Canvas sized to its CSS box in device pixels; returns the context scaled to CSS pixels. */
export function prepareCanvas(canvas: HTMLCanvasElement) {
  const dpr = window.devicePixelRatio || 1;
  const width = canvas.clientWidth;
  const height = canvas.clientHeight;
  if (canvas.width !== Math.round(width * dpr) || canvas.height !== Math.round(height * dpr)) {
    canvas.width = Math.round(width * dpr);
    canvas.height = Math.round(height * dpr);
  }
  const ctx = canvas.getContext('2d')!;
  ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
  ctx.clearRect(0, 0, width, height);
  return { ctx, width, height };
}

/** Tick spacing (minor, labelled) for the seconds shown. */
export function tickSteps(span: number): [number, number] {
  if (span <= 6) return [0.25, 1];
  if (span <= 15) return [0.5, 2];
  if (span <= 30) return [1, 5];
  if (span <= 60) return [2, 10];
  if (span <= 150) return [5, 30];
  return [10, 60];
}

export interface WaveColumns {
  /** Linear amplitude (0–1) of the loudest peak under each column. */
  heights: Float32Array;
  /** 1 where any peak was known. */
  known: Uint8Array;
  loudest: number;
}

/**
 * The loudest peak under each of `columns` canvas columns, column 0 at `t0` seconds. `peakAt(i)` is peak i from 0 s
 * (undefined when it is not loaded).
 */
export function waveColumns(
  columns: number,
  t0: number,
  pxPerSec: number,
  peakAt: (index: number) => number | undefined
): WaveColumns {
  const heights = new Float32Array(columns);
  const known = new Uint8Array(columns);
  let loudest = 0;
  for (let c = 0; c < columns; c++) {
    const from = Math.floor((t0 + c / pxPerSec) * WAVEFORM_PEAKS_PER_SECOND);
    const to = Math.max(from + 1, Math.floor((t0 + (c + 1) / pxPerSec) * WAVEFORM_PEAKS_PER_SECOND));
    let peak = 0;
    let have = false;
    for (let p = Math.max(0, from); p < to; p++) {
      const value = peakAt(p);
      if (value === undefined) continue;
      have = true;
      if (value > peak) peak = value;
    }
    // Back to linear amplitude (the server sends square roots), so loud and quiet parts stay apart.
    const amplitude = (peak / 255) ** 2;
    heights[c] = amplitude;
    known[c] = have ? 1 : 0;
    if (amplitude > loudest) loudest = amplitude;
  }
  return { heights, known, loudest };
}

/**
 * Half height of a column's bar: relative to the loudest part in view (with a floor, so near silence stays flat),
 * mildly compressed.
 */
export const waveBarHeight = (amplitude: number, loudest: number, maxHalf: number) =>
  Math.max(1, (amplitude / Math.max(loudest, 0.004)) ** 0.75 * maxHalf);
