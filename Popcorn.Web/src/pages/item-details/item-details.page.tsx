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
import { ActionIcon, Button, Flex, Group, Menu, Table, Tabs, Text, Pill, Breadcrumbs, Anchor } from '@mantine/core';
import {
  IconFile,
  IconHistory,
  IconPlayerPlayFilled,
  IconFolder,
  IconMovie,
  IconTrash,
  IconRefresh,
  IconChevronDown,
} from '@tabler/icons-react';
import { useFileUrl } from '../../state/file-context';
import './item-details.page.css';
import { useModals } from '@mantine/modals';
import { usePlayerStore } from '../../state/player.store';
import { isDesktopApp } from '../../utils/platform';
import { PlayerMenuItems, usePlayerOptions, WEB_PLAYER_ID } from '../../components/player-menu/player-menu';

interface FileContextMenu {
  fileId: string;
  x: number;
  y: number;
}

const ItemDetailsPage: React.FC = () => {
  const { id } = useParams();
  const modals = useModals();
  const navigate = useNavigate();
  const { client, isClientReady } = useWebSocket();
  const { setFileId: setPlayerFileId, setMediaId: setPlayerMediaId } = usePlayerStore();

  const [mediaItem, setMediaItem] = useState<MediaItem>();
  const [currentMediaFiles, setCurrentMediaFiles] = useState<MediaFile[]>();
  const [mediaHistory, setMediaHistory] = useState<MediaItemHistory[]>();
  const [nextFile, setNextFile] = useState<NextFileResponse>();
  const [isOpeningFolder, setIsOpeningFolder] = useState(false);
  const [breadcrumbs, setBreadcrumbs] = useState<{ id: string; title: string }[]>([]);
  const [contextMenu, setContextMenu] = useState<FileContextMenu | null>(null);
  const { players, defaultPlayerName } = usePlayerOptions();

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
      // No web player in the desktop app: server opens it with the first working player from settings.
      openFileWith(fileId, isDesktopApp ? null : WEB_PLAYER_ID);
    }
  };

  /** `playerId` null = server picks from the ordered player list. */
  const openFileWith = (fileId: string, playerId: string | null) => {
    if (playerId === WEB_PLAYER_ID) {
      setPlayerMediaId(id!);
      setPlayerFileId(fileId);
    } else if (playerId === null) {
      client.sendEvent(SocketRequests.FileOpen, fileId);
    } else {
      client.sendEvent(SocketRequests.FileOpenWith, { fileId, playerId });
    }

    //Sets as played locally
    setCurrentMediaFiles((prev) =>
      prev?.map((mediaFile) => (mediaFile.id === fileId ? { ...mediaFile, played: true } : mediaFile))
    );
  };

  const playNextWith = (playerId: string) => {
    if (playerId === WEB_PLAYER_ID) {
      playNextFile(); // App's NextFile handler starts the web player in the browser
    } else {
      client.sendEvent(SocketRequests.FileOpenWith, { mediaId: mediaItem?.id, playerId });
    }
  };

  const openContextMenu = (e: React.MouseEvent, file: MediaFile) => {
    if (file.type !== FileType.File) return;
    e.preventDefault();
    setContextMenu({ fileId: file.id, x: e.clientX, y: e.clientY });
  };

  function playNextFile() {
    client.sendEvent(SocketRequests.NextFile, mediaItem?.id);
  }

  function refreshFiles() {
    client.sendEvent(SocketRequests.RefreshMediaItem, mediaItem?.id);
  }

  function openFolder() {
    client.sendEvent(SocketRequests.OpenMediaItemFolder, mediaItem?.id);
  }

  function mediaFilesReceived(newFiles: MediaFile[]) {
    setIsOpeningFolder(false);
    setCurrentMediaFiles(newFiles);
  }

  function onBreadcrumbClick(id: string | null, index: number) {
    setIsOpeningFolder(true);
    setBreadcrumbs(breadcrumbs.slice(0, index + 1));
    client.sendEvent(SocketRequests.FolderOpen, id);
  }

  function removeMediaItem() {
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
  }

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
            <Table.Tr
              key={file.id}
              style={{ cursor: 'pointer' }}
              onClick={() => fileRowClicked(file.id)}
              onContextMenu={(e) => openContextMenu(e, file)}
            >
              <Table.Td>
                <Flex gap="xs">
                  {file.type === FileType.File ? <IconMovie /> : <IconFolder />}
                  <Text c={file.played ? 'dimmed' : ''}>{file.shortTitle || file.title}</Text>
                </Flex>
              </Table.Td>
              <Table.Td pl="0" pr="0">
                {file.formatTags?.map((t, index) => <Pill key={index}>{t}</Pill>)}
                {file.otherTags?.map((t, index) => <Pill key={index}>{t}</Pill>)}
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
            <Table.Tr
              key={item.playbackTime}
              style={{ cursor: 'pointer' }}
              onClick={() => fileRowClicked(item.fileId)}
              onContextMenu={(e) => {
                e.preventDefault();
                setContextMenu({ fileId: item.fileId, x: e.clientX, y: e.clientY });
              }}
            >
              <Table.Td>{item.title}</Table.Td>
              <Table.Td>{dayjs(item.playbackTime).format('YYYY-MM-DD HH:mm')}</Table.Td>
            </Table.Tr>
          ))}
        </Table.Tbody>
      </Table>
    );
  };

  return (
    <Flex direction="column" className="itemDetailsRoot" w="100%">
      <Flex m="sm" w="100%" h="256">
        <img src={imageUrl ? imageUrl : '/images/default-media.jpg'} className="rounded-image-header"></img>

        <Flex direction="column" align="flex-start" ml="sm" gap="sm">
          <Text fw={700} size="xl">
            {mediaItem?.shortTitle || mediaItem?.title}
          </Text>
          <Text size="md">Description</Text>
          <Flex mt="auto" gap="sm">
            <Group gap={0} wrap="nowrap">
              <Button
                variant="filled"
                leftSection={<IconPlayerPlayFilled size={14} />}
                onClick={playNextFile}
                style={{ borderTopRightRadius: 0, borderBottomRightRadius: 0 }}
              >
                Play {nextFile?.title}
              </Button>
              <Menu position="bottom-end" withinPortal>
                <Menu.Target>
                  <ActionIcon
                    variant="filled"
                    size={36}
                    aria-label="Play with…"
                    style={{
                      borderTopLeftRadius: 0,
                      borderBottomLeftRadius: 0,
                      borderLeft: '1px solid var(--mantine-color-body)',
                    }}
                  >
                    <IconChevronDown size={16} stroke={1.5} />
                  </ActionIcon>
                </Menu.Target>
                <Menu.Dropdown>
                  <PlayerMenuItems players={players} defaultPlayerName={defaultPlayerName} onSelect={playNextWith} />
                </Menu.Dropdown>
              </Menu>
            </Group>
            <Button variant="default">Setup Intro Skipping</Button>
            <Button variant="default" leftSection={<IconTrash size={14} />} onClick={removeMediaItem}>
              Remove
            </Button>
            <Button variant="default" leftSection={<IconRefresh size={14} />} onClick={refreshFiles}>
              Refresh files
            </Button>
            <Button variant="default" leftSection={<IconFolder size={14} />} onClick={openFolder}>
              Open folder
            </Button>
          </Flex>
        </Flex>
      </Flex>

      <Tabs defaultValue="files" variant="outline">
        <Tabs.List grow>
          <Tabs.Tab value="files" leftSection={<IconFile size={24} />}>
            Files
          </Tabs.Tab>
          <Tabs.Tab value="history" leftSection={<IconHistory size={24} />}>
            History
          </Tabs.Tab>
        </Tabs.List>

        <Tabs.Panel value="files" mt="xs" className="scrollablePanel">
          <Breadcrumbs separator=">" m="sm">
            {breadcrumbs.map((crumb, idx) => (
              <Anchor key={crumb.id} component="button" size="sm" onClick={() => onBreadcrumbClick(crumb.id, idx)}>
                {crumb.title}
              </Anchor>
            ))}
          </Breadcrumbs>
          {renderFiles()}
        </Tabs.Panel>
        <Tabs.Panel value="history" mt="xs" className="scrollablePanel">
          {renderHistory()}
        </Tabs.Panel>
      </Tabs>

      {/* Right-click menu: an invisible target pinned at the cursor anchors the dropdown. */}
      <Menu
        opened={contextMenu !== null}
        onChange={(opened) => !opened && setContextMenu(null)}
        position="bottom-start"
        offset={2}
        withinPortal
      >
        <Menu.Target>
          <div
            style={{ position: 'fixed', left: contextMenu?.x ?? 0, top: contextMenu?.y ?? 0, width: 0, height: 0 }}
          />
        </Menu.Target>
        <Menu.Dropdown onContextMenu={(e) => e.preventDefault()}>
          <PlayerMenuItems
            players={players}
            defaultPlayerName={defaultPlayerName}
            onSelect={(playerId) => contextMenu && openFileWith(contextMenu.fileId, playerId)}
          />
        </Menu.Dropdown>
      </Menu>
    </Flex>
  );
};

export default ItemDetailsPage;
