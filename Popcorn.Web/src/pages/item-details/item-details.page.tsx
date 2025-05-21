import React, { useEffect, useState } from 'react';
import dayjs from 'dayjs';
import { useNavigate, useParams } from 'react-router-dom';
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
import { Button, Flex, Table, Tabs, Text, Pill, Breadcrumbs, Anchor } from '@mantine/core';
import { IconFile, IconHistory, IconPlayerPlayFilled, IconFolder, IconMovie, IconTrash } from '@tabler/icons-react';
import { useFileUrl } from '../../state/file-context';
import './item-details.page.css';
import { useModals } from '@mantine/modals';

const ItemDetailsPage: React.FC = () => {
  const { id } = useParams();
  const modals = useModals();
  const navigate = useNavigate();
  const { client, isClientReady } = useWebSocket();

  const [mediaItem, setMediaItem] = useState<MediaItem>();
  const [currentMediaFiles, setCurrentMediaFiles] = useState<MediaFile[]>();
  const [mediaHistory, setMediaHistory] = useState<MediaItemHistory[]>();
  const [nextFile, setNextFile] = useState<NextFileResponse>();
  const [isOpeningFolder, setIsOpeningFolder] = useState(false);
  const [breadcrumbs, setBreadcrumbs] = useState<{ id: string; title: string }[]>([]);

  const imageUrl = useFileUrl(mediaItem?.imagePath);

  useEffect(() => {
    if (mediaItem) {
      setBreadcrumbs([{ id: mediaItem.id, title: 'Root' }]);
    }
  }, [mediaItem]);

  useEffect(() => {
    if (!id) return;

    client.sendEvent<MediaItemRequest>(SocketRequests.MediaItem, { id });
    // client.sendEvent<MediaItemRequest>(SocketRequests.MediaItemFileList, { id });
    // client.sendEvent<MediaItemRequest>(SocketRequests.MediaItemHistory, { id });
    // client.sendEvent<MediaItemRequest>(SocketRequests.NextFile, { id });
  }, [id]);

  useEffect(() => {
    if (!isClientReady) return;

    client.onEvent<MediaItem>(SocketRequests.MediaItem, (msg) => {
      setMediaItem(msg);
    });
    client.onEvent<MediaFile[]>(SocketRequests.MediaItemFileList, mediaFilesReceived);
    client.onEvent<MediaItemHistory[]>(SocketRequests.MediaItemHistory, setMediaHistory);
    client.onEvent<NextFileResponse>(SocketRequests.NextFile, setNextFile);
  }, [isClientReady]);

  const fileRowClicked = (fileId: string) => {
    if (isOpeningFolder) return; //Need to also debounce file open

    const file = currentMediaFiles?.find((c) => c.id === fileId);
    if (file?.type === FileType.Folder) {
      setIsOpeningFolder(true);
      setBreadcrumbs((prev) => [...prev, { id: file.id, title: file.title }]);
      client.sendEvent(SocketRequests.FolderOpen, fileId);
    } else {
      client.sendEvent(SocketRequests.FileOpen, fileId);

      //Sets as played locally
      setCurrentMediaFiles((prev) =>
        prev?.map((mediaFile) => (mediaFile.id === fileId ? { ...mediaFile, played: true } : mediaFile))
      );
    }
  };

  const mediaFilesReceived = (newFiles: MediaFile[]) => {
    setIsOpeningFolder(false);
    setCurrentMediaFiles(newFiles);
  };

  const onBreadcrumbClick = (id: string | null, index: number) => {
    setIsOpeningFolder(true);
    setBreadcrumbs(breadcrumbs.slice(0, index + 1));
    client.sendEvent(SocketRequests.FolderOpen, id);
  };

  const removeMediaItem = () => {
    modals.openConfirmModal({
      title: 'Confirm Deletion',
      children: <Text size="sm">Are you sure you want to delete this item?</Text>,
      labels: { confirm: 'Delete', cancel: 'Cancel' },
      confirmProps: { color: 'red' },
      onConfirm: () => {
        client.sendEvent(SocketRequests.RemoveMediaItem, mediaItem?.id);
        setTimeout(() => {
          navigate('/watch');
        }, 100);
      },
    });
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
          {currentMediaFiles?.map((file) => (
            <Table.Tr key={file.id} style={{ cursor: 'pointer' }} onClick={() => fileRowClicked(file.id)}>
              <Table.Td>
                <Flex gap="xs">
                  {file.type === FileType.File ? <IconMovie /> : <IconFolder />}
                  <Text c={file.played ? 'dimmed' : ''}>{file.shortTitle || file.title}</Text>
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
          <Text fw={700} size="xl">
            {mediaItem?.shortTitle || mediaItem?.title}
          </Text>
          <Text size="md">Description</Text>
          <Flex mt="auto" gap="sm">
            <Button variant="filled" leftSection={<IconPlayerPlayFilled size={14} />}>
              Play {nextFile?.title}
            </Button>
            <Button variant="default">Setup Intro Skipping</Button>
            <Button variant="default" leftSection={<IconTrash size={14} />} onClick={removeMediaItem}>
              Remove
            </Button>
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
          <Breadcrumbs separator=">" m="sm">
            {breadcrumbs.map((crumb, idx) => (
              <Anchor key={crumb.id} component="button" size="sm" onClick={() => onBreadcrumbClick(crumb.id, idx)}>
                {crumb.title}
              </Anchor>
            ))}
          </Breadcrumbs>
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
