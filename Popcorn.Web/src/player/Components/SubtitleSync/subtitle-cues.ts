/** One subtitle line for the syncer, times in seconds of the file (without any delay). */
export interface SubtitleCue {
  start: number;
  end: number;
  /** Plain text, styling tags removed, lines joined with " / ". */
  text: string;
}

/** Bitmap subtitle formats (browser track formats, mpv codec names): there is no text to show. */
const IMAGE_FORMATS = new Set([
  'sup',
  'hdmv_pgs_subtitle',
  'pgssub',
  'dvd_subtitle',
  'dvdsub',
  'dvb_subtitle',
  'dvbsub',
  'xsub',
  'dvb_teletext',
]);

export const isImageSubtitle = (format: string | null | undefined) =>
  !!format && IMAGE_FORMATS.has(format.toLowerCase());

/** `00:01:02.345`, `01:02,345`, `0:01:02.34` (ASS centiseconds) → seconds. */
function parseTimestamp(value: string): number | null {
  const match = /^\s*(?:(\d+):)?(\d{1,2}):(\d{1,2})(?:[.,](\d{1,3}))?\s*$/.exec(value);
  if (!match) return null;
  const [, h, m, s, fraction = '0'] = match;
  return Number(h ?? 0) * 3600 + Number(m) * 60 + Number(s) + Number(fraction) / 10 ** fraction.length;
}

const cleanText = (text: string) =>
  text
    .replace(/\{[^}]*\}/g, '') // ASS override tags, also left over by ffmpeg's SRT conversion
    .replace(/<[^>]*>/g, '') // <i>, <font>, VTT voice/class tags
    .replace(/\\[Nn]/g, '\n')
    .replace(/\\h/g, ' ')
    .replace(/&amp;/g, '&')
    .replace(/&lt;/g, '<')
    .replace(/&gt;/g, '>')
    .replace(/&nbsp;/g, ' ')
    .split(/\r?\n/)
    .map((line) => line.trim())
    .filter(Boolean)
    .join(' / ');

/** SRT and WebVTT: blocks separated by blank lines, a `start --> end` line, then the text. */
function parseBlocks(text: string): SubtitleCue[] {
  const cues: SubtitleCue[] = [];
  for (const block of text.replace(/\r/g, '').split(/\n{2,}/)) {
    const lines = block.split('\n');
    const timing = lines.findIndex((line) => line.includes('-->'));
    if (timing < 0) continue;
    const [startText, rest = ''] = lines[timing].split('-->');
    const start = parseTimestamp(startText);
    const end = parseTimestamp(rest.trim().split(/\s+/)[0] ?? '');
    if (start === null || end === null) continue;
    const content = cleanText(lines.slice(timing + 1).join('\n'));
    if (content) cues.push({ start, end: Math.max(start, end), text: content });
  }
  return cues;
}

/** ASS/SSA: `Dialogue:` lines laid out by the `[Events]` `Format:` line. Drawings (`\p1`) are left out. */
function parseAss(text: string): SubtitleCue[] {
  const cues: SubtitleCue[] = [];
  let fields = ['layer', 'start', 'end', 'style', 'name', 'marginl', 'marginr', 'marginv', 'effect', 'text'];
  let inEvents = false;
  for (const raw of text.split(/\r?\n/)) {
    const line = raw.trim();
    if (line.startsWith('[')) {
      inEvents = line.toLowerCase() === '[events]';
      continue;
    }
    if (!inEvents) continue;
    if (/^format\s*:/i.test(line)) {
      fields = line
        .slice(line.indexOf(':') + 1)
        .split(',')
        .map((f) => f.trim().toLowerCase());
      continue;
    }
    if (!/^dialogue\s*:/i.test(line)) continue;
    const values = line.slice(line.indexOf(':') + 1).split(',');
    const textIndex = fields.indexOf('text');
    const value = (name: string) => values[fields.indexOf(name)]?.trim() ?? '';
    const body = values.slice(textIndex).join(',');
    if (/\\p[1-9]/.test(body)) continue;
    const start = parseTimestamp(value('start'));
    const end = parseTimestamp(value('end'));
    const content = cleanText(body);
    if (start === null || end === null || !content) continue;
    cues.push({ start, end: Math.max(start, end), text: content });
  }
  return cues;
}

/**
 * Lines of a text subtitle (WebVTT, SRT or ASS/SSA, told apart by their content), sorted by start. Lines shown at
 * the same time (ASS layers: sign + dialogue) stay separate entries.
 */
export function parseSubtitleCues(text: string): SubtitleCue[] {
  const head = text.trimStart().slice(0, 2000);
  const isAss = /^\[script info\]/im.test(head) || /^\[events\]/im.test(text) || /^dialogue\s*:/im.test(head);
  const cues = isAss ? parseAss(text) : parseBlocks(text);
  return cues.sort((a, b) => a.start - b.start || a.end - b.end);
}

/** Index of the first cue starting at or after `time` (binary search on the sorted list). */
export function firstCueFrom(cues: SubtitleCue[], time: number) {
  let lo = 0;
  let hi = cues.length;
  while (lo < hi) {
    const mid = (lo + hi) >> 1;
    if (cues[mid].start < time) lo = mid + 1;
    else hi = mid;
  }
  return lo;
}
