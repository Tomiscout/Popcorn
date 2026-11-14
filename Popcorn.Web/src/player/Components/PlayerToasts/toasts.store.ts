import { create } from 'zustand';

export type ToastLevel = 'error' | 'warning' | 'info';

export interface Toast {
  id: string;
  level: ToastLevel;
  message: string;
  title?: string;
  /** Stays until `dismissToast(id)` (or its X); otherwise closes after `TOAST_DURATION_MS`. */
  persistent?: boolean;
}

export const TOAST_DURATION_MS = 4000;

interface ToastState {
  toasts: Toast[];
}

/** Player toasts (bottom-right stack, `PlayerToasts`). Showing an existing id replaces that toast in place. */
export const useToastStore = create<ToastState>(() => ({ toasts: [] }));

let nextId = 0;
const timers = new Map<string, ReturnType<typeof setTimeout>>();

export function dismissToast(id: string) {
  clearTimeout(timers.get(id));
  timers.delete(id);
  useToastStore.setState((state) => ({ toasts: state.toasts.filter((t) => t.id !== id) }));
}

export function showToast(toast: Omit<Toast, 'id'> & { id?: string }): string {
  const id = toast.id ?? `toast-${nextId++}`;
  const entry: Toast = { ...toast, id };
  useToastStore.setState((state) => {
    const index = state.toasts.findIndex((t) => t.id === id);
    if (index < 0) return { toasts: [...state.toasts, entry] };
    const toasts = [...state.toasts];
    toasts[index] = entry;
    return { toasts };
  });

  clearTimeout(timers.get(id));
  timers.delete(id);
  if (!toast.persistent)
    timers.set(
      id,
      setTimeout(() => dismissToast(id), TOAST_DURATION_MS)
    );
  return id;
}
