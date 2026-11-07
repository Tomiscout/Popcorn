import React, { useEffect, useRef, useState } from 'react';
import { AutostartState, PlayerOption, Settings } from '../../model/settings.contract';
import { SocketRequests } from '../../socket/socket-requests';
import { useWebSocket } from '../../state/websocket-context';
import { Button, Code, Radio, Skeleton, Stack, Switch, Tabs, Text, Title } from '@mantine/core';
import { SegmentAutoSkip, segmentAutoSkipOptions } from '../../model/segments.contract';
import { isDesktopApp } from '../../utils/platform';
import PlayerList, { newPlayerId } from './player-list';
import PluginList from './plugin-list';
import DownloadSettingsSection from './download-settings';
import UpdateSection from './update-section';

const SettingsPage: React.FC = () => {
  const { client, isClientReady } = useWebSocket();

  const [settings, setSettings] = useState<Settings | null>(null);
  const [defaultPlayerName, setDefaultPlayerName] = useState<string>();
  const [autostart, setAutostart] = useState<AutostartState>();
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
      client.onEvent<AutostartState>(SocketRequests.Autostart, setAutostart),
      client.onEvent<string>(SocketRequests.PickPlayerProgram, (path) => {
        const current = settingsRef.current;
        if (!current) return;
        const program: PlayerOption = { id: newPlayerId(), type: 'Program', path, enabled: true };
        saveSettings({ ...current, players: [...current.players, program] });
      }),
    ];

    client.sendEvent(SocketRequests.GetSettings);
    client.sendEvent(SocketRequests.DefaultPlayerName);
    client.sendEvent(SocketRequests.Autostart);

    return () => unsubs.forEach((unsub) => unsub());
  }, [isClientReady]);

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
        <Tabs defaultValue="general" keepMounted={false}>
          <Tabs.List mb="md">
            <Tabs.Tab value="general">General</Tabs.Tab>
            <Tabs.Tab value="downloads">Downloads</Tabs.Tab>
            <Tabs.Tab value="plugins">Plugins</Tabs.Tab>
          </Tabs.List>

          <Tabs.Panel value="general">
            <Button onClick={openProgramFolder}>Open Program Folder</Button>

            <UpdateSection />

            {autostart?.supported && (
              <Stack mt={30} maw={720} gap="xs">
                <Title order={3}>Startup</Title>
                <Switch
                  checked={autostart.enabled}
                  onChange={(e) => {
                    const enabled = e.currentTarget.checked;
                    setAutostart({ ...autostart, enabled });
                    client.sendEvent(SocketRequests.AutostartSet, { enabled });
                  }}
                  label="Start Popcorn on system startup"
                  description="Starts the Popcorn server in the background at sign-in. The same switch as Popcorn in Task Manager → Startup apps."
                />
              </Stack>
            )}

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

            <Stack mt={30} maw={720} gap="xs">
              <Title order={3}>Intros and outros</Title>
              <Text size="sm" c="dimmed">
                When the player skips intros and outros on its own. Media items can override this from the settings
                button in the player.
              </Text>
              <Radio.Group
                value={settings.segmentAutoSkip ?? 'Always'}
                onChange={(value) => saveSettings({ ...settings, segmentAutoSkip: value as SegmentAutoSkip })}
              >
                <Stack gap="xs">
                  {segmentAutoSkipOptions.map((o) => (
                    <Radio key={o.value} value={o.value} label={o.label} description={o.description} />
                  ))}
                </Stack>
              </Radio.Group>
              <Switch
                checked={settings.skipOutroTail ?? true}
                onChange={(e) => saveSettings({ ...settings, skipOutroTail: e.currentTarget.checked })}
                label="Also skip what's left after an outro"
                description="When an outro ends within 10 seconds of the end of the file, skipping it goes straight to the end."
              />
              <Switch
                checked={settings.skipSegmentsWithoutDelay ?? false}
                onChange={(e) => saveSettings({ ...settings, skipSegmentsWithoutDelay: e.currentTarget.checked })}
                label="Skip segments with no delay"
                description="Auto-skip jumps past an intro or outro as soon as it starts, without the skip button's count-down."
              />
            </Stack>
          </Tabs.Panel>

          <Tabs.Panel value="downloads">
            <DownloadSettingsSection settings={settings} onChange={saveSettings} />
          </Tabs.Panel>

          <Tabs.Panel value="plugins">
            <Stack maw={720} gap="xs">
              <Text size="sm" c="dimmed">
                Media and file sources for Search. Drag to set the order in which their results are listed. Installed
                plugins are extracted to the <Code>plugins</Code> folder in the data folder; a plugin there replaces a
                built-in one with the same id.
              </Text>
              <PluginList />
            </Stack>
          </Tabs.Panel>
        </Tabs>
      )}
    </div>
  );
};

export default SettingsPage;
