import { IconX } from '@tabler/icons-react';
import { dismissToast, useToastStore } from './toasts.store';
import './PlayerToasts.css';

/** Toast stack of the player overlay; show/dismiss through `toasts.store.ts`. */
const PlayerToasts = () => {
  const toasts = useToastStore((state) => state.toasts);
  if (toasts.length === 0) return null;

  return (
    <div className="player-toasts">
      {toasts.map((toast) => (
        <div key={toast.id} className={`player-toast player-toast-${toast.level}`} role="status">
          <div className="player-toast-text">
            {toast.title && <span className="player-toast-title">{toast.title}</span>}
            <span>{toast.message}</span>
          </div>
          <button className="player-toast-close" aria-label="Close" onClick={() => dismissToast(toast.id)}>
            <IconX size={16} />
          </button>
        </div>
      ))}
    </div>
  );
};

export default PlayerToasts;
