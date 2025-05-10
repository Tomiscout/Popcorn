import IconTrash from '@tabler/icons-react/dist/esm/icons/IconTrash';
import IconPlus from '@tabler/icons-react/dist/esm/icons/IconPlus';
import IconPlayerPlay from '@tabler/icons-react/dist/esm/icons/IconPlayerPlay';

import { Flex, Text, Table, FileInput, Button, Accordion, Skeleton, Box, List, Divider } from '@mantine/core';
import React, { useEffect, useState } from 'react';
import { useModals } from '@mantine/modals';
import { useWebSocket } from '../../state/websocket-context';
import { SocketRequests } from '../../socket/socket-requests';
import { LibraryFoldersData, MediaScanResult } from '../../model/folders.contract';
import { AddToLibraryModal } from './add-to-library.modal';

const FoldersPage: React.FC = () => {
  const { client, isClientReady } = useWebSocket();
  const modals = useModals();

  const [libraryFolders, setLibraryFolders] = useState<string[]>([]);
  const [libraryData, setLibraryData] = useState<LibraryFoldersData>({});
  const [isLibraryScanning, setIsLibraryScanning] = useState(false);
  const [mediaScanForAdding, setMediaScanForAdding] = useState<MediaScanResult | null>(null);

  const libraryDataReceived = (data: LibraryFoldersData) => {
    setLibraryData(data);
    setIsLibraryScanning(false);
  };

  useEffect(() => {
    if (!isClientReady) return;

    client.onEvent<string[]>(SocketRequests.LibraryFolders, setLibraryFolders);
    client.onEvent<LibraryFoldersData>(SocketRequests.LibraryFoldersData, libraryDataReceived);

    client.sendEvent(SocketRequests.LibraryFolders);

    setIsLibraryScanning(true);
    client.sendEvent(SocketRequests.LibraryFoldersData);
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

  const createMediaItem = async (rootPath: string, title: string, imageFile?: File) => {
    client.sendBinaryEvent(
      SocketRequests.CreateMediaItem,
      imageFile ? await imageFile.arrayBuffer() : new ArrayBuffer(0),
      rootPath,
      title
    );
  };

  const renderLibraryData = () => {
    if (isLibraryScanning)
      return (
        <>
          <Skeleton height={60} radius="xl" />
          <Skeleton height={60} radius="xl" mt="md" />
          <Skeleton height={60} radius="xl" mt="md" />
        </>
      );

    const folders = Object.keys(libraryData);
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
                      <Box key={scanResult.filePath} mb="sm">
                        <Flex align="center" gap={8}>
                          <Button variant="subtle" onClick={() => openAddScanResultDialog(scanResult)}>
                            <IconPlus size={16} />
                          </Button>

                          <Text fw={700} size="sm">
                            {scanResult.files.length} file
                            {scanResult.files.length !== 1 && 's'}
                          </Text>
                          <Text>{scanResult.title}</Text>
                          {scanResult.files.length === 1 && (
                            <Button variant="subtle" onClick={() => launchFile(scanResult.filePath)}>
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

      <Button onClick={openLibraryFolderPicker}>Add folder to library</Button>

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
        scanResult={mediaScanForAdding!}
        onConfirm={createMediaItem}
      />
    </div>
  );
};

export default FoldersPage;
