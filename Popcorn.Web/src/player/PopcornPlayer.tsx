import React, { useEffect, useRef, useState, useMemo } from 'react';
import Draggable from 'react-draggable';
import './PopcornPlayer.css';
import ErrorOverlay from './Components/ErrorOverlay';
import { disableDefaultListeners } from './Utlis/listener.utils';
import PlaybackNotificationControl from './Components/PlaybackNotificationControl';
import { CaptureClickArea } from './Components/CaptureClickArea';
import { ControlBar } from './Components/ControlBar';
import { initEvents, registerEventHandler, unregisterEventHandler } from '../utils/events';
import { usePlayerStore } from '../state/player.store';
import { createPortal } from 'react-dom';
import { VideoPlayer as RawVideoPlayer } from './Components/VideoPlayer';
import { TopBar } from './Components/TopBar';
import { getVideoMetadata } from '../query/video.query';
import { Loader } from '@mantine/core';
import { postMessage } from '../utils/events';

export type Mode = 'inline' | 'floating' | 'fullscreen';

// 1. memoize the VideoPlayer so it only mounts once
const VideoPlayer = React.memo(
  React.forwardRef<HTMLVideoElement>((props, ref) => <RawVideoPlayer ref={ref} {...props} />)
);

export const PopcornPlayer: React.FC = () => {
  const videoContainerRef = useRef<HTMLDivElement>(null);
  const videoRef = useRef<HTMLVideoElement>(null);
  const { fileId, isFloating, isFullscreen, setIsPlaying, setMetadata } = usePlayerStore();

  const { data: videoMetadata, isFetching: isFetchingMetadata, error: metadataError } = getVideoMetadata(fileId || '');
  useEffect(() => {
    if (metadataError) {
      postMessage('player_error', 'Failed to fetch metadata: ' + metadataError.message);
    }
  }, [metadataError]);
  useEffect(() => {
    if (videoMetadata) {
      setMetadata(videoMetadata);
    }
  }, [videoMetadata]);

  // track our mode locally for CSS classes (sync'd to store)
  const [mode, setMode] = useState<Mode>('inline');

  // whenever store flags change, update local mode
  useEffect(() => {
    const newMode: Mode = isFloating ? 'floating' : isFullscreen ? 'fullscreen' : 'inline';
    setMode(newMode);
  }, [isFloating, isFullscreen]);

  // whenever mode flips, trigger the Fullscreen API
  useEffect(() => {
    if (!videoContainerRef.current) return;
    if (mode === 'fullscreen') {
      if (!document.fullscreenElement) {
        videoContainerRef.current.requestFullscreen().catch(console.error);
      }
    } else {
      if (document.fullscreenElement) {
        document.exitFullscreen().catch(console.error);
      }
    }
  }, [mode]);

  useEffect(() => {
    const onToggle = (shouldPlay: boolean) => {
      if (!videoRef.current) return;
      if (shouldPlay) {
        videoRef.current.play();
        setIsPlaying(true);
      } else {
        videoRef.current.pause();
        setIsPlaying(false);
      }
    };
    const onSkip = (seconds: number) => {
      if (!videoRef.current) return;
      videoRef.current.currentTime += seconds;
    };

    const setPlaybackTime = (newTime: number) => {
      if (!videoRef.current) return;
      videoRef.current.currentTime = newTime / 1000;
    };

    registerEventHandler('set_playback_time', setPlaybackTime);
    registerEventHandler('play_status_changed', onToggle);
    registerEventHandler('skip_seconds', onSkip);

    return () => {
      unregisterEventHandler('set_playback_time', setPlaybackTime);
      unregisterEventHandler('play_status_changed', onToggle);
      unregisterEventHandler('skip_seconds', onSkip);
    };
  }, []);

  // global init once
  useEffect(() => {
    disableDefaultListeners();
    initEvents();
    videoContainerRef.current?.focus();
  }, []);

  const loadingSpinner = () => {
    return (
      <div className="spinner-wrapper">
        <Loader color="red" />
      </div>
    );
  };

  // This is the one-and-only "container" tree you'll ever mount.
  const container = (
    <div className={`video-container ${mode}`} tabIndex={-1} ref={videoContainerRef}>
      <CaptureClickArea isFloating={mode === 'floating'} />
      <TopBar />
      <ErrorOverlay />
      {isFetchingMetadata && loadingSpinner()}
      {videoMetadata && (
        <>
          <ControlBar containerRef={videoContainerRef} metadata={videoMetadata} />
          <PlaybackNotificationControl />
          <VideoPlayer ref={videoRef} />
        </>
      )}
    </div>
  );

  // if floating, wrap in Draggable
  const wrapped =
    mode === 'floating' ? (
      <Draggable nodeRef={videoContainerRef} handle=".capture-click-area">
        {container}
      </Draggable>
    ) : (
      container
    );

  // always portal to document.body → NEVER unmount on mode change
  return createPortal(wrapped, document.body);
};
