import { create } from 'zustand';
import { PluginInfo } from '../model/plugin.contract';

type PluginStore = {
  /** All plugin folders incl. disabled/broken ones; null until the server answered PluginList. */
  plugins: PluginInfo[] | null;
  setPlugins: (plugins: PluginInfo[]) => void;
};

export const usePluginStore = create<PluginStore>((set) => ({
  plugins: null,
  setPlugins: (plugins) => set({ plugins }),
}));
