import { create } from 'zustand';
import { persist } from 'zustand/middleware';

type ExcludedState = {
  excluded: Record<string, boolean>;
  setExcluded: (id: string, excluded: boolean) => void;
};

/** Ids the user switched off, remembered across pages and restarts under `name` in localStorage. */
const excludedStore = (name: string) =>
  create<ExcludedState>()(
    persist(
      (set) => ({
        excluded: {},
        setExcluded: (id, excluded) =>
          set((s) => {
            const next = { ...s.excluded };
            if (excluded) next[id] = true;
            else delete next[id];
            return { excluded: next };
          }),
      }),
      { name }
    )
  );

/** File sources the user switched off for file searches (`FileSource.id`). */
export const useExcludedFileSources = excludedStore('popcorn-excluded-file-sources');

/** Image providers (plugin ids) the user switched off in the poster/backdrop picker. */
export const useExcludedImageProviders = excludedStore('popcorn-excluded-image-providers');
