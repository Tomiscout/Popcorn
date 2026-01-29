import { useState, useEffect } from 'react';
import { registerEventHandler } from '../../../utils/events';

import {
  IconPlayerPlayFilled as PlayButton,
  IconPlayerPauseFilled as PauseButton,
  IconRewindBackward10 as RewindButton,
  IconRewindForward10 as ForwardButton,
  IconPlayerSkipForwardFilled as NextTrack,
  IconArrowsMaximize as Fullscreen,
  IconArrowsMinimize as ExitFullscreen,
  IconVolume3 as MuteVolume,
} from '@tabler/icons-react';

import './PlaybackNotificationControl.css';

const PlaybackNotificationControl = () => {
  const [showNotification, setShowNotification] = useState(false);
  const [message, setMessage] = useState('');
  const [currentIcon, setCurrentIcon] = useState(null);
  const [notificationKey, setNotificationKey] = useState(0); // Add a key state
  let hideNotificationTimeout = null;

  const icons = {
    mute: <MuteVolume />,
    rewind: <RewindButton />,
    forward: <ForwardButton />,
    play: <PlayButton />,
    stop: <PauseButton />,
    nextTrack: <NextTrack />,
  };

  useEffect(() => {
    registerHandler('volume', (volume) =>
      volume <= 0 ? setCurrentIcon(icons.mute) : setMessage(`${parseInt(volume * 100)}%`)
    );

    registerHandler('skip_seconds', (seconds) =>
      seconds < 0 ? setCurrentIcon(icons.rewind) : setCurrentIcon(icons.forward)
    );

    registerHandler('play_status_changed', (isPlaying) =>
      isPlaying ? setCurrentIcon(icons.play) : setCurrentIcon(icons.stop)
    );

    registerHandler('play_next_title', () => setCurrentIcon(icons.nextTrack));

    return () => clearTimeout(hideNotificationTimeout); // Clear timeout on unmount
  });

  const registerHandler = (eventName, callback) => {
    registerEventHandler(eventName, (data) => {
      resetContent();
      callback(data);
      showNotificationBox();
    });
  };

  const showNotificationBox = () => {
    setShowNotification(true);
    setNotificationKey((prevKey) => prevKey + 1); // Increment key to force re-render
    clearTimeout(hideNotificationTimeout);
    hideNotificationTimeout = setTimeout(() => setShowNotification(false), 1500);
  };

  const resetContent = () => {
    setCurrentIcon(null);
    setMessage(null);
  };

  if (!showNotification || (!currentIcon && !message)) return null;

  return (
    <div key={notificationKey} className="notification-container">
      {currentIcon}
      {message && <span>{message}</span>}
    </div>
  );
};

export default PlaybackNotificationControl;
