import { usePlayerStore } from '../state/player.store';
import { postMessage } from '../utils/events';

/** Sets the volume (clamped to 0–1, unmutes) and tells the host. Used by the volume control, its keys and the swipe gesture. */
export function setPlayerVolume(volume: number) {
  const clamped = parseFloat(Math.max(0, Math.min(volume, 1)).toFixed(2));
  usePlayerStore.setState({ volume: clamped, muted: false });
  postMessage('volume', clamped);
}

export function togglePlayerMute() {
  const { muted, volume } = usePlayerStore.getState();
  usePlayerStore.setState({ muted: !muted });
  postMessage('volume', muted ? volume : 0);
}
