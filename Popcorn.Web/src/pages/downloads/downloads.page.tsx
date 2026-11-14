import {
  ActionIcon,
  Badge,
  Button,
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
import { Download, DownloadKind, DownloadPriority, DownloadState } from '../../model/download.contract';
import {
  ItemTarget,
  ItemTargetPicker,
  isTargetReady,
  libraryFields,
  linkName,
} from '../../components/item-picker/item-picker';
import { SocketRequests } from '../../socket/socket-requests';
import { useSocketEvents } from '../../socket/useSocketEvents.hook';
import { usePlayerStore } from '../../state/player.store';
import { useWebSocket } from '../../state/websocket-context';
import { isDesktopApp, useIsMobile } from '../../utils/platform';
import { formatBytes, formatDuration, formatRate } from './format';
import { DownloadEntry, groupDownloads } from './download-groups';
import { useColumnWidths } from './downloads.hooks';
import { useSubscription } from '../../socket/useSubscription.hook';
import { ResizableTh } from './resizable-columns';
import { PortWarning } from './port-warning';
import { DownloadDetailsPanel } from './download-details';

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

/**
 * Dialog: link / .torrent files + where they go in the library: a new item, an existing one (ranked for the link's
 * name, the best match preselected; the download becomes a folder of its own there) or nowhere.
 */
function AddDownloadDialog({ kind, opened, onClose }: { kind: DownloadKind; opened: boolean; onClose: () => void }) {
  const { client } = useWebSocket();
  const [url, setUrl] = useState('');
  const [files, setFiles] = useState<File[]>([]);
  const [target, setTarget] = useState<ItemTarget>({ kind: 'new', title: '' });
  const torrent = kind === 'Torrent';
  const name = files.length > 0 ? files[0].name.replace(/\.torrent$/i, '') : linkName(url);
  const library = libraryFields(target);
  const ready = (url.trim() !== '' || files.length > 0) && isTargetReady(target);

  const close = () => {
    setUrl('');
    setFiles([]);
    setTarget({ kind: 'new', title: '' });
    onClose();
  };

  const add = async () => {
    if (!ready) return;
    if (files.length === 0) {
      client.sendEvent(SocketRequests.DownloadAdd, { kind, url: url.trim(), ...library });
      close();
      return;
    }
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
        {files.length === 0 ? (
          <TextInput
            data-autofocus
            label={torrent ? 'Magnet link or .torrent URL' : 'File URL'}
            placeholder={torrent ? 'magnet:?xt=urn:btih:…' : 'https://…'}
            leftSection={torrent ? <IconMagnet size={16} /> : <IconLink size={16} />}
            value={url}
            onChange={(e) => setUrl(e.currentTarget.value)}
            onKeyDown={(e) => e.key === 'Enter' && add()}
          />
        ) : (
          <Group justify="space-between" wrap="nowrap">
            <Text size="sm" style={WRAP}>
              {files.map((f) => f.name).join(', ')}
            </Text>
            <Button variant="subtle" size="xs" onClick={() => setFiles([])}>
              Clear
            </Button>
          </Group>
        )}
        <ItemTargetPicker name={name} opened={opened} value={target} onChange={setTarget} />
        <Group justify="flex-end">
          {torrent && (
            <FileButton onChange={setFiles} accept=".torrent,application/x-bittorrent" multiple>
              {(props) => (
                <Button variant="light" leftSection={<IconUpload size={16} />} {...props}>
                  .torrent files
                </Button>
              )}
            </FileButton>
          )}
          <Button leftSection={<IconDownload size={16} />} onClick={add} disabled={!ready}>
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

/** Clicks on the row's own controls don't select it. */
const isControl = (target: EventTarget) =>
  !!(target as HTMLElement).closest?.('button, a, input, [role="combobox"], [role="listbox"], .mantine-Select-root');

const SELECTED_BG = 'var(--mantine-color-blue-light)';

function DownloadRow({
  download,
  actions,
  selected,
  onSelect,
  inGroup = false,
}: {
  download: Download;
  actions: DownloadActions;
  selected: boolean;
  onSelect: (id: string) => void;
  inGroup?: boolean;
}) {
  const { client } = useWebSocket();
  const d = download;
  const torrent = d.kind === 'Torrent';
  const firstVideo = d.files.find((f) => f.isVideo && f.wanted && f.mediaFileId);

  return (
    <Table.Tr
      bg={selected ? SELECTED_BG : inGroup ? 'var(--mantine-color-dark-6)' : undefined}
      style={{ cursor: 'pointer' }}
      onClick={(e) => !isControl(e.target) && onSelect(d.id)}
      aria-selected={selected}
    >
      <Table.Td />
      <Table.Td pl={inGroup ? 'lg' : undefined}>
        <Tooltip label={d.name} multiline maw={600} openDelay={300} style={WRAP}>
          <Text size="sm" style={WRAP}>
            {d.displayName}
          </Text>
        </Tooltip>
        {d.files.length > 1 && (
          <Text size="xs" c="dimmed">
            {d.files.filter((f) => f.wanted).length < d.files.length
              ? `${d.files.filter((f) => f.wanted).length} of ${d.files.length} files`
              : `${d.files.length} files`}
          </Text>
        )}
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
  selectedId,
  onSelect,
}: {
  entry: Extract<DownloadEntry, { kind: 'group' }>;
  kind: DownloadKind;
  actions: DownloadActions;
  selectedId: string | null;
  onSelect: (id: string) => void;
}) {
  // Open while one of its downloads is selected (e.g. picked before the group formed).
  const [expanded, setExpanded] = useState(() => entry.downloads.some((d) => d.id === selectedId));
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
      <Table.Tr style={{ cursor: 'pointer' }} onClick={(e) => !isControl(e.target) && setExpanded((x) => !x)}>
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
      {expanded &&
        list.map((d) => (
          <DownloadRow
            key={d.id}
            download={d}
            actions={actions}
            selected={d.id === selectedId}
            onSelect={onSelect}
            inGroup
          />
        ))}
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
  selectedId,
  onSelect,
}: {
  downloads: Download[];
  kind: DownloadKind;
  actions: DownloadActions;
  selectedId: string | null;
  onSelect: (id: string) => void;
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
    <div style={{ overflow: 'auto', flex: 1, minHeight: 0 }}>
      <Table verticalSpacing={6} highlightOnHover stickyHeader style={{ tableLayout: 'fixed', width: tableWidth }}>
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
              <GroupRows
                key={e.key}
                entry={e}
                kind={kind}
                actions={actions}
                selectedId={selectedId}
                onSelect={onSelect}
              />
            ) : (
              <DownloadRow
                key={e.key}
                download={e.download}
                actions={actions}
                selected={e.download.id === selectedId}
                onSelect={onSelect}
              />
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

const PANEL_HEIGHT_KEY = 'popcorn.downloads.panelHeight';
const DEFAULT_PANEL_HEIGHT = 340;
const MIN_PANEL_HEIGHT = 120;
/** The list keeps at least this much of the page when the panel is dragged up. */
const MIN_LIST_HEIGHT = 140;

/**
 * Height of the bottom panel, dragged at its top edge and remembered in localStorage. It is the panel's preferred
 * height: in a short window the panel shrinks (down to {@link MIN_PANEL_HEIGHT}) so the list keeps
 * {@link MIN_LIST_HEIGHT}. Layout: list, separator (the drag handle), panel as siblings.
 */
function usePanelHeight() {
  const [height, setHeight] = useState(() => Number(localStorage.getItem(PANEL_HEIGHT_KEY)) || DEFAULT_PANEL_HEIGHT);
  const startDrag = (e: React.PointerEvent<HTMLDivElement>) => {
    e.preventDefault();
    const handle = e.currentTarget;
    const list = handle.previousElementSibling as HTMLElement;
    const panel = handle.nextElementSibling as HTMLElement;
    const startY = e.clientY;
    // From what is shown, not the preferred height: that may be more than fits.
    const start = panel.offsetHeight;
    const max = Math.max(MIN_PANEL_HEIGHT, start + list.offsetHeight - MIN_LIST_HEIGHT);
    let last = start;
    handle.setPointerCapture(e.pointerId);
    const move = (ev: PointerEvent) => {
      last = Math.round(Math.max(MIN_PANEL_HEIGHT, Math.min(max, start - (ev.clientY - startY))));
      setHeight(last);
    };
    const up = () => {
      localStorage.setItem(PANEL_HEIGHT_KEY, String(last));
      handle.removeEventListener('pointermove', move);
      handle.removeEventListener('pointerup', up);
      handle.removeEventListener('pointercancel', up);
    };
    handle.addEventListener('pointermove', move);
    handle.addEventListener('pointerup', up);
    handle.addEventListener('pointercancel', up);
  };
  const reset = () => {
    localStorage.removeItem(PANEL_HEIGHT_KEY);
    setHeight(DEFAULT_PANEL_HEIGHT);
  };
  return { height, startDrag, reset };
}

/**
 * The page fills the app's content area (AppShell.Main: the viewport minus header and padding), so the list scrolls
 * above and the panel stays at the bottom. Phones: the page scrolls as a whole instead.
 */
const PAGE_HEIGHT = 'calc(100dvh - var(--app-shell-header-offset, 0rem) - 2 * var(--app-shell-padding, 0rem))';

/** /downloads — built-in torrent client and direct downloader: list above, details of the selected one below. */
const DownloadsPage: React.FC = () => {
  const [received, setDownloads] = useState<Download[] | null>(null);
  const [downloads, markStopped] = usePendingStops(received);
  const actions = useDownloadActions(markStopped);
  const [tab, setTab] = useState<string | null>('Torrent');
  const [adding, setAdding] = useState(false);
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const isMobile = useIsMobile();
  const panel = usePanelHeight();
  const addKind: DownloadKind = tab === 'Direct' ? 'Direct' : 'Torrent';
  // Dropped .torrent files show their tab (useTorrentFileDrop).
  const { state: locationState } = useLocation();
  useEffect(() => {
    if (locationState?.tab) setTab(locationState.tab);
  }, [locationState]);
  useSocketEvents(useMemo(() => ({ [SocketRequests.DownloadList]: setDownloads }), []));
  useSubscription(SocketRequests.DownloadList);

  const byKind = (kind: DownloadKind) => (downloads ?? []).filter((d) => d.kind === kind);
  const count = (kind: DownloadKind) => {
    const n = byKind(kind).length;
    return n ? ` (${n})` : '';
  };
  // Only a download of the tab shown; a removed one closes the panel.
  const selected = downloads?.find((d) => d.id === selectedId && d.kind === addKind) ?? null;

  return (
    <Stack
      gap="sm"
      h={isMobile ? undefined : PAGE_HEIGHT}
      mih={isMobile ? undefined : 360}
      style={{ overflow: isMobile ? undefined : 'hidden' }}
    >
      <Group justify="space-between">
        <Title order={2}>Downloads</Title>
        <Button leftSection={<IconPlus size={16} />} onClick={() => setAdding(true)}>
          {addKind === 'Torrent' ? 'Add torrent' : 'Add download'}
        </Button>
      </Group>
      <AddDownloadDialog kind={addKind} opened={adding} onClose={() => setAdding(false)} />
      <Tabs
        value={tab}
        onChange={setTab}
        keepMounted={false}
        style={{ display: 'flex', flexDirection: 'column', flex: 1, minHeight: isMobile ? undefined : MIN_LIST_HEIGHT }}
      >
        <Tabs.List>
          <Tabs.Tab value="Torrent" leftSection={<IconMagnet size={16} />}>
            Torrent{count('Torrent')}
          </Tabs.Tab>
          <Tabs.Tab value="Direct" leftSection={<IconLink size={16} />}>
            Direct download{count('Direct')}
          </Tabs.Tab>
        </Tabs.List>
        {(['Torrent', 'Direct'] as const).map((kind) => (
          <Tabs.Panel
            key={kind}
            value={kind}
            pt="md"
            style={{ display: 'flex', flexDirection: 'column', flex: 1, minHeight: isMobile ? undefined : 0 }}
          >
            {kind === 'Torrent' && <PortWarning />}
            {downloads && (
              <DownloadTable
                downloads={byKind(kind)}
                kind={kind}
                actions={actions}
                selectedId={selectedId}
                onSelect={(id) => setSelectedId((current) => (current === id ? null : id))}
              />
            )}
          </Tabs.Panel>
        ))}
      </Tabs>
      {selected ? (
        <>
          {!isMobile && (
            <div
              role="separator"
              aria-orientation="horizontal"
              title="Drag to resize, double click to reset"
              onPointerDown={panel.startDrag}
              onDoubleClick={panel.reset}
              style={{
                height: 6,
                margin: '-4px 0',
                cursor: 'row-resize',
                touchAction: 'none',
                flexShrink: 0,
                borderTop: '1px solid var(--mantine-color-default-border)',
              }}
            />
          )}
          <div
            style={
              isMobile
                ? { height: '70dvh', flexShrink: 0 }
                : { flex: `0 1 ${panel.height}px`, minHeight: MIN_PANEL_HEIGHT }
            }
          >
            <DownloadDetailsPanel
              download={selected}
              color={progressColor(selected.progress, selected.state)}
              onPlay={(fileId) => actions.play(selected, fileId)}
            />
          </div>
        </>
      ) : (
        downloads?.some((d) => d.kind === addKind) && (
          <Text size="xs" c="dimmed" style={{ flexShrink: 0 }}>
            Select a {addKind === 'Torrent' ? 'torrent' : 'download'} to see its files, details
            {addKind === 'Torrent' ? ', peers' : ''} and pieces.
          </Text>
        )
      )}
    </Stack>
  );
};

export default DownloadsPage;
