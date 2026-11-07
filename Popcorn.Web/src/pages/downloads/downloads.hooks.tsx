import { Anchor } from '@mantine/core';
import { notifications } from '@mantine/notifications';
import { useCallback, useEffect, useMemo, useState } from 'react';
import { Link, useNavigate } from 'react-router-dom';
import { DownloadAddedResponse } from '../../model/download.contract';
import { SocketRequests } from '../../socket/socket-requests';
import { useSocketEvents } from '../../socket/useSocketEvents.hook';
import { useWebSocket } from '../../state/websocket-context';

/** Sends `event` now and every `ms` while the socket is ready. */
export function usePolling(event: SocketRequests, ms = 1000) {
  const { client, isClientReady } = useWebSocket();
  useEffect(() => {
    if (!isClientReady) return;
    client.sendEvent(event);
    const timer = setInterval(() => client.sendEvent(event), ms);
    return () => clearInterval(timer);
  }, [client, isClientReady, event, ms]);
}

/** Tells the user whether a download started from anywhere in the app was added. */
export function useDownloadAddedNotifications() {
  useSocketEvents(
    useMemo(
      () => ({
        [SocketRequests.DownloadAdded]: (r: DownloadAddedResponse) =>
          r.error
            ? notifications.show({ color: 'red', title: 'Download failed', message: r.error })
            : notifications.show({
                color: 'green',
                title: 'Download added',
                message: (
                  <>
                    {r.name}{' '}
                    <Anchor component={Link} to="/downloads" size="sm">
                      Show downloads
                    </Anchor>
                  </>
                ),
              }),
      }),
      []
    )
  );
}

const isTorrentFile = (file: File) =>
  file.name.toLowerCase().endsWith('.torrent') || file.type === 'application/x-bittorrent';

/**
 * .torrent files dropped anywhere in the app start downloading (added to the library, like the add dialog's default)
 * and open the Downloads page. The player keeps its own drops (subtitle / audio files).
 * Returns whether files are being dragged over a place that takes them.
 */
export function useTorrentFileDrop() {
  const { client } = useWebSocket();
  const navigate = useNavigate();
  const [isDragging, setIsDragging] = useState(false);

  useEffect(() => {
    const accepts = (e: DragEvent) =>
      !!e.dataTransfer?.types.includes('Files') && !(e.target as Element).closest?.('.video-container');

    const onDragOver = (e: DragEvent) => {
      const accepted = accepts(e);
      setIsDragging(accepted);
      if (accepted) e.preventDefault(); // Allows the drop
    };

    // No related target: the drag left the window.
    const onDragLeave = (e: DragEvent) => {
      if (!e.relatedTarget) setIsDragging(false);
    };

    const onDrop = (e: DragEvent) => {
      setIsDragging(false);
      if (!accepts(e)) return;
      e.preventDefault(); // The browser would navigate to the file

      const files = Array.from(e.dataTransfer!.files).filter(isTorrentFile);
      if (!files.length) {
        notifications.show({
          color: 'red',
          title: 'Not a torrent',
          message: 'Only .torrent files can be dropped here',
        });
        return;
      }
      files.forEach(async (file) => {
        const bytes = new Uint8Array(await file.arrayBuffer());
        client.sendBinaryEvent(SocketRequests.DownloadAddTorrentFile, bytes, '1', '', '');
      });
      navigate('/downloads', { state: { tab: 'Torrent' } });
    };

    // Capture: the player stops propagation of the drags it handles.
    window.addEventListener('dragover', onDragOver, true);
    window.addEventListener('dragleave', onDragLeave, true);
    window.addEventListener('drop', onDrop, true);
    return () => {
      window.removeEventListener('dragover', onDragOver, true);
      window.removeEventListener('dragleave', onDragLeave, true);
      window.removeEventListener('drop', onDrop, true);
    };
  }, [client, navigate]);

  return isDragging;
}

/** Smallest width a column can be dragged to. */
const MIN_COLUMN_WIDTH = 48;

/** Column widths in px by key, remembered in localStorage under `storageKey`; unset columns use `defaults`. */
export function useColumnWidths<K extends string>(storageKey: string, defaults: Record<K, number>) {
  const [saved, setSaved] = useState<Partial<Record<K, number>>>(() => {
    try {
      return JSON.parse(localStorage.getItem(storageKey) ?? '{}');
    } catch {
      return {};
    }
  });

  const widths = { ...defaults, ...saved } as Record<K, number>;

  const setWidth = useCallback(
    (key: K, width: number) =>
      setSaved((current) => {
        const next = { ...current, [key]: Math.max(MIN_COLUMN_WIDTH, Math.round(width)) };
        localStorage.setItem(storageKey, JSON.stringify(next));
        return next;
      }),
    [storageKey]
  );

  const reset = useCallback(
    (key: K) =>
      setSaved((current) => {
        const next = { ...current };
        delete next[key];
        localStorage.setItem(storageKey, JSON.stringify(next));
        return next;
      }),
    [storageKey]
  );

  return { widths, setWidth, reset };
}
