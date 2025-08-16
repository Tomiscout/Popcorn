import React, { useEffect, useState, useRef } from 'react';
import { postMessage } from '../../utils/events';
import {
  IconListDetails as Episodes,
  IconFolder,
  IconChevronRight,
  IconPlayerPlayFilled as PlayButton,
} from '@tabler/icons-react';

import '../Styles/EpisodesControl.css';

const EpisodesControl = () => {
  const episodesPopupWidth = 400;

  const [navigation, setNavigation] = useState<any[]>([]);
  const [currentFiles, setCurrentFiles] = useState<any[]>([]);
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

  const openRootFolder = () => {
    postMessage('folder_open_root');
  };

  const fileClick = (file) => {
    if (file.Type === 1) {
      openFolder(file.Path);
      return;
    }
    postMessage('file_open', file.Id);
  };

  const openFolder = (folderPath) => {
    postMessage('folder_open', folderPath);
  };

  useEffect(() => {
    // registerWebviewEventHandler('folder_loaded', (data) => {
    //   console.log('Folder loaded:', data);
    //   setCurrentFiles(data.files);
    //   setNavigation(data.navigation);
    // });
  }, []);

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
            {navigation.map((navFolder, index) => (
              <React.Fragment key={index}>
                <span>
                  <IconChevronRight className="white ml" />
                </span>
                <p className="ml nav-item" onClick={() => openFolder(navFolder.path)}>
                  {navFolder.title}
                </p>
              </React.Fragment>
            ))}
          </div>
          <div className="file-container">
            {currentFiles.map((file, index) => (
              <div
                className={`file-item ${file.Played ? 'played-item' : ''}`}
                key={index}
                onClick={() => fileClick(file)}
              >
                {file.Type === 0 ? <PlayButton className="white" /> : <IconFolder className="white" />}
                <span>{file.ShortTitle}</span>
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
