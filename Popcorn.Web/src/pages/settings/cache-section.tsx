import React, { useEffect, useMemo, useState } from 'react';
import { Link } from 'react-router-dom';
import {
  ActionIcon,
  Anchor,
  Badge,
  Button,
  Code,
  Group,
  NumberInput,
  Paper,
  SegmentedControl,
  SimpleGrid,
  Skeleton,
  Stack,
  Table,
  Text,
  Title,
  Tooltip,
} from '@mantine/core';
import { modals } from '@mantine/modals';
import { notifications } from '@mantine/notifications';
import { IconTrash } from '@tabler/icons-react';
import { SocketRequests } from '../../socket/socket-requests';
import { useWebSocket } from '../../state/websocket-context';
import { CacheClearResult, CacheEntry, CacheKind, CacheOverview, cacheKindLabels } from '../../model/cache.contract';
import { formatBytes } from '../downloads/format';
import { Settings } from '../../model/settings.contract';

const kindColors: Record<CacheKind, string> = {
  Thumbnails: 'grape',
  Remux: 'blue',
  Audio: 'teal',
  Subtitles: 'orange',
};

const fileName = (path: string) => path.split(/[\\/]/).pop() ?? path;

const EntryName: React.FC<{ entry: CacheEntry }> = ({ entry }) => {
  if (!entry.fileId)
    return (
      <Text size="sm" c="dimmed">
        Unknown file
      </Text>
    );
  if (!entry.filePath)
    return (
      <Tooltip label={entry.fileId}>
        <Text size="sm" c="dimmed">
          Not in the library anymore
        </Text>
      </Tooltip>
    );
  return (
    <Stack gap={0}>
      {entry.itemId && (
        <Anchor component={Link} to={`/watch/${entry.itemId}`} size="sm" fw={500}>
          {entry.itemTitle ?? 'Media item'}
        </Anchor>
      )}
      <Tooltip label={entry.filePath} openDelay={400} multiline maw={600}>
        <Text size="xs" c="dimmed" lineClamp={1} style={{ wordBreak: 'break-all' }}>
          {fileName(entry.filePath)}
        </Text>
      </Tooltip>
    </Stack>
  );
};

const LogsStorage: React.FC<{
  logs: CacheOverview['logs'];
  settings: Settings;
  onChange: (settings: Settings) => void;
  onClear: () => void;
  busy: boolean;
}> = ({ logs, settings, onChange, onClear, busy }) => {
  const saved = settings.logRetentionDays ?? 7;
  // Saved on blur / Enter, not per keystroke: lowering it deletes the older files at once.
  const [days, setDays] = useState<number | string>(saved);
  useEffect(() => setDays(saved), [saved]);

  const save = () => {
    const value = typeof days === 'number' ? days : parseInt(days, 10);
    if (!Number.isFinite(value) || value < 1) return setDays(saved);
    if (value !== saved) onChange({ ...settings, logRetentionDays: value });
  };

  const confirmClear = () =>
    modals.openConfirmModal({
      title: 'Clear logs',
      children: (
        <Text size="sm">
          Delete the log files of all Popcorn apps except today&apos;s? Today&apos;s stay because the running apps are
          writing to them.
        </Text>
      ),
      labels: { confirm: 'Clear', cancel: 'Cancel' },
      confirmProps: { color: 'red' },
      onConfirm: onClear,
    });

  return (
    <Stack gap="xs" mt="md">
      <Group justify="space-between" align="center">
        <Title order={3}>Logs</Title>
        <Button
          color="red"
          variant="light"
          leftSection={<IconTrash size={16} />}
          onClick={confirmClear}
          disabled={busy || logs.files === 0}
        >
          Clear logs
        </Button>
      </Group>
      <Text size="sm" c="dimmed">
        {formatBytes(logs.bytes)} in {logs.files.toLocaleString()} files in <Code>{logs.folder}</Code>. Largest file:{' '}
        {formatBytes(logs.largestFileBytes)}. A log file growing past {formatBytes(logs.maxFileBytes)} is emptied and
        starts again with its last 10 entries.
      </Text>
      <NumberInput
        label="Keep logs for"
        description="Days, today included. Older log files of all Popcorn apps are deleted."
        suffix={days === 1 ? ' day' : ' days'}
        min={1}
        max={3650}
        allowDecimal={false}
        allowNegative={false}
        clampBehavior="strict"
        value={days}
        onChange={setDays}
        onBlur={save}
        onKeyDown={(e) => e.key === 'Enter' && save()}
        w={220}
      />
    </Stack>
  );
};

/**
 * Settings → Storage: what the caches in the data folder hold (seek bar thumbnails of the web and native players,
 * the web player's remuxes, converted audio and subtitles), in total per kind and per media file; and the logs,
 * with how long they are kept.
 */
const CacheSection: React.FC<{ settings: Settings; onChange: (settings: Settings) => void }> = ({
  settings,
  onChange,
}) => {
  const { client, isClientReady } = useWebSocket();
  const [overview, setOverview] = useState<CacheOverview>();
  const [loading, setLoading] = useState(true);
  const [filter, setFilter] = useState<'All' | CacheKind>('All');

  const refresh = () => {
    setLoading(true);
    client.sendEvent(SocketRequests.CacheList);
  };

  // The server answers with CacheClear, then the new CacheList.
  const clear = (kind: CacheKind | null, fileId: string | null) => {
    setLoading(true);
    client.sendEvent(SocketRequests.CacheClear, { kind, fileId });
  };

  const confirmClear = () => {
    const kind = filter === 'All' ? null : filter;
    const what = kind ? cacheKindLabels[kind].toLowerCase() : 'all cached files';
    const bytes = kind
      ? (overview?.kinds.find((k) => k.kind === kind)?.bytes ?? 0)
      : (overview?.kinds.reduce((sum, k) => sum + k.bytes, 0) ?? 0);
    modals.openConfirmModal({
      title: 'Clear cache',
      children: (
        <Text size="sm">
          Delete {what} ({formatBytes(bytes)})? They are made again when needed: thumbnails when you seek, remuxes and
          converted audio the next time the web player plays the file. Files still being written are kept.
        </Text>
      ),
      labels: { confirm: 'Clear', cancel: 'Cancel' },
      confirmProps: { color: 'red' },
      onConfirm: () => clear(kind, null),
    });
  };

  useEffect(() => {
    if (!isClientReady) return;
    const unsub = client.onEvent<CacheOverview>(SocketRequests.CacheList, (answer) => {
      setOverview(answer);
      setLoading(false);
    });
    const notifyCleared = (title: string, kept: string) => (r: CacheClearResult) =>
      notifications.show({
        title,
        message:
          `Deleted ${r.files.toLocaleString()} ${r.files === 1 ? 'file' : 'files'} (${formatBytes(r.bytes)}).` +
          (r.skipped > 0 ? ` ${r.skipped.toLocaleString()} ${kept}.` : ''),
      });
    const unsubClear = client.onEvent<CacheClearResult>(
      SocketRequests.CacheClear,
      notifyCleared('Cache cleared', 'still in use were kept')
    );
    const unsubClearLogs = client.onEvent<CacheClearResult>(
      SocketRequests.CacheClearLogs,
      notifyCleared('Logs cleared', 'could not be deleted')
    );
    refresh();
    return () => {
      unsub();
      unsubClear();
      unsubClearLogs();
    };
  }, [isClientReady]);

  const entries = useMemo(
    () => overview?.entries.filter((e) => filter === 'All' || e.kind === filter) ?? [],
    [overview, filter]
  );

  if (!overview)
    return (
      <Stack maw={960}>
        <Skeleton height={80} />
        <Skeleton height={240} />
      </Stack>
    );

  const total = overview.kinds.reduce((sum, k) => sum + k.bytes, 0);
  const totalFiles = overview.kinds.reduce((sum, k) => sum + k.files, 0);

  return (
    <Stack maw={960} gap="md">
      <Group justify="space-between" align="flex-end">
        <Stack gap={2}>
          <Title order={3}>Cache</Title>
          <Text size="sm" c="dimmed">
            {formatBytes(total)} in {totalFiles.toLocaleString()} files. On start the server deletes cached files older
            than {overview.maxAgeDays} {overview.maxAgeDays === 1 ? 'day' : 'days'} and keeps the stream cache under{' '}
            {formatBytes(overview.streamCacheLimitBytes)}.
          </Text>
        </Stack>
        <Group gap="xs">
          <Button variant="default" onClick={refresh} loading={loading}>
            Refresh
          </Button>
          <Button
            color="red"
            variant="light"
            leftSection={<IconTrash size={16} />}
            onClick={confirmClear}
            disabled={loading || entries.length === 0}
          >
            {filter === 'All' ? 'Clear all' : `Clear ${cacheKindLabels[filter].toLowerCase()}`}
          </Button>
        </Group>
      </Group>

      <SimpleGrid cols={{ base: 2, sm: 4 }}>
        {overview.kinds.map((k) => (
          <Tooltip
            key={k.kind}
            label={k.folders.map((f) => (
              <div key={f}>{f}</div>
            ))}
            openDelay={300}
          >
            <Paper
              withBorder
              p="sm"
              style={{ cursor: 'pointer' }}
              bg={filter === k.kind ? 'var(--mantine-color-default-hover)' : undefined}
              onClick={() => setFilter(filter === k.kind ? 'All' : k.kind)}
            >
              <Text size="xs" c={kindColors[k.kind]} fw={600} tt="uppercase">
                {cacheKindLabels[k.kind]}
              </Text>
              <Text size="lg" fw={600}>
                {formatBytes(k.bytes)}
              </Text>
              <Text size="xs" c="dimmed">
                {k.files.toLocaleString()} {k.files === 1 ? 'file' : 'files'}
              </Text>
            </Paper>
          </Tooltip>
        ))}
      </SimpleGrid>

      <SegmentedControl
        value={filter}
        onChange={(v) => setFilter(v as 'All' | CacheKind)}
        data={[
          { value: 'All', label: 'All' },
          ...overview.kinds.map((k) => ({ value: k.kind, label: cacheKindLabels[k.kind] })),
        ]}
        style={{ alignSelf: 'flex-start' }}
      />

      {entries.length === 0 ? (
        <Text size="sm" c="dimmed">
          Nothing cached.
        </Text>
      ) : (
        <Table.ScrollContainer minWidth={640}>
          <Table striped highlightOnHover verticalSpacing="xs">
            <Table.Thead>
              <Table.Tr>
                <Table.Th>Media file</Table.Th>
                <Table.Th>Type</Table.Th>
                <Table.Th ta="right">Files</Table.Th>
                <Table.Th ta="right">Size</Table.Th>
                <Table.Th>Last written</Table.Th>
                <Table.Th />
              </Table.Tr>
            </Table.Thead>
            <Table.Tbody>
              {entries.map((e) => (
                <Table.Tr key={`${e.kind}-${e.fileId ?? 'unknown'}`}>
                  <Table.Td maw={420}>
                    <EntryName entry={e} />
                  </Table.Td>
                  <Table.Td>
                    <Group gap={4} wrap="nowrap">
                      <Badge variant="light" color={kindColors[e.kind]} size="sm">
                        {cacheKindLabels[e.kind]}
                      </Badge>
                      {e.inProgress && (
                        <Badge variant="outline" color="yellow" size="sm">
                          Writing
                        </Badge>
                      )}
                    </Group>
                  </Table.Td>
                  <Table.Td ta="right">{e.files.toLocaleString()}</Table.Td>
                  <Table.Td ta="right" style={{ whiteSpace: 'nowrap' }}>
                    {formatBytes(e.bytes)}
                  </Table.Td>
                  <Table.Td style={{ whiteSpace: 'nowrap' }}>
                    <Text size="sm">{new Date(e.lastWrite).toLocaleString()}</Text>
                  </Table.Td>
                  <Table.Td>
                    {e.fileId && (
                      <Tooltip label="Delete these files">
                        <ActionIcon
                          variant="subtle"
                          color="red"
                          aria-label="Delete these files"
                          disabled={loading}
                          onClick={() => clear(e.kind, e.fileId ?? null)}
                        >
                          <IconTrash size={16} />
                        </ActionIcon>
                      </Tooltip>
                    )}
                  </Table.Td>
                </Table.Tr>
              ))}
            </Table.Tbody>
          </Table>
        </Table.ScrollContainer>
      )}

      <Text size="xs" c="dimmed">
        Everything is in the data folder: <Code>ThumbnailsCache</Code> (seek bar thumbnails of the web and native
        players), <Code>StreamCache</Code> (remuxes, audio, subtitles).
      </Text>

      <LogsStorage
        logs={overview.logs}
        settings={settings}
        onChange={(updated) => {
          onChange(updated);
          refresh(); // after the settings on the socket: the server has deleted the older files by then
        }}
        onClear={() => {
          setLoading(true);
          client.sendEvent(SocketRequests.CacheClearLogs); // answered with CacheClearLogs, then CacheList
        }}
        busy={loading}
      />
    </Stack>
  );
};

export default CacheSection;
