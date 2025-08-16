import React, { useRef } from 'react';
import '../Styles/CaptureClickArea.css';
import { postMessage } from '../../utils/events';

export interface CaptureClickAreaProps {
  isFloating: boolean;
}

export const CaptureClickArea: React.FC<CaptureClickAreaProps> = ({ isFloating }) => {
  const clickTimeoutRef = useRef<NodeJS.Timeout>(null);

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

  return <div className="capture-click-area" onClick={handleClick} onDoubleClick={handleDoubleClick} />;
};
