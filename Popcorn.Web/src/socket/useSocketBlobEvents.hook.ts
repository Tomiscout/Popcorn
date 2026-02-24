import { useEffect } from 'react';
import { useWebSocket } from './../state/websocket-context';
import { SocketRequests } from '../socket/socket-requests';

type BlobEventMap = Partial<Record<SocketRequests, (payload: Uint8Array<ArrayBuffer>, metadata: string[]) => void>>;

export function useSocketBlobEvents(events: BlobEventMap) {
  const { client, isClientReady } = useWebSocket();

  useEffect(() => {
    if (!isClientReady) return;

    const unsubs = Object.entries(events).map(([event, handler]) => client.onBlobEvent(event, handler as any));

    return () => {
      unsubs.forEach((u) => u());
    };
  }, [client, isClientReady, events]);
}
