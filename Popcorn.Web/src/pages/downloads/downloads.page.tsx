import {
  ActionIcon,
  Autocomplete,
  Badge,
  Button,
  Checkbox,
  FileButton,
  Group,
  Modal,
  Progress,
  Select,
  Stack,
  Table,
  Tabs,
  Text,
  TextInput,
  Title,
  Tooltip,
} from '@mantine/core';
import { modals } from '@mantine/modals';
import {
  IconChevronDown,
  IconChevronRight,
  IconDownload,
  IconFolderOpen,
  IconLink,
  IconMagnet,
  IconPlayerPause,
  IconPlayerPlay,
  IconPlayerPlayFilled,
  IconPlus,
  IconStack2,
  IconTrash,
  IconUpload,
} from '@tabler/icons-react';
import React, { useCallback, useEffect, useMemo, useState } from 'react';
import { Link, useLocation } from 'react-router-dom';
import {
  Download,
  DownloadAddRequest,
  DownloadKind,
  DownloadPriority,
  DownloadState,
  FilePriority,
} from '../../model/download.contract';
import { MediaItem, MediaListResponse } from '../../model/media.contract';
import { SocketRequests } from '../../socket/socket-requests';
import { useSocketEvents } from '../../socket/useSocketEvents.hook';
import { usePlayerStore } from '../../state/player.store';
import { useWebSocket } from '../../state/websocket-context';
import { isDesktopApp, useIsMobile } from '../../utils/platform';
import { formatBytes, formatDuration, formatRate } from './format';
import { DownloadEntry, groupDownloads } from './download-groups';
import { useColumnWidths, usePolling } from './downloads.hooks';
import { ResizableTh } from './resizable-columns';

const STATE_COLORS: Record<DownloadState, string> = {
  Metadata: 'gray',
  Hashing: 'yellow',
  Downloading: 'blue',
  Seeding: 'teal',
  Paused: 'gray',
  Completed: 'green',
  Error: 'red',
};

/** A finished download stays green when it is paused: gray is for a bar that still has a way to go. */
const progressColor = (progress: number, state: DownloadState) =>
  state === 'Paused' && progress >= 1 ? STATE_COLORS.Completed : STATE_COLORS[state];

const STATE_LABELS: Partial<Record<DownloadState, string>> = {
  Metadata: 'Getting metadata',
  Hashing: 'Checking files',
};

/** The name the library shows for an item. */
const displayTitle = (item: MediaItem) => item.shortTitle || item.title;

/** Library items the "Add to library" field suggests; loaded while the dialog is open. */
function useLibraryItems(enabled: boolean) {
  const { client, isClientReady } = useWebSocket();
  const [items, setItems] = useState<MediaItem[]>([]);
  useSocketEvents(
    useMemo(
      () => ({
        [SocketRequests.MediaList]: (r: MediaListResponse) => setItems([...r.recentItems, ...r.newItems]),
      }),
      []
    )
  );
  useEffect(() => {
    if (enabled && isClientReady) client.sendEvent(SocketRequests.MediaList, {});
  }, [enabled, isClientReady, client]);
  return items;
}

/**
 * Dialog: link / .torrent file input + "Add to library" option of one tab. The library field
 * autocompletes existing items (the download is added to the picked one), other text = title of a new item.
 */
function AddDownloadDialog({ kind, opened, onClose }: { kind: DownloadKind; opened: boolean; onClose: () => void }) {
  const { client } = useWebSocket();
  const [url, setUrl] = useState('');
  const [title, setTitle] = useState('');
  const [addToLibrary, setAddToLibrary] = useState(true);
  const items = useLibraryItems(opened);
  const torrent = kind === 'Torrent';

  const titles = useMemo(() => [...new Set(items.map(displayTitle))].sort((a, b) => a.localeCompare(b)), [items]);
  const target = title.trim() ? items.find((i) => displayTitle(i) === title.trim()) : undefined;
  const library = {
    addToLibrary,
    mediaTitle: addToLibrary && !target ? title.trim() || null : null,
    mediaItemId: addToLibrary ? (target?.id ?? null) : null,
  };

  const close = () => {
    setUrl('');
    setTitle('');
    onClose();
  };

  const add = () => {
    if (!url.trim()) return;
    client.sendEvent<DownloadAddRequest>(SocketRequests.DownloadAdd, { kind, url: url.trim(), ...library });
    close();
  };

  const upload = async (files: File[]) => {
    for (const file of files) {
      const bytes = new Uint8Array(await file.arrayBuffer());
      client.sendBinaryEvent(
        SocketRequests.DownloadAddTorrentFile,
        bytes,
        library.addToLibrary ? '1' : '0',
        encodeURIComponent(library.mediaTitle ?? ''),
        library.mediaItemId ?? ''
      );
    }
    close();
  };

  return (
    <Modal opened={opened} onClose={close} title={torrent ? 'Add torrent' : 'Add direct download'} size="lg">
      <Stack gap="md">
        <TextInput
          data-autofocus
          label={torrent ? 'Magnet link or .torrent URL' : 'File URL'}
          placeholder={torrent ? 'magnet:?xt=urn:btih:…' : 'https://…'}
          leftSection={torrent ? <IconMagnet size={16} /> : <IconLink size={16} />}
          value={url}
          onChange={(e) => setUrl(e.currentTarget.value)}
          onKeyDown={(e) => e.key === 'Enter' && add()}
        />
        <Checkbox
          label="Add to library"
          checked={addToLibrary}
          onChange={(e) => setAddToLibrary(e.currentTarget.checked)}
        />
        {addToLibrary && (
          <Autocomplete
            label="Library item"
            description={
              target
                ? 'Files are added to this existing item'
                : title.trim()
                  ? 'A new item with this title'
                  : 'Pick an existing item or type a new title (default: from the file name)'
            }
            placeholder="Title in the library"
            data={titles}
            limit={20}
            value={title}
            onChange={setTitle}
          />
        )}
        <Group justify="flex-end">
          {torrent && (
            <FileButton onChange={upload} accept=".torrent,application/x-bittorrent" multiple>
              {(props) => (
                <Button variant="light" leftSection={<IconUpload size={16} />} {...props}>
                  .torrent files
                </Button>
              )}
            </FileButton>
          )}
          <Button leftSection={<IconDownload size={16} />} onClick={add} disabled={!url.trim()}>
            Download
          </Button>
        </Group>
      </Stack>
    </Modal>
  );
}

/** Release names often have no spaces ("Show.Name.S01E02..."): break anywhere rather than overflow the column. */
const WRAP: React.CSSProperties = { overflowWrap: 'anywhere' };

const isStopped = (state: DownloadState) => state === 'Paused' || state === 'Completed' || state === 'Error';

const PRIORITIES: DownloadPriority[] = ['Low', 'Normal', 'High'];
const FILE_PRIORITIES: FilePriority[] = ['Low', 'Normal', 'High'];

/** What the rows do to downloads; pausing / resuming shows at once, before the server confirms. */
type DownloadActions = {
  setStopped: (downloads: Download[], stopped: boolean) => void;
  setPriority: (downloads: Download[], priority: DownloadPriority) => void;
  remove: (downloads: Download[], title: string) => void;
  play: (download: Download, fileId: string) => void;
};

function useDownloadActions(onStopped: (ids: string[], stopped: boolean) => void): DownloadActions {
  const { client } = useWebSocket();
  return useMemo(
    () => ({
      setStopped: (downloads, stopped) => {
        onStopped(
          downloads.map((d) => d.id),
          stopped
        );
        for (const d of downloads)
          client.sendEvent(stopped ? SocketRequests.DownloadPause : SocketRequests.DownloadResume, { id: d.id });
      },
      setPriority: (downloads, priority) => {
        for (const d of downloads) client.sendEvent(SocketRequests.DownloadSetPriority, { id: d.id, priority });
      },
      remove: (downloads, title) => {
        const owned = downloads.some((d) => d.mediaItemId && !d.attachedToItem);
        const send = (deleteFiles: boolean) => {
          for (const d of downloads) client.sendEvent(SocketRequests.DownloadRemove, { id: d.id, deleteFiles });
        };
        modals.openConfirmModal({
          title: `Remove ${title}?`,
          children: (
            <Text size="sm">
              Remove from the list only, or also delete the downloaded files{owned ? ' and the library item' : ''}?
            </Text>
          ),
          labels: { confirm: 'Delete files', cancel: 'Remove from list' },
          confirmProps: { color: 'red' },
          closeOnCancel: true,
          onConfirm: () => send(true),
          onCancel: () => send(false),
        });
      },
      play: (download, fileId) => {
        if (isDesktopApp) {
          client.sendEvent(SocketRequests.FileOpen, fileId);
          return;
        }
        const player = usePlayerStore.getState();
        player.resetState();
        if (download.mediaItemId) player.setMediaId(download.mediaItemId);
        player.setFileId(fileId);
      },
    }),
    [client, onStopped]
  );
}

function PrioritySelect({
  value,
  onChange,
}: {
  value: DownloadPriority | null;
  onChange: (p: DownloadPriority) => void;
}) {
  return (
    <Select
      size="xs"
      data={PRIORITIES}
      value={value}
      placeholder="Mixed"
      allowDeselect={false}
      onChange={(v) => v && onChange(v as DownloadPriority)}
      aria-label="Priority"
    />
  );
}

function StateBadge({ state, streaming }: { state: DownloadState; streaming: boolean }) {
  return (
    <Group gap={4}>
      <Badge variant="light" color={STATE_COLORS[state]} size="sm">
        {STATE_LABELS[state] ?? state}
      </Badge>
      {streaming && (
        <Tooltip label="A player is reading it: downloads first, before everything else">
          <Badge variant="filled" color="grape" size="sm">
            Streaming
          </Badge>
        </Tooltip>
      )}
    </Group>
  );
}

function PauseButton({ downloads, actions }: { downloads: Download[]; actions: DownloadActions }) {
  // A group resumes when anything in it is stopped.
  const stopped = downloads.some((d) => isStopped(d.state));
  const seedAgain = downloads.every((d) => d.state === 'Completed');
  return (
    <Tooltip label={stopped ? (seedAgain ? 'Seed again' : 'Resume') : 'Pause'}>
      <ActionIcon
        variant="subtle"
        color="gray"
        onClick={() => actions.setStopped(stopped ? downloads.filter((d) => isStopped(d.state)) : downloads, !stopped)}
      >
        {stopped ? <IconPlayerPlay size={18} /> : <IconPlayerPause size={18} />}
      </ActionIcon>
    </Tooltip>
  );
}

const PlayButton = React.forwardRef<HTMLButtonElement, { onClick: () => void }>(({ onClick }, ref) => (
  <Button
    ref={ref}
    size="compact-xs"
    variant="light"
    mt={4}
    leftSection={<IconPlayerPlayFilled size={12} />}
    onClick={onClick}
  >
    Play
  </Button>
));

function DownloadRow({
  download,
  actions,
  inGroup = false,
}: {
  download: Download;
  actions: DownloadActions;
  inGroup?: boolean;
}) {
  const { client } = useWebSocket();
  const [expanded, setExpanded] = useState(false);
  const d = download;
  const torrent = d.kind === 'Torrent';

  const setFilePriority = (index: number, priority: FilePriority) =>
    client.sendEvent(SocketRequests.DownloadSetFilePriority, { id: d.id, index, priority });

  const firstVideo = d.files.find((f) => f.isVideo && f.wanted && f.mediaFileId);
  const multiFile = d.files.length > 1;

  return (
    <>
      <Table.Tr bg={inGroup ? 'var(--mantine-color-dark-6)' : undefined}>
        <Table.Td>
          {multiFile && (
            <ActionIcon variant="subtle" color="gray" size="sm" onClick={() => setExpanded((e) => !e)}>
              {expanded ? <IconChevronDown size={16} /> : <IconChevronRight size={16} />}
            </ActionIcon>
          )}
        </Table.Td>
        <Table.Td pl={inGroup ? 'lg' : undefined}>
          <Tooltip label={d.name} multiline maw={600} openDelay={300} style={WRAP}>
            <Text size="sm" style={WRAP}>
              {d.displayName}
            </Text>
          </Tooltip>
          {d.error && (
            <Text size="xs" c="red" style={WRAP}>
              {d.error}
            </Text>
          )}
          {d.mediaItemId && !inGroup && (
            <Text size="xs" component={Link} to={`/watch/${d.mediaItemId}`} c="dimmed" td="underline">
              In library{d.mediaItemTitle ? `: ${d.mediaItemTitle}` : ''}
            </Text>
          )}
        </Table.Td>
        <Table.Td>
          <Group gap={0} wrap="nowrap">
            <PauseButton downloads={[d]} actions={actions} />
            <Tooltip label="Open folder">
              <ActionIcon
                variant="subtle"
                color="gray"
                onClick={() => client.sendEvent(SocketRequests.DownloadOpenFolder, { id: d.id })}
              >
                <IconFolderOpen size={18} />
              </ActionIcon>
            </Tooltip>
            <Tooltip label="Remove">
              <ActionIcon variant="subtle" color="red" onClick={() => actions.remove([d], d.displayName)}>
                <IconTrash size={18} />
              </ActionIcon>
            </Tooltip>
          </Group>
        </Table.Td>
        <Table.Td>
          <StateBadge state={d.state} streaming={d.streaming} />
        </Table.Td>
        <Table.Td>
          <Text size="xs">{d.state === 'Downloading' ? formatDuration(d.eta) : ''}</Text>
        </Table.Td>
        <Table.Td>
          <Progress value={d.progress * 100} color={progressColor(d.progress, d.state)} size="sm" />
          <Text size="xs" c="dimmed">
            {(d.progress * 100).toFixed(1)}% of {formatBytes(d.size)}
          </Text>
          {firstVideo && (
            <Tooltip label="Play (waits for the parts that are not downloaded yet)">
              <PlayButton onClick={() => actions.play(d, firstVideo.mediaFileId!)} />
            </Tooltip>
          )}
        </Table.Td>
        <Table.Td>
          <Text size="xs">↓ {formatRate(d.downloadRate)}</Text>
          {torrent && <Text size="xs">↑ {formatRate(d.uploadRate)}</Text>}
        </Table.Td>
        {torrent && (
          <Table.Td>
            <Text size="xs">
              {d.seeds} seeds / {d.peers} peers
            </Text>
            <Text size="xs" c="dimmed">
              Ratio {d.ratio.toFixed(2)}
            </Text>
          </Table.Td>
        )}
        <Table.Td>
          <PrioritySelect value={d.priority} onChange={(p) => actions.setPriority([d], p)} />
        </Table.Td>
      </Table.Tr>
      {expanded &&
        d.files.map((f) => (
          <Table.Tr key={f.index} bg="var(--mantine-color-dark-7)">
            <Table.Td />
            <Table.Td colSpan={4} pl={inGroup ? 'lg' : undefined}>
              <Group gap="xs" wrap="nowrap">
                <Checkbox
                  size="xs"
                  checked={f.wanted}
                  aria-label="Download this file"
                  onChange={(e) => setFilePriority(f.index, e.currentTarget.checked ? 'Normal' : 'Skip')}
                />
                <Text size="xs" style={{ ...WRAP, flex: 1 }}>
                  {f.path}
                </Text>
                {f.streaming && (
                  <Badge variant="filled" color="grape" size="xs">
                    Streaming
                  </Badge>
                )}
              </Group>
            </Table.Td>
            <Table.Td>
              <Text size="xs" c="dimmed">
                {(f.progress * 100).toFixed(1)}% of {formatBytes(f.length)}
              </Text>
              {f.isVideo && f.mediaFileId && f.wanted && <PlayButton onClick={() => actions.play(d, f.mediaFileId!)} />}
            </Table.Td>
            <Table.Td colSpan={torrent ? 2 : 1} />
            <Table.Td>
              {torrent && f.wanted && (
                <Select
                  size="xs"
                  data={FILE_PRIORITIES}
                  value={f.priority}
                  allowDeselect={false}
                  onChange={(v) => v && setFilePriority(f.index, v as FilePriority)}
                  aria-label="File priority"
                />
              )}
            </Table.Td>
          </Table.Tr>
        ))}
    </>
  );
}

/** Group state: the most active state of its members. */
const GROUP_STATE_ORDER: DownloadState[] = [
  'Error',
  'Downloading',
  'Metadata',
  'Hashing',
  'Seeding',
  'Paused',
  'Completed',
];

function GroupRows({
  entry,
  kind,
  actions,
}: {
  entry: Extract<DownloadEntry, { kind: 'group' }>;
  kind: DownloadKind;
  actions: DownloadActions;
}) {
  const [expanded, setExpanded] = useState(false);
  const list = entry.downloads;
  const torrent = kind === 'Torrent';
  const known = list.filter((d) => d.size >= 0);
  const size = known.reduce((s, d) => s + d.size, 0);
  const downloaded = known.reduce((s, d) => s + d.downloaded, 0);
  const progress = size > 0 ? downloaded / size : list.every((d) => d.progress >= 1) ? 1 : 0;
  const downRate = list.reduce((s, d) => s + d.downloadRate, 0);
  const upRate = list.reduce((s, d) => s + d.uploadRate, 0);
  const state = GROUP_STATE_ORDER.find((s) => list.some((d) => d.state === s)) ?? 'Paused';
  const counts = GROUP_STATE_ORDER.map((s) => [s, list.filter((d) => d.state === s).length] as const).filter(
    ([, n]) => n > 0
  );
  const priorities = new Set(list.map((d) => d.priority));
  const remaining = size - downloaded;

  return (
    <>
      <Table.Tr>
        <Table.Td>
          <ActionIcon variant="subtle" color="gray" size="sm" onClick={() => setExpanded((e) => !e)}>
            {expanded ? <IconChevronDown size={16} /> : <IconChevronRight size={16} />}
          </ActionIcon>
        </Table.Td>
        <Table.Td>
          <Group gap={6} wrap="nowrap" align="flex-start">
            <IconStack2 size={16} style={{ flexShrink: 0 }} />
            {entry.mediaItemId ? (
              <Text
                size="sm"
                fw={500}
                style={WRAP}
                component={Link}
                to={`/watch/${entry.mediaItemId}`}
                td="underline"
                title={`Open ${entry.label} in the library`}
              >
                {entry.label}
              </Text>
            ) : (
              <Text size="sm" fw={500} style={WRAP}>
                {entry.label}
              </Text>
            )}
          </Group>
          <Text size="xs" c="dimmed">
            {list.length} downloads{entry.episodes ? ` · ${entry.episodes}` : ''}
          </Text>
        </Table.Td>
        <Table.Td>
          <Group gap={0} wrap="nowrap">
            <PauseButton downloads={list} actions={actions} />
            <Tooltip label="Remove all">
              <ActionIcon
                variant="subtle"
                color="red"
                onClick={() => actions.remove(list, `${list.length} downloads of ${entry.label}`)}
              >
                <IconTrash size={18} />
              </ActionIcon>
            </Tooltip>
          </Group>
        </Table.Td>
        <Table.Td>
          <StateBadge state={state} streaming={list.some((d) => d.streaming)} />
          {counts.length > 1 && (
            <Text size="xs" c="dimmed">
              {counts.map(([s, n]) => `${n} ${(STATE_LABELS[s] ?? s).toLowerCase()}`).join(', ')}
            </Text>
          )}
        </Table.Td>
        <Table.Td>
          <Text size="xs">
            {state === 'Downloading' && downRate > 0 && remaining > 0 ? formatDuration(remaining / downRate) : ''}
          </Text>
        </Table.Td>
        <Table.Td>
          <Progress value={progress * 100} color={progressColor(progress, state)} size="sm" />
          <Text size="xs" c="dimmed">
            {(progress * 100).toFixed(1)}% of {formatBytes(size)}
          </Text>
        </Table.Td>
        <Table.Td>
          <Text size="xs">↓ {formatRate(downRate)}</Text>
          {torrent && <Text size="xs">↑ {formatRate(upRate)}</Text>}
        </Table.Td>
        {torrent && (
          <Table.Td>
            <Text size="xs">
              {list.reduce((s, d) => s + d.seeds, 0)} seeds / {list.reduce((s, d) => s + d.peers, 0)} peers
            </Text>
          </Table.Td>
        )}
        <Table.Td>
          <PrioritySelect
            value={priorities.size === 1 ? list[0].priority : null}
            onChange={(p) => actions.setPriority(list, p)}
          />
        </Table.Td>
      </Table.Tr>
      {expanded && list.map((d) => <DownloadRow key={d.id} download={d} actions={actions} inGroup />)}
    </>
  );
}

type Column = 'name' | 'actions' | 'state' | 'eta' | 'progress' | 'speed' | 'peers' | 'priority';

const COLUMN_WIDTHS: Record<Column, number> = {
  name: 420,
  actions: 110,
  state: 130,
  eta: 80,
  progress: 170,
  speed: 100,
  peers: 130,
  priority: 110,
};

/** Phones: the name column leaves room for the actions and state next to it (the table scrolls sideways). */
const MOBILE_COLUMN_WIDTHS: Record<Column, number> = { ...COLUMN_WIDTHS, name: 190 };

/** Width of the expand-arrow column (not resizable). */
const EXPANDER_WIDTH = 40;

function DownloadTable({
  downloads,
  kind,
  actions,
}: {
  downloads: Download[];
  kind: DownloadKind;
  actions: DownloadActions;
}) {
  const entries = useMemo(() => groupDownloads(downloads), [downloads]);
  const isMobile = useIsMobile();
  const { widths, setWidth, reset } = useColumnWidths<Column>(
    'popcorn.downloads.columns',
    isMobile ? MOBILE_COLUMN_WIDTHS : COLUMN_WIDTHS
  );
  if (!downloads.length)
    return (
      <Text c="dimmed" size="sm">
        No {kind === 'Torrent' ? 'torrents' : 'direct downloads'} yet.
      </Text>
    );

  const columns: Column[] = [
    'name',
    'actions',
    'state',
    'eta',
    'progress',
    'speed',
    ...(kind === 'Torrent' ? ['peers' as const] : []),
    'priority',
  ];
  const labels: Record<Column, React.ReactNode> = {
    name: 'Name',
    progress: 'Progress',
    state: 'State',
    speed: 'Speed',
    peers: 'Peers',
    eta: 'ETA',
    priority: (
      <Tooltip
        multiline
        w={280}
        label="While downloads of different priorities run, lower ones only get a share of the bandwidth. A download being streamed goes before all."
      >
        <span>Priority</span>
      </Tooltip>
    ),
    actions: null,
  };
  const tableWidth = EXPANDER_WIDTH + columns.reduce((sum, c) => sum + widths[c], 0);

  return (
    <div style={{ overflowX: 'auto' }}>
      <Table verticalSpacing={6} highlightOnHover style={{ tableLayout: 'fixed', width: tableWidth }}>
        <colgroup>
          <col style={{ width: EXPANDER_WIDTH }} />
          {columns.map((c) => (
            <col key={c} style={{ width: widths[c] }} />
          ))}
        </colgroup>
        <Table.Thead>
          <Table.Tr>
            <Table.Th />
            {columns.map((c) => (
              <ResizableTh key={c} width={widths[c]} onResize={(w) => setWidth(c, w)} onReset={() => reset(c)}>
                {labels[c]}
              </ResizableTh>
            ))}
          </Table.Tr>
        </Table.Thead>
        <Table.Tbody>
          {entries.map((e) =>
            e.kind === 'group' ? (
              <GroupRows key={e.key} entry={e} kind={kind} actions={actions} />
            ) : (
              <DownloadRow key={e.key} download={e.download} actions={actions} />
            )
          )}
        </Table.Tbody>
      </Table>
    </div>
  );
}

/** How long a pause / resume shows before the server has to confirm it. */
const PENDING_MS = 10_000;

/** Pause / resume clicks shown at once; dropped when the server's state agrees or after {@link PENDING_MS}. */
function usePendingStops(downloads: Download[] | null) {
  const [pending, setPending] = useState<Record<string, { stopped: boolean; at: number }>>({});

  useEffect(() => {
    if (!downloads) return;
    setPending((current) => {
      const now = Date.now();
      const next = Object.fromEntries(
        Object.entries(current).filter(([id, p]) => {
          const d = downloads.find((x) => x.id === id);
          return d && now - p.at < PENDING_MS && isStopped(d.state) !== p.stopped;
        })
      );
      return Object.keys(next).length === Object.keys(current).length ? current : next;
    });
  }, [downloads]);

  const shown = useMemo(
    () =>
      downloads?.map((d): Download => {
        const p = pending[d.id];
        if (!p || isStopped(d.state) === p.stopped) return d;
        const state: DownloadState = p.stopped ? 'Paused' : d.progress >= 1 ? 'Seeding' : 'Downloading';
        return { ...d, state, downloadRate: p.stopped ? 0 : d.downloadRate, uploadRate: p.stopped ? 0 : d.uploadRate };
      }) ?? null,
    [downloads, pending]
  );

  const mark = useCallback((ids: string[], stopped: boolean) => {
    const at = Date.now();
    setPending((current) => ({ ...current, ...Object.fromEntries(ids.map((id) => [id, { stopped, at }])) }));
  }, []);

  return [shown, mark] as const;
}

/** /downloads — built-in torrent client and direct downloader. */
const DownloadsPage: React.FC = () => {
  const [received, setDownloads] = useState<Download[] | null>(null);
  const [downloads, markStopped] = usePendingStops(received);
  const actions = useDownloadActions(markStopped);
  const [tab, setTab] = useState<string | null>('Torrent');
  const [adding, setAdding] = useState(false);
  const addKind: DownloadKind = tab === 'Direct' ? 'Direct' : 'Torrent';
  // Dropped .torrent files show their tab (useTorrentFileDrop).
  const { state: locationState } = useLocation();
  useEffect(() => {
    if (locationState?.tab) setTab(locationState.tab);
  }, [locationState]);
  useSocketEvents(useMemo(() => ({ [SocketRequests.DownloadList]: setDownloads }), []));
  usePolling(SocketRequests.DownloadList);

  const byKind = (kind: DownloadKind) => (downloads ?? []).filter((d) => d.kind === kind);
  const count = (kind: DownloadKind) => {
    const n = byKind(kind).length;
    return n ? ` (${n})` : '';
  };

  return (
    <Stack>
      <Group justify="space-between">
        <Title order={2}>Downloads</Title>
        <Button leftSection={<IconPlus size={16} />} onClick={() => setAdding(true)}>
          {addKind === 'Torrent' ? 'Add torrent' : 'Add download'}
        </Button>
      </Group>
      <AddDownloadDialog kind={addKind} opened={adding} onClose={() => setAdding(false)} />
      <Tabs value={tab} onChange={setTab} keepMounted={false}>
        <Tabs.List>
          <Tabs.Tab value="Torrent" leftSection={<IconMagnet size={16} />}>
            Torrent{count('Torrent')}
          </Tabs.Tab>
          <Tabs.Tab value="Direct" leftSection={<IconLink size={16} />}>
            Direct download{count('Direct')}
          </Tabs.Tab>
        </Tabs.List>
        {(['Torrent', 'Direct'] as const).map((kind) => (
          <Tabs.Panel key={kind} value={kind} pt="md">
            {downloads && <DownloadTable downloads={byKind(kind)} kind={kind} actions={actions} />}
          </Tabs.Panel>
        ))}
      </Tabs>
    </Stack>
  );
};

export default DownloadsPage;
