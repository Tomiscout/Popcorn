import React, { useMemo } from 'react';
import { ActionIcon, Checkbox, Group, Table, Text } from '@mantine/core';
import { IconChevronDown, IconChevronRight, IconFolder } from '@tabler/icons-react';
import { hasApplied, SegmentFileState, SegmentType } from '../../model/segments.contract';

interface FolderNode {
  /** Folder path in the item, the key of its expanded state. */
  path: string;
  /** Shown name; chains of folders holding only one folder are merged ("Show/Season 1"). */
  name: string;
  folders: FolderNode[];
  files: SegmentFileState[];
  /** Every file below, for the counts and the folder checkbox. */
  all: SegmentFileState[];
}

const INDENT = 20;

/** Folders of the files' paths in the item (`title`), in the files' order; folders before files on each level. */
const buildTree = (files: SegmentFileState[]): FolderNode => {
  const root: FolderNode = { path: '', name: '', folders: [], files: [], all: files };
  for (const file of files) {
    const dirs = file.title.split(/[\\/]/).slice(0, -1);
    let node = root;
    for (const dir of dirs) {
      const path = node.path ? `${node.path}/${dir}` : dir;
      let child = node.folders.find((f) => f.path === path);
      if (!child) {
        child = { path, name: dir, folders: [], files: [], all: [] };
        node.folders.push(child);
      }
      child.all.push(file);
      node = child;
    }
    node.files.push(file);
  }
  const merge = (node: FolderNode): FolderNode => {
    while (node.files.length === 0 && node.folders.length === 1) {
      const only = node.folders[0];
      node = { ...only, name: node.name ? `${node.name}/${only.name}` : only.name };
    }
    return { ...node, folders: node.folders.map(merge) };
  };
  // A root holding a single folder (all files in "Season 1") shows its content directly.
  const top = merge(root);
  return { ...top, name: '' };
};

interface SegmentFileTreeProps {
  files: SegmentFileState[];
  /** Expanded folder paths; folders start collapsed. */
  expanded: string[];
  onToggleExpanded: (path: string) => void;
  selected: string[];
  /** Folder checkbox (selects / deselects the ready files below); none without it. */
  onSelectFolder?: (ids: string[], select: boolean) => void;
  /** The row of a file; `indent` is the left padding of its name cell. */
  renderFile: (file: SegmentFileState, indent: number) => React.ReactNode;
}

/**
 * Table body of the segment dialog's files grouped by folder (items can hold thousands of files in season / extras
 * folders). A folder row shows how many of its files are selected and have an intro / outro; click it to expand.
 */
const SegmentFileTree: React.FC<SegmentFileTreeProps> = ({
  files,
  expanded,
  onToggleExpanded,
  selected,
  onSelectFolder,
  renderFile,
}) => {
  const tree = useMemo(() => buildTree(files), [files]);

  const folderRow = (node: FolderNode, depth: number) => {
    const open = expanded.includes(node.path);
    const ready = node.all.filter((f) => !f.downloading);
    const selectedCount = node.all.filter((f) => selected.includes(f.id)).length;
    const allSelected = ready.length > 0 && ready.every((f) => selected.includes(f.id));
    const count = (type: SegmentType) => (
      <Text size="xs" c="dimmed">
        {node.all.filter((f) => hasApplied(f, type)).length}/{node.all.length}
      </Text>
    );
    return (
      <Table.Tr key={`folder:${node.path}`} style={{ cursor: 'pointer' }} onClick={() => onToggleExpanded(node.path)}>
        <Table.Td onClick={(e) => e.stopPropagation()}>
          {onSelectFolder && (
            <Checkbox
              checked={allSelected}
              indeterminate={selectedCount > 0 && !allSelected}
              disabled={ready.length === 0}
              onChange={() =>
                onSelectFolder(
                  ready.map((f) => f.id),
                  !allSelected
                )
              }
              aria-label={`Select all files in ${node.name}`}
            />
          )}
        </Table.Td>
        <Table.Td style={{ paddingLeft: 10 + depth * INDENT }}>
          <Group gap={6} wrap="nowrap">
            <ActionIcon
              size="xs"
              variant="subtle"
              color="gray"
              aria-expanded={open}
              aria-label={`${open ? 'Collapse' : 'Expand'} ${node.name}`}
              onClick={(e) => {
                e.stopPropagation();
                onToggleExpanded(node.path);
              }}
            >
              {open ? <IconChevronDown size={14} /> : <IconChevronRight size={14} />}
            </ActionIcon>
            <IconFolder size={16} style={{ flexShrink: 0 }} />
            <Text size="sm" fw={500} truncate>
              {node.name}
            </Text>
            <Text size="xs" c="dimmed" style={{ flexShrink: 0 }}>
              {node.all.length} file{node.all.length === 1 ? '' : 's'}
              {selectedCount > 0 && ` · ${selectedCount} selected`}
            </Text>
          </Group>
        </Table.Td>
        <Table.Td>{count(SegmentType.Intro)}</Table.Td>
        <Table.Td>{count(SegmentType.Outro)}</Table.Td>
      </Table.Tr>
    );
  };

  const rows = (node: FolderNode, depth: number): React.ReactNode[] => [
    ...node.folders.flatMap((folder) => [
      folderRow(folder, depth),
      ...(expanded.includes(folder.path) ? rows(folder, depth + 1) : []),
    ]),
    // Files sit under their folder's name, past its chevron.
    ...node.files.map((f) => renderFile(f, 10 + depth * INDENT + (depth > 0 ? 28 : 0))),
  ];

  return <Table.Tbody>{rows(tree, 0)}</Table.Tbody>;
};

export default SegmentFileTree;
