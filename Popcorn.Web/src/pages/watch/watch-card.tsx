import React, { useState, useEffect } from 'react';
import { useNavigate } from 'react-router-dom';
import { MediaItem } from '../../model/media-item';
import './watch-card.css';

type WatchCardProps = {
  item: MediaItem;
};

const WatchCard: React.FC<WatchCardProps> = ({ item }) => {
  const [itemImageData, setItemImageData] = useState('');
  const [showOptions, setShowOptions] = useState(false);
  const navigate = useNavigate();

  const onItemClick = (item: MediaItem) => {
    navigate(`/watch/${item.id}`);
  };

  useEffect(() => {
    async function loadImage() {
      if (item.imagePath) {
        // Replace this with actual image loading logic if needed
        setItemImageData(item.imagePath);
      }
    }
    loadImage();
  }, [item.imagePath]);

  return (
    <div className="watch-card">
      {showOptions && (
        <div className="options-panel">
          <span>hello</span>
          {/* Options content goes here */}
        </div>
      )}
      <div className="image-container" onClick={() => onItemClick(item)}>
        {itemImageData === '' ? (
          <img
            className="rounded-image"
            src="/images/default-media.jpg"
            alt="default media"
          />
        ) : (
          <img src={itemImageData} className="rounded-image" alt={item.title} />
        )}
        <span className="display-title">{item.displayTitle || item.title}</span>
      </div>
    </div>
  );
};

export default WatchCard;
