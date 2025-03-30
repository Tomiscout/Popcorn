import React, { useState, useEffect, useMemo } from 'react';
import { useNavigate } from 'react-router-dom';
import './watch-card.css';
import { useFileUrls } from '../../state/file-context';
import { MediaItem } from '../../model/media.contract';

type WatchCardProps = {
  item: MediaItem;
};

const WatchCard: React.FC<WatchCardProps> = ({ item }) => {
  const [itemImageUrl, setItemImageUrl] = useState('');
  const [showOptions, setShowOptions] = useState(false);
  const navigate = useNavigate();

  const requestedFilenames = useMemo(() => (item.imagePath ? [item.imagePath] : []), [item.imagePath]);
  const availableUrls = useFileUrls(requestedFilenames);

  const onItemClick = (item: MediaItem) => {
    navigate(`/watch/${item.id}`);
  };

  useEffect(() => {
    if (!item.imagePath) return;

    const imageUrl = availableUrls[item.imagePath];
    if (!imageUrl) return;

    setItemImageUrl(imageUrl);
    console.log('setting imae');
  }, [availableUrls]);

  return (
    <div className="watch-card">
      {showOptions && (
        <div className="options-panel">
          <span>hello</span>
          {/* Options content goes here */}
        </div>
      )}
      <div className="image-container" onClick={() => onItemClick(item)}>
        {!itemImageUrl ? (
          <img className="rounded-image" src="/images/default-media.jpg" alt="default media" />
        ) : (
          <img src={itemImageUrl} className="rounded-image" alt={item.title} />
        )}
        <span className="display-title">{item.shortTitle || item.title}</span>
      </div>
    </div>
  );
};

export default WatchCard;
