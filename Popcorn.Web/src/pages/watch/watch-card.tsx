import React from 'react';
import { useNavigate } from 'react-router-dom';
import { IconPlayerPlayFilled } from '@tabler/icons-react';
import './watch-card.css';
import { useFileUrl } from '../../state/file-context';
import { ContinueWatching, MediaItem } from '../../model/media.contract';
import { useWebSocket } from '../../state/websocket-context';
import { SocketRequests } from '../../socket/socket-requests';
import { usePlayerStore } from '../../state/player.store';
import { isDesktopApp } from '../../utils/platform';
import { ContinueProgress, MarqueeText } from '../../components/continue-watching/continue-watching';

type WatchCardProps = {
  item: MediaItem;
  /** What the hover panel plays; no panel without it. */
  continueWatching?: ContinueWatching;
};

const WatchCard: React.FC<WatchCardProps> = ({ item, continueWatching }) => {
  const navigate = useNavigate();
  const { client } = useWebSocket();

  const imageUrl = useFileUrl(item.imagePath);

  const play = (e: React.MouseEvent) => {
    e.stopPropagation();
    if (!continueWatching) return;
    if (isDesktopApp) {
      client.sendEvent(SocketRequests.FileOpen, continueWatching.fileId);
      return;
    }
    const player = usePlayerStore.getState();
    player.resetState();
    player.setMediaId(item.id);
    player.setFileId(continueWatching.fileId);
  };

  return (
    <div
      className={`watch-card${continueWatching ? ' has-panel' : ''}${imageUrl ? '' : ' no-poster'}`}
      onClick={() => navigate(`/watch/${item.id}`)}
    >
      <div className="image-container">
        <img
          src={imageUrl || '/images/default-media.jpg'}
          className="rounded-image"
          alt={item?.title || 'Default media'}
        />
        {/* A poster already shows the title. */}
        {!imageUrl && <span className="display-title">{item.shortTitle || item.title}</span>}
      </div>
      {continueWatching && <ContinuePanel info={continueWatching} onPlay={play} />}
    </div>
  );
};

const ContinuePanel: React.FC<{ info: ContinueWatching; onPlay: (e: React.MouseEvent) => void }> = ({
  info,
  onPlay,
}) => {
  return (
    <div className="continue-panel">
      <div className="continue-header">
        <button type="button" className="continue-play" onClick={onPlay} aria-label="Play">
          <IconPlayerPlayFilled size={16} />
        </button>
        {info.label && <span className="continue-label">{info.label}</span>}
      </div>
      {info.episodeTitle && <MarqueeText text={`“${info.episodeTitle}”`} />}
      {info.position != null && info.duration ? (
        <ContinueProgress position={info.position} duration={info.duration} />
      ) : null}
    </div>
  );
};

export default WatchCard;
