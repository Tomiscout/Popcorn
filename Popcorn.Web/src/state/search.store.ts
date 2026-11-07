import { create } from 'zustand';
import type { MediaCard } from '@popcorn/plugin-sdk';
import type { FileSearch, FileSearchUpdate, ProviderResults } from '../plugins/file-sources';

export type SearchTab = 'media' | 'files';

type SearchStore = {
  searchQuery: string;
  tab: SearchTab;
  /** Query the current results belong to; media responses for older queries are dropped. */
  submittedQuery: string;
  media: Record<string, ProviderResults<MediaCard>>;
  /** File search of `submittedQuery`; null until the Files tab is opened for it (file searches are not free). */
  files: FileSearch | null;
  setSearchQuery: (query: string) => void;
  setTab: (tab: SearchTab) => void;
  startSearch: (query: string, media: Record<string, ProviderResults<MediaCard>>) => void;
  setMediaResults: (pluginId: string, query: string, results: MediaCard[], error: string | null) => void;
  setFiles: FileSearchUpdate;
};

export const useSearchStore = create<SearchStore>((set) => ({
  searchQuery: '',
  tab: 'media',
  submittedQuery: '',
  media: {},
  files: null,
  setSearchQuery: (query) => set({ searchQuery: query }),
  setTab: (tab) => set({ tab }),
  startSearch: (query, media) => set({ submittedQuery: query, media, files: null }),
  setMediaResults: (pluginId, query, results, error) =>
    set((state) =>
      query !== state.submittedQuery
        ? state
        : { media: { ...state.media, [pluginId]: { loading: false, results, error } } }
    ),
  setFiles: (update) => set((state) => ({ files: update(state.files) })),
}));
