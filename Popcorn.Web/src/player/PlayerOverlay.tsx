import React, { forwardRef, useEffect, useImperativeHandle, useRef, useState } from 'react';
import { Loader } from '@mantine/core';
import ErrorOverlay from './Components/ErrorOverlay/ErrorOverlay';
import PlaybackNotificationControl from './Components/PlaybackNotificationControl/PlaybackNotificationControl';
import PlayerToasts from './Components/PlayerToasts/PlayerToasts';
import { CaptureClickArea } from './Components/CaptureClickArea/CaptureClickArea';
import { ControlBar } from './Components/ControlBar/ControlBar';
import { TopBar } from './Components/TopBar/TopBar';
import { NextFileDialog } from './Components/NextFileDialog/NextFileDialog';
import { FinOverlay } from './Components/FinOverlay/FinOverlay';
import { useEventListeners } from './Utlis/listener.utils';
import { needsBuffering, usePlayerStore } from '../state/player.store';
import { CenterPlayButton } from './Components/CenterPlayButton/CenterPlayButton';
import { SubtitleSync } from './Components/SubtitleSync/SubtitleSync';
import { isTouchDevice } from '../utils/platform';

/** Idle time before the controls hide. */
const CONTROLS_HIDE_MS = 4000;
/** The controls stay up this long when a new file starts playing. */
const NEW_FILE_CONTROLS_MS = 5000;

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
    const subtitleSyncOpen = usePlayerStore((s) => s.subtitleSyncOpen);
    const [controlsActive, setShowControls] = useState(true);
    const holdControls = usePlayerStore((s) => s.holdControls);
    const showControls = controlsActive || holdControls;
    // A user's pause wins over the indicator: nothing is waiting for the download then.
    const showBuffering = usePlayerStore((s) => s.isBuffering && s.isPlaying);
    const controlsTimeoutRef = useRef<ReturnType<typeof setTimeout>>(null);
    // Time the controls hide at: activity only extends it (a mouse move must not cut a new file's 5 s short).
    const controlsHideAt = useRef(0);
    const fileId = usePlayerStore((s) => s.fileId);

    const displayControls = (hideAfterMs = CONTROLS_HIDE_MS) => {
      const now = Date.now();
      controlsHideAt.current = Math.max(controlsHideAt.current, now + hideAfterMs);
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
      }, controlsHideAt.current - now);
    };

    const hideControls = () => {
      if (controlsTimeoutRef.current) clearTimeout(controlsTimeoutRef.current);
      controlsHideAt.current = 0;
      setShowControls(false);
    };
    const toggleControlsByTap = () => (controlsActive ? hideControls() : displayControls());

    // Another file started (next/previous episode, picked from the list): show what is playing for a while.
    useEffect(() => {
      if (fileId) displayControls(NEW_FILE_CONTROLS_MS);
      // eslint-disable-next-line react-hooks/exhaustive-deps
    }, [fileId]);

    useEventListeners('keyup', { default: () => displayControls() });
    // Mouse only: a tap also sends a mouse move, but taps on the video toggle the controls (CaptureClickArea).
    // Touches elsewhere (buttons, seek bar) keep the controls up.
    useEffect(() => {
      const onPointerMove = (e: PointerEvent) => e.pointerType === 'mouse' && displayControls();
      const onPointerDown = (e: PointerEvent) => {
        if (e.pointerType !== 'mouse' && !(e.target as HTMLElement).closest('.capture-click-area')) displayControls();
      };
      document.addEventListener('pointermove', onPointerMove);
      document.addEventListener('pointerdown', onPointerDown);
      return () => {
        document.removeEventListener('pointermove', onPointerMove);
        document.removeEventListener('pointerdown', onPointerDown);
      };
      // eslint-disable-next-line react-hooks/exhaustive-deps
    }, []);

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
      displayControls(); // starts the hide timer: touch screens have no mouse move to start it
      return () => {
        if (controlsTimeoutRef.current) clearTimeout(controlsTimeoutRef.current);
      };
    }, []);

    return (
      <div {...rest} className={`video-container ${className}`} tabIndex={-1} ref={containerRef}>
        <CaptureClickArea isFloating={isFloating} onTouchTap={toggleControlsByTap} />
        <TopBar showControls={showControls} />
        <ErrorOverlay />
        <PlayerToasts />
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
            {isTouchDevice && !isFloating && <CenterPlayButton visible={showControls} />}
            <PlaybackNotificationControl />
            {subtitleSyncOpen && !isFloating && <SubtitleSync />}
            {children}
          </>
        )}
      </div>
    );
  }
);
