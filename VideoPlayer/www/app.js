const videoContainer = document.querySelector('.video-container');
// const video = document.querySelector('.video-container video');

class Video {
  paused = false;
  muted = false;
  fullscreen = false;
  duration = 0;
  volume = 0;

  play() {
    this.paused = false;
    console.log('play')
    this.postMessage({name: 'play_status_changed', data: true});
  }

  pause() {
    this.paused = true;
    console.log('pausing');
    this.postMessage({name: 'play_status_changed', data: false});
  }

  requestFullscreen() {
    this.fullscreen = true;
    this.postMessage({name: 'fullscreen', data: true});
  }

  exitFullscreen() {
    this.fullscreen = false;
    this.postMessage({name: 'fullscreen', data: false});
  }

  setCurrentTime(time) {
    console.log('Setting time',time)
    this.postMessage({name: 'set_playback_time', data: time});
  }
  
  //Note: could do this with set_playback_time, but it's easier and more accurate to do it in the backend
  skipBackward() {
    this.postMessage({name: 'skip_seconds', data: -10});
  }

  skipForward() {
    this.postMessage({name: 'skip_seconds', data: 10});
  }

  setVolume(volume) {
    this.postMessage({name: 'volume', data: volume});
  }

  toggleMute() {
    this.muted = !this.muted;
    if(this.muted) {
      this.postMessage({name: 'volume', data: 0});
    } else {
      this.postMessage({name: 'volume', data: this.volume});
    }
  }

  playNextTitle() {
    console.log('playing next title')
    this.postMessage({name: 'play_next_title'});
  }

  selectEpisode(id) {
    console.log('selecting episode', id);
  }

  selectSubtitle(id) {
    console.log('selecting subtitle', id);
  }

  postMessage(message) {
    window.chrome.webview.postMessage(message);
  }
}
const video = new Video();

const controlsContainer = document.querySelector('.video-container .controls-container');

const playPauseButton = document.querySelector('.video-container .controls button.play-pause');
const rewindButton = document.querySelector('.video-container .controls button.rewind');
const fastForwardButton = document.querySelector('.video-container .controls button.fast-forward');
const volumeButton = document.querySelector('.video-container .controls button.volume');
const fullScreenButton = document.querySelector('.video-container .controls button.full-screen');
const nextButton = document.querySelector('.next');
const episodesButton = document.querySelector('.episodes');
const subtitlesButton = document.querySelector('.subtitles');

const playButton = playPauseButton.querySelector('.playing');
const pauseButton = playPauseButton.querySelector('.paused');
const fullVolumeButton = volumeButton.querySelector('.full-volume');
const mutedButton = volumeButton.querySelector('.muted');
const maximizeButton = fullScreenButton.querySelector('.maximize');
const minimizeButton = fullScreenButton.querySelector('.minimize');


const progressBar = document.querySelector('.video-container .progress-controls .progress-bar');
const watchedBar = document.querySelector('.video-container .progress-controls .progress-bar .watched-bar');
const timeLeft = document.querySelector('.video-container .progress-controls .time-remaining');

let controlsTimeout;
controlsContainer.style.opacity = '0';
watchedBar.style.width = '0px';
pauseButton.style.display = 'none';
minimizeButton.style.display = 'none';

startup();


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
  video.toggleMute();
  recalculateVolumeButton();
};

const toggleFullScreen = () => {
  if (!video.fullscreen) {
    video.requestFullscreen();
  } else {
    video.exitFullscreen();
  }
  recalculateFullscreenButton();
};

function startup() {
  recalculateFullscreenButton();
  recalculateVolumeButton();
  recalculatePlayButton();
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
    fullVolumeButton.style.display = 'none';
    mutedButton.style.display = '';
  } else {
    fullVolumeButton.style.display = '';
    mutedButton.style.display = 'none';
  }
}

function recalculatePlayButton() {
  if (video.paused) {
    playButton.style.display = 'none';
    pauseButton.style.display = '';
  } else {
    playButton.style.display = '';
    pauseButton.style.display = 'none';
  }
}

document.addEventListener('keyup', (event) => {
  if (event.code === 'Space') {
    playPause(); 
  }

  if (event.code === 'KeyM') {
    toggleMute();
  }

  if (event.code === 'KeyF') {
    toggleFullScreen();
  }

  displayControls();
});

document.addEventListener('mousemove', () => {
  displayControls();
});

// video.addEventListener('timeupdate', () => {
//   watchedBar.style.width = ((video.currentTime / video.duration) * 100) + '%';
//   // TODO: calculate hours as well...
//   const totalSecondsRemaining = video.duration - video.currentTime;
//   // THANK YOU: BEGANOVICH
//   const time = new Date(null);
//   time.setSeconds(totalSecondsRemaining);
//   let hours = null;

//   if(totalSecondsRemaining >= 3600) {
//     hours = (time.getHours().toString()).padStart('2', '0');
//   }

//   let minutes = (time.getMinutes().toString()).padStart('2', '0');
//   let seconds = (time.getSeconds().toString()).padStart('2', '0');

//   timeLeft.textContent = `${hours ? hours : '00'}:${minutes}:${seconds}`;
// });

progressBar.addEventListener('click', (event) => {
  const pos = (event.pageX  - (progressBar.offsetLeft + progressBar.offsetParent.offsetLeft)) / progressBar.offsetWidth;
  video.setCurrentTime(pos * video.duration);
});

playPauseButton.addEventListener('click', playPause);

rewindButton.addEventListener('click', () => {
  video.skipBackward();
});

fastForwardButton.addEventListener('click', () => {
  video.skipForward();
});

nextButton.addEventListener('click', video.playNextTitle);

episodesButton.addEventListener('click', () => {
  //TODO display episodes from list
  video.selectEpisode('episode-id');
});

subtitlesButton.addEventListener('click', () => {
  //TODO display subtitles from list
  video.selectSubtitle('subtitle-id');
});

volumeButton.addEventListener('click', toggleMute);

fullScreenButton.addEventListener('click', toggleFullScreen);

//Events from WPF
window.chrome.webview.addEventListener('message', event => {
  debugger;
  if (event.data === 'message') {
    // process message
  }

  //TODO loaded media event, setting name, video duration
});