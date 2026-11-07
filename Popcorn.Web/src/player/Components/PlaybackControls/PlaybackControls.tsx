import { useEffect, useRef } from 'react';
import { Tooltip } from '@mantine/core';
import {
  IconPlayerPlayFilled as PlayButton,
  IconPlayerPauseFilled as PauseButton,
  IconRewindBackward10 as RewindButton,
  IconRewindForward10 as ForwardButton,
  IconPlayerSkipBackFilled as PreviousTrack,
  IconPlayerSkipForwardFilled as NextTrack,
  IconArrowsMaximize as Fullscreen,
  IconArrowsMinimize as ExitFullscreen,
} from '@tabler/icons-react';
import './PlaybackControls.css';

import VolumeControl from '../VolumeControl/VolumeControl';
import EpisodesControl from '../EpisodesControl/EpisodesControl';
import TrackControl from '../TrackControl/TrackControl';
import { postMessage, registerEventHandler, unregisterEventHandler } from '../../../utils/events';

import { useEventListeners } from '../../Utlis/listener.utils';
import { canSeekTo, selectCurrentSegmentName, usePlayerStore } from '../../../state/player.store';
import { nextFileName, previousFileName } from '../../next-file';

/** Intro/outro or file chapter being played. Own component: it re-renders with the current time only when the name changes. */
const CurrentSegmentName = () => {
  const name = usePlayerStore(selectCurrentSegmentName);
  return name ? (
    <div className="playback-controls__segment" title={name}>
      {name}
    </div>
  ) : null;
};

const PlaybackControls = () => {
  const { isFloating, isFullscreen, isPlaying, setIsFullscreen, setIsFloating } = usePlayerStore();
  const nextName = usePlayerStore((s) => nextFileName(s.metadata?.nextFile));
  const previousName = usePlayerStore((s) => previousFileName(s.metadata?.nextFile));
  const previousFileId = usePlayerStore((s) => s.metadata?.nextFile?.previousFileId);
  const floatingBeforeFullscreen = useRef(false);

  const playPause = () => {
    const { isPlaying } = usePlayerStore.getState();
    postMessage('play_status_changed', !isPlaying);
  };
  const skip = (seconds: number) => {
    const state = usePlayerStore.getState();
    if (canSeekTo(state, state.currentTime + seconds * 1000)) postMessage('skip_seconds', seconds);
  };
  const skipBackward = () => skip(-10);
  const skipForward = () => skip(10);
  const previousTrack = () => {
    if (previousFileId) postMessage('play_file', previousFileId);
  };
  const nextTrack = () => {
    postMessage('play_next_title');
  };
  // Read the store at call time: these also run from handlers registered once on mount.
  const toggleFullscreen = () => {
    const { isFullscreen, isFloating } = usePlayerStore.getState();
    const newFullscreen = !isFullscreen;
    if (newFullscreen) {
      floatingBeforeFullscreen.current = isFloating;
      setIsFloating(false);
    }
    setIsFullscreen(newFullscreen);
  };
  const exitFullscreen = () => {
    if (!usePlayerStore.getState().isFullscreen) return;
    setIsFullscreen(false);
    setIsFloating(floatingBeforeFullscreen.current);
  };

  useEventListeners('keyup', {
    Space: playPause,
    Escape: exitFullscreen,
    ArrowLeft: skipBackward,
    ArrowRight: skipForward,
    KeyF: toggleFullscreen,
  });

  useEffect(() => {
    registerEventHandler('playback_toggle', playPause);
    registerEventHandler('request_fullscreen_toggle', toggleFullscreen);

    return () => {
      unregisterEventHandler('playback_toggle', playPause);
      unregisterEventHandler('request_fullscreen_toggle', toggleFullscreen);
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
          <div className="playback-controls__spacer">
            <CurrentSegmentName />
          </div>

          {/* A Mantine tooltip: the native players' off-screen CEF does not show `title` attributes. */}
          <Tooltip label={previousName} disabled={!previousName} position="top" withinPortal={false}>
            <div className={`control-button previous${previousFileId ? '' : ' disabled'}`} onClick={previousTrack}>
              <PreviousTrack />
            </div>
          </Tooltip>
          <Tooltip label={nextName} disabled={!nextName} position="top" withinPortal={false}>
            <div className="control-button next" onClick={nextTrack}>
              <NextTrack />
            </div>
          </Tooltip>
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
