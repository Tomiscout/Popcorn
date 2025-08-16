import React, { useState, useEffect } from 'react';
import {
  IconPlayerPlayFilled as PlayButton,
  IconPlayerPauseFilled as PauseButton,
  IconRewindBackward10 as RewindButton,
  IconRewindForward10 as ForwardButton,
  IconPlayerSkipForwardFilled as NextTrack,
  IconArrowsMaximize as Fullscreen,
  IconArrowsMinimize as ExitFullscreen,
} from '@tabler/icons-react';

import VolumeControl from './VolumeControl';
import EpisodesControl from './EpisodesControl';
import TrackControl from './SubtitleControl';
import TrackTitle from './TrackTitle';
import { postMessage, registerEventHandler, unregisterEventHandler } from '../../utils/events';

import { useEventListeners } from '../Utlis/listener.utils';
import { usePlayerStore } from '../../state/player.store';

const PlaybackControls = () => {
  const { isFloating, isFullscreen, isPlaying, setIsFullscreen } = usePlayerStore();

  const playPause = () => {
    postMessage('play_status_changed', !isPlaying);
    // setIsPlaying(!isPlaying);
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
    setIsFullscreen(!isFullscreen);
  };
  const exitFullscreen = () => {
    setIsFullscreen(false);
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

  const getPlayButton = () => (isPlaying ? <PauseButton /> : <PlayButton />);
  const getFullscreenButton = () => (isFullscreen ? <ExitFullscreen /> : <Fullscreen />);

  return (
    <div className="controls">
      <button className="main-button play-pause" onClick={playPause}>
        {getPlayButton()}
      </button>

      {!isFloating && (
        <>
          <button className="main-button rewind" onClick={skipBackward}>
            <RewindButton />
          </button>
          <button className="main-button fast-forward" onClick={skipForward}>
            <ForwardButton />
          </button>
          <VolumeControl />
          <TrackTitle />

          <button className="main-button next" onClick={nextTrack}>
            <NextTrack />
          </button>
          <EpisodesControl />
          <TrackControl />
        </>
      )}

      <button className="main-button fullscreen" onClick={toggleFullscreen}>
        {getFullscreenButton()}
      </button>
    </div>
  );
};

export default PlaybackControls;
