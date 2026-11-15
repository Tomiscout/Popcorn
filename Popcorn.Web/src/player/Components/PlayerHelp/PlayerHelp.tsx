import React, { useState } from 'react';
import { Kbd, Modal, Table, Text } from '@mantine/core';
import { IconHelp } from '@tabler/icons-react';
import './PlayerHelp.css';

interface Hotkey {
  keys: string[];
  action: string;
}

/**
 * Keep in sync with the `useEventListeners('keyup', ...)` maps (PlaybackControls, VolumeControl, TrackControl,
 * SkipButton), the Escape handler in PopcornPlayer and the keys of SubtitleSync.
 */
const sections: { title: string; hotkeys: Hotkey[] }[] = [
  {
    title: 'Playback',
    hotkeys: [
      { keys: ['Space'], action: 'Play / pause' },
      { keys: ['←'], action: 'Back 10 seconds' },
      { keys: ['→'], action: 'Forward 10 seconds' },
      { keys: ['Enter'], action: 'Skip intro / outro' },
      { keys: ['F'], action: 'Toggle fullscreen' },
      { keys: ['Esc'], action: 'Cancel auto-skip, leave fullscreen, else close the player' },
    ],
  },
  {
    title: 'Audio and subtitles',
    hotkeys: [
      { keys: ['↑'], action: 'Volume up 10%' },
      { keys: ['↓'], action: 'Volume down 10%' },
      { keys: ['M'], action: 'Mute / unmute' },
      { keys: ['A'], action: 'Next audio track' },
      { keys: ['C', 'S'], action: 'Next subtitle track (then off)' },
      { keys: ['Q'], action: 'Open / close subtitle sync' },
    ],
  },
  {
    title: 'Subtitle sync (while open)',
    hotkeys: [
      { keys: ['Z'], action: 'Subtitles 50 ms earlier (Shift: 10 ms)' },
      { keys: ['X'], action: 'Subtitles 50 ms later (Shift: 10 ms)' },
      { keys: ['S'], action: 'Start the selected (else nearest) line at the playhead' },
      { keys: ['Esc'], action: 'Close the syncer' },
    ],
  },
];

/** Top bar button with a centred dialog listing the player's keyboard shortcuts. */
export const PlayerHelp: React.FC = () => {
  // Rendered into the player container like the files dialog: a portal in <body> is hidden in browser fullscreen.
  const [container, setContainer] = useState<HTMLElement | null>(null);
  const close = () => setContainer(null);

  // Player shortcuts listen for keyup on the document: keep the dialog's keys away from them.
  const onKeyUp = (e: React.KeyboardEvent) => {
    e.stopPropagation();
    if (e.key === 'Escape') close();
  };

  return (
    <>
      <button
        className="control-button"
        title="Keyboard shortcuts"
        onClick={(e) => setContainer(e.currentTarget.closest<HTMLElement>('.video-container') ?? document.body)}
      >
        <IconHelp />
      </button>
      <Modal
        opened={container !== null}
        onClose={close}
        title="Keyboard shortcuts"
        centered
        size="md"
        zIndex={50}
        closeOnEscape={false}
        portalProps={{ target: container ?? undefined }}
        overlayProps={{ backgroundOpacity: 0.55, blur: 2 }}
        classNames={{ root: 'player-help' }}
        onKeyUp={onKeyUp}
      >
        {sections.map((section) => (
          <div key={section.title} className="player-help__section">
            <Text fw={600} size="sm" mb={4}>
              {section.title}
            </Text>
            <Table verticalSpacing={4} withRowBorders={false} fz="sm">
              <Table.Tbody>
                {section.hotkeys.map((hotkey) => (
                  <Table.Tr key={hotkey.action}>
                    <Table.Td className="player-help__keys">
                      {hotkey.keys.map((key, i) => (
                        <React.Fragment key={key}>
                          {i > 0 && ' / '}
                          <Kbd>{key}</Kbd>
                        </React.Fragment>
                      ))}
                    </Table.Td>
                    <Table.Td>{hotkey.action}</Table.Td>
                  </Table.Tr>
                ))}
              </Table.Tbody>
            </Table>
          </div>
        ))}
      </Modal>
    </>
  );
};
