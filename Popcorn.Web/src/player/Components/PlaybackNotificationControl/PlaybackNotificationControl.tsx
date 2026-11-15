import React, { useEffect, useRef, useState } from 'react';
import { registerEventHandler, unregisterEventHandler } from '../../../utils/events';

import {
  IconPlayerPlayFilled as PlayButton,
  IconPlayerPauseFilled as PauseButton,
  IconRewindBackward10 as RewindButton,
  IconRewindForward10 as ForwardButton,
  IconPlayerSkipForwardFilled as NextTrack,
  IconVolume3 as MuteVolume,
  IconBadgeCc as SubtitlesIcon,
  IconMusic as AudioIcon,
} from '@tabler/icons-react';
import { usePlayerStore } from '../../../state/player.store';
import { NO_SUBTITLE_ID, trackName } from '../TrackControl/TrackControl';

import { isTouchDevice } from '../../../utils/platform';
import './PlaybackNotificationControl.css';

interface Notification {
  key: number;
  icon?: React.ReactNode;
  message?: string;
}

/** Short centred feedback (play/pause, skip, volume, track changes) for commands posted on the event bus. */
const PlaybackNotificationControl = () => {
  const [notification, setNotification] = useState<Notification | null>(null);
  const hideTimeout = useRef<ReturnType<typeof setTimeout>>(null);

  useEffect(() => {
    const show = (content: Omit<Notification, 'key'>) => {
      setNotification((prev) => ({ key: (prev?.key ?? 0) + 1, ...content }));
      if (hideTimeout.current) clearTimeout(hideTimeout.current);
      hideTimeout.current = setTimeout(() => setNotification(null), 1500);
    };

    const handlers: Record<string, (data: any) => void> = {
      volume: (volume: number) =>
        show(volume <= 0 ? { icon: <MuteVolume /> } : { message: `${Math.round(volume * 100)}%` }),
      skip_seconds: (seconds: number) => show({ icon: seconds < 0 ? <RewindButton /> : <ForwardButton /> }),
      // Touch screens have the centre play button in the same place.
      play_status_changed: (isPlaying: boolean) =>
        !isTouchDevice && show({ icon: isPlaying ? <PlayButton /> : <PauseButton /> }),
      play_next_title: () => show({ icon: <NextTrack /> }),
      audio_track_selected: (id: string) =>
        show({ icon: <AudioIcon />, message: trackName(usePlayerStore.getState().metadata?.audioTracks, id) }),
      subtitle_track_selected: (id: string) =>
        show({
          icon: <SubtitlesIcon />,
          message: id === NO_SUBTITLE_ID ? 'Off' : trackName(usePlayerStore.getState().metadata?.subtitleTracks, id),
        }),
    };

    for (const [name, handler] of Object.entries(handlers)) registerEventHandler(name, handler);
    return () => {
      for (const [name, handler] of Object.entries(handlers)) unregisterEventHandler(name, handler);
      if (hideTimeout.current) clearTimeout(hideTimeout.current);
    };
  }, []);

  if (!notification) return null;

  return (
    <div key={notification.key} className="notification-container">
      {notification.icon}
      {notification.message && <span>{notification.message}</span>}
    </div>
  );
};

export default PlaybackNotificationControl;
