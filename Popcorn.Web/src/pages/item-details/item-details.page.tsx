import React, { useEffect, useState } from 'react';
import dayjs from 'dayjs';
import { useNavigate, useParams, useSearchParams } from 'react-router-dom';
import { SocketRequests } from '../../socket/socket-requests';
import {
  FileType,
  MediaFile,
  MediaItem,
  MediaItemHistory,
  MediaItemRequest,
  ContinueWatching,
  RemoveMediaItemRequest,
} from '../../model/media.contract';
import { ContinueProgress, MarqueeText } from '../../components/continue-watching/continue-watching';
import { useWebSocket } from '../../state/websocket-context';
import {
  ActionIcon,
  Button,
  Flex,
  Group,
  Menu,
  Table,
  Tabs,
  Text,
  Pill,
  Breadcrumbs,
  Anchor,
  Tooltip,
  Checkbox,
  Stack,
} from '@mantine/core';
import {
  IconFile,
  IconHistory,
  IconPlayerPlayFilled,
  IconFolder,
  IconMovie,
  IconTrash,
  IconRefresh,
  IconChevronDown,
  IconPlayerSkipForward,
  IconPhotoEdit,
  IconWorldDownload,
} from '@tabler/icons-react';
import { useFileUrl } from '../../state/file-context';
import './item-details.page.css';
import { useModals } from '@mantine/modals';
import { usePlayerStore } from '../../state/player.store';
import { isDesktopApp } from '../../utils/platform';
import { PlayerMenuItems, usePlayerOptions, WEB_PLAYER_ID } from '../../components/player-menu/player-menu';
import SegmentsDialog from './segments-dialog';
import { EditImagesModal } from './edit-images.modal';
import { ItemFileSources } from './item-file-sources';

interface FileContextMenu {
  fileId: string;
  x: number;
  y: number;
}

const ItemDetailsPage: React.FC = () => {
  const { id } = useParams();
  // Opened from a player's missing episode warning: `?tab=sources&q=<query>`.
  const [searchParams] = useSearchParams();
  const sourceQuery = searchParams.get('q') ?? undefined;
  const [tab, setTab] = useState<string | null>(searchParams.get('tab') ?? 'files');
  useEffect(() => {
    const requested = searchParams.get('tab');
    if (requested) setTab(requested);
  }, [searchParams]);
  const modals = useModals();
  const navigate = useNavigate();
  const { client, isClientReady } = useWebSocket();
  const { setFileId: setPlayerFileId, setMediaId: setPlayerMediaId } = usePlayerStore();

  const [mediaItem, setMediaItem] = useState<MediaItem>();
  const [currentMediaFiles, setCurrentMediaFiles] = useState<MediaFile[]>();
  const [mediaHistory, setMediaHistory] = useState<MediaItemHistory[]>();
  /** What Play opens (same pick as the home page card); null = nothing to play. */
  const [continueWatching, setContinueWatching] = useState<ContinueWatching | null>(null);
  const [isOpeningFolder, setIsOpeningFolder] = useState(false);
  const [breadcrumbs, setBreadcrumbs] = useState<{ id: string; title: string }[]>([]);
  const [contextMenu, setContextMenu] = useState<FileContextMenu | null>(null);
  const [segmentsOpened, setSegmentsOpened] = useState(false);
  const { players, defaultPlayerName } = usePlayerOptions();

  const imageUrl = useFileUrl(mediaItem?.imagePath);
  const backdropUrl = useFileUrl(mediaItem?.backdropPath);
  const [imagesOpened, setImagesOpened] = useState(false);

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
    client.onEvent<ContinueWatching | null>(SocketRequests.MediaItemContinue, setContinueWatching);
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
    if (continueWatching) {
      openFileWith(continueWatching.fileId, playerId);
    } else if (playerId === WEB_PLAYER_ID) {
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
    if (continueWatching) {
      openFileWith(continueWatching.fileId, isDesktopApp ? null : WEB_PLAYER_ID);
      return;
    }
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
    if (!mediaItem) return;
    let deleteFiles = false;
    modals.openConfirmModal({
      title: 'Confirm Deletion',
      children: (
        <Stack gap="sm">
          <Text size="sm">Are you sure you want to delete this item?</Text>
          <Checkbox
            label="Also delete files from disk"
            description={`Its downloads, the library files in ${mediaItem.folderDirectory} and folders left empty`}
            onChange={(e) => (deleteFiles = e.currentTarget.checked)}
          />
        </Stack>
      ),
      labels: { confirm: 'Delete', cancel: 'Cancel' },
      confirmProps: { color: 'red' },
      onConfirm: () => {
        const request: RemoveMediaItemRequest = { id: mediaItem.id, deleteFiles };
        client.sendEvent(SocketRequests.RemoveMediaItem, request);
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
      <Flex p="sm" w="100%" h={256 + 24} className="item-header">
        {backdropUrl && <img key={backdropUrl} src={backdropUrl} alt="" className="item-backdrop" />}
        {backdropUrl && <div className="item-backdrop-shade" />}
        <Group gap="xs" wrap="nowrap" className="item-actions">
          <Button
            variant="default"
            leftSection={<IconPlayerSkipForward size={14} />}
            onClick={() => setSegmentsOpened(true)}
          >
            Intro Skipping
          </Button>
          <Button variant="default" leftSection={<IconFolder size={14} />} onClick={openFolder}>
            Open folder
          </Button>
          <Tooltip label="Remove from library" openDelay={300}>
            <ActionIcon
              variant="subtle"
              color="red"
              size={36}
              aria-label="Remove from library"
              onClick={removeMediaItem}
            >
              <IconTrash size={20} />
            </ActionIcon>
          </Tooltip>
        </Group>
        <Tooltip label="Edit pictures" openDelay={300}>
          <button type="button" className="item-poster-button" onClick={() => setImagesOpened(true)}>
            <img src={imageUrl ? imageUrl : '/images/default-media.jpg'} className="rounded-image-header"></img>
            <span className="item-poster-edit">
              <IconPhotoEdit size={28} />
            </span>
          </button>
        </Tooltip>

        <Flex direction="column" align="flex-start" ml="sm" gap="sm" className="item-header-content">
          <Text fw={700} size="xl" className="item-title">
            {mediaItem?.shortTitle || mediaItem?.title}
          </Text>
          {mediaItem?.description && (
            <Text size="sm" className="item-description">
              {mediaItem.description}
            </Text>
          )}
          <Flex direction="column" mt="auto" gap="sm" className="item-play">
            <Group gap={0} wrap="nowrap">
              <Button
                variant="filled"
                leftSection={<IconPlayerPlayFilled size={14} />}
                onClick={playNextFile}
                style={{ borderTopRightRadius: 0, borderBottomRightRadius: 0 }}
              >
                Play
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
            {continueWatching && (continueWatching.label || continueWatching.position != null) && (
              <div className="item-continue">
                <Text size="sm">
                  {continueWatching.position != null ? 'Continue' : 'Next'}
                  {continueWatching.label && (
                    <>
                      : <b>{continueWatching.label}</b>
                    </>
                  )}
                </Text>
                {continueWatching.episodeTitle && <MarqueeText text={`“${continueWatching.episodeTitle}”`} />}
                {continueWatching.position != null && continueWatching.duration ? (
                  <ContinueProgress position={continueWatching.position} duration={continueWatching.duration} />
                ) : null}
              </div>
            )}
          </Flex>
        </Flex>
      </Flex>

      <Tabs value={tab} onChange={setTab} variant="outline">
        <Tabs.List grow>
          <Tabs.Tab value="files" leftSection={<IconFile size={24} />}>
            Files
          </Tabs.Tab>
          <Tabs.Tab value="history" leftSection={<IconHistory size={24} />}>
            History
          </Tabs.Tab>
          <Tabs.Tab value="sources" leftSection={<IconWorldDownload size={24} />}>
            File sources
          </Tabs.Tab>
        </Tabs.List>

        <Tabs.Panel value="files" mt="xs" className="scrollablePanel">
          <Group justify="space-between" wrap="nowrap" m="sm">
            <Breadcrumbs separator=">">
              {breadcrumbs.map((crumb, idx) => (
                <Anchor key={crumb.id} component="button" size="sm" onClick={() => onBreadcrumbClick(crumb.id, idx)}>
                  {crumb.title}
                </Anchor>
              ))}
            </Breadcrumbs>
            <Tooltip label="Refresh files" openDelay={300}>
              <ActionIcon variant="subtle" color="gray" aria-label="Refresh files" onClick={refreshFiles}>
                <IconRefresh size={18} />
              </ActionIcon>
            </Tooltip>
          </Group>
          {renderFiles()}
        </Tabs.Panel>
        <Tabs.Panel value="history" mt="xs" className="scrollablePanel">
          {renderHistory()}
        </Tabs.Panel>
        <Tabs.Panel value="sources" mt="xs" className="scrollablePanel">
          {mediaItem && (
            <ItemFileSources key={`${mediaItem.id}:${sourceQuery}`} item={mediaItem} initialQuery={sourceQuery} />
          )}
        </Tabs.Panel>
      </Tabs>

      {mediaItem && (
        <EditImagesModal
          item={mediaItem}
          opened={imagesOpened}
          onClose={() => setImagesOpened(false)}
          posterUrl={imageUrl ?? null}
          backdropUrl={backdropUrl ?? null}
        />
      )}

      {mediaItem && (
        <SegmentsDialog itemId={mediaItem.id} opened={segmentsOpened} onClose={() => setSegmentsOpened(false)} />
      )}

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
