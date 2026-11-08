import { useEffect, useMemo, useRef, useState } from 'react';
import { IconArrowBackUp, IconFocusCentered, IconWaveSine, IconX, IconZoomIn, IconZoomOut } from '@tabler/icons-react';
import {
  usePlayerStore,
  waveformKey,
  WAVEFORM_PEAKS_PER_SECOND,
  WAVEFORM_WINDOW_SECONDS,
  type PlayerState,
} from '../../../state/player.store';
import { postMessage } from '../../../utils/events';
import { formatDelay, nudgeSubtitleDelay, setSubtitleDelay } from '../../subtitle-delay';
import { firstCueFrom, isImageSubtitle, parseSubtitleCues, type SubtitleCue } from './subtitle-cues';
import './SubtitleSync.css';

/** Seconds shown across the lanes: default, and the zoom limits. */
const DEFAULT_SPAN = 12;
const MIN_SPAN = 3;
const MAX_SPAN = 60;
const ZOOM_STEP = 1.25;
/** Delay steps: buttons / Z X keys, and with Shift. */
const STEP = 0.05;
const FINE_STEP = 0.01;
/** A dragged line snaps onto the playhead within this many CSS pixels (Alt drags freely). */
const SNAP_PX = 7;
/** Seeks and delay changes while dragging go out at most this often (ms). */
const DRAG_THROTTLE_MS = 90;
/** Pointer movement (px) that turns a press into a drag. */
const DRAG_THRESHOLD = 4;

const COLORS = {
  wave: 'rgba(125, 200, 255, 0.9)',
  waveDim: 'rgba(125, 200, 255, 0.35)',
  missing: 'rgba(255, 255, 255, 0.12)',
  cueBand: 'rgba(255, 190, 70, 0.07)',
  cueLine: 'rgba(255, 190, 70, 0.55)',
  cueFill: 'rgba(255, 190, 70, 0.16)',
  cueFillActive: 'rgba(255, 190, 70, 0.34)',
  cueEdge: '#ffbe46',
  cueText: 'rgba(255, 255, 255, 0.82)',
  cueTextActive: '#fff',
  selected: '#ffffff',
  tick: 'rgba(255, 255, 255, 0.22)',
  tickText: 'rgba(255, 255, 255, 0.5)',
};

const isPlayingNow = (s: PlayerState) => s.isPlaying && !s.isBuffering && !s.isAudioLoading && !s.playbackEnded;

function formatClock(seconds: number, withMs = false) {
  const sign = seconds < 0 ? '-' : '';
  const total = Math.abs(seconds);
  const h = Math.floor(total / 3600);
  const m = Math.floor((total % 3600) / 60);
  const s = total % 60;
  const sec = withMs ? s.toFixed(3).padStart(6, '0') : Math.floor(s).toString().padStart(2, '0');
  return `${sign}${h > 0 ? `${h}:${m.toString().padStart(2, '0')}` : m}:${sec}`;
}

/** Tick spacing (minor, labelled) for the seconds shown. */
function tickSteps(span: number): [number, number] {
  if (span <= 6) return [0.25, 1];
  if (span <= 15) return [0.5, 2];
  if (span <= 30) return [1, 5];
  return [2, 10];
}

/** Canvas sized to its CSS box in device pixels; returns the context scaled to CSS pixels. */
function prepareCanvas(canvas: HTMLCanvasElement) {
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

function roundRect(ctx: CanvasRenderingContext2D, x: number, y: number, w: number, h: number, r: number) {
  const radius = Math.min(r, w / 2, h / 2);
  ctx.beginPath();
  ctx.roundRect(x, y, w, h, radius);
}

type Drag =
  | { kind: 'scrub'; pointerId: number; startX: number; startTime: number; moved: boolean; wasPlaying: boolean }
  | {
      kind: 'shift';
      pointerId: number;
      startX: number;
      startDelay: number;
      cue: number | null;
      moved: boolean;
    };

/**
 * Visual subtitle syncer: the audio's waveform (around the playback position only, read in windows from the host)
 * above the subtitle lines, on one time axis with the playhead in the middle. Dragging the waveform seeks the
 * video; dragging the lines shifts them, which is the subtitle delay. Bitmap subtitles (PGS, DVD) only get the
 * delay buttons.
 */
export const SubtitleSync = () => {
  const metadata = usePlayerStore((s) => s.metadata);
  const fileId = usePlayerStore((s) => s.fileId);
  const currentSubtitle = usePlayerStore((s) => s.currentSubtitle);
  const currentAudio = usePlayerStore((s) => s.currentAudio);
  const delay = usePlayerStore((s) => s.subtitleDelay);
  const subtitleText = usePlayerStore((s) =>
    currentSubtitle ? s.subtitleFiles[currentSubtitle.index.toString()] : undefined
  );
  const textError = usePlayerStore((s) =>
    currentSubtitle ? s.subtitleTextErrors[currentSubtitle.index.toString()] : undefined
  );

  const [span, setSpan] = useState(DEFAULT_SPAN);
  const [selected, setSelected] = useState<number | null>(null);
  const [dragging, setDragging] = useState<Drag['kind'] | null>(null);

  const waveCanvas = useRef<HTMLCanvasElement>(null);
  const cueCanvas = useRef<HTMLCanvasElement>(null);

  const imageBased = isImageSubtitle(currentSubtitle?.format) || !!subtitleText?.startsWith('blob:');
  const cues = useMemo<SubtitleCue[]>(
    () => (subtitleText && !imageBased ? parseSubtitleCues(subtitleText) : []),
    [subtitleText, imageBased]
  );
  // Lines on screen together (signs + dialogue, two speakers) go in a second row instead of over each other.
  const rows = useMemo(() => {
    const result = new Uint8Array(cues.length);
    const rowEnd = [-Infinity, -Infinity];
    cues.forEach((cue, i) => {
      const row = cue.start >= rowEnd[0] - 0.01 ? 0 : 1;
      result[i] = row;
      rowEnd[row] = Math.max(rowEnd[row], cue.end);
    });
    return { of: result, count: result.includes(1) ? 2 : 1 };
  }, [cues]);
  const audioTracks = metadata?.audioTracks ?? [];
  const audioTrack = currentAudio?.index ?? audioTracks[0]?.index ?? null;

  // Everything the draw loop and the pointer handlers read: kept in a ref so the loop is set up once.
  const live = useRef({ span, selected, cues, rows, audioTrack, fileId, dragDelay: null as number | null });
  live.current.span = span;
  live.current.selected = selected;
  live.current.cues = cues;
  live.current.rows = rows;
  live.current.audioTrack = audioTrack;
  live.current.fileId = fileId;

  // New track: no selection from the old one.
  useEffect(() => setSelected(null), [cues]);

  // The native players read the text on request (browser: SubtitleOverlay has loaded it already).
  useEffect(() => {
    if (!currentSubtitle || imageBased || subtitleText || textError) return;
    postMessage('subtitle_text_requested', currentSubtitle.index);
  }, [currentSubtitle, imageBased, subtitleText, textError]);

  // The controls stay up while syncing (the seek bar and play button are part of it).
  useEffect(() => {
    usePlayerStore.getState().setHoldControls(true);
    return () => usePlayerStore.getState().setHoldControls(false);
  }, []);

  // ---- clock: the store's time comes a few times a second, the lanes move every frame ----
  const clock = useRef({ time: Math.max(0, usePlayerStore.getState().currentTime / 1000), at: performance.now() });
  /** While scrubbing (and until the player got there): the time the user dragged to. */
  const scrub = useRef<{ time: number; releasedAt: number | null } | null>(null);

  const predicted = () => {
    const state = usePlayerStore.getState();
    const { time, at } = clock.current;
    return isPlayingNow(state) ? time + (performance.now() - at) / 1000 : time;
  };
  const viewTime = () => scrub.current?.time ?? predicted();

  useEffect(
    () =>
      usePlayerStore.subscribe((state, prev) => {
        const now = performance.now();
        if (isPlayingNow(state) !== isPlayingNow(prev)) clock.current = { time: predicted(), at: now };
        if (state.currentTime === prev.currentTime || state.currentTime < 0) return;
        const reported = state.currentTime / 1000;
        const guess = predicted();
        // Small differences are reporting jitter: ease towards them instead of jumping back and forth.
        const time =
          isPlayingNow(state) && Math.abs(reported - guess) < 0.3 ? guess + (reported - guess) * 0.25 : reported;
        clock.current = { time, at: now };
        const pending = scrub.current;
        if (
          pending?.releasedAt != null &&
          (Math.abs(reported - pending.time) < 0.3 || now - pending.releasedAt > 2000)
        ) {
          scrub.current = null;
        }
      }),
    []
  );

  // ---- waveform windows ----
  const requested = useRef(new Set<string>());
  useEffect(() => requested.current.clear(), [fileId, audioTrack]);

  function requestWindows(from: number, to: number) {
    const { audioTrack, fileId } = live.current;
    if (audioTrack === null) return;
    const { waveforms, duration } = usePlayerStore.getState();
    const last = duration > 0 ? Math.floor(duration / 1000 / WAVEFORM_WINDOW_SECONDS) : Infinity;
    const first = Math.max(0, Math.floor(from / WAVEFORM_WINDOW_SECONDS));
    const end = Math.min(last, Math.floor(to / WAVEFORM_WINDOW_SECONDS));
    for (let window = first; window <= end; window++) {
      const key = waveformKey(fileId, audioTrack, window);
      if (key in waveforms || requested.current.has(key)) continue;
      requested.current.add(key);
      postMessage('waveform_requested', {
        track: audioTrack,
        start: window * WAVEFORM_WINDOW_SECONDS,
        duration: WAVEFORM_WINDOW_SECONDS,
        key,
      });
    }
  }

  // ---- drawing ----
  useEffect(() => {
    let frame = 0;
    const draw = () => {
      frame = requestAnimationFrame(draw);
      const wave = waveCanvas.current;
      const lane = cueCanvas.current;
      if (!wave || !lane) return;
      const { span, selected, cues, rows, audioTrack, fileId, dragDelay } = live.current;
      const state = usePlayerStore.getState();
      const shift = dragDelay ?? state.subtitleDelay;
      const now = viewTime();

      const w = prepareCanvas(wave);
      const pxPerSec = w.width / span;
      const t0 = now - span / 2;
      const t1 = now + span / 2;
      const x = (t: number) => (t - t0) * pxPerSec;
      requestWindows(t0 - WAVEFORM_WINDOW_SECONDS / 2, t1 + WAVEFORM_WINDOW_SECONDS / 2);

      // Visible lines (shifted by the delay); cues are sorted by start, so start one span early for long lines.
      const visible: number[] = [];
      for (let i = Math.max(0, firstCueFrom(cues, t0 - shift - 30)); i < cues.length; i++) {
        const cue = cues[i];
        if (cue.start + shift > t1) break;
        if (cue.end + shift >= t0) visible.push(i);
      }

      // Waveform lane: line bands and starts behind the audio, so speech can be matched to them by eye.
      const tickHeight = 16;
      const waveHeight = w.height - tickHeight;
      const mid = waveHeight / 2;
      for (const i of visible) {
        const cue = cues[i];
        const left = x(cue.start + shift);
        w.ctx.fillStyle = COLORS.cueBand;
        w.ctx.fillRect(left, 0, Math.max(1, x(cue.end + shift) - left), waveHeight);
        w.ctx.fillStyle = COLORS.cueLine;
        w.ctx.fillRect(Math.round(left), 0, 1, waveHeight);
      }

      if (audioTrack !== null) {
        const columns = Math.ceil(w.width);
        const heights = new Float32Array(columns);
        const known = new Uint8Array(columns);
        let loudest = 0;
        for (let c = 0; c < columns; c++) {
          const from = Math.floor((t0 + c / pxPerSec) * WAVEFORM_PEAKS_PER_SECOND);
          const to = Math.max(from + 1, Math.floor((t0 + (c + 1) / pxPerSec) * WAVEFORM_PEAKS_PER_SECOND));
          let peak = 0;
          let have = false;
          for (let p = Math.max(0, from); p < to; p++) {
            const window = Math.floor(p / (WAVEFORM_WINDOW_SECONDS * WAVEFORM_PEAKS_PER_SECOND));
            const peaks = state.waveforms[waveformKey(fileId, audioTrack, window)];
            if (!peaks) continue;
            const value = peaks[p - window * WAVEFORM_WINDOW_SECONDS * WAVEFORM_PEAKS_PER_SECOND];
            if (value === undefined) continue;
            have = true;
            if (value > peak) peak = value;
          }
          // Back to linear amplitude (the host sends square roots), so loud and quiet parts stay apart.
          const amplitude = (peak / 255) ** 2;
          heights[c] = amplitude;
          known[c] = have ? 1 : 0;
          if (amplitude > loudest) loudest = amplitude;
        }
        // Relative to the loudest part in view (with a floor, so near silence stays flat), mildly compressed.
        const reference = Math.max(loudest, 0.004);
        const centre = Math.floor(columns / 2);
        for (let c = 0; c < columns; c++) {
          if (!known[c]) {
            if (c % 4 === 0) {
              w.ctx.fillStyle = COLORS.missing;
              w.ctx.fillRect(c, mid - 0.5, 2, 1);
            }
            continue;
          }
          const h = Math.max(1, (heights[c] / reference) ** 0.75 * (mid - 3));
          w.ctx.fillStyle = c < centre ? COLORS.waveDim : COLORS.wave;
          w.ctx.fillRect(c, mid - h, 1, h * 2);
        }
      }

      // Time ticks under the waveform.
      const [minor, major] = tickSteps(span);
      w.ctx.font = '10px Rubik, sans-serif';
      w.ctx.textAlign = 'center';
      w.ctx.textBaseline = 'bottom';
      for (let t = Math.ceil(t0 / minor) * minor; t <= t1; t += minor) {
        if (t < 0) continue;
        const isMajor = Math.abs(t / major - Math.round(t / major)) < 1e-6;
        w.ctx.fillStyle = COLORS.tick;
        w.ctx.fillRect(Math.round(x(t)), waveHeight, 1, isMajor ? 6 : 3);
        if (isMajor) {
          w.ctx.fillStyle = COLORS.tickText;
          w.ctx.fillText(formatClock(t), x(t), w.height);
        }
      }

      // Subtitle lane: one block per line, its text inside.
      const l = prepareCanvas(lane);
      const pad = 6;
      const gap = 3;
      const blockHeight = (l.height - pad * 2 - gap * (rows.count - 1)) / rows.count;
      l.ctx.font = '12px Rubik, sans-serif';
      l.ctx.textAlign = 'left';
      l.ctx.textBaseline = 'middle';
      for (const i of visible) {
        const cue = cues[i];
        const top = pad + rows.of[i] * (blockHeight + gap);
        const left = x(cue.start + shift);
        const width = Math.max(4, x(cue.end + shift) - left);
        const active = now >= cue.start + shift && now < cue.end + shift;
        l.ctx.fillStyle = active ? COLORS.cueFillActive : COLORS.cueFill;
        roundRect(l.ctx, left, top, width, blockHeight, 5);
        l.ctx.fill();
        if (i === selected) {
          l.ctx.strokeStyle = COLORS.selected;
          l.ctx.lineWidth = 1.5;
          l.ctx.stroke();
        }
        l.ctx.fillStyle = COLORS.cueEdge;
        l.ctx.fillRect(left, top, 2, blockHeight);
        if (width > 18) {
          l.ctx.save();
          l.ctx.beginPath();
          l.ctx.rect(left + 2, top, width - 6, blockHeight);
          l.ctx.clip();
          l.ctx.fillStyle = active ? COLORS.cueTextActive : COLORS.cueText;
          l.ctx.fillText(cue.text, Math.max(left + 8, Math.min(8, left + width - 40)), top + blockHeight / 2);
          l.ctx.restore();
        }
      }
    };
    frame = requestAnimationFrame(draw);
    return () => cancelAnimationFrame(frame);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  // ---- interaction ----
  const drag = useRef<Drag | null>(null);
  const lastSent = useRef(0);

  const pxPerSecond = (element: HTMLElement) => element.clientWidth / live.current.span;
  const timeAt = (element: HTMLElement, clientX: number) => {
    const rect = element.getBoundingClientRect();
    return viewTime() + (clientX - rect.left - rect.width / 2) / pxPerSecond(element);
  };
  const clampTime = (t: number) => {
    const duration = usePlayerStore.getState().duration / 1000;
    return Math.max(0, duration > 0 ? Math.min(duration - 0.05, t) : t);
  };
  const seek = (time: number) => postMessage('set_playback_time', Math.round(clampTime(time) * 1000));

  /** The line under the pointer (its row by height); 4 px of slack around short lines. */
  const cueAt = (element: HTMLElement, clientX: number, clientY: number) => {
    const rect = element.getBoundingClientRect();
    const { cues, rows } = live.current;
    const row = rows.count > 1 && clientY - rect.top > rect.height / 2 ? 1 : 0;
    const slack = 4 / pxPerSecond(element);
    const t = timeAt(element, clientX) - usePlayerStore.getState().subtitleDelay;
    const lowest = firstCueFrom(cues, t - 30);
    for (let i = Math.min(cues.length - 1, firstCueFrom(cues, t + slack)); i >= lowest; i--) {
      if (rows.of[i] === row && cues[i].start - slack <= t && cues[i].end + slack >= t) return i;
    }
    return null;
  };

  /** Delay that puts line `index` on the playhead. */
  const delayToPlayhead = (index: number) => viewTime() - live.current.cues[index].start;

  /** The selected line, else the one whose (shifted) start is closest to the playhead. */
  const alignTarget = () => {
    const { selected, cues } = live.current;
    if (selected !== null && cues[selected]) return selected;
    if (!cues.length) return null;
    const t = viewTime() - usePlayerStore.getState().subtitleDelay;
    const next = Math.min(cues.length - 1, firstCueFrom(cues, t));
    const prev = Math.max(0, next - 1);
    return Math.abs(cues[prev].start - t) < Math.abs(cues[next].start - t) ? prev : next;
  };
  const alignToPlayhead = () => {
    const target = alignTarget();
    if (target === null) return;
    setSelected(target);
    setSubtitleDelay(delayToPlayhead(target));
  };

  /** Keeps the drag going when the pointer leaves the lane (throws for pointers the browser doesn't know). */
  const capture = (e: React.PointerEvent<HTMLElement>) => {
    try {
      e.currentTarget.setPointerCapture(e.pointerId);
    } catch {
      // the drag still works inside the lane
    }
  };

  function onWavePointerDown(e: React.PointerEvent<HTMLCanvasElement>) {
    if (e.button !== 0) return;
    capture(e);
    const state = usePlayerStore.getState();
    drag.current = {
      kind: 'scrub',
      pointerId: e.pointerId,
      startX: e.clientX,
      startTime: viewTime(),
      moved: false,
      wasPlaying: state.isPlaying,
    };
  }

  function onLanePointerDown(e: React.PointerEvent<HTMLCanvasElement>) {
    if (e.button !== 0) return;
    capture(e);
    drag.current = {
      kind: 'shift',
      pointerId: e.pointerId,
      startX: e.clientX,
      startDelay: usePlayerStore.getState().subtitleDelay,
      cue: cueAt(e.currentTarget, e.clientX, e.clientY),
      moved: false,
    };
  }

  function onPointerMove(e: React.PointerEvent<HTMLCanvasElement>) {
    const current = drag.current;
    if (!current || current.pointerId !== e.pointerId) return;
    const dx = e.clientX - current.startX;
    if (!current.moved) {
      if (Math.abs(dx) < DRAG_THRESHOLD) return;
      current.moved = true;
      setDragging(current.kind);
      if (current.kind === 'scrub' && current.wasPlaying) postMessage('play_status_changed', false);
    }
    const pps = pxPerSecond(e.currentTarget);
    const throttled = performance.now() - lastSent.current > DRAG_THROTTLE_MS;

    if (current.kind === 'scrub') {
      // Grabbing the tape: dragging left moves the audio left, i.e. forward in time.
      const time = clampTime(current.startTime - dx / pps);
      scrub.current = { time, releasedAt: null };
      if (throttled) {
        lastSent.current = performance.now();
        seek(time);
      }
      return;
    }

    let next = current.startDelay + dx / pps;
    if (current.cue !== null && !e.altKey) {
      const toPlayhead = delayToPlayhead(current.cue);
      if (Math.abs(toPlayhead - next) * pps < SNAP_PX) next = toPlayhead;
    }
    live.current.dragDelay = next;
    if (throttled) {
      lastSent.current = performance.now();
      setSubtitleDelay(next);
    }
  }

  function onPointerUp(e: React.PointerEvent<HTMLCanvasElement>) {
    const current = drag.current;
    if (!current || current.pointerId !== e.pointerId) return;
    drag.current = null;
    setDragging(null);

    if (current.kind === 'scrub') {
      // A click seeks to the point clicked, a drag to where it was dragged.
      const time = current.moved ? (scrub.current?.time ?? viewTime()) : clampTime(timeAt(e.currentTarget, e.clientX));
      scrub.current = { time, releasedAt: performance.now() };
      seek(time);
      if (current.moved && current.wasPlaying) postMessage('play_status_changed', true);
      return;
    }

    if (current.moved) {
      if (live.current.dragDelay !== null) setSubtitleDelay(live.current.dragDelay);
      live.current.dragDelay = null;
      if (current.cue !== null) setSelected(current.cue);
    } else {
      setSelected(current.cue === live.current.selected ? null : current.cue);
    }
  }

  function onLaneDoubleClick(e: React.MouseEvent<HTMLCanvasElement>) {
    const cue = cueAt(e.currentTarget, e.clientX, e.clientY);
    if (cue === null) return;
    setSelected(cue);
    setSubtitleDelay(delayToPlayhead(cue));
  }

  const zoom = (factor: number) => setSpan((s) => Math.min(MAX_SPAN, Math.max(MIN_SPAN, s * factor)));
  function onWheel(e: React.WheelEvent) {
    if (e.deltaY === 0) return;
    zoom(e.deltaY > 0 ? ZOOM_STEP : 1 / ZOOM_STEP);
  }

  const close = () => usePlayerStore.getState().setSubtitleSyncOpen(false);

  // Keys of the syncer win over the player's while it is open (the player's are on document keyup).
  useEffect(() => {
    const handled = (e: KeyboardEvent) => ['KeyZ', 'KeyX', 'KeyS', 'Escape'].includes(e.code);
    const onKeyDown = (e: KeyboardEvent) => {
      if (!handled(e) || (e.target instanceof Element && e.target.closest('input, textarea'))) return;
      e.stopPropagation();
      e.preventDefault();
      if (e.code === 'KeyZ') nudgeSubtitleDelay(e.shiftKey ? -FINE_STEP : -STEP);
      if (e.code === 'KeyX') nudgeSubtitleDelay(e.shiftKey ? FINE_STEP : STEP);
      if (e.code === 'KeyS' && !e.repeat) alignToPlayhead();
    };
    const onKeyUp = (e: KeyboardEvent) => {
      if (!handled(e) || (e.target instanceof Element && e.target.closest('input, textarea'))) return;
      e.stopPropagation();
      if (e.code === 'Escape') close();
    };
    window.addEventListener('keydown', onKeyDown, true);
    window.addEventListener('keyup', onKeyUp, true);
    return () => {
      window.removeEventListener('keydown', onKeyDown, true);
      window.removeEventListener('keyup', onKeyUp, true);
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  const selectedCue = selected !== null ? cues[selected] : null;
  const trackName = currentSubtitle
    ? [currentSubtitle.languageName || currentSubtitle.language, currentSubtitle.label].filter(Boolean).join(' · ')
    : '';

  let laneMessage: string | null = null;
  if (!currentSubtitle) laneMessage = 'Choose a subtitle track to sync';
  else if (imageBased) laneMessage = 'Image based subtitles (PGS / DVD) have no text to line up: use the delay buttons';
  else if (textError) laneMessage = textError;
  else if (!subtitleText) laneMessage = 'Reading subtitles…';
  else if (!cues.length) laneMessage = 'No subtitle lines found in this track';

  return (
    <div
      className="subtitle-sync"
      role="dialog"
      aria-label="Subtitle sync"
      onPointerDown={(e) => e.stopPropagation()}
      onDoubleClick={(e) => e.stopPropagation()}
    >
      <div className="subtitle-sync__header">
        <div className="subtitle-sync__title">
          <IconWaveSine size={18} />
          <span>Subtitle sync</span>
          {trackName && <span className="subtitle-sync__track">{trackName}</span>}
        </div>

        <div className="subtitle-sync__delay">
          <button onClick={() => nudgeSubtitleDelay(-0.5)} title="0.5 s earlier">
            −0.5
          </button>
          <button onClick={() => nudgeSubtitleDelay(-STEP)} title="50 ms earlier (Z)">
            −50
          </button>
          <button onClick={() => nudgeSubtitleDelay(-FINE_STEP)} title="10 ms earlier (Shift+Z)">
            −10
          </button>
          <span
            className={`subtitle-sync__value${delay !== 0 ? ' subtitle-sync__value--changed' : ''}`}
            title="Positive = subtitles later, negative = earlier"
          >
            {formatDelay(delay)}
          </span>
          <button onClick={() => nudgeSubtitleDelay(FINE_STEP)} title="10 ms later (Shift+X)">
            +10
          </button>
          <button onClick={() => nudgeSubtitleDelay(STEP)} title="50 ms later (X)">
            +50
          </button>
          <button onClick={() => nudgeSubtitleDelay(0.5)} title="0.5 s later">
            +0.5
          </button>
        </div>

        <div className="subtitle-sync__actions">
          <button
            onClick={alignToPlayhead}
            disabled={!cues.length}
            title="Move the selected line (else the nearest one) to the playhead (S)"
          >
            <IconFocusCentered size={16} />
            <span>Align to playhead</span>
          </button>
          <button onClick={() => setSubtitleDelay(0)} disabled={delay === 0} title="Reset the delay">
            <IconArrowBackUp size={16} />
          </button>
          <span className="subtitle-sync__divider" />
          <button onClick={() => zoom(1 / ZOOM_STEP)} disabled={span <= MIN_SPAN} title="Zoom in (wheel)">
            <IconZoomIn size={16} />
          </button>
          <button onClick={() => zoom(ZOOM_STEP)} disabled={span >= MAX_SPAN} title="Zoom out (wheel)">
            <IconZoomOut size={16} />
          </button>
          <button onClick={close} title="Close (Esc)">
            <IconX size={16} />
          </button>
        </div>
      </div>

      <div className="subtitle-sync__lanes" onWheel={onWheel}>
        <div className="subtitle-sync__lane-label subtitle-sync__lane-label--audio">Audio</div>
        <canvas
          ref={waveCanvas}
          className={`subtitle-sync__wave${dragging === 'scrub' ? ' is-dragging' : ''}`}
          onPointerDown={onWavePointerDown}
          onPointerMove={onPointerMove}
          onPointerUp={onPointerUp}
          onPointerCancel={onPointerUp}
        />
        {audioTrack === null && (
          <div className="subtitle-sync__lane-message subtitle-sync__lane-message--audio">No audio track</div>
        )}
        <div className="subtitle-sync__lane-label subtitle-sync__lane-label--subs">Subtitles</div>
        <canvas
          ref={cueCanvas}
          className={`subtitle-sync__cues${dragging === 'shift' ? ' is-dragging' : ''}`}
          onPointerDown={onLanePointerDown}
          onPointerMove={onPointerMove}
          onPointerUp={onPointerUp}
          onPointerCancel={onPointerUp}
          onDoubleClick={onLaneDoubleClick}
        />
        {laneMessage && (
          <div className="subtitle-sync__lane-message subtitle-sync__lane-message--subs">{laneMessage}</div>
        )}
        <div className="subtitle-sync__playhead" />
      </div>

      <div className="subtitle-sync__footer">
        {selectedCue ? (
          <span className="subtitle-sync__selected">
            <b>{formatClock(selectedCue.start + delay, true)}</b>
            <span className="subtitle-sync__selected-text">{selectedCue.text}</span>
          </span>
        ) : (
          <span className="subtitle-sync__hint">
            Drag the waveform to seek · drag the lines to shift them · click a line, then <kbd>S</kbd> when you hear it
            start (or double-click it) · <kbd>Z</kbd>/<kbd>X</kbd> nudge
          </span>
        )}
      </div>
    </div>
  );
};
