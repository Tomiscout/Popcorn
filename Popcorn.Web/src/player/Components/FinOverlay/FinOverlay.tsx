import { usePlayerStore } from '../../../state/player.store';
import finImage from '../../fin.png';
import './FinOverlay.css';

/** Nothing is left to play after the file that just ended. Clicks go through: the controls stay usable. */
export const FinOverlay = () => {
  const showFin = usePlayerStore((s) => s.showFin);
  return showFin ? (
    <div className="fin-overlay">
      <img src={finImage} alt="Fin" draggable={false} />
    </div>
  ) : null;
};
