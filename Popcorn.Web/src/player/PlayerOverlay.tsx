import React, { forwardRef, useEffect, useImperativeHandle, useRef, useState } from 'react';
import { Loader } from '@mantine/core';
import ErrorOverlay from './Components/ErrorOverlay/ErrorOverlay';
import PlaybackNotificationControl from './Components/PlaybackNotificationControl/PlaybackNotificationControl';
import { CaptureClickArea } from './Components/CaptureClickArea/CaptureClickArea';
import { ControlBar } from './Components/ControlBar/ControlBar';
import { TopBar } from './Components/TopBar/TopBar';
import { useEventListeners } from './Utlis/listener.utils';
import { usePlayerStore } from '../state/player.store';

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
    const [showControls, setShowControls] = useState(true);
    const controlsTimeoutRef = useRef<ReturnType<typeof setTimeout>>(null);

    const displayControls = () => {
      setShowControls(true);
      if (containerRef.current) containerRef.current.style.cursor = 'initial';

      if (controlsTimeoutRef.current) {
        clearTimeout(controlsTimeoutRef.current);
      }

      controlsTimeoutRef.current = setTimeout(() => {
        setShowControls(false);
        if (containerRef.current) {
          containerRef.current.style.cursor = 'none';
        }
      }, 4000);
    };

    useEventListeners('keyup', { default: displayControls });
    useEventListeners('mousemove', { default: displayControls });

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
        {isLoading && (
          <div className="spinner-wrapper">
            <Loader color="red" />
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
