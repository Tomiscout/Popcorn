import { useState } from 'react';
import { Button, Divider, Group, Modal, Stack } from '@mantine/core';
import { MediaImagePicker } from '../../components/image-picker/media-image-picker';
import { MediaImageKind, MediaItem, SetMediaItemImageRequest } from '../../model/media.contract';
import { useWebSocket } from '../../state/websocket-context';
import { SocketRequests } from '../../socket/socket-requests';

interface EditImagesModalProps {
  item: MediaItem;
  opened: boolean;
  onClose(): void;
  /** Object URLs of the stored images (null = none). */
  posterUrl: string | null;
  backdropUrl: string | null;
}

export function EditImagesModal({ item, opened, onClose, posterUrl, backdropUrl }: EditImagesModalProps) {
  return (
    <Modal opened={opened} onClose={onClose} size="xl" centered title="Edit pictures">
      {opened && <EditImagesForm item={item} onClose={onClose} posterUrl={posterUrl} backdropUrl={backdropUrl} />}
    </Modal>
  );
}

/** undefined = unchanged, null = remove, string = new image (http(s) or data: URL). */
type Change = string | null | undefined;

function EditImagesForm({ item, onClose, posterUrl, backdropUrl }: Omit<EditImagesModalProps, 'opened'>) {
  const { client } = useWebSocket();
  const [poster, setPoster] = useState<Change>(undefined);
  const [backdrop, setBackdrop] = useState<Change>(undefined);
  const name = item.title; // the full name: the server parses title + year from it

  const save = () => {
    const send = (kind: MediaImageKind, url: Change) => {
      if (url === undefined) return;
      client.sendEvent<SetMediaItemImageRequest>(SocketRequests.SetMediaItemImage, { id: item.id, kind, url });
    };
    send('Poster', poster);
    send('Backdrop', backdrop);
    onClose();
  };

  return (
    <Stack gap="md">
      <MediaImagePicker
        kind="Poster"
        name={name}
        itemId={item.id}
        value={poster === undefined ? posterUrl : poster}
        onChange={setPoster}
      />
      <Divider />
      <MediaImagePicker
        kind="Backdrop"
        name={name}
        itemId={item.id}
        value={backdrop === undefined ? backdropUrl : backdrop}
        onChange={setBackdrop}
      />
      <Group justify="flex-end">
        <Button variant="default" onClick={onClose}>
          Cancel
        </Button>
        <Button onClick={save} disabled={poster === undefined && backdrop === undefined}>
          Save
        </Button>
      </Group>
    </Stack>
  );
}
