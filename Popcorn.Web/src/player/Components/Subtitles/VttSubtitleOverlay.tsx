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
    setTimeout(updateCues, 0); // initial cue update
  }, [currentKey]);

  function updateCues() {
    if (!videoRef.current) return;
    const currentTime = videoRef.current.currentTime;
    const active = cues.filter((cue) => currentTime >= cue.startTime && currentTime <= cue.endTime);
    setActiveCues(active);
  }

  // Sync cues with video time
  useEffect(() => {
    if (!videoRef.current) return;

    videoRef.current?.addEventListener('timeupdate', updateCues);
    return () => {
      videoRef.current?.removeEventListener('timeupdate', updateCues);
    };
  }, [cues, videoRef]);

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
