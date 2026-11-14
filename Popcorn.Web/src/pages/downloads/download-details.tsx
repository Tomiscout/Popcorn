import {
  ActionIcon,
  Anchor,
  Badge,
  Box,
  CopyButton,
  Group,
  ScrollArea,
  SimpleGrid,
  Table,
  Tabs,
  Text,
  Tooltip,
} from '@mantine/core';
import {
  IconCheck,
  IconCopy,
  IconFiles,
  IconFolderOpen,
  IconGridDots,
  IconInfoCircle,
  IconUsers,
} from '@tabler/icons-react';
import React, { useEffect, useMemo, useState } from 'react';
import { Link } from 'react-router-dom';
import { Download, DownloadDetails, Peer, Tracker } from '../../model/download.contract';
import { SocketRequests } from '../../socket/socket-requests';
import { useSocketEvents } from '../../socket/useSocketEvents.hook';
import { useSubscription } from '../../socket/useSubscription.hook';
import { useWebSocket } from '../../state/websocket-context';
import { DownloadFiles } from './download-files';
import { DownloadPieces } from './download-pieces';
import { formatBytes, formatDuration, formatRate } from './format';

type PanelTab = 'files' | 'general' | 'peers' | 'pieces';
const TAB_KEY = 'popcorn.downloads.panelTab';

/** Pushed details of one download while the panel shows it (server `DownloadManager.GetDetailsAsync`). */
function useDownloadDetails(id: string) {
  const [details, setDetails] = useState<DownloadDetails | null>(null);
  useEffect(() => setDetails(null), [id]);
  useSocketEvents(
    useMemo(() => ({ [SocketRequests.DownloadDetails]: (d: DownloadDetails) => d.id === id && setDetails(d) }), [id])
  );
  useSubscription(SocketRequests.DownloadDetails, id);
  return details?.id === id ? details : null;
}

const formatDate = (iso: string | null) => (iso ? new Date(iso).toLocaleString() : '—');

function Field({ label, children }: { label: string; children: React.ReactNode }) {
  return (
    <Box style={{ minWidth: 0 }}>
      <Text size="xs" c="dimmed">
        {label}
      </Text>
      <Text size="sm" component="div" style={{ overflowWrap: 'anywhere' }}>
        {children}
      </Text>
    </Box>
  );
}

function Copyable({ value, label }: { value: string; label?: string }) {
  return (
    <Group gap={4} wrap="nowrap" align="flex-start">
      <Text size="sm" style={{ overflowWrap: 'anywhere' }} lineClamp={2} title={value}>
        {label ?? value}
      </Text>
      <CopyButton value={value}>
        {({ copied, copy }) => (
          <ActionIcon variant="subtle" color={copied ? 'teal' : 'gray'} size="xs" onClick={copy}>
            {copied ? <IconCheck size={12} /> : <IconCopy size={12} />}
          </ActionIcon>
        )}
      </CopyButton>
    </Group>
  );
}

const TRACKER_COLORS: Record<string, string> = {
  Ok: 'teal',
  Connecting: 'blue',
  Offline: 'red',
  InvalidResponse: 'orange',
};

function Trackers({ trackers }: { trackers: Tracker[] }) {
  if (!trackers.length) return null;
  return (
    <>
      <Text size="sm" fw={500} mt="md" mb={4}>
        Trackers
      </Text>
      <Table verticalSpacing={2} style={{ minWidth: 640 }}>
        <Table.Thead>
          <Table.Tr>
            <Table.Th>URL</Table.Th>
            <Table.Th w={110}>Status</Table.Th>
            <Table.Th w={70}>Seeds</Table.Th>
            <Table.Th w={70}>Leechers</Table.Th>
            <Table.Th w={110}>Announced</Table.Th>
          </Table.Tr>
        </Table.Thead>
        <Table.Tbody>
          {trackers.map((t, i) => (
            <Table.Tr key={`${t.url}-${i}`}>
              <Table.Td>
                <Text size="xs" style={{ overflowWrap: 'anywhere' }}>
                  {t.url}
                </Text>
                {t.message && (
                  <Text size="xs" c="dimmed">
                    {t.message}
                  </Text>
                )}
              </Table.Td>
              <Table.Td>
                <Badge size="xs" variant="light" color={TRACKER_COLORS[t.status] ?? 'gray'}>
                  {t.status === 'InvalidResponse' ? 'Bad response' : t.status}
                </Badge>
              </Table.Td>
              <Table.Td>
                <Text size="xs">{t.seeds ?? '—'}</Text>
              </Table.Td>
              <Table.Td>
                <Text size="xs">{t.leechers ?? '—'}</Text>
              </Table.Td>
              <Table.Td>
                <Text size="xs">
                  {t.lastAnnounceSeconds != null ? `${formatDuration(t.lastAnnounceSeconds)} ago` : 'Never'}
                </Text>
              </Table.Td>
            </Table.Tr>
          ))}
        </Table.Tbody>
      </Table>
    </>
  );
}

function General({ download: d, details }: { download: Download; details: DownloadDetails | null }) {
  const { client } = useWebSocket();
  const torrent = d.kind === 'Torrent';
  const pieces = details?.pieces;
  return (
    <Box p="sm">
      <SimpleGrid cols={{ base: 1, xs: 2, md: 4 }} spacing="sm" verticalSpacing="xs">
        <Field label="Name">{d.name}</Field>
        <Field label="Save path">
          <Group gap={4} wrap="nowrap" align="flex-start">
            <span style={{ overflowWrap: 'anywhere' }}>{d.savePath}</span>
            <Tooltip label="Open folder">
              <ActionIcon
                variant="subtle"
                color="gray"
                size="xs"
                onClick={() => client.sendEvent(SocketRequests.DownloadOpenFolder, { id: d.id })}
              >
                <IconFolderOpen size={12} />
              </ActionIcon>
            </Tooltip>
          </Group>
        </Field>
        <Field label="Library">
          {d.mediaItemId ? (
            <Anchor component={Link} to={`/watch/${d.mediaItemId}`} size="sm">
              {d.mediaItemTitle || 'Open item'}
            </Anchor>
          ) : (
            '—'
          )}
        </Field>
        <Field label="Priority">{d.streaming ? 'Streaming (above all)' : d.priority}</Field>

        <Field label="Size">
          {formatBytes(d.size)}
          {details && details.totalSize > d.size && details.totalSize >= 0 && ` of ${formatBytes(details.totalSize)}`}
        </Field>
        <Field label="Downloaded">
          {formatBytes(d.downloaded)} ({(d.progress * 100).toFixed(1)}%)
        </Field>
        <Field label="Speed">
          ↓ {formatRate(d.downloadRate)}
          {torrent && ` · ↑ ${formatRate(d.uploadRate)}`}
        </Field>
        <Field label="ETA">{d.state === 'Downloading' ? formatDuration(d.eta) : '—'}</Field>

        {torrent && (
          <>
            <Field label="Uploaded">
              {formatBytes(d.uploaded)} (ratio {d.ratio.toFixed(2)})
            </Field>
            <Field label="Connections">
              {d.peers} peers · {d.seeds} seeds
            </Field>
            <Field label="Availability">{details?.availability != null ? details.availability.toFixed(2) : '—'}</Field>
            <Field label="Seeding time">{details ? formatDuration(details.seedingSeconds) : '—'}</Field>
          </>
        )}

        <Field label="Added">{formatDate(d.addedAt)}</Field>
        <Field label="Completed">{formatDate(details?.completedAt ?? null)}</Field>
        {pieces && (
          <Field label={torrent ? 'Pieces' : 'Chunks'}>
            {pieces.count} × {formatBytes(pieces.pieceLength)}
            {details!.hashFails > 0 && ` · ${details!.hashFails} failed hash checks`}
          </Field>
        )}
        {details?.infoHash && (
          <Field label="Info hash">
            <Copyable value={details.infoHash} />
          </Field>
        )}
        {details?.source && (
          <Field label={torrent ? 'Magnet link' : 'URL'}>
            <Copyable value={details.source} />
          </Field>
        )}
        {details?.createdBy && <Field label="Created by">{details.createdBy}</Field>}
        {details?.createdAt && <Field label="Created">{formatDate(details.createdAt)}</Field>}
        {details?.private && <Field label="Private">Yes (no DHT / peer exchange)</Field>}
        {details?.comment && <Field label="Comment">{details.comment}</Field>}
        {d.error && (
          <Field label="Error">
            <Text c="red" size="sm">
              {d.error}
            </Text>
          </Field>
        )}
      </SimpleGrid>
      {details && <Trackers trackers={details.trackers} />}
    </Box>
  );
}

const FLAG_HELP =
  'D downloading from them · d we want to, they choke us · U uploading to them · u they want to, we choke them · E encrypted · I incoming · F fast extension · X extension protocol';

function Peers({ peers }: { peers: Peer[] | undefined }) {
  if (!peers) return null;
  if (!peers.length)
    return (
      <Text size="sm" c="dimmed" p="sm">
        No connected peers.
      </Text>
    );
  return (
    <Table verticalSpacing={2} highlightOnHover stickyHeader style={{ minWidth: 860, tableLayout: 'fixed' }}>
      <Table.Thead>
        <Table.Tr>
          <Table.Th w={210}>Address</Table.Th>
          <Table.Th>Client</Table.Th>
          <Table.Th w={70}>
            <Tooltip multiline w={300} label={FLAG_HELP}>
              <span>Flags</span>
            </Tooltip>
          </Table.Th>
          <Table.Th w={80}>Progress</Table.Th>
          <Table.Th w={95}>↓ Speed</Table.Th>
          <Table.Th w={95}>↑ Speed</Table.Th>
          <Table.Th w={95}>Downloaded</Table.Th>
          <Table.Th w={95}>Uploaded</Table.Th>
        </Table.Tr>
      </Table.Thead>
      <Table.Tbody>
        {peers.map((p) => (
          <Table.Tr key={p.address}>
            <Table.Td>
              <Text size="xs" style={{ overflowWrap: 'anywhere' }}>
                {p.address}
              </Text>
            </Table.Td>
            <Table.Td>
              <Text size="xs" truncate title={p.client}>
                {p.client}
              </Text>
            </Table.Td>
            <Table.Td>
              <Text size="xs" ff="monospace" title={p.encryption}>
                {p.flags}
              </Text>
            </Table.Td>
            <Table.Td>
              <Text size="xs">{p.seeder ? 'Seed' : `${(p.progress * 100).toFixed(1)}%`}</Text>
            </Table.Td>
            <Table.Td>
              <Text size="xs">{p.downloadRate ? formatRate(p.downloadRate) : ''}</Text>
            </Table.Td>
            <Table.Td>
              <Text size="xs">{p.uploadRate ? formatRate(p.uploadRate) : ''}</Text>
            </Table.Td>
            <Table.Td>
              <Text size="xs">{p.downloaded ? formatBytes(p.downloaded) : ''}</Text>
            </Table.Td>
            <Table.Td>
              <Text size="xs">{p.uploaded ? formatBytes(p.uploaded) : ''}</Text>
            </Table.Td>
          </Table.Tr>
        ))}
      </Table.Tbody>
    </Table>
  );
}

const Loading = () => (
  <Text size="sm" c="dimmed" p="sm">
    Loading…
  </Text>
);

/**
 * Bottom panel of the Downloads page for the selected download: Files (tree, priorities, where the parts are),
 * General (info + trackers), Peers (torrents) and Pieces.
 */
export function DownloadDetailsPanel({
  download,
  color,
  onPlay,
}: {
  download: Download;
  /** Progress color of the download's state. */
  color: string;
  onPlay: (fileId: string) => void;
}) {
  const details = useDownloadDetails(download.id);
  const torrent = download.kind === 'Torrent';
  const [saved, setSaved] = useState<PanelTab>(() => (localStorage.getItem(TAB_KEY) as PanelTab) || 'files');
  const tab: PanelTab = !torrent && saved === 'peers' ? 'files' : saved;
  const select = (value: string | null) => {
    if (!value) return;
    setSaved(value as PanelTab);
    localStorage.setItem(TAB_KEY, value);
  };

  return (
    <Tabs
      value={tab}
      onChange={select}
      keepMounted={false}
      style={{ display: 'flex', flexDirection: 'column', height: '100%', minHeight: 0 }}
    >
      <Tabs.List>
        <Tabs.Tab value="files" leftSection={<IconFiles size={14} />}>
          Files{download.files.length > 1 ? ` (${download.files.length})` : ''}
        </Tabs.Tab>
        <Tabs.Tab value="general" leftSection={<IconInfoCircle size={14} />}>
          General
        </Tabs.Tab>
        {torrent && (
          <Tabs.Tab value="peers" leftSection={<IconUsers size={14} />}>
            Peers{details ? ` (${details.peers.length})` : ''}
          </Tabs.Tab>
        )}
        <Tabs.Tab value="pieces" leftSection={<IconGridDots size={14} />}>
          {torrent ? 'Pieces' : 'Chunks'}
        </Tabs.Tab>
        <Text size="xs" c="dimmed" ml="auto" px="sm" style={{ alignSelf: 'center' }} truncate maw="40%">
          {download.displayName}
        </Text>
      </Tabs.List>
      <ScrollArea style={{ flex: 1, minHeight: 0 }} type="auto" offsetScrollbars>
        <Tabs.Panel value="files">
          <DownloadFiles download={download} ranges={details?.fileRanges} color={color} onPlay={onPlay} />
        </Tabs.Panel>
        <Tabs.Panel value="general">
          <General download={download} details={details} />
        </Tabs.Panel>
        {torrent && <Tabs.Panel value="peers">{details ? <Peers peers={details.peers} /> : <Loading />}</Tabs.Panel>}
        <Tabs.Panel value="pieces">
          {!details ? (
            <Loading />
          ) : details.pieces ? (
            <DownloadPieces pieces={details.pieces} files={download.files} />
          ) : (
            <Text size="sm" c="dimmed" p="sm">
              {download.state === 'Metadata' ? 'Getting metadata…' : 'No piece map (the size is not known).'}
            </Text>
          )}
        </Tabs.Panel>
      </ScrollArea>
    </Tabs>
  );
}
