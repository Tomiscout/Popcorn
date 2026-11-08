import { IconPlayerPauseFilled, IconPlayerPlayFilled } from '@tabler/icons-react';
import { usePlayerStore } from '../../../state/player.store';
import { postMessage } from '../../../utils/events';
import './CenterPlayButton.css';

/** Touch screens: a tap on the video only shows the controls, playback is toggled here (or in the control bar). */
export const CenterPlayButton = ({ visible }: { visible: boolean }) => {
  const isPlaying = usePlayerStore((s) => s.isPlaying);
  return (
    <button
      className={`center-play-button${visible ? '' : ' center-play-button--hidden'}`}
      aria-label={isPlaying ? 'Pause' : 'Play'}
      onClick={() => postMessage('play_status_changed', !usePlayerStore.getState().isPlaying)}
    >
      {isPlaying ? <IconPlayerPauseFilled /> : <IconPlayerPlayFilled />}
    </button>
  );
};
