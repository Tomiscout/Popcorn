import { forwardRef, SyntheticEvent } from 'react';
import { usePlayerStore } from '../../state/player.store';
import { resolveServerHost } from '../../utils/url-resolver';
import { postMessage } from '../../utils/events';
import { getVideoMetadata } from '../../query/video.query';

export interface VideoPlayerProps {}

export const VideoPlayer = forwardRef<HTMLVideoElement, VideoPlayerProps>(({}, ref) => {
  const { fileId, setCurrentTime, setDuration } = usePlayerStore();

  const src = `http://${resolveServerHost()}/api/video/${fileId}`;

  function handleLoadedMetadata(e: SyntheticEvent<HTMLVideoElement>) {
    const duration = e.currentTarget.duration;
    setDuration(duration * 1000);
    postMessage('video_loaded', {});
  }

  function handleTimeUpdate(e: SyntheticEvent<HTMLVideoElement>) {
    const current = e.currentTarget.currentTime;
    setCurrentTime(current * 1000);
  }

  function handleError(e: SyntheticEvent<HTMLVideoElement>) {
    // e.currentTarget.error is the native MediaError object
    const err = e.currentTarget.error as MediaError;
    postMessage('player_error', parseMediaErrorCode(err));
  }

  function parseMediaErrorCode(error: MediaError): string {
    switch (error.code) {
      case 1:
        return 'Fetching media aborted';
      case 2:
        return 'Network error while fetching';
      case 3:
        return 'Media Decoding error';
      case 4:
        return 'Media source not found on server: ' + src;
    }
    return 'Unknown error:' + JSON.stringify(error);
  }

  return (
    <video
      ref={ref}
      src={src}
      // controls
      style={{ width: '100%', height: '100%', background: 'black' }}
      preload="metadata"
      onLoadedMetadata={handleLoadedMetadata}
      onTimeUpdate={handleTimeUpdate}
      onError={handleError}
    />
  );
});
