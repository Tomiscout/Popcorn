import {
  ActionIcon,
  Badge,
  Button,
  Checkbox,
  Group,
  HoverCard,
  Modal,
  Paper,
  Stack,
  Text,
  Tooltip,
} from '@mantine/core';
import { IconInfoCircle, IconTrash } from '@tabler/icons-react';
import dayjs from 'dayjs';
import { useEffect, useState } from 'react';
import { ItemSource, ItemSourceKind, MediaFile, MediaItem } from '../../model/media.contract';
import { SocketRequests } from '../../socket/socket-requests';
import { useWebSocket } from '../../state/websocket-context';

const WRAP: React.CSSProperties = { overflowWrap: 'anywhere' };

const trimSlash = (path: string) => path.replace(/[\\/]+$/, '');

/** Where a source's files came from: folder, downloads (with the file source result), merged item. */
function SourceDetails({ source }: { source: ItemSource }) {
  return (
    <Stack gap={6}>
      <Text size="xs" c="dimmed" style={WRAP}>
        {source.kind === ItemSourceKind.Download ? 'Downloaded' : 'Library folder'} ·{' '}
        {dayjs(source.addedAt).format('YYYY-MM-DD')} · {source.rootPath}
      </Text>
      {source.mergedFromTitle && <Text size="xs">Merged from “{source.mergedFromTitle}”</Text>}
      {source.origins.map((o, i) => (
        <Stack key={o.downloadId ?? i} gap={0}>
          <Text size="xs" style={WRAP}>
            {o.name}
          </Text>
          {o.file && (
            <Text size="xs" c="dimmed">
              From {[o.file.pluginId, o.file.sourceId, o.file.origin].filter(Boolean).join(' / ')}
              {o.file.title && o.file.title !== o.name ? `: ${o.file.title}` : ''}
            </Text>
          )}
        </Stack>
      ))}
    </Stack>
  );
}

/** Info icon of a virtual folder row: its source's details on hover. */
export function SourceInfo({ source }: { source: ItemSource | undefined }) {
  if (!source) return null;
  return (
    <HoverCard width={420} shadow="md" openDelay={200} withinPortal>
      <HoverCard.Target>
        <IconInfoCircle size={16} style={{ flexShrink: 0, opacity: 0.6 }} onClick={(e) => e.stopPropagation()} />
      </HoverCard.Target>
      <HoverCard.Dropdown>
        <SourceDetails source={source} />
      </HoverCard.Dropdown>
    </HoverCard>
  );
}

/**
 * The item's sources (library folders, downloads, merged items) with removal (`MediaItemSourceRemove`): its files
 * and downloads leave the library, optionally the disk. The last source stays (remove the item instead).
 * `files` = the item's files (play order list) for the counts.
 */
export function SourcesModal({
  item,
  files,
  opened,
  onClose,
}: {
  item: MediaItem;
  files: MediaFile[];
  opened: boolean;
  onClose: () => void;
}) {
  const { client } = useWebSocket();
  const [removing, setRemoving] = useState<ItemSource | null>(null);
  const [deleteFiles, setDeleteFiles] = useState(false);
  const sources = item.sources ?? [];

  useEffect(() => {
    if (!opened) setRemoving(null);
    setDeleteFiles(false);
  }, [opened, removing]);

  // Files of older rows have no source: they belong to the item folder's (as on the server, MediaTree).
  const folderSource = sources.find((s) => trimSlash(s.rootPath) === trimSlash(item.folderDirectory)) ?? sources[0];
  const count = (source: ItemSource) =>
    files.filter((f) => (sources.some((s) => s.id === f.sourceId) ? f.sourceId : folderSource?.id) === source.id)
      .length;

  const remove = () => {
    if (!removing) return;
    client.sendEvent(SocketRequests.MediaItemSourceRemove, { itemId: item.id, sourceId: removing.id, deleteFiles });
    setRemoving(null);
  };

  return (
    <Modal opened={opened} onClose={onClose} title="Sources" size="lg">
      {removing ? (
        <Stack gap="md">
          <Text size="sm">
            Remove “{removing.title}” from this item? Its {count(removing)} video{count(removing) === 1 ? '' : 's'} and
            their watch history leave the library
            {removing.kind === ItemSourceKind.Download ? '; its downloads go on outside the library' : ''}.
          </Text>
          <Checkbox
            label="Also delete files from disk"
            description={`${removing.kind === ItemSourceKind.Download ? 'Its downloads, ' : ''}the files in ${removing.rootPath} the library knows and folders left empty`}
            checked={deleteFiles}
            onChange={(e) => setDeleteFiles(e.currentTarget.checked)}
          />
          <Group justify="flex-end">
            <Button variant="default" onClick={() => setRemoving(null)}>
              Back
            </Button>
            <Button color="red" leftSection={<IconTrash size={16} />} onClick={remove}>
              Remove
            </Button>
          </Group>
        </Stack>
      ) : (
        <Stack gap="sm">
          {sources.length === 0 && (
            <Text size="sm" c="dimmed">
              Loading…
            </Text>
          )}
          {sources.map((source) => (
            <Paper key={source.id} withBorder p="sm" radius="md">
              <Group justify="space-between" wrap="nowrap" align="flex-start">
                <Stack gap={4} miw={0}>
                  <Group gap="xs" wrap="nowrap">
                    <Text size="sm" fw={600} style={WRAP}>
                      {source.title}
                    </Text>
                    <Badge size="xs" variant="light" color={source.kind === ItemSourceKind.Download ? 'blue' : 'gray'}>
                      {source.kind === ItemSourceKind.Download ? 'Download' : 'Folder'}
                    </Badge>
                    <Text size="xs" c="dimmed" style={{ whiteSpace: 'nowrap' }}>
                      {count(source)} video{count(source) === 1 ? '' : 's'}
                    </Text>
                  </Group>
                  <SourceDetails source={source} />
                </Stack>
                <Tooltip
                  label={sources.length > 1 ? 'Remove from this item' : 'The only source: remove the item instead'}
                  openDelay={300}
                >
                  <ActionIcon
                    variant="subtle"
                    color="red"
                    aria-label={`Remove ${source.title}`}
                    disabled={sources.length <= 1}
                    onClick={() => setRemoving(source)}
                  >
                    <IconTrash size={16} />
                  </ActionIcon>
                </Tooltip>
              </Group>
            </Paper>
          ))}
        </Stack>
      )}
    </Modal>
  );
}
