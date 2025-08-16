import React, { useState, useRef } from 'react';
import {
  IconVolume2 as LowVolume,
  IconVolume as MidVolume,
  IconVolume as FullVolume,
  IconVolume3 as MuteVolume,
} from '@tabler/icons-react';

import { postMessage } from '../../utils/events';
import '../Styles/VolumeSlider.css';
import { useEventListeners } from '../Utlis/listener.utils';

const VolumeControl = () => {
  const volumePopupHalfWidth = 20; //volumePopup.offsetWidth is not present on first render

  const [volume, setVolume] = useState(1);
  const [muted, setMuted] = useState(false);
  const [showPopup, setShowPopup] = useState(false);
  const volumeRef = useRef(null);
  const volumePopupRef = useRef(null);

  // const throttledPostMessage = useMemo(() => throttle((type, value) => {
  //   postMessage(type, value);
  // }, 50), []);

  const onToggleMute = () => {
    const newMutedState = !muted;
    setMuted(newMutedState);
    postMessage('volume', newMutedState ? 0 : volume);
  };
  const changeVolume = (vol) => {
    if (muted) {
      setMuted(false);
    }
    vol = parseFloat(Math.max(0, Math.min(vol, 1)).toFixed(2));

    setVolume(vol);
    postMessage('volume', vol);
    //throttledPostMessage('volume', vol); Deadlock is fixed now? maybe dont need this anymore
  };

  const upHandler = () => {
    changeVolume(volume + 0.1);
  };
  const downHandler = () => {
    changeVolume(volume - 0.1);
  };

  const handleMouseEnter = () => {
    const volumeElement = volumeRef.current;
    const volumePopup = volumePopupRef.current;
    const rect = volumeElement.getBoundingClientRect();

    const left = rect.left + rect.width / 2 - volumePopupHalfWidth;
    const bottom = rect.height;

    volumePopup.style.left = `${left}px`;
    volumePopup.style.bottom = `${bottom}px`;
    setShowPopup(true);
  };

  const handleMouseLeave = () => {
    setShowPopup(false);
  };

  const getVolumeIcon = () => {
    if (muted || volume <= 0) {
      return <MuteVolume />;
    } else if (volume <= 0.33) {
      return <LowVolume />;
    } else if (volume <= 0.66) {
      return <MidVolume />;
    } else {
      return <FullVolume />;
    }
  };

  const getBackgroundSize = () => {
    return {
      backgroundSize: `${volume * 100}% 100%`,
    };
  };

  useEventListeners('keyup', {
    KeyM: onToggleMute,
    ArrowUp: upHandler,
    ArrowDown: downHandler,
  });

  return (
    <div id="volume" ref={volumeRef} onMouseEnter={handleMouseEnter} onMouseLeave={handleMouseLeave}>
      <div id="volume-popup" className="popup" ref={volumePopupRef} style={{ display: showPopup ? '' : 'none' }}>
        <div className="volume-slider-container">
          <input
            type="range"
            min="0"
            max="1"
            step="0.01"
            onChange={(e) => changeVolume(e.target.value)}
            style={getBackgroundSize()}
            className="volume-slider"
            value={volume}
          />
        </div>
      </div>
      <button className="main-button" id="volume-button" onClick={onToggleMute}>
        {getVolumeIcon()}
      </button>
    </div>
  );
};

export default VolumeControl;
