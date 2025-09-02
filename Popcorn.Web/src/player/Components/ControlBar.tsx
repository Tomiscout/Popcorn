import React, { useEffect, useRef } from 'react';
import ProgressBar from './ProgressBar';
import PlaybackControls from './PlaybackControls';
import '../Styles/ControlBar.css';
import { MediaFileMetadata } from '../../query/models/video.models';

export interface ControlBarProps {
  showControls: boolean;
  metadata: MediaFileMetadata;
}

export const ControlBar: React.FC<ControlBarProps> = ({ showControls }) => {
  return (
    <div style={{ opacity: showControls ? '1' : '0' }} className="controls-root">
      <div className="fading-background" />
      <div className="controls-container">
        <ProgressBar />
        <PlaybackControls />
      </div>
    </div>
  );
};
