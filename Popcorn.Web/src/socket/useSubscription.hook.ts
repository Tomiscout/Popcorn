import { useEffect } from 'react';
import { useWebSocket } from '../state/websocket-context';
import { SocketRequests } from './socket-requests';

/** Mounted subscribers per client and topic: the server keeps one subscription per connection and topic. */
const subscribers = new WeakMap<object, Map<string, number>>();

/**
 * Has the server push `topic` (an event name, + `key` for per-item ones) while the component is mounted: the current
 * state right away, then every change (server `Push/LivePushService.cs`). Listen for the event itself with
 * `useSocketEvents`. Sent again after a reconnect, as subscriptions belong to the connection. `enabled = false`
 * (e.g. a closed dialog) unsubscribes. Several components may watch one topic: every mount subscribes (so it gets the
 * current state too), only the last unmount unsubscribes.
 */
export function useSubscription(topic: SocketRequests, key?: string, enabled = true) {
  const { client, isClientReady } = useWebSocket();

  useEffect(() => {
    if (!isClientReady || !enabled) return;
    const name = key ? `${topic}:${key}` : topic;
    let counts = subscribers.get(client);
    if (!counts) subscribers.set(client, (counts = new Map()));
    counts.set(name, (counts.get(name) ?? 0) + 1);
    client.sendEvent(SocketRequests.Subscribe, { topic, key });
    return () => {
      const left = (counts.get(name) ?? 1) - 1;
      if (left > 0) {
        counts.set(name, left);
        return;
      }
      counts.delete(name);
      client.sendEvent(SocketRequests.Unsubscribe, { topic, key });
    };
  }, [client, isClientReady, topic, key, enabled]);
}
