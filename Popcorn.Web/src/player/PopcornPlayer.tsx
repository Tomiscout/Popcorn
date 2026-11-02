import React, { ReactPortal, useEffect, useRef, useState } from 'react';
import Draggable from 'react-draggable';
import './PopcornPlayer.css';
import { disableDefaultListeners } from './Utlis/listener.utils';
import { initEvents, registerEventHandler, unregisterEventHandler } from '../utils/events';
import { usePlayerStore } from '../state/player.store';
import { createPortal } from 'react-dom';
import { VideoPlayer as RawVideoPlayer } from './Components/VideoPlayer';
import { getVideoMetadata } from '../query/video.query';
import { postMessage } from '../utils/events';
import { PlayerOverlay } from './PlayerOverlay';
import SubtitleOverlay from './Components/Subtitles/SubtitleOverlay';
import { useWebSocket } from '../state/websocket-context';
import { SocketRequests } from '../socket/socket-requests';
import { PlayerPlaylistResponse } from '../model/media.contract';

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
    setIsPlaying,
    setMetadata,
    setCurrentSubtitle,
    setCurrentAudio,
    setCurrentPlaylist,
    setCurrentPlaylistNavigation,
  } = usePlayerStore();

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
    const newMode: Mode = isFullscreen ? 'fullscreen' : isFloating ? 'floating' : 'inline';
    setMode(newMode);
    console.log('Setting new mode:', newMode);
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
    client.onEvent<PlayerPlaylistResponse>(SocketRequests.FolderOpenPlayer, (response) => {
      setCurrentPlaylistNavigation(response.navigation);
      setCurrentPlaylist(response.files);
    });

    //TODO consolidate these functions
    const playFileHandler = (fileId: string) => {
      const { resetState, setFileId } = usePlayerStore.getState();
      resetState();
      setFileId(fileId);
    };

    const playNextFileHandler = () => {
      const { mediaId } = usePlayerStore.getState();
      client?.sendEvent(SocketRequests.NextFile, mediaId);
    };

    const folderOpenHandler = (folder: { fileId: string }) => {
      client?.sendEvent(SocketRequests.FolderOpenPlayer, folder.fileId);
    };

    const folderOpenRootHandler = () => {
      const { mediaId } = usePlayerStore.getState();
      client?.sendEvent(SocketRequests.FolderOpenPlayer, mediaId);
    };

    const thumbnailRequestHandler = (timeMs: number) => {
      const { fileId: currentFileId } = usePlayerStore.getState();
      client?.sendEvent(SocketRequests.ThumbnailFile, { fileId: currentFileId, timeSeconds: timeMs / 1000 });
    };

    const closeHandler = () => {
      const { resetState, closePlayer } = usePlayerStore.getState();
      resetState();
      closePlayer();
    };

    registerEventHandler('play_file', playFileHandler);
    registerEventHandler('play_next_title', playNextFileHandler);
    registerEventHandler('folder_open', folderOpenHandler);
    registerEventHandler('folder_open_root', folderOpenRootHandler);
    registerEventHandler('thumb_requested', thumbnailRequestHandler);
    registerEventHandler('player_close', closeHandler);
    registerEventHandler('set_playback_time', setPlaybackTime);
    registerEventHandler('play_status_changed', onToggle);
    registerEventHandler('skip_seconds', onSkip);
    registerEventHandler('volume', setVolume);
    registerEventHandler('subtitle_track_selected', subtitleTrackSelected);
    registerEventHandler('audio_track_selected', audioTrackSelected);

    return () => {
      unregisterEventHandler('play_file', playFileHandler);
      unregisterEventHandler('play_next_title', playNextFileHandler);
      unregisterEventHandler('folder_open', folderOpenHandler);
      unregisterEventHandler('folder_open_root', folderOpenRootHandler);
      unregisterEventHandler('thumb_requested', thumbnailRequestHandler);
      unregisterEventHandler('player_close', closeHandler);
      unregisterEventHandler('set_playback_time', setPlaybackTime);
      unregisterEventHandler('play_status_changed', onToggle);
      unregisterEventHandler('skip_seconds', onSkip);
      unregisterEventHandler('volume', setVolume);
      unregisterEventHandler('subtitle_track_selected', subtitleTrackSelected);
      unregisterEventHandler('audio_track_selected', audioTrackSelected);
    };
  }, []);

  // global init once
  useEffect(() => {
    disableDefaultListeners();
    initEvents();
  }, []);

  // This is the one-and-only "container" tree you'll ever mount.
  const container = (
    <PlayerOverlay
      ref={videoContainerRef}
      className={mode}
      isFloating={mode === 'floating'}
      isLoading={isFetchingMetadata}
    >
      <SubtitleOverlay videoRef={videoRef} />
    </PlayerOverlay>
  );

  const wrapped = (
    <Draggable nodeRef={videoContainerRef} handle=".capture-click-area" disabled={mode !== 'floating'}>
      {container}
    </Draggable>
  );

  const portalContainer = createPortal(wrapped, document.body);
  let portalVideo: ReactPortal | null = null;
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
