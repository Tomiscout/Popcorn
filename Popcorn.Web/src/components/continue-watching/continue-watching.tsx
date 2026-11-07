import React, { useLayoutEffect, useRef, useState } from 'react';
import './continue-watching.css';

/** Saved position in the file the item continues with: red bar + "7 of 23min". */
export const ContinueProgress: React.FC<{ position: number; duration: number }> = ({ position, duration }) => (
  <div className="continue-progress">
    <div className="continue-bar">
      <div className="continue-bar-fill" style={{ width: `${Math.min(position / duration, 1) * 100}%` }} />
    </div>
    <span className="continue-time">
      {Math.round(position / 60)} of {Math.round(duration / 60)}min
    </span>
  </div>
);

/** One line; text wider than its box scrolls back and forth while hovered (or its watch card is). */
export const MarqueeText: React.FC<{ text: string }> = ({ text }) => {
  const boxRef = useRef<HTMLDivElement>(null);
  const textRef = useRef<HTMLSpanElement>(null);
  const [overflow, setOverflow] = useState(0);

  useLayoutEffect(() => {
    if (boxRef.current && textRef.current) {
      setOverflow(Math.max(0, textRef.current.scrollWidth - boxRef.current.clientWidth));
    }
  }, [text]);

  return (
    <div ref={boxRef} className="continue-title" title={text}>
      <span
        ref={textRef}
        className={overflow > 0 ? 'marquee' : undefined}
        style={
          overflow > 0
            ? ({
                '--marquee-distance': `-${overflow}px`,
                '--marquee-duration': `${2 + overflow / 25}s`,
              } as React.CSSProperties)
            : undefined
        }
      >
        {text}
      </span>
    </div>
  );
};
