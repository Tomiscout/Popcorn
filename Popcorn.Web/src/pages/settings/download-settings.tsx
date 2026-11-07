import { Button, Checkbox, Group, NumberInput, Stack, Text, TextInput, Title } from '@mantine/core';
import { IconFolderOpen } from '@tabler/icons-react';
import { useEffect, useRef, useState } from 'react';
import { DownloadSettings, Settings } from '../../model/settings.contract';
import { SocketRequests } from '../../socket/socket-requests';
import { useWebSocket } from '../../state/websocket-context';

/** Settings → Downloads: built-in torrent client and direct downloader. */
export default function DownloadSettingsSection({
  settings,
  onChange,
}: {
  settings: Settings;
  onChange: (settings: Settings) => void;
}) {
  const { client } = useWebSocket();
  const downloads = settings.downloads;
  const set = (patch: Partial<DownloadSettings>) => onChange({ ...settings, downloads: { ...downloads, ...patch } });

  // The folder picker answers asynchronously; apply it to the settings of that moment.
  const setRef = useRef(set);
  setRef.current = set;
  useEffect(
    () =>
      client.onEvent<string>(SocketRequests.PickDownloadFolder, (downloadFolder) => setRef.current({ downloadFolder })),
    [client]
  );

  // Text and number fields are saved when they lose focus, not on every key.
  const [folder, setFolder] = useState(downloads.downloadFolder);
  useEffect(() => setFolder(downloads.downloadFolder), [downloads.downloadFolder]);

  const number = (key: keyof DownloadSettings, label: string, description: string, props: object = {}) => (
    <NumberInput
      key={`${key}-${downloads[key]}`}
      label={label}
      description={description}
      defaultValue={downloads[key] as number}
      min={0}
      w={260}
      onBlur={(e) => {
        const value = Number(e.currentTarget.value.replace(/[^\d.]/g, '')) || 0;
        if (value !== downloads[key]) set({ [key]: value });
      }}
      {...props}
    />
  );

  return (
    <Stack maw={720} gap="lg">
      <Stack gap="xs">
        <Title order={3}>Where links open</Title>
        <Text size="sm" c="dimmed">
          The default action of file source links. Every link also has a menu to pick the other option.
        </Text>
        <Checkbox
          label="Download torrents (magnet / .torrent links) with the built-in torrent client"
          description="Off: open them in the system's torrent program."
          checked={settings.useInternalTorrentClient}
          onChange={(e) => onChange({ ...settings, useInternalTorrentClient: e.currentTarget.checked })}
        />
        <Checkbox
          label="Download direct links with the built-in downloader"
          description="Off: open them in the browser."
          checked={downloads.useInternalDirectDownloader}
          onChange={(e) => set({ useInternalDirectDownloader: e.currentTarget.checked })}
        />
      </Stack>

      <Stack gap="xs">
        <Title order={3}>Download folder</Title>
        <Group align="flex-end" wrap="nowrap">
          <TextInput
            style={{ flex: 1 }}
            description="Each download gets a folder of its own inside. Empty = ~/Downloads/Popcorn."
            placeholder="~/Downloads/Popcorn"
            value={folder}
            onChange={(e) => setFolder(e.currentTarget.value)}
            onBlur={() => folder !== downloads.downloadFolder && set({ downloadFolder: folder.trim() })}
          />
          <Button
            variant="light"
            leftSection={<IconFolderOpen size={16} />}
            onClick={() => client.sendEvent(SocketRequests.PickDownloadFolder)}
          >
            Browse
          </Button>
        </Group>
      </Stack>

      <Stack gap="xs">
        <Title order={3}>Seeding</Title>
        <Text size="sm" c="dimmed">
          A finished torrent keeps seeding until either limit is reached, then it stops. Both 0 = stop as soon as the
          download is complete.
        </Text>
        <Group align="flex-start">
          {number('seedRatioLimit', 'Seed ratio limit', 'Uploaded / size, e.g. 1 = upload as much as was downloaded.', {
            decimalScale: 2,
            step: 0.1,
          })}
          {number('seedTimeLimitMinutes', 'Seed time limit (minutes)', '0 = no time limit.')}
        </Group>
      </Stack>

      <Stack gap="xs">
        <Title order={3}>Connections</Title>
        <Group align="flex-start">
          {number('listenPort', 'Listen port', 'Incoming torrent connections (TCP/UDP).', { max: 65535 })}
          {number('maxConnections', 'Max connections', 'Peers across all torrents.', { min: 1 })}
        </Group>
      </Stack>
    </Stack>
  );
}
