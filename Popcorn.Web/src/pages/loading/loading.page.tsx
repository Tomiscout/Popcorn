import { Flex } from '@mantine/core';
import React from 'react';
import './loading.page.css';
import { wsAddress } from '../../state/websocket-context';
import { useAppStore } from '../../state/app.store';

const LoadingPage: React.FC = () => {
  const { isWebSocketReconnecting } = useAppStore();

  const message = isWebSocketReconnecting ? 'Reconnecting to' : 'Connecting to';
  return (
    <Flex
      justify={'center'}
      align={'center'}
      direction={'column'}
      style={{
        position: 'fixed',
        top: 0,
        left: 0,
        height: '100vh',
        width: '100vw',
        zIndex: 9999,
        backgroundColor: '#242424', //TODO mantine background color
      }}
    >
      <img src="images/popcorn.ico" className="pulsate"></img>
      <h1>Loading Popcorn</h1>
      <Flex align="center" gap="0.5rem">
        <h5>{message}</h5>
        <h5 style={{ color: '#888' }}>{wsAddress}</h5>
      </Flex>
    </Flex>
  );
};

export default LoadingPage;
