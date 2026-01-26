import { forwardRef, useEffect, useRef, SyntheticEvent, useState } from 'react';
import { usePlayerStore } from '../../state/player.store';
import { resolveServerHost } from '../../utils/url-resolver';
import { postMessage } from '../../utils/events';
import { useAudioDownloadMutation } from '../../query/video.query';
import { Loader } from '@mantine/core';
import '../Styles/VideoPlayer.css';

export interface AdditionalAudioFile {
  url: string;
  index: number;
  mime: string;
}

export interface VideoPlayerProps {}

export const VideoPlayer = forwardRef<HTMLVideoElement, VideoPlayerProps>(({}, ref) => {
  const {
    fileId,
    metadata,
    additionalAudioFiles,
    currentAudio,
    isAudioLoading,
    addAdditionalAudioFile,
    setIsAudioLoading,
    setCurrentTime,
    setDuration,
    setIsPlaying,
    setCurrentAudio,
  } = usePlayerStore();

  const videoSrc = `http://${resolveServerHost()}/api/video/${fileId}`;
  const audioRef = useRef<HTMLAudioElement | null>(null);

  const [useExternalAudio, setUseExternalAudio] = useState(false);
  const [videoContainsAudio, setVideoContainsAudio] = useState(false);

  const getAudioFileMutation = useAudioDownloadMutation(fileId || '');

  useEffect(() => {
    if (!metadata) return;

    const videoEl = (ref as any).current as HTMLVideoElement;
    videoEl.pause();

    if (metadata.audioDefaultIndex === currentAudio?.index && !videoContainsAudio) {
      setUseExternalAudio(true);
    }

    const isAudioFetched = !!additionalAudioFiles.find((a) => a.index === currentAudio?.index);
    setIsAudioLoading(!isAudioFetched);

    if (!isAudioFetched) {
      getAudioFileMutation.mutateAsync({ trackId: currentAudio?.index.toString() || '' }).then((blob) => {
        const url = URL.createObjectURL(blob);
        const newAudioFile: AdditionalAudioFile = {
          url,
          index: currentAudio?.index || 0,
          mime: blob.type,
        };
        addAdditionalAudioFile(newAudioFile);
      });
    }
  }, [currentAudio]);

  useEffect(() => {
    const fetchedFile = additionalAudioFiles.find((a) => a.index === currentAudio?.index);

    if (fetchedFile) {
      console.log('fetched audio file', fetchedFile);

      const videoEl = (ref as any).current as HTMLVideoElement;
      const audioEl = audioRef.current;
      // Mechanism to switch audio source between <video> and <audio>
      if (currentAudio) {
        if (videoContainsAudio && currentAudio.index === metadata?.audioDefaultIndex) {
          // Use video element's audio
          if (audioEl) {
            audioEl.pause();
            audioEl.src = '';
          }
          setUseExternalAudio(false);
        } else if (audioEl) {
          // Use external audio
          audioEl.src = fetchedFile.url;

          const onCanPlay = () => {
            if (!videoEl.paused) {
              audioEl.play().catch((err) => postMessage('player_error', 'Audio play failed ' + err));
            }
          };

          audioEl.addEventListener('canplaythrough', onCanPlay, { once: true });
        }
      }
      if (videoContainsAudio && currentAudio?.index === metadata?.audioDefaultIndex) {
        videoEl.muted = true;
      }
    }
    setIsAudioLoading(!fetchedFile);
  }, [additionalAudioFiles]);

  useEffect(() => {
    console.log('isAudioLoading', isAudioLoading);

    if (isAudioLoading) {
      const videoEl = (ref as any).current as HTMLVideoElement;
      const audioEl = audioRef.current;

      videoEl.pause();
      audioEl?.pause();
    }
  }, [isAudioLoading]);

  useEffect(() => {
    const videoEl = (ref as any)?.current as HTMLVideoElement | null;
    if (!videoEl) return;
    videoEl.muted = useExternalAudio; // runtime property, not just JSX attrib
  }, [useExternalAudio]);

  function handleLoadedMetadata(e: SyntheticEvent<HTMLVideoElement>) {
    const video = e.currentTarget;

    setDuration(video.duration * 1000);

    const hasAudio = (video as any).webkitAudioDecodedByteCount > 0 || (video as any).mozHasAudio;
    setVideoContainsAudio(hasAudio);

    const needsAudioFetched = !!metadata?.audioDefaultIndex && !hasAudio;
    setUseExternalAudio(needsAudioFetched);

    setIsPlaying(!needsAudioFetched && video.autoplay);

    if (metadata?.audioDefaultIndex) {
      const audio = metadata?.audioTracks.find((track) => track.index === metadata?.audioDefaultIndex);
      setCurrentAudio(audio || null);
    }

    postMessage('video_loaded');
  }

  function handleTimeUpdate(e: SyntheticEvent<HTMLVideoElement>) {
    const current = e.currentTarget.currentTime;
    setCurrentTime(current * 1000);
  }

  function handleError(e: SyntheticEvent<HTMLVideoElement>) {
    const err = e.currentTarget.error as MediaError;
    postMessage('player_error', parseMediaError(err));
  }

  function parseMediaError(error: MediaError): string {
    const errorName = parseMediaErrorCode(error.code);
    return `${errorName}: ${error.message}`;
  }

  function parseMediaErrorCode(errorCode: number): string {
    switch (errorCode) {
      case 1:
        return 'Fetching media aborted';
      case 2:
        return 'Network error while fetching';
      case 3:
        return 'Media Decoding error';
      case 4:
        return 'Media source not found on server: ' + videoSrc;
    }
    return 'Unknown error ' + errorCode;
  }

  // --- Sync audio with video ---
  useEffect(() => {
    if (!useExternalAudio || !audioRef.current || !(ref as any)?.current) return;

    const videoEl = (ref as any).current as HTMLVideoElement;
    const audioEl = audioRef.current;

    // load external track
    const defaultAudio = additionalAudioFiles?.find((f) => f.index === metadata?.audioDefaultIndex);
    if (defaultAudio) {
      audioEl.src = defaultAudio.url;
      audioEl.load();
    }

    // prevent video from playing before audio ready
    const handleAudioCanPlay = () => {
      if (videoEl.paused && videoEl.autoplay) {
        videoEl.play();
        audioEl.play();
      }
    };

    audioEl.addEventListener('canplaythrough', handleAudioCanPlay);

    // Sync playback events
    const syncPlay = () => {
      if (audioEl.paused) audioEl.play();
    };
    const syncPause = () => {
      if (!audioEl.paused) audioEl.pause();
    };

    videoEl.addEventListener('play', syncPlay);
    videoEl.addEventListener('pause', syncPause);

    // Sync currentTime occasionally
    const syncTime = () => {
      if (Math.abs(videoEl.currentTime - audioEl.currentTime) > 0.3) {
        audioEl.currentTime = videoEl.currentTime;
      }
    };
    videoEl.addEventListener('timeupdate', syncTime);

    return () => {
      audioEl.removeEventListener('canplaythrough', handleAudioCanPlay);
      videoEl.removeEventListener('play', syncPlay);
      videoEl.removeEventListener('pause', syncPause);
      videoEl.removeEventListener('timeupdate', syncTime);
    };
  }, [useExternalAudio, additionalAudioFiles, metadata, ref]);

  return (
    <div style={{ width: '100%', height: '100%', background: 'black' }}>
      {isAudioLoading && <Loader></Loader>}
      <button className="player-btn" onClick={() => audioRef?.current?.play()}>
        Play audio
      </button>
      <video
        ref={ref}
        src={videoSrc}
        preload="metadata"
        muted={useExternalAudio}
        autoPlay
        onLoadedMetadata={handleLoadedMetadata}
        onTimeUpdate={handleTimeUpdate}
        onError={handleError}
        style={{ width: '100%', height: '100%' }}
      />
      {useExternalAudio && <audio ref={audioRef} />}
    </div>
  );
});
