import { useEffect } from 'react';
import { useWebSocket } from '../state/websocket-context';
import { SocketRequests } from './socket-requests';

type EventMap = Partial<Record<SocketRequests, (data: any) => void>>;

export function useSocketEvents(events: EventMap) {
  const { client, isClientReady } = useWebSocket();

  useEffect(() => {
    if (!isClientReady) return;

    const unsubs = Object.entries(events).map(([event, handler]) => client.onEvent(event, handler as any));

    return () => {
      unsubs.forEach((u) => u());
    };
  }, [client, isClientReady, events]);
}
