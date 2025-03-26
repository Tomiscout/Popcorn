import React, { useState } from 'react';
import ItemsGrid from './items-grid';
import { MediaItem } from '../../model/media-item';

const WatchPage: React.FC = () => {
  const [recentItems, setRecentItems] = useState<MediaItem[]>([
    { id: '1', title: 'some stuff', displayTitle: 'some more stuff' },
    {
      id: '2',
      title: 'aa',
      displayTitle: 'Naruto',
      imagePath: 'images/naruto.jpeg',
    },
    { id: '2', title: 'aa', displayTitle: 'Naruto' },
    { id: '2', title: 'aa', displayTitle: 'Naruto' },
    { id: '2', title: 'aa', displayTitle: 'Naruto' },
    { id: '2', title: 'aa', displayTitle: 'Naruto' },
    { id: '2', title: 'aa', displayTitle: 'Naruto' },
  ]);
  const [newItems, setNewItems] = useState<MediaItem[]>([]);
  const [removedItems, setRemovedItems] = useState<MediaItem[]>([]);

  return (
    <>
      <ItemsGrid
        headerTitle="Continue watching"
        items={recentItems}
        buttonText="Do something"
        onButtonClick={() => ''}
      />
      <ItemsGrid headerTitle="Not yet watched" items={newItems} />
      <ItemsGrid headerTitle="No longer on disk" items={removedItems} />
    </>
  );
};

export default WatchPage;
