import React, { useEffect, useRef, useState } from 'react';
import { Anchor, Badge, Breadcrumbs, Modal, ScrollArea, Text, UnstyledButton } from '@mantine/core';
import {
  IconCheck,
  IconFolder,
  IconFolders,
  IconListDetails,
  IconMovie,
  IconPlayerPlayFilled,
} from '@tabler/icons-react';
import { postMessage } from '../../../utils/events';
import { usePlayerStore } from '../../../state/player.store';
import { FileType, MediaFile } from '../../../model/media.contract';
import './EpisodesControl.css';

/**
 * Files of the current folder in a centred dialog. Rendered into the player container (not `document.body`) so it
 * also shows in browser fullscreen, where only the fullscreen element is painted. The modal overlay takes every
 * click outside the dialog (closes it) so nothing reaches the player controls below.
 */
const EpisodesControl = () => {
  const { currentPlaylist, currentPlaylistNavigation, fileId, metadata } = usePlayerStore();
  const [container, setContainer] = useState<HTMLElement | null>(null);
  const buttonRef = useRef<HTMLButtonElement>(null);
  const currentRowRef = useRef<HTMLButtonElement>(null);
  const opened = container !== null;

  const open = () => setContainer(buttonRef.current?.closest<HTMLElement>('.video-container') ?? document.body);
  const close = () => setContainer(null);

  const isCurrent = (file: MediaFile) =>
    file.type !== FileType.Folder && (file.id === fileId || file.path === metadata?.filePath);

  useEffect(() => {
    if (!opened) return;
    const frame = requestAnimationFrame(() => currentRowRef.current?.scrollIntoView({ block: 'center' }));
    return () => cancelAnimationFrame(frame);
  }, [opened]);

  // The host resolves the folder: the server by id, the native player by path.
  const openFolder = (fileId: string, path: string) => postMessage('folder_open', { fileId, path });

  const fileClick = (file: MediaFile) => {
    if (file.type === FileType.Folder) {
      openFolder(file.id, file.path);
      return;
    }
    if (!isCurrent(file)) postMessage('play_file', file.id);
    close();
  };

  // Player shortcuts listen for keyup on the document: keep the dialog's keys (Space on a row, Escape) away from them.
  const onKeyUp = (e: React.KeyboardEvent) => {
    e.stopPropagation();
    if (e.key === 'Escape') close();
  };

  const breadcrumbs = (
    <Breadcrumbs separatorMargin={6} className="files-dialog__breadcrumbs">
      <Anchor component="button" c="dimmed" onClick={() => postMessage('folder_open_root')} title="Library folders">
        <IconFolders size={18} />
      </Anchor>
      {currentPlaylistNavigation?.map((folder, index) => {
        const last = index === currentPlaylistNavigation.length - 1;
        return (
          <Anchor
            key={index}
            component="button"
            c={last ? 'white' : 'dimmed'}
            fw={last ? 600 : 400}
            onClick={() => openFolder(folder.fileId, folder.path)}
          >
            {folder.title}
          </Anchor>
        );
      })}
    </Breadcrumbs>
  );

  return (
    <>
      <button ref={buttonRef} className="control-button episodes" onClick={open} title="Files">
        <IconListDetails />
      </button>

      <Modal
        opened={opened}
        onClose={close}
        title={breadcrumbs}
        centered
        size="lg"
        zIndex={50}
        closeOnEscape={false}
        portalProps={{ target: container ?? undefined }}
        overlayProps={{ backgroundOpacity: 0.55, blur: 2 }}
        scrollAreaComponent={ScrollArea.Autosize}
        classNames={{ root: 'files-dialog', title: 'files-dialog__title', body: 'files-dialog__body' }}
        onKeyUp={onKeyUp}
      >
        {!currentPlaylist?.length && (
          <Text c="dimmed" ta="center" py="xl">
            This folder is empty
          </Text>
        )}
        {currentPlaylist?.map((file) => {
          const current = isCurrent(file);
          const folder = file.type === FileType.Folder;
          const Icon = current ? IconPlayerPlayFilled : folder ? IconFolder : IconMovie;
          return (
            <UnstyledButton
              key={file.id}
              ref={current ? currentRowRef : undefined}
              className="files-dialog__row"
              data-current={current || undefined}
              data-played={(!current && file.played) || undefined}
              onClick={() => fileClick(file)}
            >
              <Icon size={20} className="files-dialog__icon" />
              <span className="files-dialog__name">{file.shortTitle || file.title}</span>
              {current ? (
                <Badge size="sm" variant="filled">
                  Playing
                </Badge>
              ) : (
                file.played && <IconCheck size={18} className="files-dialog__played" aria-label="Watched" />
              )}
            </UnstyledButton>
          );
        })}
      </Modal>
    </>
  );
};

export default EpisodesControl;
