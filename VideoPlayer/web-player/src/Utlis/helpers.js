import { useRef, useEffect } from 'react';

export const useEventListeners = (eventName, keyCallbacks) => {
  const keyCallbacksRef = useRef(keyCallbacks);

  useEffect(() => {
    keyCallbacksRef.current = keyCallbacks;
  }, [keyCallbacks]);

  useEffect(() => {
    const keyUpHandler = (event) => {
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