import React, { useEffect, useMemo, useState } from 'react';
import {
  ActionIcon,
  Alert,
  Badge,
  Button,
  Checkbox,
  Chip,
  Group,
  Modal,
  Progress,
  ScrollArea,
  SegmentedControl,
  Stack,
  Table,
  Tabs,
  Text,
  Title,
  Tooltip,
} from '@mantine/core';
import { IconAlertCircle, IconPencil, IconTrash } from '@tabler/icons-react';
import { useWebSocket } from '../../state/websocket-context';
import { useSocketEvents } from '../../socket/useSocketEvents.hook';
import { useSubscription } from '../../socket/useSubscription.hook';
import { SocketRequests } from '../../socket/socket-requests';
import { resolveServerHost } from '../../utils/url-resolver';
import {
  hasApplied,
  SegmentFileState,
  SegmentJobKind,
  SegmentOption,
  SegmentSpan,
  SegmentsState,
  SegmentType,
} from '../../model/segments.contract';
import SegmentRangeEditor from './segment-range-editor';
import SegmentFileTree from './segment-file-tree';

interface SegmentsDialogProps {
  itemId: string;
  opened: boolean;
  onClose: () => void;
}

type Tab = 'candidates' | 'apply';

/** Most files one detection compares, same limit as `MaxDetectFiles` on the server. */
const MAX_DETECT_FILES = 5;
const TYPES = [SegmentType.Intro, SegmentType.Outro];
/** Episode ranges shown under a saved segment before "…" (all of them on hover). */
const SHOWN_RANGES = 3;

const typeName = (t: SegmentType) => (t === SegmentType.Intro ? 'Intro' : 'Outro');
const typeColor = (t: SegmentType) => (t === SegmentType.Intro ? 'teal' : 'grape');

const clock = (seconds: number) => {
  const s = Math.max(0, Math.round(seconds));
  return `${Math.floor(s / 60)}:${String(s % 60).padStart(2, '0')}`;
};

/** Intros: "0:12–1:40". Outros (negative start): "−1:30 (1:20)", i.e. starts 1:30 before the end. */
const formatSpan = ({ start, length }: SegmentSpan) =>
  start >= 0 ? `${clock(start)}–${clock(start + length)}` : `−${clock(-start)} (${clock(length)})`;

const audioUrl = (segmentId: string, version: number) =>
  `http://${resolveServerHost()}/api/segments/${segmentId}/audio?v=${version}`;

/** The item has this segment type: a saved reference or a file that has / could get it (e.g. from a chapter). */
const inUse = (state: SegmentsState, type: SegmentType) =>
  state.references.some((r) => r.type === type) || state.files.some((f) => f.segments.some((o) => o.type === type));

const missing = (f: SegmentFileState, state: SegmentsState) =>
  !f.downloading && TYPES.some((t) => inUse(state, t) && !hasApplied(f, t));

const FileLabel: React.FC<{ file: SegmentFileState }> = ({ file }) => (
  <Group gap={6} wrap="nowrap">
    <Tooltip label={file.title} openDelay={300}>
      <Text size="sm" truncate>
        {file.label}
      </Text>
    </Tooltip>
    {file.downloading && (
      <Tooltip label="Not fully downloaded yet: scans skip it until it is">
        <Badge size="xs" color="gray" variant="light" style={{ flexShrink: 0 }}>
          downloading
        </Badge>
      </Tooltip>
    )}
  </Group>
);

const AppliedTo: React.FC<{ ranges: string[] }> = ({ ranges }) => {
  if (ranges.length === 0)
    return (
      <Text size="xs" c="dimmed">
        Not applied to any file
      </Text>
    );
  const text = ranges.slice(0, SHOWN_RANGES).join(', ') + (ranges.length > SHOWN_RANGES ? ', …' : '');
  return (
    <Tooltip label={ranges.join(', ')} multiline maw={360} disabled={ranges.length <= SHOWN_RANGES}>
      <Text size="xs" c="dimmed" truncate>
        Applied to {text}
      </Text>
    </Tooltip>
  );
};

/**
 * Intro/outro skip setup for a media item, in two tabs:
 * 1. Segment candidates: pick 2 to 5 files → "Find shared audio" compares their beginnings and endings and saves the
 *    shared audio as segments (intro from the beginning, outro from the ending); change the type or delete them.
 * 2. Apply segments: "Scan all" looks for every saved segment (and intro / outro chapters) in every file and saves
 *    the best match for each type a file has none of yet. "Scan missing" / "Scan selected" look only for the ticked
 *    saved segments in the files missing a segment / the selected files; "Scan selected" replaces what the files had
 *    of the scanned types with the find. Every match is listed per file: click to add / remove one. Files where
 *    nothing was found (e.g. a season with a new opening) can be compared in the first tab.
 * The saved segments list is the same in both tabs (in Apply segments with the ticks for the scans). The pencil of a
 * saved segment opens its range editor (waveform of the file it was cut from); a new range drops it from its files.
 */
const SegmentsDialog: React.FC<SegmentsDialogProps> = ({ itemId, opened, onClose }) => {
  const { client } = useWebSocket();
  const [state, setState] = useState<SegmentsState | null>(null);
  const [tab, setTab] = useState<Tab>('candidates');
  const [selected, setSelected] = useState<string[]>([]);
  const [filter, setFilter] = useState<'all' | 'missing'>('all');
  /** Apply tab file selection (no limit, unlike the candidates one). */
  const [scanFiles, setScanFiles] = useState<string[]>([]);
  /** Saved segments unticked for "Scan missing" / "Scan selected"; new ones start ticked. */
  const [unpicked, setUnpicked] = useState<string[]>([]);
  /** Saved segment open in the range editor. */
  const [editing, setEditing] = useState<string | null>(null);
  /** Expanded folders of the files tables (both tabs). */
  const [expanded, setExpanded] = useState<string[]>([]);

  const events = useMemo(
    () => ({
      [SocketRequests.SegmentsState]: (s: SegmentsState) => {
        if (s.itemId === itemId) setState(s);
      },
    }),
    [itemId]
  );
  useSocketEvents(events);

  // The current state, then every job step pushed by the server while the dialog is open.
  useSubscription(SocketRequests.SegmentsState, itemId, opened);
  const running = state?.job != null;

  // Preselect two files to compare: the first two missing a segment, or the first two of the item.
  const missingFiles = useMemo(() => (state ? state.files.filter((f) => missing(f, state)) : []), [state]);
  useEffect(() => {
    if (!state) return;
    // Files still downloading cannot be compared or scanned.
    const ids = new Set(state.files.filter((f) => !f.downloading).map((f) => f.id));
    setSelected((prev) => {
      const kept = prev.filter((id) => ids.has(id));
      if (kept.length > 0) return kept;
      const ready = state.files.filter((f) => !f.downloading);
      return (missingFiles.length >= 2 ? missingFiles : ready).slice(0, 2).map((f) => f.id);
    });
    setScanFiles((prev) => prev.filter((id) => ids.has(id)));
  }, [state?.files]);
  useEffect(() => {
    if (!state) return;
    const ids = new Set(state.references.map((r) => r.id));
    setUnpicked((prev) => prev.filter((id) => ids.has(id)));
  }, [state?.references]);

  if (!state) {
    return (
      <Modal opened={opened} onClose={onClose} title="Intro & outro skipping" size="xl">
        <Text c="dimmed">Loading…</Text>
      </Modal>
    );
  }

  const isDownloading = (id: string) => state.files.some((f) => f.id === id && f.downloading);
  const toggleFile = (id: string) => {
    if (isDownloading(id)) return;
    setSelected((prev) => (prev.includes(id) ? prev.filter((x) => x !== id) : [...prev, id].slice(-MAX_DETECT_FILES)));
  };

  const detect = () => client.sendEvent(SocketRequests.SegmentsDetect, { itemId, fileIds: selected });
  const pickedSegments = state.references.filter((r) => !unpicked.includes(r.id)).map((r) => r.id);
  /** Nothing ticked while there are saved segments: a scan of the subset would look for nothing. */
  const noneTicked = state.references.length > 0 && pickedSegments.length === 0;
  const scanAll = () => client.sendEvent(SocketRequests.SegmentsScan, { itemId });
  /** Only the ticked saved segments; all ticked = as "Scan all" (chapters too) but only on these files. */
  const scanFilesFor = (fileIds: string[], replace: boolean) =>
    client.sendEvent(SocketRequests.SegmentsScan, {
      itemId,
      fileIds,
      segmentIds: pickedSegments.length === state.references.length ? null : pickedSegments,
      replace,
    });
  const togglePicked = (id: string) =>
    setUnpicked((prev) => (prev.includes(id) ? prev.filter((x) => x !== id) : [...prev, id]));
  const toggleScanFile = (id: string) =>
    setScanFiles((prev) => (prev.includes(id) ? prev.filter((x) => x !== id) : [...prev, id]));
  const selectScanFiles = (ids: string[], select: boolean) =>
    setScanFiles((prev) => [...prev.filter((id) => !ids.includes(id)), ...(select ? ids : [])]);
  const toggleExpanded = (path: string) =>
    setExpanded((prev) => (prev.includes(path) ? prev.filter((x) => x !== path) : [...prev, path]));
  const setType = (segmentId: string, type: SegmentType) =>
    client.sendEvent(SocketRequests.SegmentsSetType, { itemId, segmentId, type });
  const remove = (segmentId: string) => client.sendEvent(SocketRequests.SegmentsDelete, { itemId, segmentId });

  /** Adds / removes one segment of a file; saved right away. */
  const toggleOption = (f: SegmentFileState, option: SegmentOption) => {
    const segments = f.segments
      .filter((o) => (o === option ? !o.applied : o.applied))
      .map(({ segmentId, start, length }) => ({ segmentId, start, length }));
    client.sendEvent(SocketRequests.SegmentsApply, { itemId, files: { [f.id]: segments } });
  };

  const shownFiles = filter === 'missing' ? missingFiles : state.files;
  // The header checkbox of the Apply tab only (de)selects the files the filter shows.
  const shownSelectable = shownFiles.filter((f) => !f.downloading).length;
  const shownSelected = shownFiles.filter((f) => scanFiles.includes(f.id)).length;
  const allShownSelected = shownSelectable > 0 && shownSelected === shownSelectable;
  const toggleAllShown = () => {
    const shown = new Set(shownFiles.filter((f) => !f.downloading).map((f) => f.id));
    setScanFiles((prev) =>
      allShownSelected ? prev.filter((id) => !shown.has(id)) : [...prev.filter((id) => !shown.has(id)), ...shown]
    );
  };
  const introCount = state.files.filter((f) => hasApplied(f, SegmentType.Intro)).length;
  const outroCount = state.files.filter((f) => hasApplied(f, SegmentType.Outro)).length;
  const readyFiles = state.files.filter((f) => !f.downloading).length;
  const notScanned = state.files.filter((f) => !f.downloading && (!f.introScanned || !f.outroScanned)).length;

  // "Intro 1", "Outro 2": references numbered per type as listed under Saved segments.
  const referenceNames = new Map(
    state.references.map((r) => [
      r.id,
      `${typeName(r.type)} ${state.references.filter((x) => x.type === r.type).indexOf(r) + 1}`,
    ])
  );

  const candidatesHint = (() => {
    if (state.references.length === 0)
      return `Select 2 to ${MAX_DETECT_FILES} files that share the intro / ending (e.g. episodes of the same season) and find their shared audio.`;
    if (notScanned > 0)
      return 'Listen to the saved segments and fix their type or delete wrong ones, then scan in Apply segments.';
    if (missingFiles.length > 0)
      return `${missingFiles.length} file(s) have no segment. If a season changed its intro or ending, select some of them (Missing filter) and find again.`;
    return null;
  })();

  const appliedBadges = (f: SegmentFileState, type: SegmentType) =>
    f.segments
      .filter((o) => o.type === type && o.applied)
      .map((o, i) => (
        <Badge key={i} color={typeColor(type)} variant={o.fromChapter ? 'outline' : 'light'} mr={4}>
          {formatSpan(o)}
        </Badge>
      ));

  const optionChips = (f: SegmentFileState, type: SegmentType) => {
    const options = f.segments.filter((o) => o.type === type);
    if (options.length === 0) {
      if (!inUse(state, type) || f.downloading) return null;
      const scanned = type === SegmentType.Intro ? f.introScanned : f.outroScanned;
      return (
        <Badge color={scanned ? 'red' : 'gray'} variant="outline">
          {scanned ? 'not found' : 'not scanned'}
        </Badge>
      );
    }
    return (
      <Group gap={4}>
        {options.map((o, i) => {
          const source = o.fromChapter ? 'Chapter' : (referenceNames.get(o.segmentId) ?? typeName(type));
          const status = o.found ? 'found by the last scan' : 'saved earlier';
          return (
            <Tooltip key={i} label={`${source} · ${clock(o.length)} long · ${status}`}>
              <div>
                <Chip
                  size="xs"
                  color={typeColor(type)}
                  checked={o.applied}
                  disabled={running}
                  onChange={() => toggleOption(f, o)}
                >
                  {source} · {formatSpan(o)}
                </Chip>
              </div>
            </Tooltip>
          );
        })}
      </Group>
    );
  };

  /** The saved segments, in the Apply tab with a tick per segment for "Scan missing" / "Scan selected". */
  const savedSegments = (pickable: boolean) =>
    state.references.length > 0 && (
      <Stack gap="xs">
        <Title order={5}>
          Saved segments
          {pickable && (
            <Text span size="sm" c="dimmed">
              {' '}
              ticked ones are looked for by Scan missing / Scan selected
            </Text>
          )}
        </Title>
        {state.references.map((r) => (
          <Group key={r.id} gap="sm" wrap="nowrap">
            {pickable && (
              <Checkbox
                checked={!unpicked.includes(r.id)}
                disabled={running}
                onChange={() => togglePicked(r.id)}
                aria-label={`Scan for ${referenceNames.get(r.id)}`}
              />
            )}
            <Stack gap={0} style={{ flex: 1, minWidth: 0 }}>
              <Text size="sm">
                {referenceNames.get(r.id)} · {clock(r.length)}
              </Text>
              <AppliedTo ranges={r.appliedTo} />
            </Stack>
            <audio controls preload="none" src={audioUrl(r.id, r.audioVersion)} style={{ height: 32 }} />
            <Tooltip label="Edit the range (removes it from the files it is applied to)">
              <ActionIcon variant="subtle" disabled={running} onClick={() => setEditing(r.id)}>
                <IconPencil size={16} />
              </ActionIcon>
            </Tooltip>
            <Button.Group>
              {TYPES.map((t) => (
                <Button
                  key={t}
                  size="xs"
                  color={typeColor(t)}
                  variant={r.type === t ? 'filled' : 'default'}
                  disabled={running}
                  onClick={() => r.type !== t && setType(r.id, t)}
                >
                  {typeName(t)}
                </Button>
              ))}
            </Button.Group>
            <Tooltip label="Delete (removes it from all files)">
              <ActionIcon variant="subtle" color="red" disabled={running} onClick={() => remove(r.id)}>
                <IconTrash size={16} />
              </ActionIcon>
            </Tooltip>
          </Group>
        ))}
      </Stack>
    );

  const filesHeader = (
    <Group justify="space-between">
      <Title order={5}>
        Files{' '}
        <Text span size="sm" c="dimmed">
          intro {introCount}/{state.files.length} · outro {outroCount}/{state.files.length}
        </Text>
      </Title>
      <SegmentedControl
        size="xs"
        value={filter}
        onChange={(v) => setFilter(v as 'all' | 'missing')}
        data={[
          { value: 'all', label: 'All' },
          { value: 'missing', label: `Missing (${missingFiles.length})` },
        ]}
      />
    </Group>
  );

  return (
    <Modal opened={opened} onClose={onClose} title="Intro & outro skipping" size="xl">
      <Stack gap="md">
        {state.job ? (
          <Stack gap={4}>
            <Text size="sm">
              {state.job.kind === SegmentJobKind.Scan ? 'Scanning' : 'Comparing'}: {state.job.step}…
            </Text>
            <Progress value={state.job.total > 0 ? (state.job.done / state.job.total) * 100 : 0} animated striped />
          </Stack>
        ) : (
          <>
            {state.error && (
              <Alert color="red" icon={<IconAlertCircle size={16} />}>
                {state.error}
              </Alert>
            )}
            {state.message && <Alert color="blue">{state.message}</Alert>}
          </>
        )}

        <Tabs value={tab} onChange={(v) => v && setTab(v as Tab)} keepMounted={false}>
          <Tabs.List mb="md">
            <Tabs.Tab value="candidates">Segment candidates</Tabs.Tab>
            <Tabs.Tab value="apply">
              Apply segments
              {notScanned > 0 && state.references.length > 0 && (
                <Badge size="xs" ml={6} color="orange" circle>
                  {notScanned}
                </Badge>
              )}
            </Tabs.Tab>
          </Tabs.List>

          <Tabs.Panel value="candidates">
            <Stack gap="md">
              {candidatesHint && !running && (
                <Text size="sm" c="dimmed">
                  {candidatesHint}
                </Text>
              )}

              {savedSegments(false)}

              <Stack gap="xs">
                {filesHeader}
                <ScrollArea.Autosize mah={320}>
                  <Table highlightOnHover stickyHeader>
                    <Table.Thead>
                      <Table.Tr>
                        <Table.Th w={32} />
                        <Table.Th>File</Table.Th>
                        <Table.Th>Intro</Table.Th>
                        <Table.Th>Outro</Table.Th>
                      </Table.Tr>
                    </Table.Thead>
                    <SegmentFileTree
                      files={shownFiles}
                      expanded={expanded}
                      onToggleExpanded={toggleExpanded}
                      selected={selected}
                      renderFile={(f, indent) => (
                        <Table.Tr
                          key={f.id}
                          style={{ cursor: f.downloading ? 'default' : 'pointer' }}
                          onClick={() => toggleFile(f.id)}
                        >
                          <Table.Td>
                            <Checkbox
                              checked={selected.includes(f.id)}
                              disabled={f.downloading}
                              onChange={() => {}}
                              tabIndex={-1}
                            />
                          </Table.Td>
                          <Table.Td style={{ paddingLeft: indent }}>
                            <FileLabel file={f} />
                          </Table.Td>
                          <Table.Td>{appliedBadges(f, SegmentType.Intro)}</Table.Td>
                          <Table.Td>{appliedBadges(f, SegmentType.Outro)}</Table.Td>
                        </Table.Tr>
                      )}
                    />
                  </Table>
                </ScrollArea.Autosize>
              </Stack>

              <Group justify="flex-end">
                <Button variant="default" disabled={running || selected.length < 2} onClick={detect}>
                  Find shared audio in selected ({selected.length}/{MAX_DETECT_FILES})
                </Button>
                <Button disabled={running} onClick={() => setTab('apply')}>
                  Apply segments →
                </Button>
              </Group>
            </Stack>
          </Tabs.Panel>

          <Tabs.Panel value="apply">
            <Stack gap="md">
              {!running && (
                <Text size="sm" c="dimmed">
                  {state.references.length === 0
                    ? 'No saved segments yet: a scan only picks up intro / outro chapter markers of the files.'
                    : notScanned > 0
                      ? `Scan to find the saved segments in the files (${notScanned} not scanned with the current ones). The best match is saved for every file that has none yet.`
                      : 'Click a segment to add it to or remove it from a file. Scan selected puts the ticked segments on the selected files in place of what they had. Hover a file for its full name.'}
                </Text>
              )}

              {savedSegments(true)}

              <Stack gap="xs">
                {filesHeader}
                <ScrollArea.Autosize mah={420}>
                  <Table stickyHeader>
                    <Table.Thead>
                      <Table.Tr>
                        <Table.Th w={32}>
                          <Checkbox
                            checked={allShownSelected}
                            indeterminate={shownSelected > 0 && !allShownSelected}
                            disabled={shownSelectable === 0}
                            onChange={toggleAllShown}
                            aria-label="Select all shown files"
                          />
                        </Table.Th>
                        <Table.Th>File</Table.Th>
                        <Table.Th>Intro</Table.Th>
                        <Table.Th>Outro</Table.Th>
                      </Table.Tr>
                    </Table.Thead>
                    <SegmentFileTree
                      files={shownFiles}
                      expanded={expanded}
                      onToggleExpanded={toggleExpanded}
                      selected={scanFiles}
                      onSelectFolder={selectScanFiles}
                      renderFile={(f, indent) => (
                        <Table.Tr key={f.id}>
                          <Table.Td>
                            <Checkbox
                              checked={scanFiles.includes(f.id)}
                              disabled={f.downloading}
                              onChange={() => toggleScanFile(f.id)}
                            />
                          </Table.Td>
                          <Table.Td maw={200} style={{ paddingLeft: indent }}>
                            <FileLabel file={f} />
                          </Table.Td>
                          <Table.Td>{optionChips(f, SegmentType.Intro)}</Table.Td>
                          <Table.Td>{optionChips(f, SegmentType.Outro)}</Table.Td>
                        </Table.Tr>
                      )}
                    />
                  </Table>
                </ScrollArea.Autosize>
              </Stack>

              <Group justify="flex-end">
                <Button
                  variant="default"
                  disabled={running || noneTicked || scanFiles.length === 0}
                  onClick={() => scanFilesFor(scanFiles, true)}
                >
                  Scan selected ({scanFiles.length})
                </Button>
                <Button
                  variant="default"
                  disabled={running || noneTicked || missingFiles.length === 0}
                  onClick={() =>
                    scanFilesFor(
                      missingFiles.map((f) => f.id),
                      false
                    )
                  }
                >
                  Scan missing ({missingFiles.length})
                </Button>
                <Button disabled={running} onClick={scanAll}>
                  Scan all ({readyFiles})
                </Button>
              </Group>
            </Stack>
          </Tabs.Panel>
        </Tabs>
      </Stack>
      <SegmentRangeEditor
        itemId={itemId}
        reference={(editing && !running && state.references.find((r) => r.id === editing)) || null}
        name={(editing && referenceNames.get(editing)) || ''}
        onClose={() => setEditing(null)}
      />
    </Modal>
  );
};

export default SegmentsDialog;
