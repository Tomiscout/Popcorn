import React from 'react';
import { Box, Button, Flex, Title } from '@mantine/core';
import WatchCard from './watch-card';
import { MediaItem } from '../../model/media.contract';

type ItemsGridProps = {
  items: MediaItem[];
  headerTitle: string;
  buttonText?: string;
  onButtonClick?: () => void;
};

const ItemsGrid: React.FC<ItemsGridProps> = ({ items, headerTitle, buttonText, onButtonClick }) => {
  return (
    <Box>
      <Flex justify={'space-between'} align={'center'} mb={'md'}>
        <Title size="xl">{headerTitle}</Title>

        <Flex justify={'flex-end'}>
          {buttonText && onButtonClick && (
            <Button style={{ marginRight: '8px' }} color="red" variant="filled" onClick={onButtonClick}>
              {buttonText}
            </Button>
          )}
        </Flex>
      </Flex>
      <Flex gap={'md'} justify={'flex-start'} wrap={'wrap'}>
        {items.map((item) => (
          <WatchCard key={item.id} item={item} />
        ))}
      </Flex>
    </Box>
  );
};

export default ItemsGrid;
