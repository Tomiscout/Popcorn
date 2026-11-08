import React, { useRef } from 'react';
import './CaptureClickArea.css';
import { postMessage } from '../../../utils/events';
import { Text } from '@mantine/core';
import { isAudioFormat, isSubtitleFormat } from '../../../utils/file.utils';
import { usePlayerFeatures } from '../../player-features';
import { usePlayerStore } from '../../../state/player.store';
import { setPlayerVolume } from '../../volume';

export interface CaptureClickAreaProps {
  isFloating: boolean;
  /** A tap with a finger (instead of play/pause): the overlay shows or hides the controls. */
  onTouchTap: () => void;
}

/** Movement before a touch counts as a swipe rather than a tap. */
const SWIPE_THRESHOLD_PX = 12;
/** A swipe over this share of the area's height goes from 0 to 100 % volume. */
const VOLUME_SWIPE_HEIGHT = 0.6;

interface TouchTrack {
  pointerId: number;
  x: number;
  y: number;
  startVolume: number;
  /** Right half: vertical swipes change the volume. */
  volumeSide: boolean;
  swiping: boolean;
}

/**
 * The video surface. Mouse: click = play/pause, double click = fullscreen, file drops. Touch: tap = show/hide the
 * controls, vertical swipe on the right half = volume.
 */
export const CaptureClickArea: React.FC<CaptureClickAreaProps> = ({ isFloating, onTouchTap }) => {
  const clickTimeoutRef = useRef<NodeJS.Timeout>(null);
  const [isFileDragging, setIsFileDragging] = React.useState(false);
  const { fileDrop } = usePlayerFeatures();
  const lastPointerType = useRef<string>('mouse');
  const touch = useRef<TouchTrack | null>(null);
  /** The click that follows a swipe is not a tap. */
  const swiped = useRef(false);

  const handlePointerDown = (e: React.PointerEvent<HTMLDivElement>) => {
    lastPointerType.current = e.pointerType;
    if (e.pointerType !== 'touch' || isFloating || touch.current) return;
    const rect = e.currentTarget.getBoundingClientRect();
    const { volume, muted } = usePlayerStore.getState();
    touch.current = {
      pointerId: e.pointerId,
      x: e.clientX,
      y: e.clientY,
      startVolume: muted ? 0 : volume,
      volumeSide: e.clientX > rect.left + rect.width / 2,
      swiping: false,
    };
    swiped.current = false;
  };

  const handlePointerMove = (e: React.PointerEvent<HTMLDivElement>) => {
    const t = touch.current;
    if (!t || t.pointerId !== e.pointerId) return;
    const dx = e.clientX - t.x;
    const dy = e.clientY - t.y;
    if (!t.swiping) {
      if (Math.max(Math.abs(dx), Math.abs(dy)) < SWIPE_THRESHOLD_PX) return;
      t.swiping = true;
      swiped.current = true;
      // Horizontal swipes, and vertical ones on the left half, do nothing (yet).
      if (!t.volumeSide || Math.abs(dy) < Math.abs(dx)) t.volumeSide = false;
    }
    if (!t.volumeSide) return;
    const height = e.currentTarget.getBoundingClientRect().height;
    const volume = t.startVolume - dy / (height * VOLUME_SWIPE_HEIGHT);
    if (Math.round(volume * 100) !== Math.round(usePlayerStore.getState().volume * 100)) setPlayerVolume(volume);
  };

  const handlePointerEnd = (e: React.PointerEvent<HTMLDivElement>) => {
    if (touch.current?.pointerId === e.pointerId) touch.current = null;
  };

  const handleClick = () => {
    if (lastPointerType.current === 'touch') {
      if (!swiped.current) onTouchTap();
      swiped.current = false;
      return;
    }

    if (clickTimeoutRef.current) {
      clearTimeout(clickTimeoutRef.current);
    }

    if (isFloating) return;

    clickTimeoutRef.current = setTimeout(() => {
      postMessage('playback_toggle');
      clickTimeoutRef.current = null;
    }, 250);
  };

  const handleDoubleClick = () => {
    if (lastPointerType.current === 'touch') return; // two taps = show and hide the controls
    if (clickTimeoutRef.current) {
      clearTimeout(clickTimeoutRef.current);
      clickTimeoutRef.current = null;
    }
    postMessage('request_fullscreen_toggle');
  };

  const handleDragOver = (e: React.DragEvent<HTMLDivElement>) => {
    e.preventDefault(); // Needed to allow drop
    e.stopPropagation();

    setIsFileDragging(true);
  };

  const handleDragLeave = (e: React.DragEvent<HTMLDivElement>) => {
    e.preventDefault();
    e.stopPropagation();

    setIsFileDragging(false);
  };

  const handleDrop = (e: React.DragEvent<HTMLDivElement>) => {
    e.preventDefault();
    e.stopPropagation();
    setIsFileDragging(false);

    const files = Array.from(e.dataTransfer.files);
    for (const file of files) {
      if (isSubtitleFormat(file.name)) {
        postMessage('subtitle_dropped', file);
      } else if (isAudioFormat(file.name)) {
        postMessage('audio_dropped', file);
      } else {
        postMessage('player_error', `Unsupported file type dropped: ${file.name}`);
      }
    }
  };

  return (
    <div
      className={`capture-click-area ${isFileDragging ? 'drag-over' : ''}`}
      tabIndex={-1}
      onClick={handleClick}
      onDoubleClick={handleDoubleClick}
      onPointerDown={handlePointerDown}
      onPointerMove={handlePointerMove}
      onPointerUp={handlePointerEnd}
      onPointerCancel={handlePointerEnd}
      {...(fileDrop && { onDragOver: handleDragOver, onDragLeave: handleDragLeave, onDrop: handleDrop })}
    >
      {isFileDragging && (
        <div className="drop-text" style={{ color: 'white' }}>
          <Text size="xl">Drop subtitle or audio file to add to the video</Text>
        </div>
      )}
    </div>
  );
};
