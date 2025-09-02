import React, { useEffect, useState } from 'react';
import { usePlayerStore } from '../../../state/player.store';
import AssSubtitleOverlay from './AssSubtitleOverlay';
import { resolveServerHost } from '../../../utils/url-resolver';
import { postMessage } from '../../../utils/events';
import VttSubtitleOverlay from './VttSubtitleOverlay';
import PgsSubtitleOverlay from './PgsSubtitleOverlay';

interface SubtitleOverlayProps {
  videoRef: React.RefObject<HTMLVideoElement | null>;
}

const SubtitleOverlay: React.FC<SubtitleOverlayProps> = ({ videoRef }) => {
  const { fileId, currentSubtitle, getSubtitleFile, addSubtitleFile } = usePlayerStore();
  const [loadingSubtitle, setLoadingSubtitle] = useState(false);

  async function loadSubtitleFile(): Promise<string | null> {
    if (!currentSubtitle) return null;

    const errorMessage = `Failed to fetch subtitles [${currentSubtitle.language}]${currentSubtitle.label}-${currentSubtitle.key} ${currentSubtitle.format} from server.`;

    try {
      const response = await fetch(
        `http://${resolveServerHost()}/api/video/${fileId}/subtitles/${currentSubtitle.key}`
      );
      if (response.ok) {
        const mime = response.headers.get('Content-Type');
        if (mime === 'text/vtt' || mime === 'application/x-subrip' || mime === 'text/x-ass') {
          return await response.text();
        } else if (mime === 'application/octet-stream') {
          const blob = await response.blob();
          return URL.createObjectURL(blob);
        } else {
          postMessage('player_error', `${errorMessage}\nUnsupported subtitle type: ${mime}`);
        }
      } else {
        postMessage('player_error', `${errorMessage}\n[${response.status}] - ${response.text()}`);
      }
    } catch (error) {
      console.log(JSON.stringify(error));
      postMessage('player_error', `${errorMessage}\n${JSON.stringify(error)}`);
    }
    return null;
  }

  async function subtitleChangedHandler() {
    if (!videoRef.current || !currentSubtitle) return;

    let subtitleData = getSubtitleFile(currentSubtitle.key);
    if (!subtitleData) {
      setLoadingSubtitle(true);
      const result = await loadSubtitleFile();
      if (result) {
        subtitleData = result;
        addSubtitleFile(currentSubtitle.key, subtitleData);
      } else {
        postMessage('player_error', 'Failed to load subtitle');
      }
      setLoadingSubtitle(false);
    }
  }

  useEffect(() => {
    subtitleChangedHandler();
  }, [currentSubtitle]);

  if (!videoRef.current || !currentSubtitle) return null;

  if (loadingSubtitle) {
    return (
      <div
        style={{
          position: 'absolute',
          top: '50%',
          left: '50%',
          transform: 'translate(-50%, -50%)',
          color: 'white',
          backgroundColor: 'rgba(0, 0, 0, 0.7)',
          padding: '10px',
          borderRadius: '5px',
        }}
      >
        Loading subtitles...
      </div>
    );
  }

  if (currentSubtitle.format === 'ass') {
    return <AssSubtitleOverlay videoRef={videoRef} currentKey={currentSubtitle.key} />;
  }
  if (currentSubtitle.format === 'srt' || currentSubtitle.format === 'vtt') {
    return <VttSubtitleOverlay videoRef={videoRef} currentKey={currentSubtitle.key} />;
  }
  if (currentSubtitle.format === 'sup') {
    return <PgsSubtitleOverlay videoRef={videoRef} currentKey={currentSubtitle.key} />;
  }
};

export default SubtitleOverlay;
