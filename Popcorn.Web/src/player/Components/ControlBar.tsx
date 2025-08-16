import React, { useEffect, useRef } from 'react';
import ProgressBar from './ProgressBar';
import PlaybackControls from './PlaybackControls';
import { useEventListeners } from '../Utlis/listener.utils';
import '../Styles/ControlBar.css';
import { MediaFileMetadata } from '../../query/models/video.models';

export interface ControlBarProps {
  containerRef: React.RefObject<HTMLDivElement | null>;
  metadata: MediaFileMetadata;
}

export const ControlBar: React.FC<ControlBarProps> = ({ containerRef }) => {
  const controlsTimeoutRef = useRef<NodeJS.Timeout>(null);
  const controlsContainerRef = useRef<HTMLDivElement>(null);

  const displayControls = () => {
    if (controlsContainerRef?.current) {
      controlsContainerRef.current.style.opacity = '1';
    }
    containerRef.current!.style.cursor = 'initial';

    if (controlsTimeoutRef.current) {
      clearTimeout(controlsTimeoutRef.current);
    }

    controlsTimeoutRef.current = setTimeout(() => {
      if (controlsContainerRef?.current) {
        controlsContainerRef.current.style.opacity = '0';
      }
      containerRef.current!.style.cursor = 'none';
    }, 3000);
  };

  useEventListeners('keyup', { default: displayControls });
  useEventListeners('mousemove', { default: displayControls });

  return (
    <div ref={controlsContainerRef} className="controls-root" style={{ display: 'contents' }}>
      <div className="fading-background" />
      <div className="controls-container">
        <ProgressBar />
        <PlaybackControls />
      </div>
    </div>
  );
};
