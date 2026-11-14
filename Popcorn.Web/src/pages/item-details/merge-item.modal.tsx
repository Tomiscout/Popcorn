import { Button, Group, Modal, Stack, Text } from '@mantine/core';
import { useEffect, useState } from 'react';
import { ItemSelect, useItemMatches } from '../../components/item-picker/item-picker';
import { MediaItem } from '../../model/media.contract';
import { SocketRequests } from '../../socket/socket-requests';
import { useWebSocket } from '../../state/websocket-context';

/**
 * Merges another library item into this one (`MediaItemMerge`): each of its sources becomes a folder of this item,
 * its downloads, history and intro/outro references move here and it leaves the library. Files stay on disk.
 * The item most like this one is preselected.
 */
export function MergeItemModal({ item, opened, onClose }: { item: MediaItem; opened: boolean; onClose: () => void }) {
  const { client } = useWebSocket();
  const matches = useItemMatches(item.shortTitle || item.title, opened, item.id);
  const [otherId, setOtherId] = useState<string | null>(null);

  useEffect(() => {
    if (!opened) setOtherId(null);
    else if (matches && otherId === null && matches[0]?.score >= 0.5) setOtherId(matches[0].id);
    // preselect once per ranking
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [opened, matches]);

  const other = matches?.find((m) => m.id === otherId);
  const merge = () => {
    if (!otherId) return;
    client.sendEvent(SocketRequests.MediaItemMerge, { targetId: item.id, otherId });
    onClose();
  };

  return (
    <Modal opened={opened} onClose={onClose} title={`Merge into “${item.shortTitle || item.title}”`} size="lg">
      <Stack gap="md">
        <ItemSelect matches={matches} value={otherId} onChange={setOtherId} label="Item to merge into this one" />
        {other && (
          <Text size="sm" c="dimmed">
            The files of “{other.title}” become a folder of this item (they stay where they are on disk), its downloads,
            watch history and intro/outro references move here and “{other.title}” leaves the library.
          </Text>
        )}
        <Group justify="flex-end">
          <Button variant="default" onClick={onClose}>
            Cancel
          </Button>
          <Button onClick={merge} disabled={!otherId}>
            Merge
          </Button>
        </Group>
      </Stack>
    </Modal>
  );
}
