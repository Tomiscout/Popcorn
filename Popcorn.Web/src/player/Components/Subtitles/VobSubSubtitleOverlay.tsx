import React, { useEffect, useRef, useState } from 'react';
import { usePlayerStore } from '../../../state/player.store';
import { postMessage } from '../../../utils/events';
import { decodeVobSubImage, parseVobSub, vobSubCueAt, type VobSub, type VobSubCue } from './vobsub';

interface VobSubSubtitleOverlayProps {
  videoRef: React.RefObject<HTMLVideoElement | null>;
  currentKey: string;
}

/**
 * DVD bitmap subtitles (`vobsub` tracks: .idx text + .sub program stream). The canvas has the subtitle frame's size
 * (.idx `size`) and is stretched over the picture, so anamorphic DVD frames line up with the displayed video.
 */
const VobSubSubtitleOverlay: React.FC<VobSubSubtitleOverlayProps> = ({ videoRef, currentKey }) => {
  const { getSubtitleFile } = usePlayerStore();
  const delay = usePlayerStore((s) => s.subtitleDelay);
  const canvasRef = useRef<HTMLCanvasElement>(null);
  const [vobSub, setVobSub] = useState<VobSub | null>(null);
  const [rect, setRect] = useState<{ left: number; top: number; width: number; height: number } | null>(null);

  useEffect(() => {
    setVobSub(null);
    const subtitleUrl = getSubtitleFile(currentKey);
    if (!subtitleUrl) return;
    let cancelled = false;
    (async () => {
      try {
        const buffer = await (await fetch(subtitleUrl)).arrayBuffer();
        if (!cancelled) setVobSub(parseVobSub(buffer));
      } catch (error) {
        if (!cancelled) postMessage('player_error', `VobSub subtitles could not be read:\n${error}`);
      }
    })();
    return () => {
      cancelled = true;
    };
  }, [currentKey]);

  // The picture's rectangle inside the video element (object-fit: contain).
  useEffect(() => {
    const video = videoRef.current;
    if (!video) return;
    const update = () => {
      const { clientWidth: w, clientHeight: h, videoWidth: vw, videoHeight: vh } = video;
      if (!w || !h || !vw || !vh) return setRect(null);
      const scale = Math.min(w / vw, h / vh);
      setRect({ left: (w - vw * scale) / 2, top: (h - vh * scale) / 2, width: vw * scale, height: vh * scale });
    };
    update();
    const observer = new ResizeObserver(update);
    observer.observe(video);
    video.addEventListener('loadedmetadata', update);
    video.addEventListener('resize', update);
    return () => {
      observer.disconnect();
      video.removeEventListener('loadedmetadata', update);
      video.removeEventListener('resize', update);
    };
  }, [videoRef]);

  // Checked every frame like the text subtitles (timeupdate is too coarse); drawn only when the cue changes.
  useEffect(() => {
    const video = videoRef.current;
    const canvas = canvasRef.current;
    const context = canvas?.getContext('2d');
    if (!video || !canvas || !context || !vobSub) return;
    canvas.width = vobSub.frame?.width ?? video.videoWidth;
    canvas.height = vobSub.frame?.height ?? video.videoHeight;
    let shown: VobSubCue | null = null;
    let frame = 0;
    const update = () => {
      frame = requestAnimationFrame(update);
      // Positive delay = subtitles later (mpv's sub-delay).
      const cue = vobSubCueAt(vobSub.cues, video.currentTime - usePlayerStore.getState().subtitleDelay);
      if (cue === shown) return;
      shown = cue;
      context.clearRect(0, 0, canvas.width, canvas.height);
      if (!cue) return;
      try {
        context.putImageData(decodeVobSubImage(cue, vobSub.palette), cue.x, cue.y);
      } catch (error) {
        console.warn('VobSub subtitle could not be decoded', cue.start, error);
      }
    };
    update();
    return () => {
      cancelAnimationFrame(frame);
      context.clearRect(0, 0, canvas.width, canvas.height);
    };
  }, [vobSub, videoRef, delay]);

  return (
    <canvas
      ref={canvasRef}
      style={{
        position: 'absolute',
        ...(rect ?? { left: 0, top: 0, width: '100%', height: '100%' }),
        pointerEvents: 'none',
        background: 'transparent',
      }}
    />
  );
};

export default VobSubSubtitleOverlay;
