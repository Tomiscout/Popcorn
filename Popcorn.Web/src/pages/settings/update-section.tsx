import React, { useEffect, useState } from 'react';
import { Button, Group, Modal, ScrollArea, Stack, Text, Title } from '@mantine/core';
import { SocketRequests } from '../../socket/socket-requests';
import { useWebSocket } from '../../state/websocket-context';
import { UpdateCheckResult, UpdateStatus } from '../../model/update.contract';

/**
 * Settings → General → Updates. The check runs Popcorn.Installer without a window on the server; only an available
 * release asks (version + release notes), and yes starts the installer's own window, which closes and restarts
 * Popcorn. Hidden where the installer is not bundled (dev builds, Linux for now).
 */
const UpdateSection: React.FC = () => {
  const { client, isClientReady } = useWebSocket();
  const [status, setStatus] = useState<UpdateStatus>();
  const [checking, setChecking] = useState(false);
  const [result, setResult] = useState<UpdateCheckResult>();
  const [offerOpen, setOfferOpen] = useState(false);
  const [updating, setUpdating] = useState(false);

  useEffect(() => {
    if (!isClientReady) return;
    const unsubs = [
      client.onEvent<UpdateStatus>(SocketRequests.UpdateStatus, setStatus),
      client.onEvent<UpdateCheckResult>(SocketRequests.UpdateCheck, (answer) => {
        setChecking(false);
        setResult(answer);
        setOfferOpen(answer.status === 'Available');
      }),
    ];
    client.sendEvent(SocketRequests.UpdateStatus);
    return () => unsubs.forEach((unsub) => unsub());
  }, [isClientReady]);

  if (!status?.supported) return null;

  const check = () => {
    setChecking(true);
    setResult(undefined);
    client.sendEvent(SocketRequests.UpdateCheck);
  };

  const update = () => {
    setOfferOpen(false);
    setUpdating(true);
    client.sendEvent(SocketRequests.UpdateInstall);
  };

  return (
    <Stack mt={30} maw={720} gap="xs">
      <Title order={3}>Updates</Title>
      <Text size="sm" c="dimmed">
        Installed version: {status.currentVersion}
      </Text>
      <Group>
        <Button onClick={check} loading={checking} disabled={updating}>
          Check for updates
        </Button>
        {result?.status === 'Available' && !offerOpen && !updating && (
          <Button variant="light" onClick={() => setOfferOpen(true)}>
            Popcorn {result.version} is available
          </Button>
        )}
      </Group>
      {updating && (
        <Text size="sm">
          The installer is updating Popcorn. Popcorn closes meanwhile and starts again when it is done.
        </Text>
      )}
      {result?.status === 'UpToDate' && <Text size="sm">Popcorn is up to date.</Text>}
      {result?.status === 'Failed' && (
        <Text size="sm" c="red">
          The update check failed: {result.error}
        </Text>
      )}

      <Modal
        opened={offerOpen}
        onClose={() => setOfferOpen(false)}
        title={`Popcorn ${result?.version ?? ''} is available`}
        size="lg"
      >
        <Stack gap="sm">
          <Text size="sm" c="dimmed">
            Installed: {status.currentVersion}. Popcorn closes during the update and starts again afterwards.
          </Text>
          {result?.notes ? (
            <ScrollArea.Autosize mah={360}>
              <Text size="sm" style={{ whiteSpace: 'pre-wrap' }}>
                {result.notes}
              </Text>
            </ScrollArea.Autosize>
          ) : (
            <Text size="sm" c="dimmed">
              No release notes.
            </Text>
          )}
          <Group justify="flex-end">
            <Button variant="default" onClick={() => setOfferOpen(false)}>
              Later
            </Button>
            <Button onClick={update}>Update now</Button>
          </Group>
        </Stack>
      </Modal>
    </Stack>
  );
};

export default UpdateSection;
