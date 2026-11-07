import React, { useRef } from 'react';

interface SeekBarProps {
  value: number;
  max: number;
  /** Values are rounded to this while seeking. */
  step: number;
  /** Downloaded parts as [start, end] fractions of the bar, drawn under the played part. */
  downloadRanges: [number, number][] | null;
  /** Intro/outro parts as [start, end] fractions of the bar, drawn over the played part. */
  markers: [number, number][];
  onChange: (value: number) => void;
}

const percent = (fraction: number) => `${(Math.max(0, Math.min(1, fraction)) * 100).toFixed(3)}%`;

const rangeStyle = ([start, end]: [number, number]) => ({ left: percent(start), width: percent(end - start) });

/** The seek bar's layers need a shared coordinate space and an order of their own, which Mantine's Slider cannot give. */
const SeekBar = ({ value, max, step, downloadRanges, markers, onChange }: SeekBarProps) => {
  const barRef = useRef<HTMLDivElement>(null);
  const dragging = useRef(false);
  const lastValue = useRef<number | null>(null);

  const seekTo = (clientX: number) => {
    const rect = barRef.current?.getBoundingClientRect();
    if (!rect || rect.width <= 0) return;
    const fraction = Math.max(0, Math.min(1, (clientX - rect.left) / rect.width));
    const next = Math.min(Math.round((fraction * max) / step) * step, max);
    if (next === lastValue.current) return;
    lastValue.current = next;
    onChange(next);
  };

  const handlePointerDown = (e: React.PointerEvent<HTMLDivElement>) => {
    if (e.button !== 0) return;
    e.currentTarget.setPointerCapture(e.pointerId);
    dragging.current = true;
    lastValue.current = null;
    seekTo(e.clientX);
  };

  const handlePointerMove = (e: React.PointerEvent<HTMLDivElement>) => {
    if (dragging.current) seekTo(e.clientX);
  };

  const stopDragging = () => {
    dragging.current = false;
  };

  const played = value / max;

  return (
    <div
      ref={barRef}
      className="seek-bar"
      role="slider"
      aria-label="Seek"
      aria-valuemin={0}
      aria-valuemax={max}
      aria-valuenow={value}
      onPointerDown={handlePointerDown}
      onPointerMove={handlePointerMove}
      onPointerUp={stopDragging}
      onPointerCancel={stopDragging}
    >
      <div className="seek-bar-track">
        {downloadRanges?.map((range, index) => (
          <div key={index} className="seek-bar-downloaded" style={rangeStyle(range)} />
        ))}
        <div className="seek-bar-played" style={{ width: percent(played) }} />
        {markers.map((range, index) => (
          <div key={index} className="seek-bar-marker" style={rangeStyle(range)} />
        ))}
      </div>
      <div className="seek-bar-thumb" style={{ left: percent(played) }} />
    </div>
  );
};

export default SeekBar;
