import React, { useState, useEffect } from 'react';
import './TopBar.css';
import { Box, Button, CloseButton } from '@mantine/core';
import { usePlayerStore } from '../../../state/player.store';
import { IconWindowMaximize, IconWindowMinimize, IconX } from '@tabler/icons-react';

export interface TopBarProps {
  showControls: boolean;
}

export const TopBar: React.FC<TopBarProps> = ({ showControls }) => {
  const { isFloating, isFullscreen, resetState, setIsFloating, setIsFullscreen } = usePlayerStore();

  function onClose() {
    resetState();
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

        {!isFloating && !isFullscreen && (
          <Box className="control-button" onClick={floatWindow}>
            <IconWindowMinimize />
          </Box>
        )}

        {isFloating && (
          <Box className="control-button" onClick={maximiseFloat}>
            <IconWindowMaximize />
          </Box>
        )}
      </div>
    </div>
  );
};
