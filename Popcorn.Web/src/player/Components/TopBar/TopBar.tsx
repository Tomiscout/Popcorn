import React from 'react';
import { Tooltip } from '@mantine/core';
import './TopBar.css';
import { usePlayerStore } from '../../../state/player.store';
import { IconWindowMaximize, IconWindowMinimize, IconX } from '@tabler/icons-react';
import { postMessage } from '../../../utils/events';
import { usePlayerFeatures } from '../../player-features';
import { PlayerSettings } from '../PlayerSettings/PlayerSettings';

export interface TopBarProps {
  showControls: boolean;
}

/** Window buttons (browser only), the title of the current file with its season / episode and the settings menu. */
export const TopBar: React.FC<TopBarProps> = ({ showControls }) => {
  const { metadata, isFloating, isFullscreen, setIsFloating, setIsFullscreen } = usePlayerStore();
  const { floating, close } = usePlayerFeatures();

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

  const season = metadata?.season;
  const episode = metadata?.episode;
  const fileName = metadata?.filePath.split(/[\\/]/).pop() || metadata?.fullTitle;

  return (
    <div className="top-bar" style={{ opacity: showControls ? '1' : '0' }}>
      <div className="top-bar__background" />
      <div className="top-bar__content">
        {close && (
          <button className="control-button" onClick={onClose} title="Close">
            <IconX />
          </button>
        )}

        {floating && !isFloating && !isFullscreen && (
          <button className="control-button" onClick={floatWindow} title="Float">
            <IconWindowMinimize />
          </button>
        )}

        {floating && isFloating && (
          <button className="control-button" onClick={maximiseFloat} title="Expand">
            <IconWindowMaximize />
          </button>
        )}

        {metadata && !isFloating && (
          <div className="top-bar__title">
            {/* A Mantine tooltip: the native players' off-screen CEF does not show `title` attributes. */}
            <Tooltip label={fileName} multiline maw="min(90cqw, 800px)" position="bottom-start" withinPortal={false}>
              <div className="top-bar__title-text" style={{ pointerEvents: showControls ? 'auto' : 'none' }}>
                <div className="top-bar__name">{metadata.shortTitle}</div>
                {season != null || episode != null ? (
                  <div className="top-bar__episode">
                    {season != null && <span>Season {season}</span>}
                    {episode != null && <span>Episode {episode}</span>}
                  </div>
                ) : (
                  <div className="top-bar__file-name">{fileName}</div>
                )}
              </div>
            </Tooltip>
          </div>
        )}

        {!isFloating && <PlayerSettings showControls={showControls} />}
      </div>
    </div>
  );
};
