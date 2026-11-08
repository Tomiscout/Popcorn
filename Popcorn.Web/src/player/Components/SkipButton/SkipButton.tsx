import { useState, useEffect, useLayoutEffect, memo, useRef } from 'react';
import './SkipButton.css';
import { useEventListeners } from '../../Utlis/listener.utils';
import { usePlayerStore } from '../../../state/player.store';

type SkipButtonProps = {
  text: string;
  /** Count-down length in seconds. */
  duration?: number;
  isVisible: boolean;
  /** Count down and skip on its own; false = the button is shown full and only skips when used. */
  autoSkip: boolean;
  onSkip: () => void;
};

const TICK_MS = 200;
/** Same as the button background's hover transition (SkipButton.css). */
const HOVER_TRANSITION = 'background-color 0.2s ease';

const SkipButton = memo(({ text, onSkip, isVisible, autoSkip, duration = 5 }: SkipButtonProps) => {
  const [progress, setProgress] = useState(0);
  const [countingDown, setCountingDown] = useState(false);
  const isPaused = !usePlayerStore((s) => s.isPlaying);
  const isPausedRef = useRef(isPaused);
  isPausedRef.current = isPaused;
  const onSkipRef = useRef(onSkip);
  onSkipRef.current = onSkip;

  // Layout effect: no frame of the previous fill when the button appears.
  useLayoutEffect(() => {
    if (!isVisible || !autoSkip) {
      setCountingDown(false);
      setProgress(100);
      return;
    }

    setCountingDown(true);
    setProgress(0);
    const step = (100 / duration) * (TICK_MS / 1000);
    const id = setInterval(() => {
      if (isPausedRef.current) return;
      setProgress((old) => Math.min(old + step, 100));
    }, TICK_MS);
    return () => clearInterval(id);
  }, [isVisible, autoSkip, duration]);

  // Skip once the count-down is full (outside the state updater: skipping updates the player store).
  useEffect(() => {
    if (countingDown && progress >= 100) {
      setCountingDown(false);
      onSkipRef.current();
    }
  }, [countingDown, progress]);

  const cancelAutoSkip = () => {
    if (!countingDown) return;
    setCountingDown(false);
    setProgress(100);
  };
  const forceSkip = () => {
    if (!isVisible) return;
    setCountingDown(false);
    setProgress(100);
    onSkipRef.current();
  };

  useEventListeners('keyup', {
    Escape: cancelAutoSkip,
    Enter: forceSkip,
    NumpadEnter: forceSkip,
  });

  return isVisible ? (
    <button className="skip-button" data-counting={countingDown || undefined} onClick={forceSkip}>
      <div className="skip-button-text">{text}</div>
      <div
        className="skip-button-progress"
        style={{ width: `${progress}%`, transition: countingDown ? `width ${TICK_MS}ms linear, ${HOVER_TRANSITION}` : HOVER_TRANSITION }}
      ></div>
    </button>
  ) : null;
});

export default SkipButton;
