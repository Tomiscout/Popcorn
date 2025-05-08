import React, { useEffect, useState } from 'react';
import dayjs from 'dayjs';
import { useParams } from 'react-router-dom';
import { SocketRequests } from '../../socket/socket-requests';
import {
  FileType,
  MediaFile,
  MediaItem,
  MediaItemHistory,
  MediaItemRequest,
  NextFileResponse,
} from '../../model/media.contract';
import { useWebSocket } from '../../state/websocket-context';
import { Button, Flex, Table, Tabs, Text as ManText, Pill } from '@mantine/core';
import IconFile from '@tabler/icons-react/dist/esm/icons/IconFile';
import IconHistory from '@tabler/icons-react/dist/esm/icons/IconHistory';
import IconPlayerPlayFilled from '@tabler/icons-react/dist/esm/icons/IconPlayerPlayFilled';
import IconFolder from '@tabler/icons-react/dist/esm/icons/IconFolder';
import IconMovie from '@tabler/icons-react/dist/esm/icons/IconMovie';
import { useFileUrl } from '../../state/file-context';
import './item-details.page.css';

const ItemDetailsPage: React.FC = () => {
  const { id } = useParams();
  const { client, isClientReady } = useWebSocket();

  const [mediaItem, setMediaItem] = useState<MediaItem>();
  const [currentMediaFiles, setCurrentMediaFiles] = useState<MediaFile[]>();
  const [mediaHistory, setMediaHistory] = useState<MediaItemHistory[]>();
  const [nextFile, setNextFile] = useState<NextFileResponse>();

  const imageUrl = useFileUrl(mediaItem?.imagePath);

  useEffect(() => {
    if (!id) return;

    client.sendEvent<MediaItemRequest>(SocketRequests.MediaItem, { id });
    // client.sendEvent<MediaItemRequest>(SocketRequests.MediaItemFileList, { id });
    // client.sendEvent<MediaItemRequest>(SocketRequests.MediaItemHistory, { id });
    // client.sendEvent<MediaItemRequest>(SocketRequests.NextFile, { id });
  }, [id]);

  useEffect(() => {
    client.onEvent<MediaItem>(SocketRequests.MediaItem, (msg) => {
      setMediaItem(msg);
    });
    client.onEvent<MediaFile[]>(SocketRequests.MediaItemFileList, setCurrentMediaFiles);
    client.onEvent<MediaItemHistory[]>(SocketRequests.MediaItemHistory, setMediaHistory);
    client.onEvent<NextFileResponse>(SocketRequests.NextFile, setNextFile);
  }, [isClientReady]);

  const fileRowClicked = (fileId: string) => {
    client.sendEvent(SocketRequests.FileOpen, fileId);
  };

  const renderFiles = () => {
    return (
      <Table highlightOnHover>
        <Table.Thead>
          <Table.Tr>
            <Table.Th>File</Table.Th>
            <Table.Th>Tags</Table.Th>
          </Table.Tr>
        </Table.Thead>
        <Table.Tbody>
          {currentMediaFiles?.map((file, index) => (
            <Table.Tr key={file.id} style={{ cursor: 'pointer' }} onClick={() => fileRowClicked(file.id)}>
              <Table.Td>
                <Flex gap="xs">
                  {file.type === FileType.File ? <IconMovie /> : <IconFolder />}
                  <ManText>{file.shortTitle || file.title}</ManText>
                </Flex>
              </Table.Td>
              <Table.Td pl="0" pr="0">
                {file.formatTags?.map((t) => <Pill>{t}</Pill>)}
                {file.otherTags?.map((t) => <Pill>{t}</Pill>)}
              </Table.Td>
            </Table.Tr>
          ))}
        </Table.Tbody>
      </Table>
    );
  };

  const renderHistory = () => {
    return (
      <Table highlightOnHover>
        <Table.Thead>
          <Table.Tr>
            <Table.Th>File</Table.Th>
            <Table.Th miw="103">Playback time</Table.Th>
          </Table.Tr>
        </Table.Thead>
        <Table.Tbody>
          {mediaHistory?.map((item) => (
            <Table.Tr key={item.playbackTime} onClick={() => fileRowClicked(item.fileId)}>
              <Table.Td>{item.title}</Table.Td>
              <Table.Td>{dayjs(item.playbackTime).format('YYYY-MM-DD HH:mm')}</Table.Td>
            </Table.Tr>
          ))}
        </Table.Tbody>
      </Table>
    );
  };

  return (
    <Flex direction="column" w="100%">
      <Flex m="sm" w="100%" h="256">
        <img src={imageUrl ? imageUrl : '/images/default-media.jpg'} className="rounded-image-header"></img>

        <Flex direction="column" align="flex-start" ml="sm" gap="sm">
          <ManText fw={700} size="xl">
            {mediaItem?.shortTitle || mediaItem?.title}
          </ManText>
          <ManText size="md">Description</ManText>
          <Flex mt="auto" gap="sm">
            <Button variant="filled" leftSection={<IconPlayerPlayFilled size={14} />}>
              Play {nextFile?.title}
            </Button>
            <Button variant="default">Setup Intro Skipping</Button>
          </Flex>
        </Flex>
      </Flex>

      <Tabs defaultValue="files">
        <Tabs.List grow>
          <Tabs.Tab value="files" leftSection={<IconFile size={24} />}>
            Files
          </Tabs.Tab>
          <Tabs.Tab value="history" leftSection={<IconHistory size={24} />}>
            History
          </Tabs.Tab>
        </Tabs.List>

        <Tabs.Panel value="files" mt="xs">
          {renderFiles()}
        </Tabs.Panel>
        <Tabs.Panel value="history" mt="xs">
          {renderHistory()}
        </Tabs.Panel>
      </Tabs>
    </Flex>
  );
};

export default ItemDetailsPage;
