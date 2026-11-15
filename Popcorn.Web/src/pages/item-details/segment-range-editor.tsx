import React, { useEffect, useMemo, useRef, useState } from 'react';
import { Alert, Button, Group, Modal, NumberInput, Stack, Text, Tooltip } from '@mantine/core';
import { modals } from '@mantine/modals';
import { IconAlertCircle, IconPlayerPause, IconPlayerPlay } from '@tabler/icons-react';
import { useWebSocket } from '../../state/websocket-context';
import { useSocketEvents } from '../../socket/useSocketEvents.hook';
import { SocketRequests } from '../../socket/socket-requests';
import { resolveServerHost } from '../../utils/url-resolver';
import { prepareCanvas, tickSteps, waveBarHeight, waveColumns } from '../../utils/waveform';
import { SegmentEditInfo, SegmentReference, SegmentType } from '../../model/segments.contract';

interface SegmentRangeEditorProps {
  itemId: string;
  /** The saved segment to edit; null = closed. */
  reference: SegmentReference | null;
  /** "Intro 1", as in the saved segments list. */
  name: string;
  onClose: () => void;
}

/** Shortest segment the server keeps (`ChapterRules.MinSegmentSeconds`). */
const MIN_LENGTH = 5;
/** A press within this many CSS pixels of a range edge drags that edge. */
const EDGE_PX = 8;
/** Pointer movement (px) that turns a press into a drag. */
const DRAG_THRESHOLD = 4;
/** Seconds heard around an edge with "Hear start" / "Hear end". */
const EDGE_BEFORE = 3;
const EDGE_AFTER = 3;

const COLORS = {
  wave: 'rgba(125, 200, 255, 0.9)',
  waveOutside: 'rgba(125, 200, 255, 0.3)',
  missing: 'rgba(255, 255, 255, 0.12)',
  selection: { [SegmentType.Intro]: 'rgba(32, 201, 151, 0.16)', [SegmentType.Outro]: 'rgba(204, 93, 232, 0.16)' },
  edge: { [SegmentType.Intro]: '#20c997', [SegmentType.Outro]: '#cc5de8' },
  original: 'rgba(255, 255, 255, 0.45)',
  playhead: '#ffbe46',
  tick: 'rgba(255, 255, 255, 0.22)',
  tickText: 'rgba(255, 255, 255, 0.5)',
};

const formatClock = (seconds: number, decimals = 0) => {
  const total = Math.max(0, seconds);
  const h = Math.floor(total / 3600);
  const m = Math.floor((total % 3600) / 60);
  const s = total % 60;
  const sec =
    decimals > 0 ? s.toFixed(decimals).padStart(3 + decimals, '0') : Math.floor(s).toString().padStart(2, '0');
  return `${h > 0 ? `${h}:${m.toString().padStart(2, '0')}` : m}:${sec}`;
};

const round = (t: number) => Math.round(t * 100) / 100;

type Drag = { kind: 'start' | 'end' | 'new' | 'seek'; x: number; anchor: number; moved: boolean };

/**
 * Range editor of a saved segment: the waveform of the audio it was cut from (the source file with a margin around
 * the segment, or only its own clip for older segments), the range as a band with draggable edges, and playback to
 * check the edges by ear. Saving re-cuts the segment on the server, which drops it from the files it was applied to.
 */
const SegmentRangeEditor: React.FC<SegmentRangeEditorProps> = ({ itemId, reference, name, onClose }) => {
  const { client } = useWebSocket();
  const segmentId = reference?.id ?? null;
  const [info, setInfo] = useState<SegmentEditInfo | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [peaks, setPeaks] = useState<Uint8Array | null>(null);
  const [start, setStart] = useState(0);
  const [end, setEnd] = useState(0);
  const [playing, setPlaying] = useState(false);
  const [saving, setSaving] = useState(false);

  const canvas = useRef<HTMLCanvasElement>(null);
  const audio = useRef<HTMLAudioElement>(null);
  /** Audio time (seconds in the window) where playback pauses on its own. */
  const stopAt = useRef<number | null>(null);
  const drag = useRef<Drag | null>(null);
  const live = useRef({ info, peaks, start, end });
  live.current = { info, peaks, start, end };

  // Edit info (answer to SegmentsEditInfo), then the waveform of its window.
  const events = useMemo(
    () => ({
      [SocketRequests.SegmentsEditInfo]: (data: SegmentEditInfo | { segmentId: string; error: string }) => {
        if (data.segmentId !== segmentId) return;
        if ('error' in data) {
          setError(data.error);
          return;
        }
        setInfo(data);
        setStart(round(data.start));
        setEnd(round(data.start + data.length));
      },
    }),
    [segmentId]
  );
  useSocketEvents(events);

  useEffect(() => {
    setInfo(null);
    setError(null);
    setPeaks(null);
    setPlaying(false);
    setSaving(false);
    if (segmentId) client.sendEvent(SocketRequests.SegmentsEditInfo, { itemId, segmentId });
  }, [segmentId]);

  const query = segmentId ? `itemId=${itemId}&v=${reference?.audioVersion ?? 0}` : '';
  const base = segmentId ? `http://${resolveServerHost()}/api/segments/${segmentId}/edit` : '';
  useEffect(() => {
    if (!info) return;
    const abort = new AbortController();
    fetch(`${base}/waveform?${query}`, { signal: abort.signal })
      .then((r) => (r.ok ? r.arrayBuffer() : Promise.reject(new Error(`Waveform: ${r.status}`))))
      .then((b) => setPeaks(new Uint8Array(b)))
      .catch((e) => !abort.signal.aborted && setError(String(e.message ?? e)));
    return () => abort.abort();
  }, [info, base, query]);

  // ---- drawing (every frame: the playhead moves) ----
  useEffect(() => {
    if (!info) return;
    let frame = 0;
    const draw = () => {
      frame = requestAnimationFrame(draw);
      const el = canvas.current;
      if (!el) return;
      const { info: i, peaks: p, start: s, end: e } = live.current;
      if (!i) return;
      const player = audio.current;
      if (player && stopAt.current !== null && player.currentTime >= stopAt.current) {
        player.pause();
        stopAt.current = null;
      }

      const c = prepareCanvas(el);
      const tickHeight = 16;
      const waveHeight = c.height - tickHeight;
      const mid = waveHeight / 2;
      const pxPerSec = c.width / i.windowLength;
      const x = (t: number) => (t - i.windowStart) * pxPerSec;

      // Range band behind the waveform.
      c.ctx.fillStyle = COLORS.selection[i.type];
      c.ctx.fillRect(x(s), 0, x(e) - x(s), waveHeight);

      const columns = Math.ceil(c.width);
      const offset = Math.round(i.windowStart * 100);
      if (p) {
        const { heights, known, loudest } = waveColumns(columns, i.windowStart, pxPerSec, (n) => p[n - offset]);
        const left = x(s);
        const right = x(e);
        for (let col = 0; col < columns; col++) {
          if (!known[col]) {
            if (col % 4 === 0) {
              c.ctx.fillStyle = COLORS.missing;
              c.ctx.fillRect(col, mid - 0.5, 2, 1);
            }
            continue;
          }
          const h = waveBarHeight(heights[col], loudest, mid - 3);
          c.ctx.fillStyle = col >= left && col <= right ? COLORS.wave : COLORS.waveOutside;
          c.ctx.fillRect(col, mid - h, 1, h * 2);
        }
      }

      // Where the segment is now (dashed), the range edges, the playhead.
      c.ctx.strokeStyle = COLORS.original;
      c.ctx.setLineDash([4, 4]);
      for (const t of [i.start, i.start + i.length]) {
        c.ctx.beginPath();
        c.ctx.moveTo(Math.round(x(t)) + 0.5, 0);
        c.ctx.lineTo(Math.round(x(t)) + 0.5, waveHeight);
        c.ctx.stroke();
      }
      c.ctx.setLineDash([]);
      c.ctx.fillStyle = COLORS.edge[i.type];
      for (const t of [s, e]) {
        c.ctx.fillRect(Math.round(x(t)) - 1, 0, 2, waveHeight);
        c.ctx.fillRect(Math.round(x(t)) - 4, 0, 8, 6);
      }
      if (player) {
        c.ctx.fillStyle = COLORS.playhead;
        c.ctx.fillRect(Math.round(x(i.windowStart + player.currentTime)), 0, 1, waveHeight);
      }

      // Time ticks (time in the source file).
      const [minor, major] = tickSteps(i.windowLength);
      c.ctx.font = '10px Rubik, sans-serif';
      c.ctx.textAlign = 'center';
      c.ctx.textBaseline = 'bottom';
      const t1 = i.windowStart + i.windowLength;
      for (let t = Math.ceil(i.windowStart / minor) * minor; t <= t1; t += minor) {
        const isMajor = Math.abs(t / major - Math.round(t / major)) < 1e-6;
        c.ctx.fillStyle = COLORS.tick;
        c.ctx.fillRect(Math.round(x(t)), waveHeight, 1, isMajor ? 6 : 3);
        if (isMajor) {
          c.ctx.fillStyle = COLORS.tickText;
          c.ctx.fillText(formatClock(t), x(t), c.height);
        }
      }
    };
    draw();
    return () => cancelAnimationFrame(frame);
  }, [info]);

  if (!reference) return null;

  const windowEnd = info ? info.windowStart + info.windowLength : 0;
  const clamp = (t: number) => (info ? Math.min(windowEnd, Math.max(info.windowStart, t)) : t);
  const length = end - start;
  const valid = info != null && length >= MIN_LENGTH;
  const changed =
    info != null && (Math.abs(start - info.start) >= 0.01 || Math.abs(end - (info.start + info.length)) >= 0.01);

  const timeAt = (clientX: number) => {
    const el = canvas.current!;
    const rect = el.getBoundingClientRect();
    return clamp(info!.windowStart + ((clientX - rect.left) / rect.width) * info!.windowLength);
  };
  const xOf = (t: number) => {
    const el = canvas.current!;
    return ((t - info!.windowStart) / info!.windowLength) * el.getBoundingClientRect().width;
  };

  const onPointerDown = (e: React.PointerEvent<HTMLCanvasElement>) => {
    if (!info) return;
    e.currentTarget.setPointerCapture(e.pointerId);
    const x = e.clientX - e.currentTarget.getBoundingClientRect().left;
    const toStart = Math.abs(x - xOf(start));
    const toEnd = Math.abs(x - xOf(end));
    const kind = Math.min(toStart, toEnd) <= EDGE_PX ? (toStart <= toEnd ? 'start' : 'end') : 'seek';
    drag.current = { kind, x: e.clientX, anchor: timeAt(e.clientX), moved: false };
  };
  const onPointerMove = (e: React.PointerEvent<HTMLCanvasElement>) => {
    const d = drag.current;
    if (!d || !info) return;
    if (!d.moved && Math.abs(e.clientX - d.x) < DRAG_THRESHOLD) return;
    d.moved = true;
    const t = round(timeAt(e.clientX));
    // Dragging from inside the waveform draws a new range.
    if (d.kind === 'seek') d.kind = 'new';
    if (d.kind === 'start') setStart(Math.min(t, end));
    else if (d.kind === 'end') setEnd(Math.max(t, start));
    else {
      setStart(round(Math.min(d.anchor, t)));
      setEnd(round(Math.max(d.anchor, t)));
    }
  };
  const onPointerUp = (e: React.PointerEvent<HTMLCanvasElement>) => {
    const d = drag.current;
    drag.current = null;
    if (!d || !info || d.moved) return;
    // A click moves the playhead.
    if (audio.current) audio.current.currentTime = timeAt(e.clientX) - info.windowStart;
  };
  const cursor = (e: React.PointerEvent<HTMLCanvasElement>) => {
    if (!info || drag.current) return;
    const x = e.clientX - e.currentTarget.getBoundingClientRect().left;
    const near = Math.min(Math.abs(x - xOf(start)), Math.abs(x - xOf(end))) <= EDGE_PX;
    e.currentTarget.style.cursor = near ? 'ew-resize' : 'crosshair';
  };

  /** Plays from `from` (source seconds), pausing at `to` when given. */
  const play = (from: number | null, to: number | null) => {
    const player = audio.current;
    if (!player || !info) return;
    if (from !== null) player.currentTime = Math.max(0, from - info.windowStart);
    stopAt.current = to === null ? null : to - info.windowStart;
    void player.play();
  };
  const togglePlay = () => {
    const player = audio.current;
    if (!player) return;
    if (player.paused) play(null, null);
    else player.pause();
  };

  const save = () => {
    if (!info || !segmentId) return;
    const run = () => {
      setSaving(true);
      audio.current?.pause();
      client.sendEvent(SocketRequests.SegmentsSetRange, { itemId, segmentId, start, length });
      onClose();
    };
    if (reference.appliedTo.length === 0) {
      run();
      return;
    }
    modals.openConfirmModal({
      title: `Change the range of ${name}?`,
      zIndex: 1000,
      children: (
        <Stack gap="xs">
          <Text size="sm">
            {name} is applied to {reference.appliedTo.join(', ')}. Its old range does not fit those files any more: it
            is removed from all of them.
          </Text>
          <Text size="sm">Scan the files again afterwards (Apply segments → Scan) to find the new range in them.</Text>
        </Stack>
      ),
      labels: { confirm: 'Change and remove from files', cancel: 'Cancel' },
      confirmProps: { color: 'red' },
      onConfirm: run,
    });
  };

  return (
    <Modal opened onClose={onClose} title={`Edit ${name}`} size="xl" zIndex={300}>
      <Stack gap="sm">
        {error && (
          <Alert color="red" icon={<IconAlertCircle size={16} />}>
            {error}
          </Alert>
        )}
        {!info && !error && <Text c="dimmed">Loading…</Text>}
        {info && (
          <>
            <Text size="sm" c="dimmed">
              {info.sourceLabel ? (
                <>
                  Cut from <b>{info.sourceLabel}</b>, shown with {Math.round(info.start - info.windowStart)} s before
                  and {Math.round(windowEnd - info.start - info.length)} s after.{' '}
                </>
              ) : (
                <>The file it was cut from is not known: it can only be shortened. </>
              )}
              Drag the edges, or drag across the waveform for a new range; click to move the playhead.
            </Text>
            <canvas
              ref={canvas}
              style={{ width: '100%', height: 160, display: 'block', borderRadius: 4, background: 'rgba(0,0,0,0.25)' }}
              onPointerDown={onPointerDown}
              onPointerMove={(e) => {
                cursor(e);
                onPointerMove(e);
              }}
              onPointerUp={onPointerUp}
              onPointerCancel={() => (drag.current = null)}
            />
            <audio
              ref={audio}
              preload="auto"
              src={`${base}/audio?${query}`}
              onPlay={() => setPlaying(true)}
              onPause={() => setPlaying(false)}
            />
            <Group gap="xs">
              <Button
                size="xs"
                variant="default"
                leftSection={playing ? <IconPlayerPause size={14} /> : <IconPlayerPlay size={14} />}
                onClick={togglePlay}
              >
                {playing ? 'Pause' : 'Play'}
              </Button>
              <Button size="xs" variant="default" onClick={() => play(start, end)}>
                Play range
              </Button>
              <Tooltip label={`${EDGE_BEFORE} s before to ${EDGE_AFTER} s after the start`}>
                <Button size="xs" variant="default" onClick={() => play(start - EDGE_BEFORE, start + EDGE_AFTER)}>
                  Hear start
                </Button>
              </Tooltip>
              <Tooltip label={`${EDGE_BEFORE} s before to ${EDGE_AFTER} s after the end`}>
                <Button size="xs" variant="default" onClick={() => play(end - EDGE_BEFORE, end + EDGE_AFTER)}>
                  Hear end
                </Button>
              </Tooltip>
            </Group>
            <Group gap="md" align="flex-end">
              <NumberInput
                label="Start (s)"
                description={formatClock(start, 2)}
                w={140}
                size="xs"
                step={0.1}
                decimalScale={2}
                min={info.windowStart}
                max={end}
                value={start}
                onChange={(v) => typeof v === 'number' && setStart(round(Math.min(clamp(v), end)))}
              />
              <NumberInput
                label="End (s)"
                description={formatClock(end, 2)}
                w={140}
                size="xs"
                step={0.1}
                decimalScale={2}
                min={start}
                max={windowEnd}
                value={end}
                onChange={(v) => typeof v === 'number' && setEnd(round(Math.max(clamp(v), start)))}
              />
              <Stack gap={0}>
                <Text size="xs" c="dimmed">
                  Length
                </Text>
                <Text size="sm" c={valid ? undefined : 'red'}>
                  {formatClock(length, 1)} (was {formatClock(info.length, 1)})
                </Text>
              </Stack>
              <Button
                size="xs"
                variant="subtle"
                disabled={!changed}
                onClick={() => {
                  setStart(round(info.start));
                  setEnd(round(info.start + info.length));
                }}
              >
                Reset
              </Button>
            </Group>
            {!valid && (
              <Text size="xs" c="red">
                A segment has to be at least {MIN_LENGTH} s long.
              </Text>
            )}
          </>
        )}
        <Group justify="flex-end">
          <Button variant="default" onClick={onClose}>
            Cancel
          </Button>
          <Button disabled={!valid || !changed || saving} onClick={save}>
            Save range
          </Button>
        </Group>
      </Stack>
    </Modal>
  );
};

export default SegmentRangeEditor;
