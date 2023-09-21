import React, { useEffect, useRef } from 'react';
import './App.css';
import ControlBar from './Components/ControlBar';
import { eventHandler, initEvents } from './events';
import { disableDefaultListeners } from './Utlis/helpers';

const App = () => {
  const videoContainerRef = useRef(null);

  window?.chrome?.webview?.addEventListener('message', eventHandler);

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
    </div>
  );
};

export default App;
