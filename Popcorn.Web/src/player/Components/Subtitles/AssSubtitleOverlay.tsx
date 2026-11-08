import React, { useEffect, useRef } from 'react';
import ASS from 'assjs';
import { usePlayerStore } from '../../../state/player.store';

export type ResamplingMode = 'video_width' | 'video_height' | 'script_width' | 'script_height';

interface AssSubtitleOverlayProps {
  videoRef: React.RefObject<HTMLVideoElement | null>;
  currentKey: string;
  resampling?: ResamplingMode;
}

const AssSubtitleOverlay: React.FC<AssSubtitleOverlayProps> = ({
  videoRef,
  currentKey,
  resampling = 'video_height',
}) => {
  const containerRef = useRef<HTMLDivElement>(null);
  const assInstance = useRef<ASS | null>(null);
  const { getSubtitleFile } = usePlayerStore();
  const delay = usePlayerStore((s) => s.subtitleDelay);

  useEffect(() => {
    if (!currentKey) {
      assInstance.current?.destroy();
      assInstance.current = null;
      return;
    }

    const assText = getSubtitleFile(currentKey);
    if (!assText) return;

    assInstance.current = new ASS(assText, videoRef.current!, {
      container: containerRef.current!,
      resampling,
    });
    assInstance.current.delay = usePlayerStore.getState().subtitleDelay;

    return () => {
      assInstance.current?.destroy();
      assInstance.current = null;
    };
  }, [currentKey, resampling]);

  // Same sign as mpv's sub-delay: positive = later.
  useEffect(() => {
    if (assInstance.current) assInstance.current.delay = delay;
  }, [delay]);

  return (
    <div
      ref={containerRef}
      style={{
        position: 'absolute',
        top: 0,
        left: 0,
        width: '100%',
        height: '100%',
        pointerEvents: 'none',
      }}
    />
  );
};

export default AssSubtitleOverlay;
