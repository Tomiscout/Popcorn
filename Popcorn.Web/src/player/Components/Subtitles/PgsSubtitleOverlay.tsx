import React, { useEffect, useRef } from 'react';
import { usePlayerStore } from '../../../state/player.store';
import * as libpgs from 'libpgs';

interface PgsSubtitleOverlayProps {
  videoRef: React.RefObject<HTMLVideoElement | null>;
  currentKey: string;
}

const PgsSubtitleOverlay: React.FC<PgsSubtitleOverlayProps> = ({ videoRef, currentKey }) => {
  const { getSubtitleFile } = usePlayerStore();
  const rendererRef = useRef<libpgs.PgsRenderer | null>(null);
  const canvasRef = useRef<HTMLCanvasElement>(null);

  useEffect(() => {
    if (!videoRef.current || !currentKey) return;

    const subtitleUrl = getSubtitleFile(currentKey);
    if (!subtitleUrl) return;

    let cancelled = false;

    (async () => {
      const response = await fetch(subtitleUrl);
      const arrayBuffer = await response.arrayBuffer();
      if (cancelled) return;

      rendererRef.current = new libpgs.PgsRenderer({
        workerUrl: '/lib/libpgs.worker.js',
        video: videoRef.current!,
        canvas: canvasRef.current!,
      });
      rendererRef.current.loadFromBuffer(arrayBuffer);
    })();

    return () => {
      cancelled = true;
      rendererRef.current?.dispose();
      rendererRef.current = null;
    };
  }, [currentKey]);

  return (
    <canvas
      ref={canvasRef}
      style={{
        position: 'absolute',
        top: 0,
        left: 0,
        width: '100%',
        height: '100%',
        pointerEvents: 'none',
        objectFit: 'contain',
        background: 'transparent',
      }}
    />
  );
};

export default PgsSubtitleOverlay;
