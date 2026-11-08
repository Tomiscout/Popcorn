import {
  ActionIcon,
  Affix,
  Badge,
  Button,
  Checkbox,
  Collapse,
  Group,
  Loader,
  Paper,
  Select,
  Stack,
  Switch,
  Text,
  Tooltip,
  UnstyledButton,
} from '@mantine/core';
import { IconChevronDown, IconDownload, IconRefresh, IconSortAscending, IconSortDescending } from '@tabler/icons-react';
import { useCallback, useMemo, useState } from 'react';
import { create } from 'zustand';
import { persist } from 'zustand/middleware';
import type { FileResult, FileSortField } from '@popcorn/plugin-sdk';
import type { FileSearch, FileSource, LinkMedia } from './file-sources';
import { downloadableLink, useDownloadFiles, useOpenLink } from './file-sources';
import { episodeRange, FileEntry, groupFiles, missingEpisodes, parseEpisodeName } from './file-groups';
import { PluginFileCard } from './plugin-components';
import { PluginIcon, ProviderGroup, RefreshButton } from './provider-group';

const COMBINED_PREVIEW_COUNT = 25;
/** Sort keys of the app; plugin keys are plain words. */
const PROVIDER_ORDER = '$provider';
const NAME = '$name';

type Sort = { key: string; descending: boolean };

/** How file results are shown, remembered across pages and restarts. `sort` null = the first provider's default. */
const useFileResultsView = create<{
  combined: boolean;
  /** Combined list: episodes of one release as a group, duplicates merged. */
  grouped: boolean;
  sort: Sort | null;
  setCombined: (combined: boolean) => void;
  setGrouped: (grouped: boolean) => void;
  setSort: (sort: Sort) => void;
}>()(
  persist(
    (set) => ({
      combined: true,
      grouped: true,
      sort: null,
      setCombined: (combined) => set({ combined }),
      setGrouped: (grouped) => set({ grouped }),
      setSort: (sort) => set({ sort }),
    }),
    { name: 'popcorn-file-results-view' }
  )
);

/** Sort columns of all sources, by key (the first label wins), plus the app's own. */
function sortOptions(sources: FileSource[]): FileSortField[] {
  const fields = new Map<string, FileSortField>();
  for (const field of sources.flatMap((s) => s.sortFields ?? []))
    if (!fields.has(field.key)) fields.set(field.key, field);
  return [
    { key: PROVIDER_ORDER, label: 'Provider order', descending: false },
    ...fields.values(),
    { key: NAME, label: 'Name', descending: false },
  ];
}

/** Stable: ties and results without the value (last) keep the provider order. */
function sortFiles<T>(items: T[], file: (item: T) => FileResult, sort: Sort): T[] {
  if (sort.key === PROVIDER_ORDER) return items;
  const dir = sort.descending ? -1 : 1;
  if (sort.key === NAME)
    return [...items].sort(
      (a, b) => dir * file(a).title.localeCompare(file(b).title, undefined, { numeric: true, sensitivity: 'base' })
    );
  return [...items].sort((a, b) => {
    const x = file(a).sortValues?.[sort.key];
    const y = file(b).sortValues?.[sort.key];
    if (x == null) return y == null ? 0 : 1;
    if (y == null) return -1;
    return dir * (x - y);
  });
}

function ProviderStatus({
  plugin,
  state,
  onRefresh,
}: {
  plugin: FileSource;
  state: FileSearch['results'][string];
  onRefresh: () => void;
}) {
  return (
    <Group gap={6} wrap="nowrap">
      <PluginIcon plugin={plugin} size={20} />
      <Text size="sm" fw={600}>
        {plugin.name}
      </Text>
      {state.loading ? (
        <Loader size="xs" />
      ) : state.error ? (
        <Tooltip label={state.error} multiline maw={400}>
          <Badge color="red" variant="light" size="sm">
            Failed
          </Badge>
        </Tooltip>
      ) : (
        <Badge color="gray" variant="light" size="sm">
          {state.results.length}
        </Badge>
      )}
      <RefreshButton plugin={plugin} loading={state.loading} onRefresh={onRefresh} />
    </Group>
  );
}

/** A result card; downloads started from it are added to the library (named after `media` when on a media page). */
function FileCard({ source, file, media }: { source: FileSource; file: FileResult; media?: LinkMedia }) {
  const { openLink, defaultTarget } = useOpenLink();
  const link = downloadableLink(file);
  return (
    <PluginFileCard
      pluginId={source.pluginId}
      sourceId={source.sourceId}
      file={file}
      openLink={(link, target) => void openLink(link, target, media, source)}
      defaultTarget={defaultTarget}
      add={
        link
          ? {
              onAdd: () => void openLink(link, 'internal', media, source),
              hint: media?.itemId
                ? 'Download with Popcorn into this item'
                : `Download with Popcorn into the library${media ? ` as “${media.title}”` : ''}`,
            }
          : undefined
      }
    />
  );
}

type Item = { plugin: FileSource; file: FileResult };

const itemKey = (i: Item) => `${i.plugin.id}\n${i.file.id}`;

/** Selected results (by `itemKey`) for a batch download. */
type Selection = {
  has: (item: Item) => boolean;
  /** Selects all given items, or unselects them all when they all are selected. */
  toggle: (items: Item[]) => void;
};

/** Library title for files downloaded without a media page: the release name up to its episode token. */
function titleOf(file: FileResult): string {
  const name = parseEpisodeName(file.title);
  const title = name?.label
    .split('{ep}')[0]
    .replace(/[\s\-–[(]+$/, '')
    .trim();
  return title || file.title;
}

/** A result with its selection checkbox and, in the combined list, its provider's icon. */
function SelectableFile({
  item,
  media,
  selection,
  showProvider,
  note,
}: {
  item: Item;
  media?: LinkMedia;
  selection: Selection;
  showProvider: boolean;
  note?: string;
}) {
  const { plugin, file } = item;
  const downloadable = downloadableLink(file) !== null;
  return (
    <Group gap="xs" wrap="nowrap" align="flex-start">
      <Tooltip label={downloadable ? 'Select for download' : 'No link Popcorn can download'}>
        <Checkbox
          mt={14}
          disabled={!downloadable}
          checked={selection.has(item)}
          onChange={() => selection.toggle([item])}
          aria-label={`Select ${file.title}`}
        />
      </Tooltip>
      {showProvider && (
        <Tooltip label={plugin.name}>
          <div style={{ marginTop: 12, flexShrink: 0 }}>
            <PluginIcon plugin={plugin} size={20} />
          </div>
        </Tooltip>
      )}
      <Stack gap={2} style={{ flex: 1, minWidth: 0 }}>
        <FileCard source={plugin} file={file} media={media} />
        {note && (
          <Text size="xs" c="dimmed">
            {note}
          </Text>
        )}
      </Stack>
    </Group>
  );
}

const duplicatesNote = (n: number) =>
  n > 0 ? `${n} duplicate${n === 1 ? '' : 's'} with fewer seeders hidden` : undefined;

/** Per-episode results of one release: selectable / downloadable as a whole, expanded to the single episodes. */
function EpisodeGroup({
  entry,
  media,
  selection,
}: {
  entry: Extract<FileEntry<Item>, { kind: 'group' }>;
  media?: LinkMedia;
  selection: Selection;
}) {
  const [opened, setOpened] = useState(false);
  const downloadFiles = useDownloadFiles();
  const items = entry.members.map((m) => m.item).filter((i) => downloadableLink(i.file) !== null);
  const selected = items.filter(selection.has).length;
  const missing = missingEpisodes(entry.episodes);
  const providers = [...new Map(entry.members.map((m) => [m.item.plugin.id, m.item.plugin])).values()];
  const duplicates = entry.members.reduce((n, m) => n + m.duplicates, 0);
  const label = entry.label.replace('{ep}', episodeRange(entry.season, entry.episodes));

  return (
    <Paper withBorder radius="md" p="sm">
      <Group gap="xs" wrap="nowrap">
        <Checkbox
          checked={items.length > 0 && selected === items.length}
          indeterminate={selected > 0 && selected < items.length}
          disabled={items.length === 0}
          onChange={() => selection.toggle(items)}
          aria-label={`Select all episodes of ${label}`}
        />
        <UnstyledButton onClick={() => setOpened((o) => !o)} style={{ flex: 1, minWidth: 0 }}>
          <Group gap="xs" wrap="nowrap">
            <IconChevronDown
              size={18}
              style={{ flexShrink: 0, transform: opened ? 'none' : 'rotate(-90deg)', transition: 'transform 150ms' }}
            />
            <Text fw={600} truncate>
              {label}
            </Text>
            <Badge variant="light" style={{ flexShrink: 0 }}>
              {entry.members.length} files
            </Badge>
            {missing.length > 0 && (
              <Tooltip label={`Not found: ${episodeRange(entry.season, missing)}`} multiline maw={400}>
                <Badge color="yellow" variant="light" style={{ flexShrink: 0 }}>
                  {missing.length} missing
                </Badge>
              </Tooltip>
            )}
            {providers.map((p) => (
              <Tooltip key={p.id} label={p.name}>
                <div style={{ flexShrink: 0, display: 'flex' }}>
                  <PluginIcon plugin={p} size={18} />
                </div>
              </Tooltip>
            ))}
          </Group>
        </UnstyledButton>
        <Button
          size="xs"
          variant="light"
          leftSection={<IconDownload size={14} />}
          disabled={items.length === 0}
          onClick={() => void downloadFiles(items, media, titleOf(items[0].file))}
        >
          Download all
        </Button>
      </Group>
      <Collapse in={opened}>
        <Stack gap="xs" mt="sm">
          {duplicates > 0 && (
            <Text size="xs" c="dimmed">
              {duplicatesNote(duplicates)}
            </Text>
          )}
          {entry.members.map((m) => (
            <SelectableFile key={itemKey(m.item)} item={m.item} media={media} selection={selection} showProvider />
          ))}
        </Stack>
      </Collapse>
    </Paper>
  );
}

/** All results in one list, each marked with its provider's icon; optionally episodes grouped, duplicates merged. */
function CombinedResults({
  sources,
  search,
  sort,
  grouped,
  media,
  selection,
  onRefresh,
}: {
  sources: FileSource[];
  search: FileSearch;
  sort: Sort;
  grouped: boolean;
  media?: LinkMedia;
  selection: Selection;
  onRefresh: (sourceId: string) => void;
}) {
  const [expanded, setExpanded] = useState(false);
  const shown = useMemo(() => sources.filter((p) => search.results[p.id]), [sources, search]);
  const { key, descending } = sort;
  const entries = useMemo((): FileEntry<Item>[] => {
    const items = sortFiles(
      shown.flatMap((plugin) => search.results[plugin.id].results.map((file) => ({ plugin, file }))),
      (i) => i.file,
      { key, descending }
    );
    return grouped
      ? groupFiles(items, (i) => i.file)
      : items.map((item) => ({ kind: 'file', key: itemKey(item), item, duplicates: 0 }));
  }, [shown, search, key, descending, grouped]);
  const visible = expanded ? entries : entries.slice(0, COMBINED_PREVIEW_COUNT);
  const loading = shown.some((p) => search.results[p.id].loading);

  return (
    <Stack gap="sm">
      <Group gap="lg">
        {shown.map((plugin) => (
          <ProviderStatus
            key={plugin.id}
            plugin={plugin}
            state={search.results[plugin.id]}
            onRefresh={() => onRefresh(plugin.id)}
          />
        ))}
      </Group>
      {shown.map(
        (plugin) =>
          search.results[plugin.id].error && (
            <Text key={plugin.id} c="red" size="sm">
              {plugin.name}: search failed: {search.results[plugin.id].error}
            </Text>
          )
      )}
      {!loading && entries.length === 0 && <Text c="dimmed">No results</Text>}
      {visible.map((entry) =>
        entry.kind === 'group' ? (
          <EpisodeGroup key={entry.key} entry={entry} media={media} selection={selection} />
        ) : (
          <SelectableFile
            key={entry.key}
            item={entry.item}
            media={media}
            selection={selection}
            showProvider
            note={duplicatesNote(entry.duplicates)}
          />
        )
      )}
      {entries.length > COMBINED_PREVIEW_COUNT && (
        <Button variant="subtle" onClick={() => setExpanded((e) => !e)} style={{ alignSelf: 'center' }}>
          {expanded ? 'Show less' : `Show all ${entries.length}`}
        </Button>
      )}
    </Stack>
  );
}

/** Floating bar of a selection: downloads the selected results into one library item. */
function SelectionBar({ items, media, onClear }: { items: Item[]; media?: LinkMedia; onClear: () => void }) {
  const downloadFiles = useDownloadFiles();
  if (items.length === 0) return null;
  const target = media?.itemId ? 'this item' : `“${media?.title ?? titleOf(items[0].file)}”`;
  return (
    <Affix position={{ bottom: 20, left: '50%' }} style={{ transform: 'translateX(-50%)' }}>
      <Paper withBorder shadow="lg" radius="md" p="sm">
        <Group gap="sm" wrap="nowrap">
          <Text size="sm" fw={600}>
            {items.length} selected
          </Text>
          <Tooltip label={`Downloaded with Popcorn into ${target} in the library`}>
            <Button
              leftSection={<IconDownload size={16} />}
              onClick={() => {
                void downloadFiles(items, media, titleOf(items[0].file));
                onClear();
              }}
            >
              Download {items.length}
            </Button>
          </Tooltip>
          <Button variant="subtle" color="gray" onClick={onClear}>
            Clear
          </Button>
        </Group>
      </Paper>
    </Affix>
  );
}

/**
 * Results of a file search: one combined list (default; episodes of a release grouped, duplicates from several
 * providers merged into the one with the most seeders) or grouped per provider, sorted by a column the providers
 * expose (default: the first provider's first column, e.g. seeders), with a refresh per provider. Results can be
 * selected and downloaded together into one library item.
 */
export function FileResults({
  section,
  sources,
  search,
  media,
  onRefresh,
}: {
  /** Separates the collapsed state of provider groups in different lists. */
  section: string;
  sources: FileSource[];
  search: FileSearch;
  /** The media page the files are listed on, if any. */
  media?: LinkMedia;
  /** Searches one source (`FileSource.id`) again. */
  onRefresh: (sourceId: string) => void;
}) {
  const { combined, grouped, setCombined, setGrouped, setSort } = useFileResultsView();
  const options = useMemo(() => sortOptions(sources), [sources]);
  const preferred = useFileResultsView((s) => s.sort);
  const fallback = options.length > 2 ? options[1] : options[0]; // first plugin column, else provider order
  const sort =
    preferred && options.some((o) => o.key === preferred.key)
      ? preferred
      : { key: fallback.key, descending: fallback.descending };

  // Selected items by key; dropped when a provider is searched again (its results are replaced).
  const [selected, setSelected] = useState<Map<string, Item>>(new Map());
  const selectedItems = useMemo(
    () => [...selected.values()].filter((i) => search.results[i.plugin.id]?.results.includes(i.file)),
    [selected, search]
  );
  const selection = useMemo<Selection>(
    () => ({
      has: (item) => selected.has(itemKey(item)),
      toggle: (items) =>
        setSelected((current) => {
          const next = new Map(current);
          const all = items.every((i) => next.has(itemKey(i)));
          for (const i of items) {
            if (all) next.delete(itemKey(i));
            else next.set(itemKey(i), i);
          }
          return next;
        }),
    }),
    [selected]
  );
  const clear = useCallback(() => setSelected(new Map()), []);

  return (
    <Stack gap="md">
      <Group gap="md">
        <Switch label="Combine providers" checked={combined} onChange={(e) => setCombined(e.currentTarget.checked)} />
        {combined && (
          <Tooltip label="Episodes of one release as a group; duplicates keep the one with the most seeders">
            <Switch label="Group episodes" checked={grouped} onChange={(e) => setGrouped(e.currentTarget.checked)} />
          </Tooltip>
        )}
        <Group gap={4}>
          <Select
            size="xs"
            w={160}
            aria-label="Sort by"
            allowDeselect={false}
            data={options.map((o) => ({ value: o.key, label: o.label }))}
            value={sort.key}
            onChange={(key) => {
              const option = options.find((o) => o.key === key);
              if (option) setSort({ key: option.key, descending: option.descending });
            }}
          />
          {sort.key !== PROVIDER_ORDER && (
            <Tooltip label={sort.descending ? 'Descending' : 'Ascending'}>
              <ActionIcon
                variant="subtle"
                color="gray"
                onClick={() => setSort({ ...sort, descending: !sort.descending })}
                aria-label="Toggle sort direction"
              >
                {sort.descending ? <IconSortDescending size={18} /> : <IconSortAscending size={18} />}
              </ActionIcon>
            </Tooltip>
          )}
        </Group>
        <Button
          size="xs"
          variant="subtle"
          leftSection={<IconRefresh size={14} />}
          onClick={() => sources.forEach((p) => search.results[p.id] && onRefresh(p.id))}
        >
          Refresh all
        </Button>
      </Group>

      {combined ? (
        <CombinedResults
          sources={sources}
          search={search}
          sort={sort}
          grouped={grouped}
          media={media}
          selection={selection}
          onRefresh={onRefresh}
        />
      ) : (
        <Stack gap="xl">
          {sources.map(
            (plugin) =>
              search.results[plugin.id] && (
                <ProviderGroup
                  key={`${plugin.id}\n${search.requests[plugin.id]}`}
                  section={section}
                  plugin={plugin}
                  state={{
                    ...search.results[plugin.id],
                    results: sortFiles(search.results[plugin.id].results, (f) => f, sort),
                  }}
                  onRefresh={() => onRefresh(plugin.id)}
                  renderItem={(file) => (
                    <SelectableFile
                      key={file.id}
                      item={{ plugin, file }}
                      media={media}
                      selection={selection}
                      showProvider={false}
                    />
                  )}
                />
              )
          )}
        </Stack>
      )}

      <SelectionBar items={selectedItems} media={media} onClear={clear} />
    </Stack>
  );
}
