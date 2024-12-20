import React, { useState, useEffect } from 'react';
import { registerEventHandler } from '../events';
import { ReactComponent as MuteVolume } from '../assets/icons/sound-mute.svg';
import { ReactComponent as RewindButton } from '../assets/icons/10-back.svg';
import { ReactComponent as ForwardButton } from '../assets/icons/10-forward.svg';
import { ReactComponent as PlayButton } from '../assets/icons/play.svg';
import { ReactComponent as StopButton } from '../assets/icons/stop.svg';
import { ReactComponent as NextTrack } from '../assets/icons/next.svg';
import '../Styles/PlaybackNotificationControl.css';

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
        stop: <StopButton />,
        nextTrack: <NextTrack />,
    };

    useEffect(() => {
        registerHandler('volume', (volume) => volume <= 0 ? setCurrentIcon(icons.mute) : setMessage(`${parseInt(volume * 100)}%`));

        registerHandler('skip_seconds', (seconds) => seconds < 0 ? setCurrentIcon(icons.rewind) : setCurrentIcon(icons.forward));

        registerHandler('play_status_changed', (isPlaying) => isPlaying ? setCurrentIcon(icons.play) : setCurrentIcon(icons.stop));

        registerHandler('play_next_title', () => setCurrentIcon(icons.nextTrack));

        return () => clearTimeout(hideNotificationTimeout); // Clear timeout on unmount
    });

    const registerHandler = (eventName, callback) => {
        registerEventHandler(eventName, (data) => {
            resetContent();
            callback(data);
            showNotificationBox();
        });
    }

    const showNotificationBox = () => {
        setShowNotification(true);
        setNotificationKey(prevKey => prevKey + 1); // Increment key to force re-render
        clearTimeout(hideNotificationTimeout);
        hideNotificationTimeout = setTimeout(() => setShowNotification(false), 1500);
    }

    const resetContent = () => {
        setCurrentIcon(null);
        setMessage(null);
    }

    if (!showNotification || (!currentIcon && !message)) return null;

    return (
        <div key={notificationKey} className="notification-container">
            {currentIcon}
            {message && <span>{message}</span>}
        </div>
    );
};

export default PlaybackNotificationControl;