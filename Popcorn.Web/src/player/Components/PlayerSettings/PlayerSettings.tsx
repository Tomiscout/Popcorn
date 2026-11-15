import React, { useEffect, useState } from 'react';
import { Popover, Radio, Stack, Table, Tabs, Text } from '@mantine/core';
import { IconSettings } from '@tabler/icons-react';
import { usePlayerStore } from '../../../state/player.store';
import { postMessage } from '../../../utils/events';
import { SegmentAutoSkip, segmentAutoSkipOptions } from '../../../model/segments.contract';
import { AutoSkipDelayInput } from '../../../components/auto-skip-delay-input';
import './PlayerSettings.css';

/** Auto-skip override of the playing file's library item, and the (global) auto-skip delay. */
const AutoSkipTab: React.FC = () => {
  const autoSkip = usePlayerStore((s) => s.segmentAutoSkip);
  const title = usePlayerStore((s) => s.metadata?.shortTitle);
  const setItemAutoSkip = usePlayerStore((s) => s.setItemAutoSkip);
  const delay = usePlayerStore((s) => s.autoSkipDelay);
  const setAutoSkipDelay = usePlayerStore((s) => s.setAutoSkipDelay);
  if (!autoSkip) return null;

  const onDelayChange = (seconds: number) => {
    setAutoSkipDelay(seconds);
    postMessage('segment_auto_skip_delay_set', seconds);
  };

  // No override shows the global mode; picking any option stores it for the item.
  const onChange = (value: string) => {
    const mode = value as SegmentAutoSkip;
    setItemAutoSkip(mode);
    postMessage('segment_auto_skip_set', mode);
  };

  return (
    <Stack gap="md">
      {autoSkip.itemId && (
        <div>
          <Text fw={600} mb={4}>
            Auto-skip intros and outros
          </Text>
          <Text size="xs" c="dimmed" mb="sm">
            For {title}
          </Text>
          <Radio.Group value={autoSkip.itemMode ?? autoSkip.globalMode} onChange={onChange}>
            <Stack gap="xs">
              {segmentAutoSkipOptions.map((o) => (
                <Radio key={o.value} value={o.value} label={o.label} description={o.description} />
              ))}
            </Stack>
          </Radio.Group>
        </div>
      )}
      <AutoSkipDelayInput
        value={delay}
        onChange={onDelayChange}
        description="For all media. 0 skips an intro or outro as soon as it starts."
        w="100%"
      />
    </Stack>
  );
};

/** File, codec and stream details from ffprobe (same data from both hosts). */
const MediaInfoTab: React.FC = () => {
  const info = usePlayerStore((s) => s.metadata?.mediaInfo);
  if (!info) return <Text size="sm">No media info for this file.</Text>;

  return (
    <Stack gap="md">
      {info.sections.map((section) => (
        <div key={section.title}>
          <Text fw={600} size="sm" mb={4}>
            {section.title}
          </Text>
          <Table verticalSpacing={2} horizontalSpacing={4} withRowBorders={false} fz="xs">
            <Table.Tbody>
              {section.rows.map((row) => (
                <Table.Tr key={row.label}>
                  <Table.Td className="player-settings__label">{row.label}</Table.Td>
                  <Table.Td className="player-settings__value">{row.value}</Table.Td>
                </Table.Tr>
              ))}
            </Table.Tbody>
          </Table>
        </div>
      ))}
    </Stack>
  );
};

/** Top bar settings menu. */
export const PlayerSettings: React.FC<{ showControls: boolean }> = ({ showControls }) => {
  const hasAutoSkip = usePlayerStore((s) => !!s.segmentAutoSkip);
  const hasFile = usePlayerStore((s) => !!s.metadata);
  const setHoldControls = usePlayerStore((s) => s.setHoldControls);
  const [opened, setOpened] = useState(false);
  const [tab, setTab] = useState<string | null>('autoskip');

  // Reading the media info takes longer than the controls stay up.
  useEffect(() => {
    setHoldControls(opened);
    return () => setHoldControls(false);
  }, [opened, setHoldControls]);

  useEffect(() => {
    if (!showControls) setOpened(false);
  }, [showControls]);

  if (!hasFile) return null;

  return (
    // Not portalled: the browser player's fullscreen element is the overlay, a portal in <body> would be hidden.
    <Popover opened={opened} onChange={setOpened} position="bottom-end" withinPortal={false} shadow="md">
      {/* Anchored on a wrapper: the button scales on hover, which would move the dropdown with it. */}
      <Popover.Target>
        <div className="player-settings__anchor">
          <button className="control-button" title="Settings" onClick={() => setOpened((o) => !o)}>
            <IconSettings />
          </button>
        </div>
      </Popover.Target>
      {/* Player shortcuts listen for keyup on the document: keep the menu's keys (Space on a radio) away from them. */}
      <Popover.Dropdown className="player-settings__dropdown" onKeyUp={(e) => e.stopPropagation()}>
        <Tabs value={hasAutoSkip ? tab : 'mediainfo'} onChange={setTab} keepMounted={false}>
          <Tabs.List mb="sm">
            {hasAutoSkip && <Tabs.Tab value="autoskip">Auto-skip</Tabs.Tab>}
            <Tabs.Tab value="mediainfo">Media info</Tabs.Tab>
          </Tabs.List>
          <div className="player-settings__panel">
            {hasAutoSkip && (
              <Tabs.Panel value="autoskip">
                <AutoSkipTab />
              </Tabs.Panel>
            )}
            <Tabs.Panel value="mediainfo">
              <MediaInfoTab />
            </Tabs.Panel>
          </div>
        </Tabs>
      </Popover.Dropdown>
    </Popover>
  );
};
