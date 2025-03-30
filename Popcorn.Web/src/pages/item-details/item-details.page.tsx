import React, { useEffect, useState } from 'react';
import { useParams } from 'react-router-dom';
import { SocketRequests } from '../../socket/socket-requests';
import { MediaFile, MediaItem, MediaItemHistory, MediaItemRequest } from '../../model/media.contract';
import { useWebSocket } from '../../state/websocket-context';
import { Button, Flex, Table, Tabs } from '@mantine/core';
import IconFile from '@tabler/icons-react/dist/esm/icons/IconFile';
import IconHistory from '@tabler/icons-react/dist/esm/icons/IconHistory';

const ItemDetailsPage: React.FC = () => {
  const { id } = useParams();
  const { client, isClientReady } = useWebSocket();

  const [mediaItem, setMediaItem] = useState<MediaItem>();
  const [mediafiles, setMediaFiles] = useState<MediaFile[]>();
  const [mediaHistory, setMediaHistory] = useState<MediaItemHistory[]>();

  useEffect(() => {
    if (!id) return;

    client.sendEvent<MediaItemRequest>(SocketRequests.MediaItem, { id });
    client.sendEvent<MediaItemRequest>(SocketRequests.MediaItemFileList, { id });
    client.sendEvent<MediaItemRequest>(SocketRequests.MediaItemHistory, { id });
  }, [id]);

  useEffect(() => {
    client.onEvent<MediaItem>(SocketRequests.MediaItem, (msg) => {
      setMediaItem(msg);
    });
    client.onEvent<MediaFile[]>(SocketRequests.MediaItemFileList, setMediaFiles);
    client.onEvent<MediaItemHistory[]>(SocketRequests.MediaItemHistory, setMediaHistory);
  }, [isClientReady]);

  return (
    <Flex direction="column" style={{ width: '100%' }}>
      <h1>{mediaItem?.shortTitle || mediaItem?.title}</h1>
      <Tabs defaultValue="files">
        <Tabs.List grow>
          <Tabs.Tab value="files" leftSection={<IconFile size={24} />}>
            Files
          </Tabs.Tab>
          <Tabs.Tab value="history" leftSection={<IconHistory size={24} />}>
            History
          </Tabs.Tab>
        </Tabs.List>
        <Tabs.Panel value="history" mt="xs">
          <Table highlightOnHover>
            <Table.Thead>
              <Table.Tr>
                <Table.Th>File</Table.Th>
                <Table.Th>Playback time</Table.Th>
                <Table.Th>Play</Table.Th>
              </Table.Tr>
            </Table.Thead>
            <Table.Tbody>
              {mediaHistory?.map((item, index) => (
                <Table.Tr key={item.playbackTime}>
                  <Table.Td>{item.name}</Table.Td>
                  <Table.Td>{item.playbackTime}</Table.Td>
                  <Table.Td>
                    {' '}
                    <Button>Hello</Button>{' '}
                  </Table.Td>
                </Table.Tr>
              ))}
            </Table.Tbody>
          </Table>
        </Tabs.Panel>
      </Tabs>
    </Flex>
  );
};

export default ItemDetailsPage;
