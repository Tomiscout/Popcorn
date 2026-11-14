import { Alert, Anchor, Text } from '@mantine/core';
import { IconPlugConnectedX } from '@tabler/icons-react';
import { useMemo, useState } from 'react';
import { Link } from 'react-router-dom';
import { DownloadStats, RouterMapping } from '../../model/download.contract';
import { SocketRequests } from '../../socket/socket-requests';
import { useSocketEvents } from '../../socket/useSocketEvents.hook';
import { useSubscription } from '../../socket/useSubscription.hook';

const routerHint: Record<RouterMapping, string> = {
  Mapped:
    'The router forwards the port automatically (UPnP / NAT-PMP), so a firewall on this computer most likely blocks it.',
  Failed: 'The router refused the automatic port forward (UPnP / NAT-PMP): forward the port to this computer by hand.',
  NoRouter:
    'No router answered the automatic port forward request (UPnP / NAT-PMP off or not supported): forward the port to this computer by hand.',
  Pending: 'Still waiting for the router to answer the automatic port forward request (UPnP / NAT-PMP).',
};

/**
 * Torrent tab banner: no peer has connected to the listen port although torrents had peers for minutes (the server
 * watches incoming connections, `DownloadManager.GetPortStatus`). Torrents then only reach peers that accept
 * connections themselves: fewer peers, slow starts.
 */
export function PortWarning() {
  const [stats, setStats] = useState<DownloadStats | null>(null);
  useSocketEvents(useMemo(() => ({ [SocketRequests.DownloadStats]: setStats }), []));
  useSubscription(SocketRequests.DownloadStats);

  const status = stats?.port;
  if (status?.reachability !== 'Unreachable') return null;
  const port = status.port > 0 ? `port ${status.port}` : 'the listen port';
  return (
    <Alert color="yellow" icon={<IconPlugConnectedX size={18} />} title={`Peers can't connect to ${port}`} mb="md">
      <Text size="sm">
        No peer has connected to Popcorn, so torrents only reach peers that accept connections themselves: fewer peers
        and slow starts. Allow {port} (TCP and UDP) in this computer's firewall and forward it on the router.
      </Text>
      <Text size="sm" mt={4}>
        {routerHint[status.mapping]}
      </Text>
      {status.port > 0 && (
        <Text size="sm" mt={4}>
          The port is set in{' '}
          <Anchor component={Link} to="/settings" size="sm">
            Settings → Downloads
          </Anchor>
          .
        </Text>
      )}
    </Alert>
  );
}
