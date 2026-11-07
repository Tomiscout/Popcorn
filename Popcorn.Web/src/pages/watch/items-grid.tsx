import React from 'react';
import { Box, Button, Flex, Title } from '@mantine/core';
import WatchCard from './watch-card';
import { ContinueWatching, MediaItem } from '../../model/media.contract';

type ItemsGridProps = {
  items: MediaItem[];
  /** Per item id: what the card's hover panel plays; items without an entry have no panel. */
  continueWatching?: Record<string, ContinueWatching>;
  headerTitle: string;
  buttonText?: string;
  searchQuery?: string;
  onButtonClick?: () => void;
};

const ItemsGrid: React.FC<ItemsGridProps> = ({
  items,
  continueWatching,
  headerTitle,
  buttonText,
  searchQuery,
  onButtonClick,
}) => {
  const filterItems = (item: MediaItem) => {
    const queryTags = searchQuery
      ?.toLocaleLowerCase()
      .split(' ')
      .filter((q) => q);
    if (!queryTags?.length) return true;

    const title = item.shortTitle || item.title;
    const itemTags = title
      .toLocaleLowerCase()
      .split(' ')
      .filter((q) => q);

    return queryTags.some((tag) => itemTags.some((word) => word.includes(tag)));
  };

  const shownItems = items.filter(filterItems);
  if (!shownItems.length) return null;

  return (
    <Box>
      <Flex justify={'space-between'} align={'center'} mb={'md'}>
        <Title size="xl">{headerTitle}</Title>

        <Flex justify={'flex-end'}>
          {buttonText && onButtonClick && (
            <Button mr="xs" variant="filled" onClick={onButtonClick}>
              {buttonText}
            </Button>
          )}
        </Flex>
      </Flex>
      <Flex gap={'md'} justify={'flex-start'} wrap={'wrap'}>
        {shownItems.map((item) => (
          <WatchCard key={item.id} item={item} continueWatching={continueWatching?.[item.id]} />
        ))}
      </Flex>
    </Box>
  );
};

export default ItemsGrid;
