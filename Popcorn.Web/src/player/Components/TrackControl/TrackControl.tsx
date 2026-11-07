import { useState, useRef, useMemo } from 'react';
import { postMessage } from '../../../utils/events';
import NumberStepper from '../NumberStepper/NumberStepper';
import './TrackControl.css';
import { IconPlus as AddIcon, IconBadgeCc as Subtitles } from '@tabler/icons-react';
import { usePlayerStore } from '../../../state/player.store';
import { useFileDialog } from '@mantine/hooks';
import { subtitleFormats } from '../../../utils/file.utils';
import { usePlayerFeatures } from '../../player-features';

interface TrackEntry {
  id: string;
  description: string;
  language: string;
}

/** Subtitle "None" entry; hosts get -1 in `subtitle_track_selected` and turn subtitles off. */
export const NO_SUBTITLE_ID = '-1';

const toEntry = (t: { index: number; label: string; language?: string }): TrackEntry => ({
  description: t.label,
  language: t.language ?? '',
  id: t.index.toString(),
});

const TrackControl = () => {
  const subtitlePopupWidth = 500;

  const { metadata, currentAudio, currentSubtitle } = usePlayerStore();
  const { addSubtitle, subtitleDelay } = usePlayerFeatures();

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

  const audioTracks = useMemo(() => (metadata?.audioTracks ?? []).map(toEntry), [metadata]);
  const subtitleTracks = useMemo(
    () => [{ id: NO_SUBTITLE_ID, description: '', language: '' }, ...(metadata?.subtitleTracks ?? []).map(toEntry)],
    [metadata]
  );

  // The host owns the selection (store); before it reports one, the first audio track is the one playing.
  const selectedAudio = currentAudio?.index.toString() ?? audioTracks[0]?.id;
  const selectedSubtitle = currentSubtitle?.index.toString() ?? NO_SUBTITLE_ID;

  const handleAudioChange = (value: string) => {
    if (selectedAudio === value) return;
    postMessage('audio_track_selected', value);
  };

  const handleSubtitleChange = (value: string) => {
    if (selectedSubtitle === value) return;
    postMessage('subtitle_track_selected', value);
    subtitleDelayReset();
  };

  const handleAddSubtitle = () => {
    if (addSubtitle === 'browser') {
      fileDialog.open();
    } else {
      postMessage('subtitle_file_select');
    }
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
              {track.id === NO_SUBTITLE_ID ? 'None' : track.language + ' '}
              <span className={`option-desc-text`}>{track.description}</span>
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
            {(addSubtitle || subtitleDelay) && (
              <div className="subtitles-footer">
                {addSubtitle && (
                  <button className="add-subtitle-button" onClick={handleAddSubtitle}>
                    <AddIcon />
                    <span>Add</span>
                  </button>
                )}
                {subtitleDelay && (
                  <NumberStepper
                    initialValue={0}
                    onNumberChanged={handleSubtitleDelayChanged}
                    step={subtitleDelayStep}
                    reset={resetSubtitleDelay}
                  />
                )}
              </div>
            )}
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
