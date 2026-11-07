import { Download } from '../../model/download.contract';
import { EpisodeName, episodeRange, parseEpisodeName } from '../../plugins/file-groups';

export type DownloadEntry =
  | { kind: 'single'; key: string; download: Download }
  | {
      kind: 'group';
      key: string;
      label: string;
      /** All members are in this library item. */
      mediaItemId: string | null;
      /** "S01E01–E12" of the members whose names have an episode, '' if none. */
      episodes: string;
      downloads: Download[];
    };

/**
 * Downloads that belong together, like the episode groups of the file results: the downloads of one library item
 * (a batch of episodes), else unlinked ones of one episode naming scheme. A group takes the place of its first member,
 * so the list order is kept.
 */
export function groupDownloads(downloads: Download[]): DownloadEntry[] {
  const names = new Map(downloads.map((d) => [d.id, parseEpisodeName(d.name)]));
  const keyOf = (d: Download) => {
    if (d.mediaItemId) return `item\n${d.mediaItemId}`;
    const name = names.get(d.id);
    return name ? `scheme\n${name.scheme}` : `single\n${d.id}`;
  };

  const members = new Map<string, Download[]>();
  for (const d of downloads) {
    const key = keyOf(d);
    members.set(key, [...(members.get(key) ?? []), d]);
  }

  // Map keeps insertion order = the position of each group's first member.
  return [...members].map(([key, list]): DownloadEntry => {
    if (list.length === 1) return { kind: 'single', key: list[0].id, download: list[0] };
    const parsed = list.map((d) => names.get(d.id)).filter((n): n is EpisodeName => n != null);
    const seasons = new Set(parsed.map((n) => n.season));
    const first = list[0];
    const sorted = [...list].sort(
      (a, b) =>
        (names.get(a.id)?.episodes[0] ?? Infinity) - (names.get(b.id)?.episodes[0] ?? Infinity) ||
        a.name.localeCompare(b.name)
    );
    return {
      kind: 'group',
      key,
      label: first.mediaItemId
        ? (first.mediaItemTitle ?? first.name)
        : (names.get(first.id)?.label.replace('{ep}', '').replace(/\s+/g, ' ').trim() ?? first.name),
      mediaItemId: first.mediaItemId,
      episodes: parsed.length
        ? episodeRange(
            seasons.size === 1 ? parsed[0].season : null,
            parsed.flatMap((n) => n.episodes)
          )
        : '',
      downloads: sorted,
    };
  });
}
