import React, { useEffect, useRef, useState } from 'react';
import { usePlayerStore } from '../../../state/player.store';
import { WebVTT, VTTCue } from 'vtt.js';
import { postMessage } from '../../../utils/events';

interface VttSubtitleOverlayProps {
  videoRef: React.RefObject<HTMLVideoElement | null>;
  currentKey: string;
}

const VttSubtitleOverlay: React.FC<VttSubtitleOverlayProps> = ({ videoRef, currentKey }) => {
  const containerRef = useRef<HTMLDivElement>(null);
  const { getSubtitleFile } = usePlayerStore();
  const delay = usePlayerStore((s) => s.subtitleDelay);
  const [cues, setCues] = useState<VTTCue[]>([]);
  const [activeCues, setActiveCues] = useState<VTTCue[]>([]);

  useEffect(() => {
    if (!currentKey) return;

    const vttText = getSubtitleFile(currentKey);
    if (!vttText || !videoRef.current) return;

    const parser = new WebVTT.Parser(window, WebVTT.StringDecoder());
    const parsedCues: VTTCue[] = [];

    parser.oncue = (cue: VTTCue) => {
      parsedCues.push(cue);
    };

    parser.onparsingerror = (err: any) => {
      postMessage('player_error', `VTT parsing error:\n${JSON.stringify(err)}`);
    };

    parser.parse(vttText);
    parser.flush();

    setCues(parsedCues);
  }, [currentKey]);

  // Checked every frame (timeupdate comes only ~4 times a second: lines would be up to 250 ms late, which defeats
  // the subtitle delay's 10 ms steps); React only re-renders when the set of lines on screen changes.
  useEffect(() => {
    const video = videoRef.current;
    if (!video) return;
    let shown: VTTCue[] = [];
    let frame = 0;
    const update = () => {
      frame = requestAnimationFrame(update);
      // Positive delay = subtitles later (mpv's sub-delay).
      const time = video.currentTime - usePlayerStore.getState().subtitleDelay;
      const active = cues.filter((cue) => time >= cue.startTime && time <= cue.endTime);
      if (active.length === shown.length && active.every((cue, i) => cue === shown[i])) return;
      shown = active;
      setActiveCues(active);
    };
    update();
    return () => cancelAnimationFrame(frame);
  }, [cues, videoRef, delay]);

  return (
    <div
      ref={containerRef}
      style={{
        position: 'absolute',
        bottom: '10%',
        width: '100%',
        textAlign: 'center',
        color: 'white',
        textShadow: '0px 0px 4px black',
        fontSize: '3vw',
        pointerEvents: 'none',
      }}
    >
      {activeCues.map((cue, idx) => (
        <div key={idx}>{cue.text}</div>
      ))}
    </div>
  );
};

export default VttSubtitleOverlay;
