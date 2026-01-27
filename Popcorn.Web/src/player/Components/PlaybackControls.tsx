import { useState, useEffect } from 'react';
import {
  IconPlayerPlayFilled as PlayButton,
  IconPlayerPauseFilled as PauseButton,
  IconRewindBackward10 as RewindButton,
  IconRewindForward10 as ForwardButton,
  IconPlayerSkipForwardFilled as NextTrack,
  IconArrowsMaximize as Fullscreen,
  IconArrowsMinimize as ExitFullscreen,
} from '@tabler/icons-react';
import '../Styles/PlaybackControls.css';

import VolumeControl from './VolumeControl';
import EpisodesControl from './EpisodesControl';
import TrackControl from './TrackControl';
import { TrackTitle } from './TrackTitle';
import { postMessage, registerEventHandler, unregisterEventHandler } from '../../utils/events';

import { useEventListeners } from '../Utlis/listener.utils';
import { usePlayerStore } from '../../state/player.store';

const PlaybackControls = () => {
  const { isFloating, isFullscreen, isPlaying, setIsFullscreen, setIsFloating } = usePlayerStore();
  const [floatingBeforeFullscreen, setFloatingBeforeFullscreen] = useState(false);

  const playPause = () => {
    const { isPlaying } = usePlayerStore.getState();
    postMessage('play_status_changed', !isPlaying);
  };
  const skipBackward = () => {
    postMessage('skip_seconds', -10);
  };
  const skipForward = () => {
    postMessage('skip_seconds', 10);
  };
  const nextTrack = () => {
    postMessage('play_next_title', 10);
  };
  const toggleFullscreen = () => {
    const newFullscreen = !isFullscreen;
    if (newFullscreen) {
      setFloatingBeforeFullscreen(isFloating);
      setIsFloating(false);
    }
    setIsFullscreen(newFullscreen);
  };
  const exitFullscreen = () => {
    setIsFullscreen(false);
    setIsFloating(floatingBeforeFullscreen);
  };

  useEventListeners('keyup', {
    Space: playPause,
    Escape: exitFullscreen,
    ArrowLeft: skipBackward,
    ArrowRight: skipForward,
    KeyF: toggleFullscreen,
  });

  useEffect(() => {
    // registerWebviewEventHandler('track_loaded', trackLoadedHandler);
    // registerWebviewEventHandler('fullscreen', fullscreenHandler);
    registerEventHandler('playback_toggle', playPause);
    registerEventHandler('request_fullscreen_toggle', toggleFullscreen);

    return () => {
      // unregisterWebviewEventHandler('track_loaded', trackLoadedHandler);
      // unregisterWebviewEventHandler('fullscreen', fullscreenHandler);
      unregisterEventHandler('playback_toggle', playPause);
      unregisterEventHandler('request_fullscreen_toggle', toggleFullscreen);
      unregisterEventHandler('floating_window', toggleFullscreen);
    };
  }, []);

  const getFullscreenButton = () => (isFullscreen ? <ExitFullscreen /> : <Fullscreen />);

  return (
    <div className="playback-controls">
      <button className="control-button play-pause" onClick={playPause}>
        {isPlaying ? <PauseButton /> : <PlayButton />}
      </button>

      {!isFloating && (
        <>
          <div className="control-button rewind" onClick={skipBackward}>
            <RewindButton />
          </div>
          <div className="control-button fast-forward" onClick={skipForward}>
            <ForwardButton />
          </div>
          <VolumeControl />
          <TrackTitle />

          <div className="control-button next" onClick={nextTrack}>
            <NextTrack />
          </div>
          <EpisodesControl />
          <TrackControl />
        </>
      )}

      <button className="control-button fullscreen" onClick={toggleFullscreen}>
        {getFullscreenButton()}
      </button>
    </div>
  );
};

export default PlaybackControls;
