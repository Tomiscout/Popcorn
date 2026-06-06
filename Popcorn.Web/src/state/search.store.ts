import { create } from 'zustand';
import { MovieDbSearchResult } from '../model/search.contract';

type SearchStore = {
  searchQuery: string;
  searchResults: MovieDbSearchResult[];
  setSearchResults: (results: MovieDbSearchResult[]) => void;
  setSearchQuery: (query: string) => void;
};

export const useSearchStore = create<SearchStore>((set) => ({
  searchQuery: '',
  searchResults: [],
  setSearchResults: (results) => set({ searchResults: results }),
  setSearchQuery: (query) => set({ searchQuery: query }),
}));
