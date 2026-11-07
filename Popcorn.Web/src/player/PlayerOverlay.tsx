import React, { forwardRef, useEffect, useImperativeHandle, useRef, useState } from 'react';
import { Loader } from '@mantine/core';
import ErrorOverlay from './Components/ErrorOverlay/ErrorOverlay';
import PlaybackNotificationControl from './Components/PlaybackNotificationControl/PlaybackNotificationControl';
import { CaptureClickArea } from './Components/CaptureClickArea/CaptureClickArea';
import { ControlBar } from './Components/ControlBar/ControlBar';
import { TopBar } from './Components/TopBar/TopBar';
import { NextFileDialog } from './Components/NextFileDialog/NextFileDialog';
import { FinOverlay } from './Components/FinOverlay/FinOverlay';
import { useEventListeners } from './Utlis/listener.utils';
import { needsBuffering, usePlayerStore } from '../state/player.store';

interface PlayerOverlayProps extends Omit<React.HTMLAttributes<HTMLDivElement>, 'children'> {
  /** Extra classes on the container (`inline` / `floating` / `fullscreen` / `native`). */
  className: string;
  isFloating?: boolean;
  isLoading?: boolean;
  /** Host specific layers that need the metadata (e.g. browser subtitle rendering). */
  children?: React.ReactNode;
}

/**
 * The player UI shared by every host: click area, top bar, controls, notifications, errors. Talks to the host only
 * through the player store (state) and the local event bus in `utils/events` (commands such as
 * `play_status_changed`, `set_playback_time`, `folder_open`, `thumb_requested`, `player_close`).
 */
export const PlayerOverlay = forwardRef<HTMLDivElement, PlayerOverlayProps>(
  // `rest`: react-draggable (web floating mode) injects style/className/mouse handlers into its child.
  ({ className, isFloating = false, isLoading = false, children, ...rest }, ref) => {
    const containerRef = useRef<HTMLDivElement>(null);
    useImperativeHandle(ref, () => containerRef.current!, []);

    const metadata = usePlayerStore((s) => s.metadata);
    const [controlsActive, setShowControls] = useState(true);
    const holdControls = usePlayerStore((s) => s.holdControls);
    const showControls = controlsActive || holdControls;
    // A user's pause wins over the indicator: nothing is waiting for the download then.
    const showBuffering = usePlayerStore((s) => s.isBuffering && s.isPlaying);
    const controlsTimeoutRef = useRef<ReturnType<typeof setTimeout>>(null);

    const displayControls = () => {
      setShowControls(true);
      if (containerRef.current) containerRef.current.style.cursor = 'initial';

      if (controlsTimeoutRef.current) {
        clearTimeout(controlsTimeoutRef.current);
      }

      controlsTimeoutRef.current = setTimeout(() => {
        setShowControls(false);
        if (containerRef.current && !usePlayerStore.getState().holdControls) {
          containerRef.current.style.cursor = 'none';
        }
      }, 4000);
    };

    useEventListeners('keyup', { default: displayControls });
    useEventListeners('mousemove', { default: displayControls });

    // Hosts react to `isBuffering` by holding playback. Evaluated on store changes, not renders: the time changes often.
    useEffect(
      () =>
        usePlayerStore.subscribe((state) => {
          const buffering = needsBuffering(state);
          if (buffering !== state.isBuffering) state.setIsBuffering(buffering);
        }),
      []
    );

    useEffect(() => {
      containerRef.current?.focus();
      return () => {
        if (controlsTimeoutRef.current) clearTimeout(controlsTimeoutRef.current);
      };
    }, []);

    return (
      <div {...rest} className={`video-container ${className}`} tabIndex={-1} ref={containerRef}>
        <CaptureClickArea isFloating={isFloating} />
        <TopBar showControls={showControls} />
        <ErrorOverlay />
        <NextFileDialog />
        <FinOverlay />
        {isLoading && (
          <div className="spinner-wrapper">
            <Loader color="red" />
          </div>
        )}
        {showBuffering && !isLoading && (
          <div className="spinner-wrapper buffering-indicator">
            <Loader color="red" />
            <span>Buffering</span>
          </div>
        )}
        {metadata && (
          <>
            <ControlBar showControls={showControls} />
            <PlaybackNotificationControl />
            {children}
          </>
        )}
      </div>
    );
  }
);
