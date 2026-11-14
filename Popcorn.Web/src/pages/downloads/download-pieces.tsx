import { Box, Group, Text } from '@mantine/core';
import { useElementSize } from '@mantine/hooks';
import React, { useEffect, useMemo, useRef, useState } from 'react';
import { DownloadFile, PieceMap } from '../../model/download.contract';
import { formatBytes } from './format';

const decode = (base64: string | null) => {
  if (!base64) return null;
  const text = atob(base64);
  const bytes = new Uint8Array(text.length);
  for (let i = 0; i < text.length; i++) bytes[i] = text.charCodeAt(i);
  return bytes;
};

/** Bit `i` of a BitTorrent bitfield (most significant bit first). */
const bit = (bits: Uint8Array | null, i: number) => !!bits && ((bits[i >> 3] >> (7 - (i & 7))) & 1) === 1;

const GAP = 1;
const COLORS = {
  have: '#2f9e44',
  active: '#f59f00',
  missing: '#373a40',
  /** Missing piece no connected peer has. */
  unavailable: '#5c2b2b',
  /** Missing piece of a skipped file. */
  skipped: '#25262b',
};

/** Missing pieces get lighter the more peers have them (up to this many). */
const AVAILABILITY_SCALE = 10;

function missingColor(peers: number) {
  const t = Math.min(peers, AVAILABILITY_SCALE) / AVAILABILITY_SCALE;
  // #373a40 → #5c7cfa-ish blue as more peers have it.
  const mix = (a: number, b: number) => Math.round(a + (b - a) * t * 0.6);
  return `rgb(${mix(0x37, 0x5c)}, ${mix(0x3a, 0x7c)}, ${mix(0x40, 0xfa)})`;
}

function Legend({ color, label }: { color: string; label: string }) {
  return (
    <Group gap={4} wrap="nowrap">
      <div style={{ width: 10, height: 10, background: color, borderRadius: 2 }} />
      <Text size="xs" c="dimmed">
        {label}
      </Text>
    </Group>
  );
}

/** Pieces tab: one cell per piece (chunk of a direct download) — downloaded, in progress, or missing by availability. */
export function DownloadPieces({ pieces, files }: { pieces: PieceMap; files: DownloadFile[] }) {
  const { ref, width } = useElementSize();
  const canvas = useRef<HTMLCanvasElement>(null);
  const [hover, setHover] = useState<number | null>(null);
  const have = useMemo(() => decode(pieces.have), [pieces.have]);
  const active = useMemo(() => decode(pieces.active), [pieces.active]);
  const availability = useMemo(() => decode(pieces.availability), [pieces.availability]);

  // Pieces only of skipped files (a piece shared with a wanted file is downloaded anyway).
  const skipped = useMemo(() => {
    const result = new Uint8Array(pieces.count);
    if (!files.length || files.every((f) => f.wanted)) return result;
    let offset = 0;
    const wanted = new Uint8Array(pieces.count);
    for (const f of files) {
      if (f.length > 0) {
        const first = Math.floor(offset / pieces.pieceLength);
        const last = Math.min(pieces.count - 1, Math.floor((offset + f.length - 1) / pieces.pieceLength));
        for (let i = first; i <= last; i++) {
          if (f.wanted) wanted[i] = 1;
          else result[i] = 1;
        }
      }
      offset += f.length;
    }
    for (let i = 0; i < pieces.count; i++) if (wanted[i]) result[i] = 0;
    return result;
  }, [files, pieces.count, pieces.pieceLength]);

  // Cells as big as fit, from 14 px down to 3 px for torrents with tens of thousands of pieces.
  const cell =
    pieces.count > 20000 ? 3 : pieces.count > 5000 ? 5 : pieces.count > 1000 ? 8 : pieces.count > 200 ? 11 : 14;
  const columns = Math.max(1, Math.floor((width + GAP) / (cell + GAP)));
  const rows = Math.ceil(pieces.count / columns);
  const height = rows * (cell + GAP);

  useEffect(() => {
    const c = canvas.current;
    if (!c || !width) return;
    const dpr = window.devicePixelRatio || 1;
    c.width = Math.round(width * dpr);
    c.height = Math.round(height * dpr);
    const ctx = c.getContext('2d')!;
    ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
    ctx.clearRect(0, 0, width, height);
    for (let i = 0; i < pieces.count; i++) {
      ctx.fillStyle = bit(have, i)
        ? COLORS.have
        : bit(active, i)
          ? COLORS.active
          : skipped[i]
            ? COLORS.skipped
            : availability
              ? availability[i]
                ? missingColor(availability[i])
                : COLORS.unavailable
              : COLORS.missing;
      ctx.fillRect((i % columns) * (cell + GAP), Math.floor(i / columns) * (cell + GAP), cell, cell);
    }
  }, [have, active, availability, skipped, pieces.count, width, height, columns, cell]);

  const haveCount = useMemo(() => {
    let n = 0;
    for (let i = 0; i < pieces.count; i++) if (bit(have, i)) n++;
    return n;
  }, [have, pieces.count]);
  const activeCount = useMemo(() => {
    let n = 0;
    for (let i = 0; i < pieces.count; i++) if (bit(active, i) && !bit(have, i)) n++;
    return n;
  }, [active, have, pieces.count]);

  const onMove = (e: React.MouseEvent<HTMLCanvasElement>) => {
    const rect = e.currentTarget.getBoundingClientRect();
    const col = Math.floor((e.clientX - rect.left) / (cell + GAP));
    const row = Math.floor((e.clientY - rect.top) / (cell + GAP));
    const i = row * columns + col;
    setHover(col < columns && i >= 0 && i < pieces.count ? i : null);
  };

  const hoverFile = useMemo(() => {
    if (hover == null) return null;
    const start = hover * pieces.pieceLength;
    let offset = 0;
    for (const f of files) {
      if (start < offset + f.length) return f;
      offset += f.length;
    }
    return null;
  }, [hover, files, pieces.pieceLength]);

  return (
    <Box p="xs">
      <Group gap="lg" mb="xs" wrap="wrap">
        <Text size="xs">
          {pieces.count} pieces × {formatBytes(pieces.pieceLength)} · {haveCount} downloaded (
          {((haveCount / Math.max(1, pieces.count)) * 100).toFixed(1)}%)
          {activeCount ? ` · ${activeCount} in progress` : ''}
        </Text>
        <Legend color={COLORS.have} label="Downloaded" />
        <Legend color={COLORS.active} label="In progress" />
        {availability ? (
          <>
            <Legend color={missingColor(1)} label="Missing (lighter = more peers have it)" />
            <Legend color={COLORS.unavailable} label="No peer has it" />
          </>
        ) : (
          <Legend color={COLORS.missing} label="Missing" />
        )}
        {skipped.some((s) => s) && <Legend color={COLORS.skipped} label="Skipped file" />}
      </Group>
      <Text
        size="xs"
        c="dimmed"
        mb={4}
        h={18}
        style={{ overflow: 'hidden', whiteSpace: 'nowrap', textOverflow: 'ellipsis' }}
      >
        {hover != null &&
          `Piece ${hover}: ${bit(have, hover) ? 'downloaded' : bit(active, hover) ? 'in progress' : 'missing'}` +
            (availability ? ` · ${availability[hover]}${availability[hover] === 255 ? '+' : ''} peers have it` : '') +
            (hoverFile ? ` · ${hoverFile.path}` : '')}
      </Text>
      <div ref={ref}>
        <canvas
          ref={canvas}
          style={{ width: '100%', height, display: 'block' }}
          onMouseMove={onMove}
          onMouseLeave={() => setHover(null)}
        />
      </div>
    </Box>
  );
}
