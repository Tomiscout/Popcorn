import { Flex } from '@mantine/core';
import React from 'react';
import './loading.page.css';
import PopcornBox from '../../components/popcorn-box/popcorn-box';

const LoadingPage: React.FC = () => {
  return (
    <Flex justify="center" align="center" direction="column" className="root">
      <PopcornBox />
    </Flex>
  );
};

export default LoadingPage;
