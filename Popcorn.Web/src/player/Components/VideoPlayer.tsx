import { forwardRef, useEffect, useRef, SyntheticEvent, useState } from 'react';
import { usePlayerStore } from '../../state/player.store';
import { resolveServerHost } from '../../utils/url-resolver';
import { postMessage } from '../../utils/events';
import { Loader } from '@mantine/core';
import './VideoPlayer.css';

export type VideoPlayerProps = object;

/** A separately streamed audio track is reloaded from the video's time when it drifts further than this (seconds). */
const MAX_AUDIO_DRIFT = 0.3;

function isBuffered(ranges: TimeRanges, time: number) {
  for (let i = 0; i < ranges.length; i++) {
    if (time >= ranges.start(i) && time <= ranges.end(i) - 0.5) return true;
  }
  return false;
}

export const VideoPlayer = forwardRef<HTMLVideoElement, VideoPlayerProps>((_props, ref) => {
  const {
    fileId,
    metadata,
    currentAudio,
    isAudioLoading,
    setIsAudioLoading,
    setCurrentTime,
    setDuration,
    setIsPlaying,
    setCurrentAudio,
    setCurrentSubtitle,
    setPlaybackEnded,
  } = usePlayerStore();

  const videoSrc = `http://${resolveServerHost()}/api/video/${fileId}`;
  const videoRef = ref as React.RefObject<HTMLVideoElement | null>;
  const audioRef = useRef<HTMLAudioElement | null>(null);

  // The audio track the video can't play: the server converts it in full (seekable file, reused by later plays) and
  // streams it live from a start time (`audioOffset`) until then, without ranges: seeking outside what is buffered
  // reloads the stream from there, or switches to the converted file once it is ready. The video is held
  // (`isAudioLoading`) while the audio loads.
  const externalRef = useRef(false);
  const audioOffset = useRef(0);
  /** Playing the converted file (offset 0, seekable). */
  const convertedRef = useRef(false);
  /** The converted file is there: the next reload uses it. */
  const convertedReadyRef = useRef(false);
  /** Bumped on every track or file change: answers and polls of the previous one are dropped. */
  const audioGeneration = useRef(0);
  const [videoContainsAudio, setVideoContainsAudio] = useState(false);

  const audioTrackUrl = (fileId: string, trackIndex: number) =>
    `http://${resolveServerHost()}/api/video/${fileId}/audio/${trackIndex}`;

  function loadAudioAt(time: number) {
    const audio = audioRef.current;
    const { fileId, currentAudio: track } = usePlayerStore.getState();
    if (!audio || !track || !fileId) return;
    const url = audioTrackUrl(fileId, track.index);
    convertedRef.current = convertedReadyRef.current;
    audioOffset.current = convertedRef.current ? 0 : time;
    setIsAudioLoading(true);
    audio.src = convertedRef.current ? `${url}#t=${time.toFixed(3)}` : `${url}/stream?start=${time.toFixed(3)}`;
  }

  /** Ends the audio's request (a detached or paused element keeps loading), which stops a stream's ffmpeg on the server. */
  function stopAudio() {
    const audio = audioRef.current;
    if (!audio?.getAttribute('src')) return;
    audio.pause();
    audio.removeAttribute('src');
    audio.load();
  }

  /**
   * Starts the selected track: asks the server to convert it (or whether it has), plays the converted file or the
   * live stream meanwhile, and keeps asking while it converts (which keeps the conversion alive: leaving the player
   * stops it).
   */
  async function startExternalAudio(generation: number) {
    const { fileId, currentAudio: track } = usePlayerStore.getState();
    if (!fileId || !track) return;
    const prepareUrl = `${audioTrackUrl(fileId, track.index)}/prepare`;
    const prepare = async (): Promise<{ ready: boolean; converting: boolean }> => {
      const response = await fetch(prepareUrl);
      if (!response.ok) throw new Error(`Audio track prepare: ${response.status}`);
      return response.json();
    };

    let state = { ready: false, converting: false };
    try {
      state = await prepare();
    } catch (error) {
      console.error(error); // the stream may still work
    }
    if (generation !== audioGeneration.current) return;
    convertedReadyRef.current = state.ready;
    loadAudioAt(videoRef.current?.currentTime ?? 0);

    while (state.converting) {
      await new Promise((resolve) => setTimeout(resolve, 3000));
      if (generation !== audioGeneration.current) return;
      try {
        state = await prepare();
      } catch (error) {
        console.error(error);
        state = { ready: false, converting: false };
      }
      if (generation !== audioGeneration.current) return;
      convertedReadyRef.current = state.ready;
      switchToConvertedWhilePaused();
    }
  }

  /** Moves from the live stream to the converted file while the video is paused (no hiccup to see). */
  function switchToConvertedWhilePaused() {
    const video = videoRef.current;
    const { isAudioLoading } = usePlayerStore.getState();
    if (!video?.paused || isAudioLoading || !externalRef.current) return;
    if (convertedReadyRef.current && !convertedRef.current) loadAudioAt(video.currentTime);
  }

  /** Puts the audio at the video's time: in its buffer if it is there, else loaded again from that time. */
  function syncAudio() {
    const video = videoRef.current;
    const audio = audioRef.current;
    if (!video || !audio || !externalRef.current) return;
    const time = video.currentTime - audioOffset.current;
    if (convertedRef.current || (time >= 0 && isBuffered(audio.buffered, time))) audio.currentTime = time;
    else loadAudioAt(video.currentTime);
  }

  // Track selected (by the user, or the default once the video's metadata is in).
  useEffect(() => {
    const video = videoRef.current;
    if (!metadata || !currentAudio || !video) return;
    const generation = ++audioGeneration.current;
    const external = metadata.audioDefaultIndex !== currentAudio.index || !videoContainsAudio;
    externalRef.current = external;
    convertedRef.current = convertedReadyRef.current = false;
    video.muted = external;
    stopAudio();
    if (external) {
      setIsAudioLoading(true);
      startExternalAudio(generation);
    } else setIsAudioLoading(false);
  }, [currentAudio]);

  // Another file or the player closed: no stream or conversion of the old one may go on.
  useEffect(
    () => () => {
      audioGeneration.current++;
      externalRef.current = false;
      stopAudio();
      if (videoRef.current) videoRef.current.muted = false;
    },
    [fileId]
  );

  // The audio follows the video.
  useEffect(() => {
    const video = videoRef.current;
    const audio = audioRef.current;
    if (!video || !audio) return;
    const active = () => externalRef.current && !usePlayerStore.getState().isAudioLoading;

    const onPlay = () => active() && audio.play().catch(console.error);
    const onPause = () => {
      audio.pause();
      switchToConvertedWhilePaused();
    };
    const onSeeking = () => active() && syncAudio();
    const onRate = () => (audio.playbackRate = video.playbackRate);
    const onVolume = () => (audio.volume = video.volume);
    video.addEventListener('play', onPlay);
    video.addEventListener('pause', onPause);
    video.addEventListener('seeking', onSeeking);
    video.addEventListener('ratechange', onRate);
    video.addEventListener('volumechange', onVolume);

    let rafId = 0;
    const autoSync = () => {
      if (active() && !video.paused && !audio.paused && !audio.seeking) {
        const drift = video.currentTime - (audioOffset.current + audio.currentTime);
        if (Math.abs(drift) > MAX_AUDIO_DRIFT) syncAudio();
      }
      rafId = requestAnimationFrame(autoSync);
    };
    rafId = requestAnimationFrame(autoSync);

    return () => {
      cancelAnimationFrame(rafId);
      video.removeEventListener('play', onPlay);
      video.removeEventListener('pause', onPause);
      video.removeEventListener('seeking', onSeeking);
      video.removeEventListener('ratechange', onRate);
      video.removeEventListener('volumechange', onVolume);
      audioGeneration.current++;
      stopAudio();
    };
  }, []);

  /** The audio has data at the video's time: release the video (the host plays it if the user wants, which plays the audio). */
  function handleAudioCanPlay() {
    const video = videoRef.current;
    const audio = audioRef.current;
    if (!video || !audio || !usePlayerStore.getState().isAudioLoading) return;
    audio.currentTime = Math.max(0, video.currentTime - audioOffset.current);
    audio.playbackRate = video.playbackRate;
    audio.volume = video.volume;
    setIsAudioLoading(false);
    if (!video.paused) audio.play().catch(console.error);
  }

  function handleAudioError(e: SyntheticEvent<HTMLAudioElement>) {
    if (!e.currentTarget.getAttribute('src')) return; // stopAudio
    // Play on without the track rather than wait forever.
    console.error('Audio track failed', e.currentTarget.error);
    setIsAudioLoading(false);
  }

  function handleLoadedMetadata(e: SyntheticEvent<HTMLVideoElement>) {
    const video = e.currentTarget;

    setDuration(video.duration * 1000);

    const hasAudio = (video as any).webkitAudioDecodedByteCount > 0 || (video as any).mozHasAudio;
    setVideoContainsAudio(hasAudio);

    // A separately streamed track holds the video until it can play (`isAudioLoading`), then it goes on.
    setIsPlaying(video.autoplay);

    // Preferred language (settings), else the track inside the stream.
    const startAudio = metadata?.audioStartIndex ?? metadata?.audioDefaultIndex;
    if (startAudio != null) {
      const audio = metadata?.audioTracks.find((track) => track.index === startAudio);
      setCurrentAudio(audio || null);
    }

    // A track in a preferred language, else a subtitle file the server found for a video without subtitles of its own.
    if (metadata?.subtitleDefaultIndex != null && !usePlayerStore.getState().currentSubtitle) {
      const subtitle = metadata.subtitleTracks.find((track) => track.index === metadata.subtitleDefaultIndex);
      setCurrentSubtitle(subtitle || null);
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
      {/* The host holds the video meanwhile (`isHeld` in PopcornPlayer). */}
      {isAudioLoading && (
        <div className="spinner-wrapper buffering-indicator">
          <Loader color="red" />
          <span>Loading audio track</span>
        </div>
      )}
      <video
        ref={ref}
        src={videoSrc}
        preload="metadata"
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
      <audio ref={audioRef} preload="auto" onCanPlay={handleAudioCanPlay} onError={handleAudioError} />
    </div>
  );
});
