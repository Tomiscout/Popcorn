import { useRef, useEffect } from 'react';

export const disableDefaultListeners = () => {
  document.addEventListener('keydown', (event) => {
    if (event.target.tagName === 'BUTTON' && (event.code === 'Space' || event.code === 'Enter')) {
      event.preventDefault();
    }
  });
  
  document.addEventListener('selectstart', function(event) { // Disable text selection
    event.preventDefault();
  });

  document.addEventListener('dragstart', function(event) { // Disable drag-and-drop
    event.preventDefault();
  });
}

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