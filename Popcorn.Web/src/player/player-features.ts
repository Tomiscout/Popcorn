import { createContext, useContext } from 'react';

/**
 * What the player UI offers, per host. The same component tree (`PlayerOverlay`) runs in the browser
 * (`PopcornPlayer`, HTML5 video) and as the overlay of the native players (`NativePlayer`, mpv below a
 * transparent page); features the host can't back are switched off here instead of forking components.
 */
export interface PlayerFeatures {
  /** Picture-in-picture style floating/docked player inside the web UI. */
  floating: boolean;
  /** Dropping subtitle/audio files onto the video (needs the file contents in the page). */
  fileDrop: boolean;
  /** "Add" subtitle file: `browser` = file input in the page, `host` = host opens its own picker, `false` = hidden. */
  addSubtitle: 'browser' | 'host' | false;
  /** Subtitle delay stepper. */
  subtitleDelay: boolean;
}

export const webPlayerFeatures: PlayerFeatures = {
  floating: true,
  fileDrop: true,
  addSubtitle: 'browser',
  subtitleDelay: true,
};

const PlayerFeaturesContext = createContext<PlayerFeatures>(webPlayerFeatures);

export const PlayerFeaturesProvider = PlayerFeaturesContext.Provider;

export const usePlayerFeatures = () => useContext(PlayerFeaturesContext);
