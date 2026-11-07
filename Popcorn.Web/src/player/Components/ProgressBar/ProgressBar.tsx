import React, { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { postMessage } from '../../../utils/events';
import './ProgressBar.css';
import debounce from 'lodash.debounce';
import SkipButton from '../SkipButton/SkipButton';
import {
  canSeekTo,
  isOutro,
  PlayerSegment,
  shouldAutoSkip,
  thumbnailKey,
  usePlayerStore,
} from '../../../state/player.store';
import SeekBar from './SeekBar';

const THUMBNAIL_STEP_MS = 10000;

interface ResolvedSegment {
  /** Identifies the segment within the file. */
  key: string;
  kind: 'intro' | 'outro';
  start: number;
  end: number;
  segment: PlayerSegment;
}

/** Segments in absolute ms (outros are stored relative to the end of the file). */
function resolveSegments(segments: PlayerSegment[], duration: number): ResolvedSegment[] {
  if (duration <= 0) return [];
  return segments.map((s) => {
    const start = s.start < 0 ? duration + s.start : s.start;
    return {
      key: `${s.segmentId}:${s.start}`,
      kind: isOutro(s) ? 'outro' : 'intro',
      start,
      end: Math.min(start + s.length, duration),
      segment: s,
    };
  });
}

/** Leaving a segment within this of its end (playback, skip, seek past) counts as having seen it. */
const SEEN_TOLERANCE_MS = 1000;

/** An outro ending this close to the end of the file: skipping it skips the rest as well (`skipOutroTail`). */
const OUTRO_TAIL_MARGIN_MS = 10_000;

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
  const {
    fileId,
    currentTime,
    duration,
    thumbnailFiles,
    segments,
    segmentAutoSkip,
    skipOutroTail,
    skipSegmentsWithoutDelay,
    downloadRanges,
    setCurrentTime,
  } = usePlayerStore();

  const [popupVisible, setPopupVisible] = useState(false);
  const [popupTime, setPopupTime] = useState('');
  const [popupX, setPopupX] = useState(0);
  const [lineX, setLineX] = useState(0);
  const [hoverSeekable, setHoverSeekable] = useState(true);
  const progressBarRef = useRef<HTMLDivElement>(null);

  const [thumbnail, setThumbnail] = useState<string | null>(null);
  const [isLoading, setIsLoading] = useState(false);
  const lastRequestedTime = useRef<number | null>(null);

  const resolvedSegments = useMemo(() => resolveSegments(segments, duration), [segments, duration]);
  const markers = useMemo<[number, number][]>(
    () => resolvedSegments.map((s) => [s.start / duration, s.end / duration]),
    [resolvedSegments, duration]
  );
  const activeSegment = resolvedSegments.find((s) => currentTime >= s.start && currentTime < s.end);
  const activeSegmentEnd = useRef<number | null>(null);
  activeSegmentEnd.current = !activeSegment
    ? null
    : skipOutroTail && activeSegment.kind === 'outro' && duration - activeSegment.end <= OUTRO_TAIL_MARGIN_MS
      ? duration
      : activeSegment.end;

  // Per file playback: segments entered so far, the one counting down to its auto-skip, the last active one.
  const enteredSegments = useRef(new Set<string>());
  const [countdownSegment, setCountdownSegment] = useState<string | null>(null);
  const previousSegment = useRef<ResolvedSegment | undefined>(undefined);

  // New file: drop the previous file's thumbnail and segment state (before the segment effect below runs for it).
  useEffect(() => {
    lastRequestedTime.current = null;
    setThumbnail(null);
    setIsLoading(false);
    enteredSegments.current.clear();
    previousSegment.current = undefined;
    setCountdownSegment(null);
  }, [fileId]);

  const seek = useCallback(
    (newValue: number) => {
      if (!canSeekTo(usePlayerStore.getState(), newValue)) return;
      setCurrentTime(newValue);
      postMessage('set_playback_time', Math.round(newValue)); //TODO throttle
    },
    [setCurrentTime]
  );

  // Stable: SkipButton is memoised on visibility only, so it keeps the first callback it gets.
  const handleSkip = useCallback(() => {
    if (activeSegmentEnd.current !== null) seek(activeSegmentEnd.current);
  }, [seek]);

  // Watched to (near) its end, skipped or seeked past: the "after first" auto-skip modes skip it from now on.
  const markSeen = (resolved: ResolvedSegment) => {
    const { segments: current, markSegmentSeen } = usePlayerStore.getState();
    const segment = current.find((s) => s.segmentId === resolved.segment.segmentId);
    if (!segment || segment.seen) return;
    markSegmentSeen(segment);
    postMessage('segment_seen', segment.segmentId);
  };
  // An outro running to the end of the file is never left before the next file loads.
  const nearActiveEnd = !!activeSegment && currentTime >= activeSegment.end - SEEN_TOLERANCE_MS;
  useEffect(() => {
    if (nearActiveEnd && activeSegment) markSeen(activeSegment);
  }, [nearActiveEnd, activeSegment?.key]);

  // Auto-skip is decided when a segment is entered, and only the first time per file playback: seeking back into a
  // segment shows the button without the count-down. Without delay it skips right away, else after the count-down.
  useEffect(() => {
    const previous = previousSegment.current;
    previousSegment.current = activeSegment;
    if (previous?.key === activeSegment?.key) return;

    if (previous && currentTime >= previous.end - SEEN_TOLERANCE_MS) markSeen(previous);

    setCountdownSegment(null);
    if (!activeSegment) return;
    const firstEntry = !enteredSegments.current.has(activeSegment.key);
    enteredSegments.current.add(activeSegment.key);
    const mode = segmentAutoSkip?.itemMode ?? segmentAutoSkip?.globalMode ?? 'Always';
    if (!firstEntry || !shouldAutoSkip(mode, activeSegment.segment)) return;
    if (skipSegmentsWithoutDelay) handleSkip();
    else setCountdownSegment(activeSegment.key);
  }, [activeSegment?.key]);

  useEffect(() => {
    if (lastRequestedTime.current === null) return;
    const blobUrl = thumbnailFiles[thumbnailKey(fileId, lastRequestedTime.current)] || null;
    if (blobUrl) {
      setThumbnail(blobUrl);
      setIsLoading(false);
    }
  }, [thumbnailFiles]);

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
    setLineX(x);

    const seekable = canSeekTo(usePlayerStore.getState(), hoveredTime);
    setHoverSeekable(seekable);
    if (seekable) {
      thumbnailRequest(hoveredTime);
    } else {
      // No data there to make a thumbnail from.
      lastRequestedTime.current = null;
      setThumbnail(null);
      setIsLoading(false);
    }
  };

  const handleMouseLeave = () => {
    setPopupVisible(false);
  };

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
        {(['intro', 'outro'] as const).map((kind) => (
          <SkipButton
            key={kind}
            text={`Skip ${kind}`}
            duration={10}
            isVisible={activeSegment?.kind === kind}
            autoSkip={activeSegment?.kind === kind && countdownSegment === activeSegment.key}
            onSkip={handleSkip}
          />
        ))}
      </div>
      <div className="progress-slider-row">
        <div
          className={`progress-slider-container${hoverSeekable ? '' : ' seek-blocked'}`}
          ref={progressBarRef}
          onMouseMove={handleMouseMove}
          onMouseLeave={handleMouseLeave}
        >
          <SeekBar
            value={Math.max(currentTime, 0)}
            max={Math.max(duration, 1)}
            step={1000}
            downloadRanges={downloadRanges}
            markers={markers}
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
