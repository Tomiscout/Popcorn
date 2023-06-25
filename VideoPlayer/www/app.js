const videoContainer = document.querySelector('.video-container');

const controlsContainer = document.querySelector('.video-container .controls-container');

const playPauseButton = document.querySelector('.video-container .controls button.play-pause');
const rewindButton = document.querySelector('.video-container .controls button.rewind');
const fastForwardButton = document.querySelector('.video-container .controls button.fast-forward');
const volumeButton = document.querySelector('#volume-button');
const fullScreenButton = document.querySelector('.video-container .controls button.full-screen');
const nextButton = document.querySelector('.next');
const episodesButton = document.querySelector('.episodes');
const subtitlesButton = document.querySelector('.subtitles');
const volume = $("#volume");
const volumePopup = $("#volume-popup");

const playButton = playPauseButton.querySelector('.playing');
const pauseButton = playPauseButton.querySelector('.paused');
const lowVolumeButton = volumeButton.querySelector('.low-volume');
const midVolumeButton = volumeButton.querySelector('.mid-volume');
const fullVolumeButton = volumeButton.querySelector('.full-volume');
const mutedButton = volumeButton.querySelector('.muted');
const maximizeButton = fullScreenButton.querySelector('.maximize');
const minimizeButton = fullScreenButton.querySelector('.minimize');

const plainTitle = $('#plainTitle');
const seriesTitle = $('#seriesTitle');
const progressBar = document.querySelector('.video-container .progress-controls .progress-bar');
const watchedBar = document.querySelector('.video-container .progress-controls .progress-bar .watched-bar');
const timeLeft = document.querySelector('#time-remaining');

let controlsTimeout;
controlsContainer.style.opacity = '0';
watchedBar.style.width = '0px';
pauseButton.style.display = 'none';
minimizeButton.style.display = 'none';


class Video {
  paused = false;
  muted = false;
  fullscreen = false;
  duration = 0;
  volume = 0;

  play() {
    this.paused = false;
    console.log('play')
    postMessage({name: 'play_status_changed', data: true});
  }

  pause() {
    this.paused = true;
    console.log('pausing');
    postMessage({name: 'play_status_changed', data: false});
  }

  requestFullscreen() {
    this.fullscreen = true;
    postMessage({name: 'fullscreen', data: true});
    setTimeout(videoContainer.focus(), 100); //Dont know if necessary
  }

  exitFullscreen() {
    this.fullscreen = false;
    postMessage({name: 'fullscreen', data: false});
  }

  setCurrentTime(time) {
    console.log('Setting time',time)
    postMessage({name: 'set_playback_time', data: time});
  }
  
  //Note: could do this with set_playback_time, but it's easier and more accurate to do it in the backend
  skipBackward() {
    postMessage({name: 'skip_seconds', data: -10});
  }

  skipForward() {
    postMessage({name: 'skip_seconds', data: 10});
  }

  setVolume(volume) {
    this.volume = volume;
    recalculateVolumeButton();
    postMessage({name: 'volume', data: volume});
  }

  toggleMute() {
    this.muted = !this.muted;
    if(this.muted) {
      postMessage({name: 'volume', data: 0});
    } else {
      postMessage({name: 'volume', data: this.volume});
    }
  }

  playNextTitle() {
    console.log('playing next title')
    postMessage({name: 'play_next_title'});
  }

  selectEpisode(id) {
    console.log('selecting episode', id);
  }

  selectSubtitle(id) {
    console.log('selecting subtitle', id);
  }
}
const video = new Video();


function startup() {
  postMessage({name: 'initialized'});
  recalculateFullscreenButton();
  recalculateVolumeButton();
  recalculatePlayButton();
  volumePopup[0].style.display = 'none';
}
startup();


function trackLoadedEventHandler(data) {
  if(!data.title) {
    plainTitle.css({display: ''})
    seriesTitle.css({display: 'none'})
    $('#plainTitleSpan').html(data.filename);
  } else {
    plainTitle.css({display: 'none'})
    seriesTitle.css({display: ''})
  }

  video.paused = false;
  recalculatePlayButton();
}

function playbackEndedEventHandler() {
  video.reset();
}
// const video = document.querySelector('.video-container video');

function durationUpdateEventHandler(data) {
  video.duration = data;
}

//Events from WPF
window?.chrome?.webview?.addEventListener('message', event => {
  switch (event.data.name) {
    case 'current_time': 
      currentTimeUpdateEventHandler(event.data.data);
    break;
    case 'duration_update':
      durationUpdateEventHandler(event.data.data);
    break;
    case 'track_loaded':
      trackLoadedEventHandler(event.data.data);
    break;
  }
});

function postMessage(message) {
  window?.chrome?.webview?.postMessage(message);
  //TODO error message, that it's intendend to be used in webview
}




function currentTimeUpdateEventHandler(currentTime) {
  watchedBar.style.width = ((currentTime / video.duration) * 100) + '%';
    const totalSecondsRemaining = video.duration - currentTime;
    timeLeft.textContent = timeFormat(totalSecondsRemaining);
}

const displayControls = () => {
  controlsContainer.style.opacity = '1';
  document.body.style.cursor = 'initial';
  if (controlsTimeout) {
    clearTimeout(controlsTimeout);
  }
  controlsTimeout = setTimeout(() => {
    controlsContainer.style.opacity = '0';
    document.body.style.cursor = 'none';
  }, 5000);
};

const playPause = () => {
  if (video.paused) {
    video.play();
  } else {
    video.pause();
  }
  recalculatePlayButton();
};

const toggleMute = () => {
  volumeButton.blur();

  video.toggleMute();
  recalculateVolumeButton();
};

const toggleFullScreen = () => {
  fullScreenButton.blur();

  if (!video.fullscreen) {
    video.requestFullscreen();
  } else {
    video.exitFullscreen();
  }
  recalculateFullscreenButton();
};

const exitFullscreen = () => {
  if (!video.fullscreen) return;

  video.exitFullscreen();
  recalculateFullscreenButton();
}

//TODo update slider value. Also its kinda messy
const upHandler = () => {
  video.volume += 15;
  if(video.volume > 100) {
    video.volume = 100;
  }
  video.setVolume(video.volume);
}
const downHandler = () => {
  video.volume -= 15;
  if(video.volume < 0) {
    video.volume = 0;
  }
  video.setVolume(video.volume);
}

function recalculateFullscreenButton(){
  if (!video.fullscreen) {
    maximizeButton.style.display = '';
    minimizeButton.style.display = 'none';
  } else {
    maximizeButton.style.display = 'none';
    minimizeButton.style.display = '';
  }
};

function recalculateVolumeButton() {
  if (video.muted || video.volume === 0) {
    lowVolumeButton.style.display = 'none';
    midVolumeButton.style.display = 'none';
    fullVolumeButton.style.display = 'none';
    mutedButton.style.display = '';
  } else if(video.volume <= 0.33) {
    lowVolumeButton.style.display = '';
    midVolumeButton.style.display = 'none';
    fullVolumeButton.style.display = 'none';
    mutedButton.style.display = 'none';
  }
  else if(video.volume <= 0.66) {
    lowVolumeButton.style.display = 'none';
    midVolumeButton.style.display = '';
    fullVolumeButton.style.display = 'none';
    mutedButton.style.display = 'none';
  } else {
    lowVolumeButton.style.display = 'none';
    midVolumeButton.style.display = 'none';
    fullVolumeButton.style.display = '';
    mutedButton.style.display = 'none';
  }
}

function recalculatePlayButton() {
  if (video.paused) {
    playButton.style.display = '';
    pauseButton.style.display = 'none';
  } else {
    playButton.style.display = 'none';
    pauseButton.style.display = '';
  }
}


const keyCallbacks = {
  'Space'  : playPause,
  'KeyM'  : toggleMute,
  'KeyF'  : toggleFullScreen,
  'Escape'  : exitFullscreen,
  "ArrowLeft"  : video.skipBackward,
  "ArrowRight" : video.skipForward,
  "ArrowUp"    : upHandler,
  "ArrowDown"  : downHandler,
}
document.addEventListener('keyup', (event) => {
  keyCallbacks[event.code]?.();

  displayControls();
});

document.addEventListener('mousemove', () => {
  displayControls();
});

progressBar.addEventListener('click', (event) => {
  const pos = (event.pageX  - (progressBar.offsetLeft + progressBar.offsetParent.offsetLeft)) / progressBar.offsetWidth;
  video.setCurrentTime(pos * video.duration);
});

playPauseButton.addEventListener('click', playPause);

rewindButton.addEventListener('click', () => {
  rewindButton.blur();
  video.skipBackward();
});

fastForwardButton.addEventListener('click', () => {
  fastForwardButton.blur();
  video.skipForward();
});

nextButton.addEventListener('click', video.playNextTitle);

episodesButton.addEventListener('click', () => {
  episodesButton.blur();

  //TODO display episodes from list
  video.selectEpisode('episode-id');
});

subtitlesButton.addEventListener('click', () => {
  subtitlesButton.blur();

  //TODO display subtitles from list
  video.selectSubtitle('subtitle-id');
});

volumeButton.addEventListener('click', toggleMute);
fullScreenButton.addEventListener('click', toggleFullScreen);

volume.on("mouseenter", () => {
  let pos = volume.offset();
  volumePopup.css({
    left: pos.left + volume.width()/2 - volumePopup.width()/2,
    bottom: volume.height()
  });
  volumePopup[0].style.display = '';
  console.log('entered');
});
volume.on("mouseleave", () => {
  volumePopup[0].style.display = 'none';
  console.log('leave');
});

$("#volume-control").slider({
  min: 0,
  max: 100,
  animate: "fast",
  value: 85,
  range: "min",
  orientation : 'vertical',
  slide: function(event, ui) {
    video.setVolume(ui.value / 100);
  }
});


function timeFormat(duration) {
  duration = duration / 1000;
  // Hours, minutes and seconds
  const hrs = ~~(duration / 3600);
  const mins = ~~((duration % 3600) / 60);
  const secs = ~~duration % 60;

  // Output like "1:01" or "4:03:59" or "123:03:59"
  let ret = "";

  if (hrs > 0) {
      ret += "" + hrs + ":" + (mins < 10 ? "0" : "");
  }

  ret += "" + mins + ":" + (secs < 10 ? "0" : "");
  ret += "" + secs;

  return ret;
}