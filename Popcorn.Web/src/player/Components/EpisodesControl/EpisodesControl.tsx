import React, { useState, useRef } from 'react';
import { postMessage } from '../../../utils/events';
import {
  IconListDetails as Episodes,
  IconFolder,
  IconChevronRight,
  IconPlayerPlayFilled as PlayButton,
} from '@tabler/icons-react';

import './EpisodesControl.css';
import { usePlayerStore } from '../../../state/player.store';
import { FileType, MediaFile } from '../../../model/media.contract';

const EpisodesControl = () => {
  const episodesPopupWidth = 400;

  const { currentPlaylist, currentPlaylistNavigation } = usePlayerStore();
  const [showPopup, setShowPopup] = useState(false);
  const episodesRef = useRef<HTMLDivElement | null>(null);
  const episodesPopupRef = useRef<HTMLDivElement | null>(null);

  const handleMouseEnter = () => {
    const episodesElement = episodesRef.current;
    const episodesPopup = episodesPopupRef.current;
    const rect = episodesElement!.getBoundingClientRect();
    const windowWidth = window.innerWidth;

    let left = rect.left + rect.width / 2 - episodesPopupWidth / 2;
    const bottom = rect.height;

    if (left + episodesPopupWidth > windowWidth) {
      left = windowWidth - episodesPopupWidth;
    }

    episodesPopup!.style.left = `${left}px`;
    episodesPopup!.style.bottom = `${bottom}px`;
    setShowPopup(true);
  };

  const handleMouseLeave = () => {
    setShowPopup(false);
  };

  const fileClick = (file: MediaFile) => {
    if (file.type === FileType.Folder) {
      openFolder(file.id, file.path);
      return;
    }

    postMessage('play_file', file.id);
  };

  // The host resolves the folder: the server by id, the native player by path.
  const openFolder = (fileId: string, path: string) => {
    postMessage('folder_open', { fileId, path });
  };

  const openRootFolder = () => {
    postMessage('folder_open_root');
  };

  return (
    <div ref={episodesRef} onMouseEnter={handleMouseEnter} onMouseLeave={handleMouseLeave}>
      <div
        id="episodes-popup"
        className="popup"
        ref={episodesPopupRef}
        style={{
          width: `${episodesPopupWidth}px`,
          display: showPopup ? '' : 'none',
        }}
      >
        <div className="episodes-popup-container">
          <div className="navigation-container">
            <IconFolder className="nav-item" onClick={openRootFolder} />
            {currentPlaylistNavigation?.map((navFolder, index) => (
              <React.Fragment key={index}>
                <span>
                  <IconChevronRight className="white ml" />
                </span>
                <p className="ml nav-item" onClick={() => openFolder(navFolder.fileId, navFolder.path)}>
                  {navFolder.title}
                </p>
              </React.Fragment>
            ))}
          </div>
          <div className="file-container">
            {currentPlaylist?.map((file, index) => (
              <div
                className={`file-item ${file.played ? 'played-item' : ''}`}
                key={index}
                onClick={() => fileClick(file)}
              >
                {file.type === 0 ? <PlayButton className="white" /> : <IconFolder className="white" />}
                <span>{file.shortTitle || file.title}</span>
              </div>
            ))}
          </div>
        </div>
      </div>
      <div className="control-button episodes">
        <Episodes />
      </div>
    </div>
  );
};

export default EpisodesControl;
