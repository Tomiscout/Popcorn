import React, { useState, useEffect, useRef } from 'react';
import { postMessage, registerEventHandler } from '../events';
import { ReactComponent as Subtitles } from '../assets/icons/subtitles.svg';
import '../Styles/SubtitleControl.css';

const SubtitleControl = () => {
  const subtitlePopupWidth = 500;

  const [audioTracks, setAudioTracks] = useState([]);
  const [subtitleTracks, setSubtitleTracks] = useState([]);
  const [selectedAudio, setSelectedAudio] = useState(-1);
  const [selectedSubtitle, setSelectedSubtitle] = useState(-1);
  const [showPopup, setShowPopup] = useState(false);
  const subtitleRef = useRef(null);
  const subtitlePopupRef = useRef(null);

  useEffect(() => {
    registerEventHandler('track_loaded', (data) => {
      setAudioTracks(data.audioTracks || []);
      setSubtitleTracks(data.subtitleTracks || []);
    });
  }, []);

  useEffect(() => {
    if (audioTracks.length) {
      setSelectedAudio(audioTracks[0]);
    }
    if (subtitleTracks.length) {
      setSelectedSubtitle(subtitleTracks[0]);
    }
  }, [audioTracks, subtitleTracks]);

  const handleAudioChange = (value) => {
    setSelectedAudio(value);
    postMessage('audio_track_selected', value);
  };

  const handleSubtitleChange = (value) => {
    setSelectedSubtitle(value);
    postMessage('subtitle_track_selected', value);
  };

  const handleMouseEnter = () => {
    const subtitleElement = subtitleRef.current;
    const subtitlePopup = subtitlePopupRef.current;
    const rect = subtitleElement.getBoundingClientRect();
    const windowWidth = window.innerWidth;

    let left = rect.left + rect.width / 2 - subtitlePopupWidth / 2;
    const bottom = rect.height;

    if (left + subtitlePopupWidth > windowWidth) {
      left = windowWidth - subtitlePopupWidth;
    }

    subtitlePopup.style.left = `${left}px`;
    subtitlePopup.style.bottom = `${bottom}px`;
    setShowPopup(true);
  };

  const handleMouseLeave = () => {
    setShowPopup(false);
  }

  const renderCustomSelector = (tracks, selectedValue, handleChange) => (
    <ul className="custom-select">
      {tracks.map((track, index) => (
        <li
          key={index}
          className={`custom-option ${selectedValue === track.id ? 'selected' : ''}`}
          onClick={() => handleChange(track.id)}
        >
          <div className="checkmark-container">
            {selectedValue === track && <span className="checkmark">✓</span>}
          </div>
          {<span className={`option-text ${selectedValue === track.id ? 'bold' : ''}`}>
            {track.id === -1 ? 'None' : track.language + ' '}
            <span className={`option-desc-text`}>{track.id === -1 ? '' : track.description}</span>
          </span>}
        </li>
      ))}
    </ul>
  );

  return (
    <div ref={subtitleRef} onMouseEnter={handleMouseEnter} onMouseLeave={handleMouseLeave}>
      <div id="subtitle-popup" className="popup" ref={subtitlePopupRef} style={{ width: `${subtitlePopupWidth}px`, display: showPopup ? '' : 'none' }}>
        <div className='subtitle-popup-container'>
          <div className='subtitle-popup-half'>
            <p className='titles'>Audio</p>
            {renderCustomSelector(audioTracks, selectedAudio, handleAudioChange)}
          </div>
          <div className='subtitle-popup-half'>
            <p className='titles'>Subtitles</p>
            {renderCustomSelector(subtitleTracks, selectedSubtitle, handleSubtitleChange)}
          </div>
        </div>
      </div>
      <button id="subtitle-button">
        <Subtitles />
      </button>
    </div>
  );
};

export default SubtitleControl;
