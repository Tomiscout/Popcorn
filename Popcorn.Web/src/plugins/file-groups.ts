import type { FileResult } from '@popcorn/plugin-sdk';

/**
 * Episode detection in release names, so per-episode results of one release (same naming scheme, different episode)
 * list as one group that can be downloaded together.
 */

/** A release name split around its episode token. */
export type EpisodeName = {
  /** Same for every episode of one naming scheme (lowercased, separators and checksums dropped). */
  scheme: string;
  /** The cleaned name with the episode token replaced by `{ep}`, for the group's label. */
  label: string;
  season: number | null;
  /** Usually one; several for multi-episode files (S01E01-E02). */
  episodes: number[];
};

const EXTENSION = /\.(mkv|mp4|avi|m4v|webm|ts|wmv|mov|torrent)$/i;
/** CRC32 tags of fansub releases ([1A2B3C4D]) differ per episode. */
const CHECKSUM = /[[(][0-9a-f]{8}[\])]/gi;

type Pattern = { regex: RegExp; season: (m: RegExpExecArray) => number | null; first: number; last?: number };

/** In order of reliability; the first match wins. `first`/`last` = groups of the episode (range). */
const PATTERNS: Pattern[] = [
  // S01E02, S01E02-E03, S01E02E03, S01 E02
  { regex: /\bS(\d{1,2}) ?E(\d{1,4})(?:-?E(\d{1,4}))?\b/i, season: (m) => +m[1], first: 2, last: 3 },
  // 1x02
  { regex: /\b(\d{1,2})x(\d{2,3})\b/i, season: (m) => +m[1], first: 2 },
  // Episode 12, Ep 12, Ep.12
  { regex: /\b(?:episode|ep)[ .]?(\d{1,4})\b/i, season: () => null, first: 1 },
  // Anime: "Show - 05", "Show - 05v2 (1080p)"
  { regex: /(?<= - )(\d{2,4})(?:v\d)?(?= |$)/, season: () => null, first: 1 },
];

export function parseEpisodeName(title: string): EpisodeName | null {
  const clean = title.replace(EXTENSION, '').replace(CHECKSUM, ' ').replace(/[._]+/g, ' ').replace(/\s+/g, ' ').trim();
  for (const p of PATTERNS) {
    const m = p.regex.exec(clean);
    if (!m) continue;
    const first = +m[p.first];
    const last = p.last && m[p.last] ? +m[p.last] : first;
    if (last < first || last - first > 50) continue;
    const season = p.season(m);
    const before = clean.slice(0, m.index);
    const after = clean.slice(m.index + m[0].length);
    const label = `${before}{ep}${after}`;
    const prefix = season == null ? '' : `s${season}`;
    return {
      scheme: `${before}${prefix}#${after}`
        .toLowerCase()
        .replace(/[^\p{L}\p{N}#]+/gu, ' ')
        .trim(),
      label,
      season,
      episodes: Array.from({ length: last - first + 1 }, (_, i) => first + i),
    };
  }
  return null;
}

const pad = (n: number) => String(n).padStart(2, '0');

/** "S01E01–E12", "E01–E12", "E01, E03" (lists when there are gaps, ranges otherwise). */
export function episodeRange(season: number | null, episodes: number[]): string {
  const s = season == null ? '' : `S${pad(season)}`;
  if (episodes.length === 0) return s;
  const sorted = [...new Set(episodes)].sort((a, b) => a - b);
  const contiguous = sorted[sorted.length - 1] - sorted[0] === sorted.length - 1;
  if (sorted.length === 1) return `${s}E${pad(sorted[0])}`;
  if (contiguous) return `${s}E${pad(sorted[0])}–E${pad(sorted[sorted.length - 1])}`;
  return `${s}E${sorted.map(pad).join(', E')}`;
}

/** Episodes between the first and last one that no result has. */
export function missingEpisodes(episodes: number[]): number[] {
  if (episodes.length === 0) return [];
  const have = new Set(episodes);
  const min = Math.min(...episodes);
  const max = Math.max(...episodes);
  const missing: number[] = [];
  for (let e = min; e <= max; e++) if (!have.has(e)) missing.push(e);
  return missing;
}

const seeders = (file: FileResult) => file.sortValues?.seeders ?? -1;

/** Of the members a group's "all episodes" picks one per episode: the one with the most seeders. */
export function bestPerEpisode<T>(members: GroupMember<T>[], file: (item: T) => FileResult): T[] {
  const best = new Map<string, T>();
  for (const { item, episodes } of members) {
    const key = episodes.join(',');
    const current = best.get(key);
    if (current === undefined || seeders(file(item)) > seeders(file(current))) best.set(key, item);
  }
  return [...best.values()];
}

/** Identity of a result across plugins (`origin` + its id there), null = unknown. */
function originKey(file: FileResult): string | null {
  const origin = file.origin?.trim().toLowerCase();
  return origin ? `${origin}\n${file.originId ?? file.id}` : null;
}

/**
 * Drops results that are the same item at the same origin (e.g. an aggregator's copy of another plugin's result),
 * keeping the one with the most seeders (ties: the first, i.e. provider order) at the place of the first copy.
 * Results without an origin are all kept.
 */
export function dedupeFiles<T>(items: T[], file: (item: T) => FileResult): { item: T; duplicates: number }[] {
  const kept: { item: T; duplicates: number }[] = [];
  const byKey = new Map<string, { item: T; duplicates: number }>();
  for (const item of items) {
    const key = originKey(file(item));
    const current = key === null ? undefined : byKey.get(key);
    if (current) {
      current.duplicates++;
      if (seeders(file(item)) > seeders(file(current.item))) current.item = item;
      continue;
    }
    const entry = { item, duplicates: 0 };
    kept.push(entry);
    if (key !== null) byKey.set(key, entry);
  }
  return kept;
}

/** One episode of a group: the best of its duplicates (most seeders), and how many others there were. */
export type GroupMember<T> = { item: T; episodes: number[]; duplicates: number };

export type FileEntry<T> =
  | { kind: 'file'; key: string; item: T; duplicates: number }
  | {
      kind: 'group';
      key: string;
      label: string;
      season: number | null;
      members: GroupMember<T>[];
      episodes: number[];
    };

/**
 * Groups results of one naming scheme with at least two distinct episodes; duplicates (the same episode / the same
 * name from several sources) are merged into the one with the most seeders. Results of one source are never merged
 * with each other: there the same name is a different torrent (another upload), so the n-th of a name from each
 * source are the duplicates. A group or file takes the place of its first item, so the order of `items` (the user's
 * sort) is kept. `hidden` = duplicates already dropped per item (`dedupeFiles`), added to the counts.
 */
export function groupFiles<T>(
  items: T[],
  file: (item: T) => FileResult,
  source: (item: T) => string,
  hidden: (item: T) => number = () => 0
): FileEntry<T>[] {
  type Candidate = { item: T; name: EpisodeName | null; index: number; key: string };
  const occurrences = new Map<string, number>();
  const candidates: Candidate[] = items.map((item, index) => {
    const name = parseEpisodeName(file(item).title);
    const release = name
      ? `${name.scheme}\n${name.episodes.join(',')}`
      : file(item)
          .title.toLowerCase()
          .replace(/[^\p{L}\p{N}]+/gu, ' ')
          .trim();
    const perSource = `${source(item)}\n${release}`;
    const n = occurrences.get(perSource) ?? 0;
    occurrences.set(perSource, n + 1);
    return { item, name, index, key: `${release}\n${n}` };
  });

  // Best result per exact name / episode of a scheme (and occurrence within its source).
  const best = new Map<string, { candidate: Candidate; duplicates: number }>();
  for (const c of candidates) {
    const current = best.get(c.key);
    if (!current) best.set(c.key, { candidate: c, duplicates: hidden(c.item) });
    else {
      current.duplicates += 1 + hidden(c.item);
      if (seeders(file(c.item)) > seeders(file(current.candidate.item))) current.candidate = c;
    }
  }

  const schemes = new Map<string, { candidate: Candidate; duplicates: number }[]>();
  for (const entry of best.values())
    if (entry.candidate.name) {
      const list = schemes.get(entry.candidate.name.scheme) ?? [];
      list.push(entry);
      schemes.set(entry.candidate.name.scheme, list);
    }
  // A scheme with at least two distinct episodes is a group (it may also hold several uploads of one episode).
  const grouped = new Set(
    [...schemes]
      .filter(([, list]) => new Set(list.map((e) => e.candidate.name!.episodes.join(','))).size > 1)
      .map(([scheme]) => scheme)
  );

  // Position of each entry = the earliest of its items (duplicates included) in the sorted input.
  const firstIndex = new Map<string, number>();
  for (const c of candidates) {
    const key = c.name && grouped.has(c.name.scheme) ? `group\n${c.name.scheme}` : c.key;
    if (!firstIndex.has(key)) firstIndex.set(key, c.index);
  }

  const entries: { at: number; entry: FileEntry<T> }[] = [];
  for (const [scheme, list] of schemes) {
    if (!grouped.has(scheme)) continue;
    const members = list
      .map(({ candidate, duplicates }) => ({ item: candidate.item, episodes: candidate.name!.episodes, duplicates }))
      .sort((a, b) => a.episodes[0] - b.episodes[0]);
    const top = list.reduce((a, b) => (seeders(file(b.candidate.item)) > seeders(file(a.candidate.item)) ? b : a));
    const key = `group\n${scheme}`;
    entries.push({
      at: firstIndex.get(key)!,
      entry: {
        kind: 'group',
        key,
        label: top.candidate.name!.label,
        season: top.candidate.name!.season,
        members,
        episodes: members.flatMap((m) => m.episodes),
      },
    });
  }
  for (const [key, { candidate, duplicates }] of best) {
    if (candidate.name && grouped.has(candidate.name.scheme)) continue;
    entries.push({ at: firstIndex.get(key)!, entry: { kind: 'file', key, item: candidate.item, duplicates } });
  }
  return entries.sort((a, b) => a.at - b.at).map((e) => e.entry);
}
