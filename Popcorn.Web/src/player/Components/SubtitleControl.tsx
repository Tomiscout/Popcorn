import { useState, useEffect, useRef } from 'react';
import { postMessage } from '../../utils/events';
import NumberStepper from './NumberStepper';
import '../Styles/SubtitleControl.css';
import { IconPlus as AddIcon, IconBadgeCc as Subtitles } from '@tabler/icons-react';
import { usePlayerStore } from '../../state/player.store';
import { useFileDialog } from '@mantine/hooks';
import { subtitleFormats } from '../../utils/file.utils';

interface TrackEntry {
  id: string;
  description: string;
  language: string;
  selected: boolean;
}

const TrackControl = () => {
  const subtitlePopupWidth = 500;

  const { metadata } = usePlayerStore();

  const [audioTracks, setAudioTracks] = useState<TrackEntry[]>([]);
  const [subtitleTracks, setSubtitleTracks] = useState<TrackEntry[]>([]);
  const [selectedAudio, setSelectedAudio] = useState<string>();
  const [selectedSubtitle, setSelectedSubtitle] = useState<string>();
  const [showPopup, setShowPopup] = useState(false);
  const [resetSubtitleDelay, setResetSubtitleDelay] = useState(false);
  const subtitleRef = useRef<HTMLDivElement>(null);
  const subtitlePopupRef = useRef<HTMLDivElement>(null);

  const fileDialog = useFileDialog({
    accept: subtitleFormats.join(','),
    onChange: (files) => {
      if (!files) return;
      for (const file of files) {
        postMessage('subtitle_dropped', file);
      }
    },
  });

  useEffect(() => {
    if (!metadata) return;

    setAudioTracks(
      metadata.audioTracks.map(
        (t) =>
          ({
            description: t.label,
            language: t.language,
            id: t.index,
            selected: false,
          }) as TrackEntry
      )
    );

    setSubtitleTracks(
      metadata.subtitleTracks.map(
        (t) =>
          ({
            description: t.label,
            language: t.language,
            id: t.index,
            selected: false,
          }) as TrackEntry
      )
    );
  }, [metadata]);

  useEffect(() => {
    if (audioTracks.length) {
      setSelectedAudio(audioTracks.find((t) => t.selected)?.id || audioTracks[0].id);
    }
    if (subtitleTracks.length) {
      setSelectedSubtitle(subtitleTracks.find((t) => t.selected)?.id || subtitleTracks[0].id);
    }
    console.log('Updated tracks', audioTracks, subtitleTracks);
  }, [audioTracks, subtitleTracks]);

  const handleAudioChange = (value: string) => {
    setSelectedAudio(value);
    postMessage('audio_track_selected', value);
  };

  const handleSubtitleChange = (value: string) => {
    setSelectedSubtitle(value);
    postMessage('subtitle_track_selected', value);
    subtitleDelayReset();
  };

  const subtitleDelayStep = 0.05;
  const handleSubtitleDelayChanged = (newDelay: number) => {
    postMessage('subtitle_delay_set', newDelay);
  };
  const subtitleDelayReset = () => {
    //hacky but idc
    setResetSubtitleDelay(true);
    setTimeout(() => setResetSubtitleDelay(false), 0);
  };

  const handleMouseEnter = () => {
    const subtitleElement = subtitleRef.current;
    const subtitlePopup = subtitlePopupRef.current;
    if (!subtitleElement || !subtitlePopup) return;

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
  };

  const renderCustomSelector = (
    tracks: TrackEntry[],
    selectedValue: string | undefined,
    handleChange: (value: string) => void
  ) => (
    <ul className="custom-select">
      {tracks.map((track, index) => (
        <li
          key={index}
          className={`track-option ${selectedValue === track.id ? 'selected' : ''}`}
          onClick={() => handleChange(track.id)}
        >
          <div className="checkmark-container">
            {selectedValue === track.id && <span className="checkmark">✓</span>}
          </div>
          {
            <span className={`option-text ${selectedValue === track.id ? 'bold' : ''}`}>
              {!track.id ? 'None' : track.language + ' '}
              <span className={`option-desc-text`}>{!track.id ? '' : track.description}</span>
            </span>
          }
        </li>
      ))}
    </ul>
  );

  return (
    <div ref={subtitleRef} onMouseEnter={handleMouseEnter} onMouseLeave={handleMouseLeave}>
      <div
        id="subtitle-popup"
        className="popup"
        ref={subtitlePopupRef}
        style={{
          width: `${subtitlePopupWidth}px`,
          display: showPopup ? '' : 'none',
        }}
      >
        <div className="subtitle-popup-container">
          <div className="subtitle-popup-half">
            <div className="subtitles-header">
              <p className="titles">Audio</p>
            </div>
            {renderCustomSelector(audioTracks, selectedAudio, handleAudioChange)}
          </div>
          <div className="subtitle-popup-half">
            <div className="subtitles-header">
              <p className="titles">Subtitles</p>
            </div>
            {renderCustomSelector(subtitleTracks, selectedSubtitle, handleSubtitleChange)}
            <div className="subtitles-footer">
              <button className="add-subtitle-button" onClick={fileDialog.open}>
                <AddIcon />
                <span>Add</span>
              </button>
              <NumberStepper
                initialValue={0}
                onNumberChanged={handleSubtitleDelayChanged}
                step={subtitleDelayStep}
                reset={resetSubtitleDelay}
              />
            </div>
          </div>
        </div>
      </div>
      <div className="control-button" id="subtitle-button">
        <Subtitles />
      </div>
    </div>
  );
};

export default TrackControl;
