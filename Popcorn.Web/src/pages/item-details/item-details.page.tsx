import React, { useEffect, useState } from 'react';
import { useParams } from 'react-router-dom';
import { useWebSocket } from '../../socket/websocket-context';
import { SocketRequests } from '../../socket/socket-requests';
import { MediaFile, MediaItem, MediaItemHistory, MediaItemRequest } from '../../model/media.contract';

const ItemDetailsPage: React.FC = () => {
  const { id } = useParams();
  const wsClient = useWebSocket();

  const [mediaItem, setMediaItem] = useState<MediaItem>();
  const [mediafiles, setMediaFiles] = useState<MediaFile[]>();
  const [mediaHistory, setMediaHistory] = useState<MediaItemHistory>();

  useEffect(() => {
    if (!id) return;

    wsClient.sendEvent<MediaItemRequest>(SocketRequests.MediaItem, { id });
    wsClient.sendEvent<MediaItemRequest>(SocketRequests.MediaItemFileList, { id });
    wsClient.sendEvent<MediaItemRequest>(SocketRequests.MediaItemHistory, { id });
  }, [id]);

  useEffect(() => {
    wsClient.onEvent<MediaItem>(SocketRequests.MediaItem, (msg) => {
      setMediaItem(msg);
    });
    wsClient.onEvent<MediaFile[]>(SocketRequests.MediaItemFileList, setMediaFiles);
    wsClient.onEvent<MediaItemHistory>(SocketRequests.MediaItemHistory, setMediaHistory);
  }, [wsClient]);

  return (
    <div>
      <h1>{mediaItem?.shortTitle}</h1>
      <p>Welcome to the Media search page!</p>
    </div>
  );
};

export default ItemDetailsPage;
