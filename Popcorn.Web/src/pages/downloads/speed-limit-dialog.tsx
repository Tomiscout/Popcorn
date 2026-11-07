import { Button, Modal, NumberInput, SimpleGrid, Skeleton, Stack, Text } from '@mantine/core';
import { useEffect, useState } from 'react';
import { DownloadSettings } from '../../model/settings.contract';
import { SocketRequests } from '../../socket/socket-requests';
import { useSettings, useSettingsStore } from '../../state/settings.store';
import { useWebSocket } from '../../state/websocket-context';
import { formatRate } from './format';

/** KiB/s, 0 = unlimited. */
const PRESETS = [0, 256, 512, 1024, 2048, 5120, 10240, 25600];

function LimitPicker({ label, value, onChange }: { label: string; value: number; onChange: (kiBps: number) => void }) {
  // The custom field is saved when it loses focus or on Enter, not on every key.
  const [custom, setCustom] = useState<number | string>(value || '');
  useEffect(() => setCustom(value || ''), [value]);
  const saveCustom = () => {
    const kiBps = Number(custom) || 0;
    if (kiBps !== value) onChange(kiBps);
  };

  return (
    <Stack gap="xs">
      <Text fw={500}>{label}</Text>
      <SimpleGrid cols={4} spacing="xs">
        {PRESETS.map((preset) => (
          <Button
            key={preset}
            size="xs"
            px={4}
            variant={preset === value ? 'filled' : 'default'}
            onClick={() => onChange(preset)}
          >
            {preset ? formatRate(preset * 1024) : 'Unlimited'}
          </Button>
        ))}
      </SimpleGrid>
      <NumberInput
        size="xs"
        aria-label={`${label}, custom (KB/s)`}
        placeholder="Custom"
        suffix=" KB/s"
        min={0}
        allowDecimal={false}
        allowNegative={false}
        value={custom}
        onChange={setCustom}
        onBlur={saveCustom}
        onKeyDown={(e) => e.key === 'Enter' && saveCustom()}
      />
    </Stack>
  );
}

/** Download / upload speed limits, opened from the speeds at the bottom of the nav bar. Changes apply at once. */
export function SpeedLimitDialog({ opened, onClose }: { opened: boolean; onClose: () => void }) {
  const { client } = useWebSocket();
  const settings = useSettings();

  const set = (patch: Partial<DownloadSettings>) => {
    // Read the store, not the render's copy: the two pickers can save in the same tick (blur + click).
    const current = useSettingsStore.getState().settings;
    if (!current) return;
    const updated = { ...current, downloads: { ...current.downloads, ...patch } };
    useSettingsStore.setState({ settings: updated });
    client.sendEvent(SocketRequests.UpdateSettings, updated);
  };

  return (
    <Modal opened={opened} onClose={onClose} title="Speed limits" size="md">
      {settings ? (
        <Stack>
          <LimitPicker
            label="Download"
            value={settings.downloads.maxDownloadKiBps}
            onChange={(maxDownloadKiBps) => set({ maxDownloadKiBps })}
          />
          <LimitPicker
            label="Upload"
            value={settings.downloads.maxUploadKiBps}
            onChange={(maxUploadKiBps) => set({ maxUploadKiBps })}
          />
          <Text size="xs" c="dimmed">
            The download limit is shared by torrents and direct downloads.
          </Text>
        </Stack>
      ) : (
        <Skeleton height={200} />
      )}
    </Modal>
  );
}
