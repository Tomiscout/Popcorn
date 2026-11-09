import { Group, Stack, Text, UnstyledButton } from '@mantine/core';
import { IconArrowDown, IconArrowUp } from '@tabler/icons-react';
import { useMemo, useState } from 'react';
import { DownloadStats } from '../../model/download.contract';
import { SocketRequests } from '../../socket/socket-requests';
import { useSocketEvents } from '../../socket/useSocketEvents.hook';
import { useSubscription } from '../../socket/useSubscription.hook';
import { formatRate } from './format';
import { SpeedLimitDialog } from './speed-limit-dialog';

/** Download / upload speed of all downloads, at the bottom of the nav bar, pushed by the server as they change. Click: speed limits. */
export function NetworkStats() {
  const [stats, setStats] = useState<DownloadStats | null>(null);
  useSocketEvents(useMemo(() => ({ [SocketRequests.DownloadStats]: setStats }), []));
  useSubscription(SocketRequests.DownloadStats);
  const [limitsOpened, setLimitsOpened] = useState(false);

  return (
    <>
      <UnstyledButton mt="auto" p="sm" c="dimmed" aria-label="Speed limits" onClick={() => setLimitsOpened(true)}>
        <Stack gap={2}>
          <Group gap={4} wrap="nowrap">
            <IconArrowDown size={14} color="var(--mantine-color-teal-5)" />
            <Text size="xs">{formatRate(stats?.downloadRate ?? 0)}</Text>
          </Group>
          <Group gap={4} wrap="nowrap">
            <IconArrowUp size={14} color="var(--mantine-color-orange-5)" />
            <Text size="xs">{formatRate(stats?.uploadRate ?? 0)}</Text>
          </Group>
          {!!stats?.active && (
            <Text size="xs">
              {stats.active} active download{stats.active === 1 ? '' : 's'}
            </Text>
          )}
        </Stack>
      </UnstyledButton>
      <SpeedLimitDialog opened={limitsOpened} onClose={() => setLimitsOpened(false)} />
    </>
  );
}
