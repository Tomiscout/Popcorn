import React, { useEffect, useRef, useState } from 'react';
import { PlayerOption, Settings } from '../../model/settings.contract';
import { SocketRequests } from '../../socket/socket-requests';
import { useWebSocket } from '../../state/websocket-context';
import { Button, Checkbox, Group, Skeleton, Stack, Text, Title } from '@mantine/core';
import { isDesktopApp } from '../../utils/platform';
import PlayerList, { newPlayerId } from './player-list';

const SettingsPage: React.FC = () => {
  const { client, isClientReady } = useWebSocket();

  const [settings, setSettings] = useState<Settings | null>(null);
  const [defaultPlayerName, setDefaultPlayerName] = useState<string>();
  // Latest settings for socket callbacks (the program picker answers asynchronously).
  const settingsRef = useRef<Settings | null>(null);
  settingsRef.current = settings;

  const saveSettings = (updatedSettings: Settings) => {
    settingsRef.current = updatedSettings;
    setSettings(updatedSettings);
    client.sendEvent(SocketRequests.UpdateSettings, updatedSettings);
  };

  useEffect(() => {
    if (!isClientReady) return;

    const unsubs = [
      client.onEvent<Settings>(SocketRequests.GetSettings, setSettings),
      client.onEvent<string>(SocketRequests.DefaultPlayerName, setDefaultPlayerName),
      client.onEvent<string>(SocketRequests.PickPlayerProgram, (path) => {
        const current = settingsRef.current;
        if (!current) return;
        const program: PlayerOption = { id: newPlayerId(), type: 'Program', path, enabled: true };
        saveSettings({ ...current, players: [...current.players, program] });
      }),
    ];

    client.sendEvent(SocketRequests.GetSettings);
    client.sendEvent(SocketRequests.DefaultPlayerName);

    return () => unsubs.forEach((unsub) => unsub());
  }, [isClientReady]);

  const handleCheckboxChange = (key: 'useInternalTorrentClient', value: boolean) => {
    if (settings) saveSettings({ ...settings, [key]: value });
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
              label="Use Internal Torrent Client"
              checked={settings.useInternalTorrentClient}
              onChange={(e) => handleCheckboxChange('useInternalTorrentClient', e.target.checked)}
            />
          </Group>

          <Stack mt={30} maw={720} gap="xs">
            <Title order={3}>Video players</Title>
            <Text size="sm" c="dimmed">
              {isDesktopApp
                ? 'Players used to open videos, in order of preference.'
                : 'Players used when a video is opened on the server machine (desktop app, Folders page). The browser always uses its built-in web player.'}{' '}
              The first enabled player that starts is used; if it fails (not installed, no program associated, exits
              with an error) the next one is tried.
            </Text>
            <PlayerList
              players={settings.players ?? []}
              defaultPlayerName={defaultPlayerName}
              onChange={(players) => saveSettings({ ...settings, players })}
              onAddProgram={() => client.sendEvent(SocketRequests.PickPlayerProgram)}
            />
          </Stack>
        </>
      )}
    </div>
  );
};

export default SettingsPage;
