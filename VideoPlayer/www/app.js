const videoContainer = document.querySelector('.video-container');
// const video = document.querySelector('.video-container video');

class Video {
  paused = false;
  muted = false;
  fullscreen = false;

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
    console.log('requesting fullscreen')
  }

  exitFullscreen() {
    this.fullscreen = false;
    console.log('exiting fullscreen')
  }

  setCurrentTime(time) {
    console.log('Setting time',time)
  }

  setMuted() {
    this.muted = false; //TODO rig
    console.log('muted');
  }

  skipBackward() {
    console.log('skip backward');
  }

  skipForward() {
    console.log('skip forward');
  }

  playNextTitle() {
    console.log('playing next title')
  }

  selectEpisode(id) {
    console.log('selecting episode', id);
  }

  selectSubtitle(id) {
    console.log('selecting subtitle', id);
  }

  postMessage(message) {
    debugger;
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
    playButton.style.display = 'none';
    pauseButton.style.display = '';
  } else {
    video.pause();
    playButton.style.display = '';
    pauseButton.style.display = 'none';
  }
};

const toggleMute = () => {
  video.muted = !video.muted;
  if (video.muted) {
    fullVolumeButton.style.display = 'none';
    mutedButton.style.display = '';
  } else {
    fullVolumeButton.style.display = '';
    mutedButton.style.display = 'none';
  }
};

const toggleFullScreen = () => {
  if (!video.fullscreen) {
    video.requestFullscreen();
  } else {
    video.exitFullscreen();
  }
  recalculateFullscreenButton();
};

function recalculateFullscreenButton(){
  if (!video.fullscreen) {
    maximizeButton.style.display = '';
    minimizeButton.style.display = 'none';
  } else {
    maximizeButton.style.display = 'none';
    minimizeButton.style.display = '';
  }
};

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
});