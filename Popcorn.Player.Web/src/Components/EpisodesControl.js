import React, { useEffect, useState, useRef } from "react";
import { registerWebviewEventHandler, postMessage } from "../events";
import { ReactComponent as Epsiodes } from "../assets/icons/episodes.svg";
import { FontAwesomeIcon } from "@fortawesome/react-fontawesome";
import {
  faFolder,
  faChevronRight,
  faPlay,
} from "@fortawesome/free-solid-svg-icons";
import "../Styles/EpisodesControl.css";

const EpisodesControl = () => {
  const episodesPopupWidth = 400;

  const [navigation, setNavigation] = useState([]);
  const [currentFiles, setCurrentFiles] = useState([]);
  const [showPopup, setShowPopup] = useState(false);
  const episodesRef = useRef(null);
  const episodesPopupRef = useRef(null);

  const handleMouseEnter = () => {
    const episodesElement = episodesRef.current;
    const episodesPopup = episodesPopupRef.current;
    const rect = episodesElement.getBoundingClientRect();
    const windowWidth = window.innerWidth;

    let left = rect.left + rect.width / 2 - episodesPopupWidth / 2;
    const bottom = rect.height;

    if (left + episodesPopupWidth > windowWidth) {
      left = windowWidth - episodesPopupWidth;
    }

    episodesPopup.style.left = `${left}px`;
    episodesPopup.style.bottom = `${bottom}px`;
    setShowPopup(true);
  };

  const handleMouseLeave = () => {
    setShowPopup(false);
  };

  const openRootFolder = () => {
    postMessage("folder_open_root");
  };

  const fileClick = (file) => {
    if (file.Type === 1) {
      openFolder(file.Path);
      return;
    }
    postMessage("file_open", file.Id);
  };

  const openFolder = (folderPath) => {
    postMessage("folder_open", folderPath);
  };

  useEffect(() => {
    registerWebviewEventHandler("folder_loaded", (data) => {
      console.log("Folder loaded:", data);
      setCurrentFiles(data.files);
      setNavigation(data.navigation);
    });
  }, []);

  return (
    <div
      ref={episodesRef}
      onMouseEnter={handleMouseEnter}
      onMouseLeave={handleMouseLeave}
    >
      <div
        id="episodes-popup"
        className="popup"
        ref={episodesPopupRef}
        style={{
          width: `${episodesPopupWidth}px`,
          display: showPopup ? "" : "none",
        }}
      >
        <div className="episodes-popup-container">
          <div className="navigation-container">
            <FontAwesomeIcon
              icon={faFolder}
              className="nav-item"
              onClick={openRootFolder}
            />
            {navigation.map((navFolder, index) => (
              <React.Fragment key={index}>
                <span>
                  <FontAwesomeIcon icon={faChevronRight} className="white ml" />
                </span>
                <p
                  className="ml nav-item"
                  onClick={() => openFolder(navFolder.path)}
                >
                  {navFolder.title}
                </p>
              </React.Fragment>
            ))}
          </div>
          <div className="file-container">
            {currentFiles.map((file, index) => (
              <div
                className={`file-item ${file.Played ? "played-item" : ""}`}
                key={index}
                onClick={() => fileClick(file)}
              >
                {file.Type === 0 ? (
                  <FontAwesomeIcon icon={faPlay} className="white" />
                ) : (
                  <FontAwesomeIcon icon={faFolder} className="white" />
                )}
                <span>{file.ShortTitle}</span>
              </div>
            ))}
          </div>
        </div>
      </div>
      <button className="main-button episodes">
        <Epsiodes />
      </button>
    </div>
  );
};

export default EpisodesControl;
