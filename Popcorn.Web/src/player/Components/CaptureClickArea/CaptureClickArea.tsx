import React, { useRef } from 'react';
import './CaptureClickArea.css';
import { postMessage } from '../../../utils/events';
import { Text } from '@mantine/core';
import { isAudioFormat, isSubtitleFormat } from '../../../utils/file.utils';
import { usePlayerFeatures } from '../../player-features';

export interface CaptureClickAreaProps {
  isFloating: boolean;
}

export const CaptureClickArea: React.FC<CaptureClickAreaProps> = ({ isFloating }) => {
  const clickTimeoutRef = useRef<NodeJS.Timeout>(null);
  const [isFileDragging, setIsFileDragging] = React.useState(false);
  const { fileDrop } = usePlayerFeatures();

  const handleClick = () => {
    if (clickTimeoutRef.current) {
      clearTimeout(clickTimeoutRef.current);
    }

    if (isFloating) return;

    clickTimeoutRef.current = setTimeout(() => {
      postMessage('playback_toggle');
      clickTimeoutRef.current = null;
    }, 250);
  };

  const handleDoubleClick = () => {
    if (clickTimeoutRef.current) {
      clearTimeout(clickTimeoutRef.current);
      clickTimeoutRef.current = null;
    }
    postMessage('request_fullscreen_toggle');
  };

  const handleDragOver = (e: React.DragEvent<HTMLDivElement>) => {
    e.preventDefault(); // Needed to allow drop
    e.stopPropagation();

    setIsFileDragging(true);
  };

  const handleDragLeave = (e: React.DragEvent<HTMLDivElement>) => {
    e.preventDefault();
    e.stopPropagation();

    setIsFileDragging(false);
  };

  const handleDrop = (e: React.DragEvent<HTMLDivElement>) => {
    e.preventDefault();
    e.stopPropagation();
    setIsFileDragging(false);

    const files = Array.from(e.dataTransfer.files);
    for (const file of files) {
      if (isSubtitleFormat(file.name)) {
        postMessage('subtitle_dropped', file);
      } else if (isAudioFormat(file.name)) {
        postMessage('audio_dropped', file);
      } else {
        postMessage('player_error', `Unsupported file type dropped: ${file.name}`);
      }
    }
  };

  return (
    <div
      className={`capture-click-area ${isFileDragging ? 'drag-over' : ''}`}
      tabIndex={-1}
      onClick={handleClick}
      onDoubleClick={handleDoubleClick}
      {...(fileDrop && { onDragOver: handleDragOver, onDragLeave: handleDragLeave, onDrop: handleDrop })}
    >
      {isFileDragging && (
        <div className="drop-text" style={{ color: 'white' }}>
          <Text size="xl">Drop subtitle or audio file to add to the video</Text>
        </div>
      )}
    </div>
  );
};
