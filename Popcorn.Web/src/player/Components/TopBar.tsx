import React, { useState, useEffect } from 'react';
import '../Styles/TopBar.css';
import { Button, CloseButton } from '@mantine/core';
import { usePlayerStore } from '../../state/player.store';
import { IconWindowMaximize, IconWindowMinimize } from '@tabler/icons-react';

export const TopBar: React.FC = () => {
  const { isFloating, isFullscreen, setFileId, setIsFloating, setIsFullscreen } = usePlayerStore();

  function onClose() {
    setFileId(null);
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
    <div className="top-bar">
      <CloseButton onClick={onClose} />

      {!isFloating && !isFullscreen && <CloseButton onClick={floatWindow} icon={<IconWindowMinimize />} />}
      {isFloating && <CloseButton onClick={maximiseFloat} icon={<IconWindowMaximize />} />}

      {/* <span className="top-bar__title">My Video App</span> */}
    </div>
  );
};
