import React, { useRef } from 'react';
import { postLocalMessage } from '../events';
import '../Styles/CaptureClickArea.css';

const CaptureClickArea = () => {
  const clickTimeoutRef = useRef(null);

  const handleClick = () => {
    if (clickTimeoutRef.current) {
      clearTimeout(clickTimeoutRef.current);
    }

    clickTimeoutRef.current = setTimeout(() => {
      postLocalMessage('playback_toggle');
      clickTimeoutRef.current = null;
    }, 250);
  };

  const handleDoubleClick = () => {
    if (clickTimeoutRef.current) {
      clearTimeout(clickTimeoutRef.current);
      clickTimeoutRef.current = null;
    }
    postLocalMessage('request_fullscreen_click');
  };

  return (
    <div
      className="capture-click-area"
      onClick={handleClick}
      onDoubleClick={handleDoubleClick}
    />
  );
};

export default CaptureClickArea;
