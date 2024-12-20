import React, { useEffect, useRef } from 'react';
import './App.css';
import ControlBar from './Components/ControlBar';
import ErrorOverlay from './Components/ErrorOverlay';
import { initEvents } from './events';
import { disableDefaultListeners } from './Utlis/helpers';
import PlaybackNotificationControl from './Components/PlaybackNotificationControl';

const App = () => {
  const videoContainerRef = useRef(null);

  useEffect(() => {
    disableDefaultListeners();
    initEvents();

    const videoContainer = videoContainerRef.current;
    if (videoContainer) {
      videoContainer.focus();
    }
  }, []);

  return (
    <div
      className="video-container"
      tabIndex="-1"  // Make the div focusable
      ref={videoContainerRef}
    >
      <ControlBar />
      <PlaybackNotificationControl />
      <ErrorOverlay />
    </div>
  );
};

export default App;
