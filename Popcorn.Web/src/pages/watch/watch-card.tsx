import React, { useState } from 'react';
import { useNavigate } from 'react-router-dom';
import './watch-card.css';
import { useFileUrl } from '../../state/file-context';
import { MediaItem } from '../../model/media.contract';

type WatchCardProps = {
  item: MediaItem;
};

const WatchCard: React.FC<WatchCardProps> = ({ item }) => {
  const [showOptions, _] = useState(false);
  const navigate = useNavigate();

  const imageUrl = useFileUrl(item.imagePath);

  const onItemClick = (item: MediaItem) => {
    navigate(`/watch/${item.id}`);
  };

  return (
    <div className="watch-card">
      {showOptions && (
        <div className="options-panel">
          <span>hello</span>
          {/* Options content goes here */}
        </div>
      )}
      <div className="image-container" onClick={() => onItemClick(item)}>
        <img
          src={imageUrl || '/images/default-media.jpg'}
          className="rounded-image"
          alt={item?.title || 'Default media'}
        />
        <span className="display-title">{item.shortTitle || item.title}</span>
      </div>
    </div>
  );
};

export default WatchCard;
