import { useState, useEffect, memo, useRef } from 'react';
import './SkipButton.css';
import { useEventListeners } from '../../Utlis/listener.utils';
import { registerEventHandler } from '../../../utils/events';

const SkipButton = memo(
  ({ text, onSkip, isVisible, duration = 5 }) => {
    const [buttonText, setButtonText] = useState('');
    const [progress, setProgress] = useState(0);
    const [intervalId, setIntervalId] = useState(null);
    const [loadingCanceled, setLoadingCanceled] = useState(false);
    const [isPaused, setIsPaused] = useState(false);
    const isPausedRef = useRef(isPaused);

    useEffect(() => {
      isPausedRef.current = isPaused;
    }, [isPaused]);

    useEffect(() => {
      if (!isVisible) {
        clearInterval(intervalId);
        setLoadingCanceled(false);
        setProgress(0);
        return;
      }

      setButtonText(text);
      setLoadingCanceled(false);

      // Clear any existing interval before setting a new one
      if (intervalId) {
        clearInterval(intervalId);
      }

      const id = setInterval(() => {
        if (isPausedRef.current) return;

        setProgress((oldProgress) => {
          if (oldProgress >= 100) {
            clearInterval(id);
            setLoadingCanceled(false);
            if (onSkip) {
              onSkip();
            }
            return 100;
          }
          return Math.min(oldProgress + (100 / duration) * 0.2, 100);
        });
      }, 200);

      setLoadingCanceled(false);
      setIntervalId(id);
      setTimeout(() => setProgress((100 / duration) * 0.2), 10);
      return () => clearInterval(id);
    }, [text, duration, onSkip, isVisible]);

    registerEventHandler('play_status_changed', (isPlaying) => {
      setIsPaused(!isPlaying);
    });

    const cancelAutoSkip = () => {
      clearInterval(intervalId);
      setProgress(100);
      setLoadingCanceled(true);
    };
    const forceSkip = () => {
      clearInterval(intervalId);
      setProgress(100);
      onSkip();
    };

    // TODO should also consider video pause state here
    useEventListeners('keyup', {
      Escape: cancelAutoSkip,
      Enter: forceSkip,
    });

    return isVisible ? (
      <button className="skip-button" onClick={forceSkip}>
        <div className="skip-button-text">{buttonText}</div>
        <div
          className="skip-button-progress"
          style={{
            width: `${progress}%`,
            transition: loadingCanceled ? 'none' : 'width 1s linear',
          }}
        ></div>
      </button>
    ) : null;
  },
  (prevProps, nextProps) => {
    // Only rerender if isVisible changes
    return prevProps.isVisible === nextProps.isVisible;
  }
);

export default SkipButton;
