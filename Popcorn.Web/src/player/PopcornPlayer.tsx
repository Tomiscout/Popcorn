import React, { ReactPortal, useEffect, useRef, useState } from 'react';
import type { DownloadPlayback } from '../model/download.contract';
import { resolveServerHost } from '../utils/url-resolver';
import Draggable from 'react-draggable';
import './PopcornPlayer.css';
import { disableDefaultListeners } from './Utlis/listener.utils';
import { initEvents, registerEventHandler, unregisterEventHandler } from '../utils/events';
import { usePlayerStore } from '../state/player.store';
import { createPortal } from 'react-dom';
import { VideoPlayer as RawVideoPlayer } from './Components/VideoPlayer';
import { useVideoMetadata } from '../query/video.query';
import { postMessage } from '../utils/events';
import { PlayerOverlay } from './PlayerOverlay';
import SubtitleOverlay from './Components/Subtitles/SubtitleOverlay';
import { useWebSocket } from '../state/websocket-context';
import { SocketRequests } from '../socket/socket-requests';
import { PlayerPlaylistResponse } from '../model/media.contract';
import type { SegmentAutoSkip } from '../model/segments.contract';

import { useNavigate } from 'react-router-dom';
import type { NextFilePlan } from '../query/models/video.models';
import { applyNextFilePlan } from './next-file';

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
  const navigate = useNavigate();
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

  const { data: videoMetadata, isFetching: isFetchingMetadata, error: metadataError } = useVideoMetadata(fileId || '');

  useEffect(() => {
    if (metadataError) {
      postMessage('player_error', 'Failed to fetch metadata: ' + metadataError.message);
    }
  }, [metadataError]);
  useEffect(() => {
    if (videoMetadata) {
      setMetadata(videoMetadata);
      usePlayerStore.getState().setSegments(videoMetadata.segments);
    }
  }, [videoMetadata]);

  // Start where the file was left. Waits for the fetch to finish: a cached answer has an older position.
  const resumedFileId = useRef<string | null>(null);
  useEffect(() => {
    const video = videoRef.current;
    if (!video || !fileId || isFetchingMetadata || videoMetadata?.fileId !== fileId) return;
    if (resumedFileId.current === fileId) return;
    resumedFileId.current = fileId;

    const position = videoMetadata.resumePosition;
    if (!position) return;
    const seek = () => (video.currentTime = position);
    if (video.readyState >= HTMLMediaElement.HAVE_METADATA) seek();
    else video.addEventListener('loadedmetadata', seek, { once: true });
  }, [fileId, videoMetadata, isFetchingMetadata, videoRef]);

  // A file Popcorn is still downloading: show the downloaded parts on the seek bar until it is complete.
  useEffect(() => {
    const { setDownloadRanges } = usePlayerStore.getState();
    setDownloadRanges(null);
    if (!fileId) return;
    let stopped = false;
    const poll = async () => {
      try {
        const response = await fetch(`http://${resolveServerHost()}/api/downloads/playback/${fileId}`);
        if (stopped) return;
        if (!response.ok) return setDownloadRanges(null); // not a download: stop polling
        const playback: DownloadPlayback = await response.json();
        if (stopped) return;
        setDownloadRanges(playback.complete ? [[0, 1]] : playback.ranges);
        if (!playback.complete) timer = window.setTimeout(poll, 2000);
      } catch {
        if (!stopped) timer = window.setTimeout(poll, 5000);
      }
    };
    let timer = window.setTimeout(poll, 0);
    return () => {
      stopped = true;
      window.clearTimeout(timer);
    };
  }, [fileId]);

  // Where we are in the file, for the home page's "continue watching": every 10 s, on pause / end and when it changes.
  useEffect(() => {
    if (!fileId) return;
    let lastSent = -1;
    const send = () => {
      const video = videoRef.current;
      if (!video || video.currentTime <= 0 || video.currentTime === lastSent) return;
      const duration = Number.isFinite(video.duration) ? video.duration : usePlayerStore.getState().duration / 1000;
      if (!(duration > 0)) return;
      lastSent = video.currentTime;
      client?.sendEvent(SocketRequests.PlaybackProgress, { fileId, position: video.currentTime, duration });
    };
    const video = videoRef.current;
    const timer = window.setInterval(send, 10000);
    video?.addEventListener('pause', send);
    video?.addEventListener('ended', send);
    return () => {
      send();
      window.clearInterval(timer);
      video?.removeEventListener('pause', send);
      video?.removeEventListener('ended', send);
    };
  }, [fileId, client, videoRef]);

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
        // While buffering this only records the wish; playback goes on when the data is there.
        if (!usePlayerStore.getState().isBuffering) videoRef.current.play();
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
      const { fileId: currentFileId } = usePlayerStore.getState();
      if (currentFileId) client?.sendEvent(SocketRequests.PlayerNextFile, currentFileId);
    };

    const nextFilePlanHandler = (plan: NextFilePlan) => {
      if (plan.currentFileId === usePlayerStore.getState().fileId) applyNextFilePlan(plan);
    };
    client.onEvent<NextFilePlan>(SocketRequests.PlayerNextFile, nextFilePlanHandler);

    const nextFileRefreshHandler = () => {
      const { fileId: currentFileId } = usePlayerStore.getState();
      if (currentFileId) client?.sendEvent(SocketRequests.PlayerRefreshFiles, currentFileId);
    };

    // The player keeps floating over the item page the warning's "Find file" opens.
    const nextFileFindHandler = (path: string) => {
      const { setIsFullscreen, setIsFloating } = usePlayerStore.getState();
      setIsFullscreen(false);
      setIsFloating(true);
      navigate(path);
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

    const segmentSeenHandler = (segmentId: string) => client?.sendEvent(SocketRequests.SegmentSeen, segmentId);

    const segmentAutoSkipSetHandler = (mode: SegmentAutoSkip | null) => {
      const itemId = usePlayerStore.getState().segmentAutoSkip?.itemId;
      if (itemId) client?.sendEvent(SocketRequests.SegmentAutoSkipSet, { itemId, mode });
    };

    const closeHandler = () => {
      const { resetState, closePlayer } = usePlayerStore.getState();
      resetState();
      closePlayer();
    };

    const unsubscribeBuffering = usePlayerStore.subscribe((state, prev) => {
      if (state.isBuffering === prev.isBuffering || !videoRef.current) return;
      if (state.isBuffering) videoRef.current.pause();
      else if (state.isPlaying) videoRef.current.play().catch(console.error);
    });

    registerEventHandler('play_file', playFileHandler);
    registerEventHandler('play_next_title', playNextFileHandler);
    registerEventHandler('next_file_refresh', nextFileRefreshHandler);
    registerEventHandler('next_file_find', nextFileFindHandler);
    registerEventHandler('folder_open', folderOpenHandler);
    registerEventHandler('folder_open_root', folderOpenRootHandler);
    registerEventHandler('thumb_requested', thumbnailRequestHandler);
    registerEventHandler('player_close', closeHandler);
    registerEventHandler('segment_seen', segmentSeenHandler);
    registerEventHandler('segment_auto_skip_set', segmentAutoSkipSetHandler);
    registerEventHandler('set_playback_time', setPlaybackTime);
    registerEventHandler('play_status_changed', onToggle);
    registerEventHandler('skip_seconds', onSkip);
    registerEventHandler('volume', setVolume);
    registerEventHandler('subtitle_track_selected', subtitleTrackSelected);
    registerEventHandler('audio_track_selected', audioTrackSelected);

    return () => {
      unsubscribeBuffering();
      unregisterEventHandler('play_file', playFileHandler);
      unregisterEventHandler('play_next_title', playNextFileHandler);
      unregisterEventHandler('next_file_refresh', nextFileRefreshHandler);
      unregisterEventHandler('next_file_find', nextFileFindHandler);
      unregisterEventHandler('folder_open', folderOpenHandler);
      unregisterEventHandler('folder_open_root', folderOpenRootHandler);
      unregisterEventHandler('thumb_requested', thumbnailRequestHandler);
      unregisterEventHandler('player_close', closeHandler);
      unregisterEventHandler('segment_seen', segmentSeenHandler);
      unregisterEventHandler('segment_auto_skip_set', segmentAutoSkipSetHandler);
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
