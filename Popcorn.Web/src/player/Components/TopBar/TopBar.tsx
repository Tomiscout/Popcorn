import React from 'react';
import './TopBar.css';
import { Box } from '@mantine/core';
import { usePlayerStore } from '../../../state/player.store';
import { IconWindowMaximize, IconWindowMinimize, IconX } from '@tabler/icons-react';
import { postMessage } from '../../../utils/events';
import { usePlayerFeatures } from '../../player-features';

export interface TopBarProps {
  showControls: boolean;
}

export const TopBar: React.FC<TopBarProps> = ({ showControls }) => {
  const { isFloating, isFullscreen, setIsFloating, setIsFullscreen } = usePlayerStore();
  const { floating } = usePlayerFeatures();

  function onClose() {
    postMessage('player_close');
  }

  function floatWindow() {
    setIsFullscreen(false);
    setIsFloating(true);
  }

  function maximiseFloat() {
    setIsFloating(false);
    setIsFullscreen(false);
  }

  return (
    <div className="top-bar" style={{ opacity: showControls ? '1' : '0' }}>
      <div className="top-bar__background" />
      <div className="top-bar__content">
        <Box className="control-button" onClick={onClose}>
          <IconX />
        </Box>

        {floating && !isFloating && !isFullscreen && (
          <Box className="control-button" onClick={floatWindow}>
            <IconWindowMinimize />
          </Box>
        )}

        {floating && isFloating && (
          <Box className="control-button" onClick={maximiseFloat}>
            <IconWindowMaximize />
          </Box>
        )}
      </div>
    </div>
  );
};
