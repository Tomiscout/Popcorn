import { useEffect } from 'react';
import { useWebSocket } from '../state/websocket-context';
import { SocketRequests } from './socket-requests';

/**
 * Has the server push `topic` (an event name, + `key` for per-item ones) while the component is mounted: the current
 * state right away, then every change (server `Push/LivePushService.cs`). Listen for the event itself with
 * `useSocketEvents`. Sent again after a reconnect, as subscriptions belong to the connection. `enabled = false`
 * (e.g. a closed dialog) unsubscribes.
 */
export function useSubscription(topic: SocketRequests, key?: string, enabled = true) {
  const { client, isClientReady } = useWebSocket();

  useEffect(() => {
    if (!isClientReady || !enabled) return;
    client.sendEvent(SocketRequests.Subscribe, { topic, key });
    return () => client.sendEvent(SocketRequests.Unsubscribe, { topic, key });
  }, [client, isClientReady, topic, key, enabled]);
}
