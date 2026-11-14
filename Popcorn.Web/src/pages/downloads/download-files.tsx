import { ActionIcon, Badge, Checkbox, Group, Select, Table, Text, Tooltip } from '@mantine/core';
import { IconChevronDown, IconChevronRight, IconFile, IconFolder, IconPlayerPlayFilled } from '@tabler/icons-react';
import React, { useMemo, useState } from 'react';
import { Download, DownloadFile, FilePriority } from '../../model/download.contract';
import { SocketRequests } from '../../socket/socket-requests';
import { useWebSocket } from '../../state/websocket-context';
import { formatBytes } from './format';

const FILE_PRIORITIES: { value: FilePriority; label: string }[] = [
  { value: 'Skip', label: "Don't download" },
  { value: 'Low', label: 'Low' },
  { value: 'Normal', label: 'Normal' },
  { value: 'High', label: 'High' },
];

type Range = [number, number];

/** Parts closer than this (fraction of the bar) are drawn as one: a fragmented file would be thousands of divs. */
const MERGE_GAP = 0.002;

function mergeRanges(ranges: Range[]): Range[] {
  const result: Range[] = [];
  for (const [start, end] of ranges) {
    const last = result[result.length - 1];
    if (last && start - last[1] <= MERGE_GAP) last[1] = Math.max(last[1], end);
    else result.push([start, end]);
  }
  return result;
}

/**
 * 0–100 % bar: the solid fill is the progress, the faint marks over the whole track are where the downloaded parts
 * of the file lie (pieces arrive out of order).
 */
export function PartsProgress({ progress, ranges, color }: { progress: number; ranges?: Range[]; color: string }) {
  const merged = useMemo(() => (ranges ? mergeRanges(ranges) : []), [ranges]);
  const fill = `var(--mantine-color-${color}-filled)`;
  return (
    <div
      style={{
        position: 'relative',
        height: 14,
        borderRadius: 3,
        overflow: 'hidden',
        background: 'var(--mantine-color-dark-4)',
      }}
    >
      {merged.map(([start, end], i) => (
        <div
          key={i}
          style={{
            position: 'absolute',
            top: 0,
            bottom: 0,
            left: `${start * 100}%`,
            width: `max(1px, ${(end - start) * 100}%)`,
            background: fill,
            opacity: 0.35,
          }}
        />
      ))}
      <div
        style={{
          position: 'absolute',
          top: 0,
          bottom: 0,
          left: 0,
          width: `${Math.min(1, progress) * 100}%`,
          background: fill,
          opacity: 0.85,
        }}
      />
      <Text
        size="10px"
        fw={600}
        c="white"
        style={{ position: 'absolute', inset: 0, textAlign: 'center', lineHeight: '14px', textShadow: '0 0 2px #000' }}
      >
        {(progress * 100).toFixed(1)}%
      </Text>
    </div>
  );
}

type FolderNode = { kind: 'folder'; name: string; path: string; children: TreeNode[]; files: DownloadFile[] };
type TreeNode = FolderNode | { kind: 'file'; name: string; path: string; file: DownloadFile };

/** Folder tree of the files' relative paths; folders first, then names in natural order. */
function buildTree(files: DownloadFile[]): TreeNode[] {
  const root: FolderNode = { kind: 'folder', name: '', path: '', children: [], files: [] };
  for (const file of files) {
    const parts = file.path.split(/[\\/]/).filter(Boolean);
    let folder = root;
    folder.files.push(file);
    parts.slice(0, -1).forEach((part, i) => {
      const path = parts.slice(0, i + 1).join('/');
      let next = folder.children.find((c): c is FolderNode => c.kind === 'folder' && c.name === part);
      if (!next) folder.children.push((next = { kind: 'folder', name: part, path, children: [], files: [] }));
      next.files.push(file);
      folder = next;
    });
    folder.children.push({ kind: 'file', name: parts[parts.length - 1] ?? file.path, path: file.path, file });
  }
  const sort = (nodes: TreeNode[]) => {
    nodes.sort((a, b) =>
      a.kind !== b.kind ? (a.kind === 'folder' ? -1 : 1) : a.name.localeCompare(b.name, undefined, { numeric: true })
    );
    nodes.forEach((n) => n.kind === 'folder' && sort(n.children));
  };
  sort(root.children);
  return root.children;
}

const INDENT = 18;

/** Files tab: folder tree with per-file progress (and where the parts are), priority and play. */
export function DownloadFiles({
  download,
  ranges,
  color,
  onPlay,
}: {
  download: Download;
  /** By file index; undefined while the details load. */
  ranges: Range[][] | undefined;
  color: string;
  onPlay: (fileId: string) => void;
}) {
  const { client } = useWebSocket();
  // Folders start collapsed.
  const [expanded, setExpanded] = useState<Set<string>>(new Set());
  const tree = useMemo(() => buildTree(download.files), [download.files]);
  const torrent = download.kind === 'Torrent';

  if (!download.files.length)
    return (
      <Text size="sm" c="dimmed" p="sm">
        {download.state === 'Metadata' ? 'Getting the file list…' : 'No files.'}
      </Text>
    );

  const setPriority = (files: DownloadFile[], priority: FilePriority) => {
    for (const f of files)
      if (f.priority !== priority)
        client.sendEvent(SocketRequests.DownloadSetFilePriority, { id: download.id, index: f.index, priority });
  };

  const toggle = (path: string) =>
    setExpanded((c) => {
      const next = new Set(c);
      if (!next.delete(path)) next.add(path);
      return next;
    });

  const rows: React.ReactNode[] = [];
  const walk = (nodes: TreeNode[], depth: number) => {
    for (const node of nodes) {
      if (node.kind === 'folder') {
        const files = node.files;
        const length = files.reduce((s, f) => s + f.length, 0);
        const wanted = files.filter((f) => f.wanted);
        const wantedLength = wanted.reduce((s, f) => s + f.length, 0);
        const done = wanted.reduce((s, f) => s + f.progress * f.length, 0);
        const priorities = new Set(files.map((f) => f.priority));
        const open = expanded.has(node.path);
        rows.push(
          <Table.Tr key={`d:${node.path}`}>
            <Table.Td>
              <Group gap={4} wrap="nowrap" pl={depth * INDENT}>
                <ActionIcon variant="subtle" color="gray" size="xs" onClick={() => toggle(node.path)}>
                  {open ? <IconChevronDown size={14} /> : <IconChevronRight size={14} />}
                </ActionIcon>
                {torrent && (
                  <Checkbox
                    size="xs"
                    checked={wanted.length === files.length}
                    indeterminate={wanted.length > 0 && wanted.length < files.length}
                    aria-label="Download this folder"
                    onChange={(e) => setPriority(files, e.currentTarget.checked ? 'Normal' : 'Skip')}
                  />
                )}
                <IconFolder size={16} style={{ flexShrink: 0 }} />
                <Text size="xs" fw={500} style={{ overflowWrap: 'anywhere' }}>
                  {node.name}
                </Text>
              </Group>
            </Table.Td>
            <Table.Td>
              <Text size="xs">{formatBytes(length)}</Text>
            </Table.Td>
            <Table.Td>
              <PartsProgress progress={wantedLength > 0 ? done / wantedLength : 0} color={color} />
            </Table.Td>
            <Table.Td>
              {torrent && (
                <Select
                  size="xs"
                  data={FILE_PRIORITIES}
                  value={priorities.size === 1 ? files[0].priority : null}
                  placeholder="Mixed"
                  allowDeselect={false}
                  onChange={(v) => v && setPriority(files, v as FilePriority)}
                  aria-label="Folder priority"
                />
              )}
            </Table.Td>
            <Table.Td />
          </Table.Tr>
        );
        if (open) walk(node.children, depth + 1);
      } else {
        const f = node.file;
        rows.push(
          <Table.Tr key={`f:${f.index}`} opacity={f.wanted ? 1 : 0.55}>
            <Table.Td>
              {/* Lines up with the folder rows' expand arrow. */}
              <Group
                gap={4}
                wrap="nowrap"
                pl={depth * INDENT + (depth > 0 || tree.some((n) => n.kind === 'folder') ? 26 : 0)}
              >
                {torrent && (
                  <Checkbox
                    size="xs"
                    checked={f.wanted}
                    aria-label="Download this file"
                    onChange={(e) => setPriority([f], e.currentTarget.checked ? 'Normal' : 'Skip')}
                  />
                )}
                <IconFile size={16} style={{ flexShrink: 0 }} />
                <Text size="xs" style={{ overflowWrap: 'anywhere' }} title={f.path}>
                  {node.name}
                </Text>
                {f.streaming && (
                  <Badge variant="filled" color="grape" size="xs">
                    Streaming
                  </Badge>
                )}
              </Group>
            </Table.Td>
            <Table.Td>
              <Text size="xs">{formatBytes(f.length)}</Text>
            </Table.Td>
            <Table.Td>
              <PartsProgress progress={f.progress} ranges={ranges?.[f.index]} color={f.wanted ? color : 'gray'} />
            </Table.Td>
            <Table.Td>
              {torrent && (
                <Select
                  size="xs"
                  data={FILE_PRIORITIES}
                  value={f.priority}
                  allowDeselect={false}
                  onChange={(v) => v && setPriority([f], v as FilePriority)}
                  aria-label="File priority"
                />
              )}
            </Table.Td>
            <Table.Td>
              {f.isVideo && f.mediaFileId && f.wanted && (
                <Tooltip label="Play (waits for the parts that are not downloaded yet)">
                  <ActionIcon variant="light" size="sm" onClick={() => onPlay(f.mediaFileId!)}>
                    <IconPlayerPlayFilled size={12} />
                  </ActionIcon>
                </Tooltip>
              )}
            </Table.Td>
          </Table.Tr>
        );
      }
    }
  };
  walk(tree, 0);

  return (
    <Table verticalSpacing={3} highlightOnHover stickyHeader style={{ tableLayout: 'fixed', minWidth: 640 }}>
      <colgroup>
        <col />
        <col style={{ width: 90 }} />
        <col style={{ width: 200 }} />
        <col style={{ width: 150 }} />
        <col style={{ width: 44 }} />
      </colgroup>
      <Table.Thead>
        <Table.Tr>
          <Table.Th>Name</Table.Th>
          <Table.Th>Size</Table.Th>
          <Table.Th>
            <Tooltip
              multiline
              w={280}
              label="The fill is how much is downloaded; the faint marks show where in the file the downloaded parts are."
            >
              <span>Progress</span>
            </Tooltip>
          </Table.Th>
          <Table.Th>{torrent ? 'Priority' : null}</Table.Th>
          <Table.Th />
        </Table.Tr>
      </Table.Thead>
      <Table.Tbody>{rows}</Table.Tbody>
    </Table>
  );
}
