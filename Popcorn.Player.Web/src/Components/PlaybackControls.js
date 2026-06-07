import React, { useState, useEffect } from "react";
import { ReactComponent as PlayButton } from "../assets/icons/play.svg";
import { ReactComponent as StopButton } from "../assets/icons/stop.svg";
import { ReactComponent as RewindButton } from "../assets/icons/10-back.svg";
import { ReactComponent as ForwardButton } from "../assets/icons/10-forward.svg";
import { ReactComponent as NextTrack } from "../assets/icons/next.svg";
import { ReactComponent as Fullscreen } from "../assets/icons/fullscreen.svg";
import { ReactComponent as ExitFullscreen } from "../assets/icons/minimize.svg";
// import VolumeControl from './VolumeControl';
import EpisodesControl from "./EpisodesControl";
import SubtitleControl from "./SubtitleControl";
import TrackTitle from "./TrackTitle";
import {
  postMessage,
  registerWebviewEventHandler,
  registerEventHandler,
  unregisterWebviewEventHandler,
  unregisterEventHandler,
} from "../events";
import { useEventListeners } from "../Utlis/helpers";

const PlaybackControls = () => {
  const [playing, setPlaying] = useState(false); //TODO this should come from backend, it may become out of sync in some scenario
  const [fullscreen, setFullscreen] = useState(false);

  const getPlayButton = () => (playing ? <StopButton /> : <PlayButton />);
  const getFullscreenButton = () =>
    fullscreen ? <ExitFullscreen /> : <Fullscreen />;

  const playPause = () => {
    setPlaying((prevPlaying) => {
      const newPlayingStatus = !prevPlaying;
      postMessage("play_status_changed", newPlayingStatus);
      return newPlayingStatus;
    });
  };
  const skipBackward = () => {
    postMessage("skip_seconds", -10);
  };
  const skipForward = () => {
    postMessage("skip_seconds", 10);
  };
  const nextTrack = () => {
    postMessage("play_next_title", 10);
  };
  const toggleFullscreen = () => {
    setFullscreen((prevFullscreen) => {
      postMessage("request_fullscreen", !prevFullscreen);
      return prevFullscreen;
    });
  };
  const exitFullscreen = () => {
    if (fullscreen) {
      postMessage("request_fullscreen", false);
    }
  };

  useEventListeners("keyup", {
    Space: playPause,
    Escape: exitFullscreen,
    ArrowLeft: skipBackward,
    ArrowRight: skipForward,
    KeyF: toggleFullscreen,
  });

  const trackLoadedHandler = (data) => {
    setPlaying(true);
  };

  const fullscreenHandler = (data) => {
    setFullscreen(data);
  };

  useEffect(() => {
    registerWebviewEventHandler("track_loaded", trackLoadedHandler);
    registerWebviewEventHandler("fullscreen", fullscreenHandler);
    registerEventHandler("playback_toggle", playPause);
    registerEventHandler("request_fullscreen_click", toggleFullscreen);

    return () => {
      unregisterWebviewEventHandler("track_loaded", trackLoadedHandler);
      unregisterWebviewEventHandler("fullscreen", fullscreenHandler);
      unregisterEventHandler("playback_toggle", playPause);
      unregisterEventHandler("request_fullscreen_click", toggleFullscreen);
    };
  }, []);

  return (
    <div className="controls">
      <button className="main-button play-pause" onClick={playPause}>
        {getPlayButton()}
      </button>
      <button className="main-button rewind" onClick={skipBackward}>
        <RewindButton />
      </button>
      <button className="main-button fast-forward" onClick={skipForward}>
        <ForwardButton />
      </button>
      {/* <VolumeControl /> */}
      <TrackTitle />

      <button className="main-button next" onClick={nextTrack}>
        <NextTrack />
      </button>
      <EpisodesControl />
      <SubtitleControl />
      <button className="main-button fullscreen" onClick={toggleFullscreen}>
        {getFullscreenButton()}
      </button>
    </div>
  );
};

export default PlaybackControls;
