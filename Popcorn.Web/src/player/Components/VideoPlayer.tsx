import { forwardRef, useEffect, useRef, SyntheticEvent, useState } from 'react';
import { usePlayerStore } from '../../state/player.store';
import { resolveServerHost } from '../../utils/url-resolver';
import { postMessage } from '../../utils/events';
import { useAudioDownloadMutation } from '../../query/video.query';
import { Loader } from '@mantine/core';
import './VideoPlayer.css';

export interface AdditionalAudioFile {
  url: string;
  index: number;
  mime: string;
}

export type VideoPlayerProps = object;

export const VideoPlayer = forwardRef<HTMLVideoElement, VideoPlayerProps>((_props, ref) => {
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
    setPlaybackEnded,
  } = usePlayerStore();

  const videoSrc = `http://${resolveServerHost()}/api/video/${fileId}`;
  const audioRef = useRef<HTMLAudioElement | null>(null);
  const syncCleanupRef = useRef<(() => void) | null>(null);

  const [useExternalAudio, setUseExternalAudio] = useState(false);
  const [videoContainsAudio, setVideoContainsAudio] = useState(false);

  const getAudioFileMutation = useAudioDownloadMutation(fileId || '');

  // Gets kicked off when currentAudio changes from the track selector
  useEffect(() => {
    if (!metadata || !currentAudio) return;

    (async () => {
      const videoEl = (ref as any).current as HTMLVideoElement;
      const audioEl = audioRef.current;

      //Pause everything
      await videoEl.pause();
      await audioEl?.pause();
      setIsPlaying(false);

      //Determines if we need to use external audio
      if (
        metadata.audioDefaultIndex !== currentAudio.index ||
        (metadata.audioDefaultIndex === currentAudio.index && !videoContainsAudio)
      ) {
        setUseExternalAudio(true);
      }

      //Kick off audio fetch if we don't have it yet
      const additionalTrack = additionalAudioFiles.find((a) => a.index === currentAudio?.index);
      if (!additionalTrack) {
        setIsAudioLoading(true);

        getAudioFileMutation.mutateAsync({ trackId: currentAudio?.index.toString() || '' }).then((blob) => {
          const url = URL.createObjectURL(blob);
          const newAudioFile: AdditionalAudioFile = {
            url,
            index: currentAudio?.index || 0,
            mime: blob.type,
          };
          addAdditionalAudioFile(newAudioFile);
        });
      } else {
        audioEl!.src = additionalTrack.url;
        finishAudioSwitch();
      }
    })();
  }, [currentAudio]);

  async function finishAudioSwitch() {
    const video = (ref as any).current as HTMLVideoElement;
    const audio = audioRef.current!;

    syncCleanupRef.current?.();
    syncCleanupRef.current = null;

    video.muted = useExternalAudio;

    if (!useExternalAudio || !audio) {
      audio?.pause();
      if (audio) audio.src = '';
      await video.play();
      setIsPlaying(true);
      return;
    }

    // HARD SYNC
    audio.currentTime = video.currentTime;
    audio.playbackRate = video.playbackRate;

    await audio.play();
    await video.play();

    setIsAudioLoading(false);
    setIsPlaying(true);

    /* ---------- EVENT SYNC ---------- */

    const onPlay = () => {
      if (audio.paused) audio.play();
    };

    const onPause = () => {
      if (!audio.paused) audio.pause();
    };

    const onSeek = () => {
      audio.currentTime = video.currentTime;
    };

    const onRate = () => {
      audio.playbackRate = video.playbackRate;
    };

    video.addEventListener('play', onPlay);
    video.addEventListener('pause', onPause);
    video.addEventListener('seeking', onSeek);
    video.addEventListener('ratechange', onRate);

    /* ---------- AUTOSYNC LOOP ---------- */

    let rafId: number;

    const autoSync = () => {
      if (!video.paused && !audio.paused) {
        const diff = video.currentTime - audio.currentTime;
        if (Math.abs(diff) > 0.2) {
          audio.currentTime = video.currentTime;
        }
      }
      rafId = requestAnimationFrame(autoSync);
    };

    rafId = requestAnimationFrame(autoSync);

    /* ---------- CLEANUP ---------- */

    syncCleanupRef.current = () => {
      cancelAnimationFrame(rafId);
      video.removeEventListener('play', onPlay);
      video.removeEventListener('pause', onPause);
      video.removeEventListener('seeking', onSeek);
      video.removeEventListener('ratechange', onRate);
    };
  }

  useEffect(() => {
    const fetchedFile = additionalAudioFiles.find((a) => a.index === currentAudio?.index);
    if (!fetchedFile) return;

    console.log('fetched audio file', fetchedFile);

    const audioEl = audioRef.current;

    if (!audioEl) return;
    audioEl.src = fetchedFile.url;

    finishAudioSwitch();
  }, [additionalAudioFiles]);

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

  return (
    <div tabIndex={-1} style={{ width: '100%', height: '100%', background: 'black' }}>
      {isAudioLoading && (
        <div
          style={{
            position: 'absolute',
            top: '50%',
            left: '50%',
            transform: 'translate(-50%, -50%)',
            zIndex: 10,
          }}
        >
          <Loader />
        </div>
      )}
      <video
        ref={ref}
        src={videoSrc}
        preload="metadata"
        muted={useExternalAudio}
        autoPlay
        onLoadedMetadata={handleLoadedMetadata}
        onTimeUpdate={handleTimeUpdate}
        onEnded={() => {
          setPlaybackEnded(true);
          postMessage('play_next_title');
        }}
        onPlaying={() => setPlaybackEnded(false)}
        onSeeked={() => setPlaybackEnded(false)}
        onError={handleError}
        style={{ width: '100%', height: '100%' }}
      />
      {useExternalAudio && <audio ref={audioRef} />}
    </div>
  );
});
