import React, { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { postMessage } from '../../../utils/events';
import './ProgressBar.css';
import debounce from 'lodash.debounce';
import SkipButton from '../SkipButton/SkipButton';
import { PlayerSegment, thumbnailKey, usePlayerStore } from '../../../state/player.store';
import { Slider } from '@mantine/core';

const THUMBNAIL_STEP_MS = 10000;

interface ResolvedSegment {
  kind: 'intro' | 'outro';
  start: number;
  end: number;
}

/** Segments in absolute ms (outros are stored relative to the end of the file). */
function resolveSegments(segments: PlayerSegment[], duration: number): ResolvedSegment[] {
  if (duration <= 0) return [];
  return segments.map((s) => {
    const start = s.start < 0 ? duration + s.start : s.start;
    return { kind: s.start < 0 ? 'outro' : 'intro', start, end: Math.min(start + s.length, duration) };
  });
}

function timeFormat(durationMs: number) {
  if (durationMs <= 0) return '?';

  const durationSeconds = durationMs / 1000;
  // Hours, minutes and seconds
  const hrs = ~~(durationSeconds / 3600);
  const mins = ~~((durationSeconds % 3600) / 60);
  const secs = ~~durationSeconds % 60;

  // Output like "1:01" or "4:03:59" or "123:03:59"
  let ret = '';
  if (hrs > 0) {
    ret += '' + hrs + ':' + (mins < 10 ? '0' : '');
  }
  ret += '' + mins + ':' + (secs < 10 ? '0' : '');
  ret += '' + secs;

  return ret;
}

const ProgressBar = () => {
  const { fileId, currentTime, duration, thumbnailFiles, segments, setCurrentTime } = usePlayerStore();

  const [popupVisible, setPopupVisible] = useState(false);
  const [popupTime, setPopupTime] = useState('');
  const [popupX, setPopupX] = useState(0);
  const [lineX, setLineX] = useState(0);
  const progressBarRef = useRef<HTMLDivElement>(null);

  const [thumbnail, setThumbnail] = useState<string | null>(null);
  const [isLoading, setIsLoading] = useState(false);
  const lastRequestedTime = useRef<number | null>(null);

  const resolvedSegments = useMemo(() => resolveSegments(segments, duration), [segments, duration]);
  const activeSegment = resolvedSegments.find((s) => currentTime >= s.start && currentTime < s.end);
  const activeSegmentEnd = useRef<number | null>(null);
  activeSegmentEnd.current = activeSegment?.end ?? null;

  // New file: drop the previous file's thumbnail state.
  useEffect(() => {
    lastRequestedTime.current = null;
    setThumbnail(null);
    setIsLoading(false);
  }, [fileId]);

  useEffect(() => {
    if (lastRequestedTime.current === null) return;
    const blobUrl = thumbnailFiles[thumbnailKey(fileId, lastRequestedTime.current)] || null;
    if (blobUrl) {
      setThumbnail(blobUrl);
      setIsLoading(false);
    }
  }, [thumbnailFiles]);

  const seek = useCallback(
    (newValue: number) => {
      setCurrentTime(newValue);
      postMessage('set_playback_time', Math.round(newValue)); //TODO throttle
    },
    [setCurrentTime]
  );

  // Stable: SkipButton is memoised on visibility only, so it keeps the first callback it gets.
  const handleSkip = useCallback(() => {
    if (activeSegmentEnd.current !== null) seek(activeSegmentEnd.current);
  }, [seek]);

  // The host answers with a thumbnail in `thumbnailFiles` under thumbnailKey(fileId, time).
  const debouncedThumbnailRequest = useMemo(
    () => debounce((timeMs: number) => postMessage('thumb_requested', timeMs), 100),
    []
  );
  useEffect(() => () => debouncedThumbnailRequest.cancel(), [debouncedThumbnailRequest]);

  const thumbnailRequest = (hoveredTime: number) => {
    const currentThumbTime = Math.floor(hoveredTime / THUMBNAIL_STEP_MS) * THUMBNAIL_STEP_MS;
    if (lastRequestedTime.current === currentThumbTime) {
      return;
    }
    lastRequestedTime.current = currentThumbTime;

    const cached = thumbnailFiles[thumbnailKey(fileId, currentThumbTime)];
    if (cached) {
      setThumbnail(cached);
      setIsLoading(false);
      return;
    }

    setIsLoading(true);
    debouncedThumbnailRequest(currentThumbTime);
  };

  const popupHalfWidth = 80 + 4; //width + padding
  const handleMouseMove = (e: React.MouseEvent) => {
    if (!progressBarRef.current || duration <= 0) return;
    const rect = progressBarRef.current.getBoundingClientRect();

    if (rect.left <= 0) {
      return; //out of left bounds
    }
    const x = e.clientX - rect.left; // x position within the element.
    const hoveredTime = Math.max(0, Math.min(1, x / rect.width)) * duration;
    const screenWidth = window.innerWidth || document.documentElement.clientWidth || document.body.clientWidth;

    // Clamp to the screen
    const newPopupX = Math.max(0, Math.min(e.clientX - popupHalfWidth, screenWidth - popupHalfWidth * 2));

    setPopupVisible(true);
    setPopupTime(timeFormat(hoveredTime));
    setPopupX(newPopupX);
    setLineX(e.clientX - 10); // 10px is container margin

    thumbnailRequest(hoveredTime);
  };

  const handleMouseLeave = () => {
    setPopupVisible(false);
  };

  const getSegmentStyle = (segment: ResolvedSegment) => ({
    left: `${((segment.start / duration) * 100).toFixed(2)}%`,
    width: `${(((segment.end - segment.start) / duration) * 100).toFixed(2)}%`,
  });

  return (
    <div className="progress-controls">
      {popupVisible && (
        <div className="time-seek-popup" style={{ marginLeft: `${popupX}px` }}>
          <div className="popup-image">
            {isLoading ? <div className="spinner"></div> : thumbnail && <img src={thumbnail} alt="Thumbnail" />}
          </div>
          <div>{popupTime}</div>
        </div>
      )}
      <div className="skip-button-container">
        <SkipButton text={'Skip intro'} duration={10} isVisible={activeSegment?.kind === 'intro'} onSkip={handleSkip} />
        <SkipButton text={'Skip outro'} duration={10} isVisible={activeSegment?.kind === 'outro'} onSkip={handleSkip} />
      </div>
      <div className="progress-slider-row">
        <div
          className="progress-slider-container"
          ref={progressBarRef}
          onMouseMove={handleMouseMove}
          onMouseOut={handleMouseLeave}
        >
          {resolvedSegments.map((segment, index) => (
            <div key={index} className="time-segment" style={getSegmentStyle(segment)}></div>
          ))}
          <Slider
            className="progress-slider"
            color="red"
            showLabelOnHover={false}
            label={null}
            min={0}
            max={Math.max(duration, 1)}
            step={1000}
            value={Math.max(currentTime, 0)}
            onChange={seek}
          />
          {popupVisible && <div className="time-seek-line" style={{ left: `${lineX}px` }}></div>}
        </div>
        <div id="time-remaining">{timeFormat(duration - currentTime)}</div>
      </div>
    </div>
  );
};

export default ProgressBar;
