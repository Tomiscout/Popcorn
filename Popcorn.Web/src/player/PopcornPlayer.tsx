import React, { useEffect, useRef, useState } from 'react';
import Draggable from 'react-draggable';
import './PopcornPlayer.css';
import ErrorOverlay from './Components/ErrorOverlay/ErrorOverlay';
import { disableDefaultListeners, useEventListeners } from './Utlis/listener.utils';
import PlaybackNotificationControl from './Components/PlaybackNotificationControl/PlaybackNotificationControl';
import { CaptureClickArea } from './Components/CaptureClickArea/CaptureClickArea';
import { ControlBar } from './Components/ControlBar/ControlBar';
import { initEvents, registerEventHandler, unregisterEventHandler } from '../utils/events';
import { usePlayerStore } from '../state/player.store';
import { createPortal } from 'react-dom';
import { VideoPlayer as RawVideoPlayer } from './Components/VideoPlayer';
import { TopBar } from './Components/TopBar/TopBar';
import { getVideoMetadata } from '../query/video.query';
import { Loader } from '@mantine/core';
import { postMessage } from '../utils/events';
import SubtitleOverlay from './Components/Subtitles/SubtitleOverlay';
import { useWebSocket } from '../state/websocket-context';
import { SocketRequests } from '../socket/socket-requests';

export type Mode = 'inline' | 'floating' | 'fullscreen';

// 1. memoize the VideoPlayer so it only mounts once
const VideoPlayer = React.memo(
  React.forwardRef<HTMLVideoElement>((props, ref) => <RawVideoPlayer ref={ref} {...props} />)
);

interface PopcornPlayerProps {
  videoRef: React.RefObject<HTMLVideoElement | null>;
}

export const PopcornPlayer: React.FC<PopcornPlayerProps> = ({ videoRef }) => {
  const videoContainerRef = useRef<HTMLDivElement>(null);
  const { client } = useWebSocket();
  const {
    fileId,
    isFloating,
    isFullscreen,
    additionalAudioFiles,
    setIsPlaying,
    setMetadata,
    setCurrentSubtitle,
    setCurrentAudio,
  } = usePlayerStore();

  const { data: videoMetadata, isFetching: isFetchingMetadata, error: metadataError } = getVideoMetadata(fileId || '');
  const [showControls, setShowControls] = useState(true);

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
    const newMode: Mode = isFullscreen ? 'fullscreen' : isFloating ? 'floating' : 'inline';
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

    const setVolume = (newVolume: number) => {
      if (!videoRef.current) return;
      videoRef.current.volume = newVolume;
    };

    const subtitleTrackSelected = (selectedTrack: string) => {
      const { metadata: currentMeta } = usePlayerStore.getState();
      const subtitle = currentMeta?.subtitleTracks.find((track) => track.index === Number.parseInt(selectedTrack));
      setCurrentSubtitle(subtitle || null);
    };

    const audioTrackSelected = (selectedTrack: string) => {
      const { metadata: currentMeta } = usePlayerStore.getState();
      const audio = currentMeta?.audioTracks.find((track) => track.index === Number.parseInt(selectedTrack));
      setCurrentAudio(audio || null);
    };

    // Kicks off player playlist file load for opened file id
    client?.sendEvent(SocketRequests.FolderOpenPlayer, fileId);

    registerEventHandler('set_playback_time', setPlaybackTime);
    registerEventHandler('play_status_changed', onToggle);
    registerEventHandler('skip_seconds', onSkip);
    registerEventHandler('volume', setVolume);
    registerEventHandler('subtitle_track_selected', subtitleTrackSelected);
    registerEventHandler('audio_track_selected', audioTrackSelected);

    return () => {
      unregisterEventHandler('set_playback_time', setPlaybackTime);
      unregisterEventHandler('play_status_changed', onToggle);
      unregisterEventHandler('skip_seconds', onSkip);
      unregisterEventHandler('volume', setVolume);
      unregisterEventHandler('subtitle_track_selected', subtitleTrackSelected);
      unregisterEventHandler('audio_track_selected', audioTrackSelected);
    };
  }, []);

  const controlsTimeoutRef = useRef<NodeJS.Timeout>(null);

  const displayControls = () => {
    setShowControls(true);
    videoContainerRef.current!.style.cursor = 'initial';

    if (controlsTimeoutRef.current) {
      clearTimeout(controlsTimeoutRef.current);
    }

    controlsTimeoutRef.current = setTimeout(() => {
      setShowControls(false);
      if (videoContainerRef.current) {
        videoContainerRef.current.style.cursor = 'none';
      }
    }, 4000);
  };

  useEventListeners('keyup', { default: displayControls });
  useEventListeners('mousemove', { default: displayControls });

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
      <TopBar showControls={showControls} />
      <ErrorOverlay />
      {isFetchingMetadata && loadingSpinner()}
      {videoMetadata && (
        <>
          <ControlBar showControls={showControls} metadata={videoMetadata} />
          <PlaybackNotificationControl />
          <SubtitleOverlay videoRef={videoRef} />
        </>
      )}
    </div>
  );

  const wrapped = (
    <Draggable nodeRef={videoContainerRef} handle=".capture-click-area" disabled={mode !== 'floating'}>
      {container}
    </Draggable>
  );

  const portalContainer = createPortal(wrapped, document.body);
  let portalVideo = null;
  if (videoContainerRef.current && videoMetadata) {
    portalVideo = createPortal(<VideoPlayer ref={videoRef} />, videoContainerRef.current);
  }

  return (
    fileId && (
      <>
        {portalContainer}
        {portalVideo}
      </>
    )
  );
};
