import React, { useState, useEffect, useRef } from 'react';
import { postMessage, registerWebviewEventHandler } from '../events';
import { ReactComponent as Subtitles } from '../assets/icons/subtitles.svg';
import { ReactComponent as AddIcon } from '../assets/icons/plus.svg';
import NumberStepper from './NumberStepper';
import '../Styles/SubtitleControl.css';

const SubtitleControl = () => {
  const subtitlePopupWidth = 500;

  const [audioTracks, setAudioTracks] = useState([{ description: 'test', language: 'LT', id: 1 }, { description: 'test', language: 'LT', id: 1 }, { description: 'test', language: 'LT', id: 1 }, { description: 'test', language: 'LT', id: 1 }, { description: 'test', language: 'LT', id: 1 }, { description: 'test', language: 'LT', id: 1 }, { description: 'test', language: 'LT', id: 1 }, { description: 'test', language: 'LT', id: 1 }]);
  const [subtitleTracks, setSubtitleTracks] = useState([{ description: 'test', language: 'LT', id: 1 }, { description: 'test', language: 'LT', id: 1 }, { description: 'test', language: 'LT', id: 1 }, { description: 'test', language: 'LT', id: 1 }, { description: 'test', language: 'LT', id: 1 }, { description: 'test', language: 'LT', id: 1 }, { description: 'test', language: 'LT', id: 1 }, { description: 'test', language: 'LT', id: 1 }]);
  const [selectedAudio, setSelectedAudio] = useState(-1);
  const [selectedSubtitle, setSelectedSubtitle] = useState(-1);
  const [showPopup, setShowPopup] = useState(false);
  const [resetSubtitleDelay, setResetSubtitleDelay] = useState(false);
  const subtitleRef = useRef(null);
  const subtitlePopupRef = useRef(null);

  useEffect(() => {
    registerWebviewEventHandler('audio_subtitles_loaded', (data) => {
      setAudioTracks(data.audioTracks || []);
      setSubtitleTracks(data.subtitleTracks || []);
    });
  }, []);

  useEffect(() => {
    if (audioTracks.length) {
      setSelectedAudio(audioTracks.find(t => t.selected)?.id || audioTracks[0].id);
    }
    if (subtitleTracks.length) {
      setSelectedSubtitle(subtitleTracks.find(t => t.selected)?.id || subtitleTracks[0].id);
    }
  }, [audioTracks, subtitleTracks]);

  const handleAudioChange = (value) => {
    setSelectedAudio(value);
    postMessage('audio_track_selected', value);
  };

  const handleSubtitleChange = (value) => {
    setSelectedSubtitle(value);
    postMessage('subtitle_track_selected', value);
    subtitleDelayReset();
  };

  const handleAddSubtitle = () => {
    postMessage('subtitle_file_select');
  }

  const subtitleDelayStep = 0.05;
  const handleSubtitleDelayChanged = (newDelay) => {
    postMessage('subtitle_delay_set', newDelay);
  }
  const subtitleDelayReset = () => { //hacky but idc
    setResetSubtitleDelay(true);
    setTimeout(() => setResetSubtitleDelay(false), 0);
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
            {selectedValue === track.id && <span className="checkmark">✓</span>}
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
            <div className='subtitles-header'>
              <p className='titles'>Audio</p>
            </div>
            {renderCustomSelector(audioTracks, selectedAudio, handleAudioChange)}
          </div>
          <div className='subtitle-popup-half'>
            <div className='subtitles-header'>
              <p className='titles'>Subtitles</p>
            </div>
            {renderCustomSelector(subtitleTracks, selectedSubtitle, handleSubtitleChange)}
            <div className='subtitles-footer'>
              <button className='add-subtitle-button' onClick={handleAddSubtitle}>
                <AddIcon />
                <span>Add</span>
              </button>
              <NumberStepper onNumberChanged={handleSubtitleDelayChanged} step={subtitleDelayStep} reset={resetSubtitleDelay} />
            </div>
          </div>
        </div>
      </div>
      <button className='main-button' id="subtitle-button">
        <Subtitles />
      </button>
    </div>
  );
};

export default SubtitleControl;
