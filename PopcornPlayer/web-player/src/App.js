import React, { useEffect, useRef } from 'react';
import './App.css';
import ControlBar from './Components/ControlBar';
import { eventHandler, bufferEventHandler, initEvents, postMessageLocalOnly } from './events';
import { disableDefaultListeners } from './Utlis/helpers';
import NotificationControl from './Components/NotificationControl';

const App = () => {
  const videoContainerRef = useRef(null);

  window?.chrome?.webview?.addEventListener('message', eventHandler);
  window?.chrome?.webview?.addEventListener('sharedbufferreceived', bufferEventHandler);

  useEffect(() => {
    disableDefaultListeners();
    initEvents();

    const videoContainer = videoContainerRef.current;
    if (videoContainer) {
      videoContainer.focus();

      videoContainer.addEventListener('dblclick', (event) => {
        if (event.target === videoContainer || event.target.className === 'notification-container') {
          postMessageLocalOnly('toggleFullscreen');
        }
      });
    }
  }, []);


  return (
    <div
      className="video-container"
      tabIndex="-1"  // Make the div focusable
      ref={videoContainerRef}
    >
      <ControlBar />
      <NotificationControl />
    </div>
  );
};

export default App;
