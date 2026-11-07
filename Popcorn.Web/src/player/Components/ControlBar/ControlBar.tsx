import React from 'react';
import ProgressBar from '../ProgressBar/ProgressBar';
import PlaybackControls from '../PlaybackControls/PlaybackControls';
import './ControlBar.css';

export interface ControlBarProps {
  showControls: boolean;
}

export const ControlBar: React.FC<ControlBarProps> = ({ showControls }) => {
  return (
    // Hidden controls fade out, the intro/outro skip button inside ProgressBar stays (its count-down must be seen).
    <div className={`controls-root${showControls ? '' : ' controls-hidden'}`}>
      <div className="fading-background" />
      <div className="controls-container">
        <ProgressBar />
        <PlaybackControls />
      </div>
    </div>
  );
};
