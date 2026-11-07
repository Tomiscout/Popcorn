import { IconTrash, IconPlus, IconPlayerPlay, IconRefresh } from '@tabler/icons-react';

import { Flex, Group, Text, Table, Button, Accordion, Skeleton, Box, List, Divider } from '@mantine/core';
import React, { useEffect, useState } from 'react';
import { useModals } from '@mantine/modals';
import { useWebSocket } from '../../state/websocket-context';
import { SocketRequests } from '../../socket/socket-requests';
import { LibraryFoldersData, MediaScanResult } from '../../model/folders.contract';
import { CreateMediaItemRequest } from '../../model/media.contract';
import { AddToLibraryModal } from './add-to-library.modal';

const FoldersPage: React.FC = () => {
  const { client, isClientReady } = useWebSocket();
  const modals = useModals();

  const [libraryFolders, setLibraryFolders] = useState<string[]>([]);
  const [libraryData, setLibraryData] = useState<LibraryFoldersData>({});
  const [isLibraryScanning, setIsLibraryScanning] = useState(false);
  const [mediaScanForAdding, setMediaScanForAdding] = useState<MediaScanResult | null>(null);

  const scanLibrary = () => {
    setIsLibraryScanning(true);
    client.sendEvent(SocketRequests.LibraryFoldersData);
  };

  /** Reloads the folder list; its answer (also sent after a folder is added or removed) starts a new scan. */
  const refresh = () => {
    setIsLibraryScanning(true);
    client.sendEvent(SocketRequests.LibraryFolders);
  };

  useEffect(() => {
    if (!isClientReady) return;

    const unsubscribes = [
      client.onEvent<string[]>(SocketRequests.LibraryFolders, (folders) => {
        setLibraryFolders(folders);
        scanLibrary();
      }),
      client.onEvent<LibraryFoldersData>(SocketRequests.LibraryFoldersData, (data) => {
        setLibraryData(data);
        setIsLibraryScanning(false);
      }),
      client.onEvent<string>(SocketRequests.ErrorWhenHandlingEvent, (failedEvent) => {
        if (failedEvent === SocketRequests.LibraryFoldersData) setIsLibraryScanning(false);
      }),
    ];

    refresh();
    return () => unsubscribes.forEach((unsubscribe) => unsubscribe());
  }, [isClientReady]);

  const openLibraryFolderPicker = () => {
    client.sendEvent(SocketRequests.AddLibraryFolderPicker);
  };

  const removeFolder = (folder: string) => {
    client.sendEvent(SocketRequests.RemoveLibraryFolder, folder);
  };
  const openDeleteConfirm = (folder: string) =>
    modals.openConfirmModal({
      title: 'Confirm Deletion',
      children: <Text size="sm">Are you sure you want to delete folder for scanning?</Text>,
      labels: { confirm: 'Delete', cancel: 'Cancel' },
      confirmProps: { color: 'red' },
      onConfirm: () => {
        removeFolder(folder);
      },
    });

  const launchFile = (path: string) => {
    client.sendEvent(SocketRequests.FileOpen, path);
  };

  const openAddScanResultDialog = (scanResult: MediaScanResult) => {
    setMediaScanForAdding(scanResult);
  };

  const createMediaItem = (request: CreateMediaItemRequest) => {
    client.sendEvent<CreateMediaItemRequest>(SocketRequests.CreateMediaItem, request);
    scanLibrary(); // handled after the create (one connection's events run in order): the folder leaves the list
  };

  const renderLibraryData = () => {
    const folders = Object.keys(libraryData);
    // A rescan keeps the current list on screen (and its open sections); the Refresh button shows the progress.
    if (isLibraryScanning && !folders.length)
      return (
        <>
          <Skeleton height={60} radius="xl" />
          <Skeleton height={60} radius="xl" mt="md" />
          <Skeleton height={60} radius="xl" mt="md" />
        </>
      );

    if (!folders.length) {
      return <Text>No data in libraries</Text>;
    }

    return (
      <Accordion multiple>
        {folders.map((folder) => {
          const items = libraryData[folder];
          return (
            <Accordion.Item
              key={folder}
              value={folder}
              //disabled={items.length === 0}
              //initiallyExpanded={items.length > 0}
            >
              <Accordion.Control>{folder}</Accordion.Control>
              <Accordion.Panel>
                {items.length ? (
                  <List spacing="sm">
                    {items.map((scanResult) => (
                      <Box key={scanResult.folderPath} mb="sm">
                        <Flex align="center" gap={8}>
                          <Button variant="subtle" onClick={() => openAddScanResultDialog(scanResult)}>
                            <IconPlus size={16} />
                          </Button>

                          <Text fw={700} size="sm">
                            {scanResult.files.length} file
                            {scanResult.files.length !== 1 && 's'}
                          </Text>
                          <Text title={scanResult.folderPath}>{scanResult.title}</Text>
                          {scanResult.files.length === 1 && (
                            <Button variant="subtle" onClick={() => launchFile(scanResult.folderPath)}>
                              <IconPlayerPlay size={16} />
                            </Button>
                          )}
                        </Flex>
                        <Divider />
                      </Box>
                    ))}
                  </List>
                ) : (
                  <Text>No files in folder</Text>
                )}
              </Accordion.Panel>
            </Accordion.Item>
          );
        })}
      </Accordion>
    );
  };

  return (
    <div>
      <h1>Folders Page</h1>
      <p>Welcome to the Folders page!</p>

      <Group gap="xs">
        <Button onClick={openLibraryFolderPicker}>Add folder to library</Button>
        <Button variant="default" leftSection={<IconRefresh size={16} />} loading={isLibraryScanning} onClick={refresh}>
          Refresh
        </Button>
      </Group>

      <Table highlightOnHover horizontalSpacing="md">
        <Table.Thead>
          <Table.Tr>
            <Table.Th>Library folders</Table.Th>
          </Table.Tr>
        </Table.Thead>
        <Table.Tbody>
          {!libraryFolders?.length && (
            <Table.Tr key="empty">
              <Table.Td>
                <Text>No folders present</Text>
              </Table.Td>
            </Table.Tr>
          )}
          {!!libraryFolders?.length &&
            libraryFolders.map((folder) => (
              <Table.Tr key={folder}>
                <Table.Td>
                  <Flex direction="row" justify="space-between">
                    <Text>{folder}</Text>
                    <IconTrash style={{ cursor: 'pointer ' }} onClick={() => openDeleteConfirm(folder)}></IconTrash>
                  </Flex>
                </Table.Td>
              </Table.Tr>
            ))}
        </Table.Tbody>
      </Table>

      <Box mt="xl" mb="xl">
        <Divider />
      </Box>

      {renderLibraryData()}

      <AddToLibraryModal
        opened={!!mediaScanForAdding}
        onClose={() => {
          setMediaScanForAdding(null);
        }}
        scanResult={mediaScanForAdding}
        onConfirm={createMediaItem}
      />
    </div>
  );
};

export default FoldersPage;
