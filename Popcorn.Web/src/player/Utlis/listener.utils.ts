import { useRef, useEffect } from 'react';

export const disableDefaultListeners = () => {
  // Player keys are handled on keyup (Space = play/pause, Enter = skip intro): keep a focused button (e.g. play/pause
  // after a click) from activating as well. Chromium activates on keydown/keypress; the native players' CEF gets the
  // character as a separate event, so cancelling keydown does not cancel its keypress.
  const blockButtonActivation = (event: any) => {
    const key = event.code || event.key;
    if (event.target.tagName === 'BUTTON' && ['Space', 'Enter', 'NumpadEnter', ' '].includes(key)) {
      event.preventDefault();
    }
  };
  document.addEventListener('keydown', blockButtonActivation);
  document.addEventListener('keypress', blockButtonActivation);

  document.addEventListener('selectstart', function (event) {
    // Disable text selection
    event.preventDefault();
  });

  document.addEventListener('dragstart', function (event) {
    // Disable drag-and-drop
    event.preventDefault();
  });
};

export const useEventListeners = (eventName: string, keyCallbacks: any) => {
  const keyCallbacksRef = useRef(keyCallbacks);

  useEffect(() => {
    keyCallbacksRef.current = keyCallbacks;
  }, [keyCallbacks]);

  useEffect(() => {
    const keyUpHandler = (event: any) => {
      if (keyCallbacksRef.current[event.code]) {
        keyCallbacksRef.current[event.code]?.();
      } else {
        keyCallbacksRef.current['default']?.();
      }
    };

    document.addEventListener(eventName, keyUpHandler);
    return () => {
      document.removeEventListener(eventName, keyUpHandler);
    };

    //Registeret eventName never changes
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);
};
