import { useState, useRef } from 'react';
import {
  IconVolume2 as LowVolume,
  IconVolume as MidVolume,
  IconVolume as FullVolume,
  IconVolume3 as MuteVolume,
} from '@tabler/icons-react';

import { usePlayerStore } from '../../../state/player.store';
import { setPlayerVolume, togglePlayerMute } from '../../volume';
import './VolumeSlider.css';
import { useEventListeners } from '../../Utlis/listener.utils';

const VolumeControl = () => {
  const volumePopupHalfWidth = 20; //volumePopup.offsetWidth is not present on first render

  const volume = usePlayerStore((s) => s.volume);
  const muted = usePlayerStore((s) => s.muted);
  const [showPopup, setShowPopup] = useState(false);
  const volumeRef = useRef<HTMLDivElement>(null);
  const volumePopupRef = useRef<HTMLDivElement>(null);

  const onToggleMute = togglePlayerMute;
  const changeVolume = setPlayerVolume;
  // Store at call time: the key handlers are registered once.
  const upHandler = () => setPlayerVolume(usePlayerStore.getState().volume + 0.1);
  const downHandler = () => setPlayerVolume(usePlayerStore.getState().volume - 0.1);

  const handleMouseEnter = () => {
    const volumeElement = volumeRef.current;
    const volumePopup = volumePopupRef.current;
    if (!volumeElement || !volumePopup) return;

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
            onChange={(e) => changeVolume(parseFloat(e.target.value))}
            style={getBackgroundSize()}
            className="volume-slider"
            value={volume}
          />
        </div>
      </div>
      <div className="control-button" id="volume-button" onClick={onToggleMute}>
        {getVolumeIcon()}
      </div>
    </div>
  );
};

export default VolumeControl;
