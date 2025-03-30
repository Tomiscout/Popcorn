import React, { useEffect, useState } from 'react';
import ItemsGrid from './items-grid';
import { SocketRequests } from '../../socket/socket-requests';
import { MediaItem, MediaListResponse } from '../../model/media.contract';
import { useWebSocket } from '../../state/websocket-context';
import { useFileContext } from '../../state/file-context';
import LoadingPage from '../loading/loading.page';

export interface WatchPageProps {
  pageLoaded: () => void;
}

const WatchPage: React.FC<WatchPageProps> = ({ pageLoaded }) => {
  const { client, isClientReady } = useWebSocket();
  const { getFilesToLoad } = useFileContext();

  const [hasPageLoaded, setHasPageLoaded] = useState(false);
  const [recentItems, setRecentItems] = useState<MediaItem[]>([]);
  const [newItems, setNewItems] = useState<MediaItem[]>([]);
  const [removedItems, setRemovedItems] = useState<MediaItem[]>([]);

  const pageHasLoaded = () => {
    setTimeout(() => {
      setHasPageLoaded(true);
      pageLoaded();
    }, 10);
  };

  useEffect(() => {
    if (!isClientReady) return;
    client.onEvent<MediaListResponse>(SocketRequests.MediaList, (response) => {
      setRecentItems(response.recentItems);
      setNewItems(response.newItems);
      setRemovedItems(response.removedItems);

      const imagesToLoad = getFilesToLoad(
        [
          ...response.recentItems.map((i) => i.imagePath!),
          ...response.newItems.map((i) => i.imagePath!),
          ...response.removedItems.map((i) => i.imagePath!),
        ].filter((i) => i)
      );

      if (imagesToLoad.length) {
        client.sendEvent(SocketRequests.ImageFile, { files: imagesToLoad });
      } else {
        console.log('page loaded from handler');
        pageHasLoaded();
      }
    });

    client.onEvent<any>(SocketRequests.ImageFilesBatchSent, () => {
      pageHasLoaded();
    });

    client.sendEvent<any>(SocketRequests.MediaList, {} as any);

    return () => {
      client.onEventUnregiser(SocketRequests.MediaList);
    };
  }, [isClientReady]);

  return (
    <>
      <ItemsGrid
        headerTitle="Continue watching"
        items={recentItems}
        buttonText="Do something"
        onButtonClick={() => ''}
      />
      <ItemsGrid headerTitle="Not yet watched" items={newItems} />
      <ItemsGrid headerTitle="No longer on disk" items={removedItems} />

      {!hasPageLoaded && <LoadingPage />}
    </>
  );
};

export default WatchPage;
