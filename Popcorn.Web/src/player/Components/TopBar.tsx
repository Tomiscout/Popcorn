import React, { useState, useEffect } from 'react';
import '../Styles/TopBar.css';
import { Button, CloseButton } from '@mantine/core';
import { usePlayerStore } from '../../state/player.store';
import { IconWindowMaximize, IconWindowMinimize } from '@tabler/icons-react';
import { registerEventHandler, unregisterEventHandler } from '../../utils/events';

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
      <CloseButton onClick={onClose} />

      {!isFloating && !isFullscreen && <CloseButton onClick={floatWindow} icon={<IconWindowMinimize />} />}
      {isFloating && <CloseButton onClick={maximiseFloat} icon={<IconWindowMaximize />} />}

      {/* <span className="top-bar__title">My Video App</span> */}
    </div>
  );
};
