import { Flex } from '@mantine/core';
import React from 'react';
import './loading.page.css';

const LoadingPage: React.FC = () => {
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
    </Flex>
  );
};

export default LoadingPage;
