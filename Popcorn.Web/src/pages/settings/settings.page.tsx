import React, { useEffect, useState } from 'react';
import { Settings } from '../../model/settings.contract';
import { SocketRequests } from '../../socket/socket-requests';
import { useWebSocket } from '../../state/websocket-context';
import { Button, Checkbox, Group, Skeleton } from '@mantine/core';

const SettingsPage: React.FC = () => {
  const { client, isClientReady } = useWebSocket();

  const [settings, setSettings] = useState<Settings | null>(null);

  useEffect(() => {
    if (!isClientReady) return;

    client.onEvent<Settings>(SocketRequests.GetSettings, setSettings);

    client.sendEvent(SocketRequests.GetSettings);
  }, [isClientReady]);

  const handleCheckboxChange = (key: keyof Settings, value: boolean) => {
    if (settings) {
      const updatedSettings = { ...settings, [key]: value };
      setSettings(updatedSettings);
      client.sendEvent(SocketRequests.UpdateSettings, updatedSettings);
    }
  };

  const openProgramFolder = () => {
    client.sendEvent(SocketRequests.OpenProgramFolder);
  };

  return (
    <div>
      <h1>Settings</h1>

      {!settings ? (
        <>
          <Skeleton height={60} radius="xl" />
          <Skeleton height={60} radius="xl" mt="md" />
          <Skeleton height={60} radius="xl" mt="md" />
        </>
      ) : (
        <>
          <Button onClick={openProgramFolder}>Open Program Folder</Button>
          <Group mt={20}>
            <Checkbox
              label="Use Internal Player"
              checked={settings.useInternalPlayer}
              onChange={(e) => handleCheckboxChange('useInternalPlayer', e.target.checked)}
            />
            <Checkbox
              label="Use Internal Torrent Client"
              checked={settings.useInternalTorrentClient}
              onChange={(e) => handleCheckboxChange('useInternalTorrentClient', e.target.checked)}
            />
          </Group>
        </>
      )}
    </div>
  );
};

export default SettingsPage;
