/**
 * VobSub (DVD bitmap subtitles) decoder. TypeScript port of the `vobsub` crate of
 * https://github.com/emk/subtitles-rs (Eric Kidd, Apache-2.0), with these changes:
 * - every packet of a pack is read (ffmpeg's muxer puts a system header between the first pack header and its PES);
 * - control sequence dates are 1024/90000 s (as ffmpeg and the DVD spec), not 1/100 s;
 * - a broken subtitle is skipped instead of ending the stream, over-long RLE runs are clipped;
 * - only the first substream (language) of a multi-language .sub is read;
 * - images are decoded when shown, not up front.
 *
 * Input: the `.idx` text followed by the `.sub` MPEG-2 program stream, as the server sends a `vobsub` track.
 * Format references: http://sam.zoy.org/writings/dvd/subtitles/, https://en.wikipedia.org/wiki/MPEG_program_stream
 */

export interface VobSubFrame {
  width: number;
  height: number;
}

export interface VobSubCue {
  /** Seconds. */
  start: number;
  end: number;
  /** Shown even with subtitles off. */
  force: boolean;
  x: number;
  y: number;
  width: number;
  height: number;
  /** Indexes into the .idx palette for the 4 pixel values. */
  palette: [number, number, number, number];
  /** 4-bit alpha for the 4 pixel values. */
  alpha: [number, number, number, number];
  /** Subtitle packet and its two RLE scan line field offsets, decoded by {@link decodeVobSubImage}. */
  data: Uint8Array;
  rleOffsets: [number, number];
  rleEnd: number;
}

export interface VobSub {
  frame: VobSubFrame | null;
  /** 16 RGB colours. */
  palette: [number, number, number][];
  cues: VobSubCue[];
}

/** No stop date: end before the next subtitle, at most this long. */
const DEFAULT_SUBTITLE_LENGTH = 5;
const DEFAULT_SUBTITLE_SPACING = 0.001;
/** Unit of a control sequence date. */
const DATE_UNIT = 1024 / 90000;

const PACK_START = 0xba;
const PROGRAM_END = 0xb9;
const PRIVATE_STREAM_1 = 0xbd;

export function parseVobSub(buffer: ArrayBuffer): VobSub {
  const bytes = new Uint8Array(buffer);
  const psStart = findPackStart(bytes, 0);
  const idx = new TextDecoder().decode(bytes.subarray(0, psStart < 0 ? bytes.length : psStart));
  const { frame, palette } = parseIdx(idx);
  const cues = psStart < 0 ? [] : parseSubtitles(bytes, psStart);
  return { frame, palette, cues };
}

function parseIdx(text: string) {
  let frame: VobSubFrame | null = null;
  let palette: [number, number, number][] | null = null;
  for (const line of text.split(/\r?\n/)) {
    const match = /^([A-Za-z/ ]+):\s*(.*)$/.exec(line);
    if (!match) continue;
    const [, key, value] = match;
    if (key === 'size') {
      const size = /^(\d+)x(\d+)/.exec(value);
      if (size) frame = { width: Number(size[1]), height: Number(size[2]) };
    } else if (key === 'palette') {
      const colours = value.split(',').map((c) => c.trim());
      if (colours.length !== 16 || colours.some((c) => !/^[0-9a-fA-F]{6}$/.test(c)))
        throw new Error('VobSub palette must have 16 RGB entries');
      palette = colours.map((c) => {
        const v = parseInt(c, 16);
        return [(v >> 16) & 0xff, (v >> 8) & 0xff, v & 0xff];
      });
    }
  }
  if (!palette) throw new Error('VobSub index has no palette');
  return { frame, palette };
}

const be16 = (b: Uint8Array, i: number) => (b[i] << 8) | b[i + 1];

function findPackStart(b: Uint8Array, from: number) {
  for (let i = from; i + 3 < b.length; i++) {
    if (b[i] === 0 && b[i + 1] === 0 && b[i + 2] === 1 && b[i + 3] === PACK_START) return i;
  }
  return -1;
}

interface PesPacket {
  /** Seconds, if the packet has a PTS. */
  pts: number | null;
  substreamId: number;
  data: Uint8Array;
}

/** The private stream 1 PES packets of an MPEG-2 program stream. */
function* pesPackets(b: Uint8Array, start: number): Generator<PesPacket> {
  let pos = findPackStart(b, start);
  while (pos >= 0) {
    // MPEG-2 pack header: 14 bytes ('01' marker) + stuffing.
    if (pos + 14 > b.length || b[pos + 4] >> 6 !== 0b01) {
      pos = findPackStart(b, pos + 4);
      continue;
    }
    pos += 14 + (b[pos + 13] & 0x07);
    // Packets of this pack (system header, PES, padding) until the next pack.
    while (pos + 6 <= b.length && b[pos] === 0 && b[pos + 1] === 0 && b[pos + 2] === 1) {
      const id = b[pos + 3];
      if (id === PACK_START || id === PROGRAM_END) break;
      const end = pos + 6 + be16(b, pos + 4);
      if (end > b.length) return;
      if (id === PRIVATE_STREAM_1) {
        const packet = pesPacket(b.subarray(pos + 6, end));
        if (packet) yield packet;
      }
      pos = end;
    }
    pos = findPackStart(b, pos);
  }
}

function pesPacket(p: Uint8Array): PesPacket | null {
  // '10' marker, flags, header data length, header data, substream id, payload.
  if (p.length < 3 || p[0] >> 6 !== 0b10) return null;
  const ptsDtsFlags = p[1] >> 6;
  const headerLength = p[2];
  const dataStart = 3 + headerLength;
  if (dataStart >= p.length) return null;
  let pts: number | null = null;
  if (ptsDtsFlags & 0b10) {
    if (headerLength < 5) return null;
    pts = readClock(p, 3) / 90000;
  }
  return { pts, substreamId: p[dataStart], data: p.subarray(dataStart + 1) };
}

/** 33-bit PTS in 5 bytes with marker bits. */
function readClock(b: Uint8Array, i: number) {
  const hi = (b[i] >> 1) & 0x07;
  const mid = be16(b, i + 1) >> 1;
  const lo = be16(b, i + 3) >> 1;
  return hi * 2 ** 30 + mid * 2 ** 15 + lo;
}

function parseSubtitles(b: Uint8Array, start: number): VobSubCue[] {
  const cues: VobSubCue[] = [];
  const packets = pesPackets(b, start);
  // A .sub can hold several languages (substreams 0x20...): the first one's is read.
  let stream: number | null = null;
  let next = packets.next();
  while (!next.done) {
    const first = next.value;
    next = packets.next();
    // A subtitle starts with a timed packet holding its total size; the rest follows in packets of the same stream.
    if (first.pts === null || first.data.length < 2) continue;
    stream ??= first.substreamId;
    if (first.substreamId !== stream) continue;
    const wanted = be16(first.data, 0);
    const chunks = [first.data];
    let length = first.data.length;
    while (length < wanted && !next.done) {
      if (next.value.substreamId === first.substreamId) {
        chunks.push(next.value.data);
        length += next.value.data.length;
      }
      next = packets.next();
    }
    if (length < wanted) break;
    const data = new Uint8Array(wanted);
    let offset = 0;
    for (const chunk of chunks) {
      const part = chunk.subarray(0, wanted - offset);
      data.set(part, offset);
      offset += part.length;
    }
    try {
      cues.push(parseSubtitle(data, first.pts));
    } catch (error) {
      console.warn('Skipping VobSub subtitle at', first.pts, error);
    }
  }

  // Subtitles without a stop date end just before the next one.
  cues.forEach((cue, i) => {
    if (!Number.isNaN(cue.end)) return;
    const following = cues[i + 1];
    cue.end = Math.min(
      cue.start + DEFAULT_SUBTITLE_LENGTH,
      following ? following.start - DEFAULT_SUBTITLE_SPACING : Infinity
    );
  });
  return cues;
}

const nibbles = (b0: number, b1: number): [number, number, number, number] => [b0 >> 4, b0 & 0xf, b1 >> 4, b1 & 0xf];

/** A subtitle packet: size, control offset, RLE data, control sequences. */
function parseSubtitle(data: Uint8Array, baseTime: number): VobSubCue {
  if (data.length < 4) throw new Error('subtitle packet too short');
  const initialControlOffset = be16(data, 2);

  let start: number | null = null;
  let end: number | null = null;
  let force = false;
  let coordinates: { x1: number; x2: number; y1: number; y2: number } | null = null;
  let palette: [number, number, number, number] | null = null;
  let alpha: [number, number, number, number] | null = null;
  let rleOffsets: [number, number] | null = null;

  let controlOffset = initialControlOffset;
  for (;;) {
    if (controlOffset + 4 > data.length) throw new Error('control offset past the end of the packet');
    const time = baseTime + be16(data, controlOffset) * DATE_UNIT;
    const nextOffset = be16(data, controlOffset + 2);
    let i = controlOffset + 4;
    commands: for (;;) {
      if (i >= data.length) throw new Error('control sequence without end');
      const command = data[i++];
      const need = (n: number) => {
        if (i + n > data.length) throw new Error('control command past the end of the packet');
      };
      switch (command) {
        case 0x00:
          force = true;
          break;
        case 0x01:
          start ??= time;
          break;
        case 0x02:
          end ??= time;
          break;
        case 0x03:
          need(2);
          palette ??= nibbles(data[i], data[i + 1]);
          i += 2;
          break;
        case 0x04:
          need(2);
          alpha ??= nibbles(data[i], data[i + 1]);
          i += 2;
          break;
        case 0x05: {
          need(6);
          const c = {
            x1: (data[i] << 4) | (data[i + 1] >> 4),
            x2: ((data[i + 1] & 0xf) << 8) | data[i + 2],
            y1: (data[i + 3] << 4) | (data[i + 4] >> 4),
            y2: ((data[i + 4] & 0xf) << 8) | data[i + 5],
          };
          if (c.x2 <= c.x1 || c.y2 <= c.y1) throw new Error('invalid bounding box');
          coordinates ??= c;
          i += 6;
          break;
        }
        case 0x06:
          need(4);
          rleOffsets = [be16(data, i), be16(data, i + 2)];
          i += 4;
          break;
        default:
          // 0xff ends the sequence; anything else is unsupported, and the next sequence is found by its offset.
          break commands;
      }
    }
    if (nextOffset === controlOffset) break;
    if (nextOffset < controlOffset) throw new Error('control offset went backwards');
    controlOffset = nextOffset;
  }

  if (start === null) throw new Error('no start time');
  if (!coordinates) throw new Error('no coordinates');
  if (!palette) throw new Error('no palette');
  if (!alpha) throw new Error('no alpha');
  if (!rleOffsets) throw new Error('no RLE offsets');
  // Scan lines end where the control data starts (encoders reuse bytes, the real end is unknown).
  const rleEnd = Math.min(initialControlOffset + 2, data.length);
  if (rleOffsets[0] > rleOffsets[1] || rleOffsets[1] > rleEnd) throw new Error('invalid scan line offsets');

  return {
    start,
    end: end ?? NaN,
    force,
    x: coordinates.x1,
    y: coordinates.y1,
    width: coordinates.x2 + 1 - coordinates.x1,
    height: coordinates.y2 + 1 - coordinates.y1,
    palette,
    alpha,
    data,
    rleOffsets,
    rleEnd,
  };
}

/** Reads 4-bit units of one RLE field (even or odd lines). */
class NibbleReader {
  private readonly data: Uint8Array;
  private readonly end: number;
  private pos: number;
  private half = false;

  constructor(data: Uint8Array, start: number, end: number) {
    this.data = data;
    this.pos = start;
    this.end = end;
  }

  nibble() {
    if (this.pos >= this.end) throw new Error('not enough RLE data');
    const byte = this.data[this.pos];
    if (this.half) {
      this.half = false;
      this.pos++;
      return byte & 0xf;
    }
    this.half = true;
    return byte >> 4;
  }

  /** Lines start on a byte boundary. */
  align() {
    if (this.half) {
      this.half = false;
      this.pos++;
    }
  }
}

/** Decodes the cue's bitmap with the .idx palette. */
export function decodeVobSubImage(cue: VobSubCue, palette: [number, number, number][]): ImageData {
  const { width, height } = cue;
  const image = new ImageData(width, height);
  const pixels = new Uint32Array(image.data.buffer);
  // RGBA (little endian) of the 4 pixel values; the control commands list them from value 3 down to 0.
  const colours = [0, 1, 2, 3].map((value) => {
    const entry = 3 - value;
    const [r, g, b] = palette[cue.palette[entry]] ?? [0, 0, 0];
    const a = cue.alpha[entry] * 17;
    return ((a << 24) | (b << 16) | (g << 8) | r) >>> 0;
  });
  const fields = [
    new NibbleReader(cue.data, cue.rleOffsets[0], cue.rleEnd),
    new NibbleReader(cue.data, cue.rleOffsets[1], cue.rleEnd),
  ];

  for (let y = 0; y < height; y++) {
    const reader = fields[y % 2];
    const row = y * width;
    let x = 0;
    while (x < width) {
      // Variable length code: 4, 8, 12 or 16 bits, count = all but the low 2 bits (0 = rest of the line).
      let v = reader.nibble();
      if (v < 0x4) {
        v = (v << 4) | reader.nibble();
        if (v < 0x10) {
          v = (v << 4) | reader.nibble();
          if (v < 0x40) v = (v << 4) | reader.nibble();
        }
      }
      const count = v >> 2 || width - x;
      pixels.fill(colours[v & 0x3], row + x, row + Math.min(width, x + count));
      x += count;
    }
    reader.align();
  }
  return image;
}

/** The cue shown at `time` (cues are in start order; a later one replaces an earlier one). */
export function vobSubCueAt(cues: VobSubCue[], time: number): VobSubCue | null {
  let lo = 0;
  let hi = cues.length - 1;
  let found = -1;
  while (lo <= hi) {
    const mid = (lo + hi) >> 1;
    if (cues[mid].start <= time) {
      found = mid;
      lo = mid + 1;
    } else hi = mid - 1;
  }
  const cue = found >= 0 ? cues[found] : null;
  return cue && time < cue.end ? cue : null;
}
