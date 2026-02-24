import { Button } from '@mantine/core';
import { IconArrowLeft } from '@tabler/icons-react';
import React from 'react';
import { useNavigate } from 'react-router-dom';
import { useSocketEvents } from '../../socket/useSocketEvents.hook';
import { SocketRequests } from '../../socket/socket-requests';

const MediaDetailsPage: React.FC = () => {
  const navigate = useNavigate();

  useSocketEvents({
    [SocketRequests.MediaSearchDetails]: (data) => {
      console.log('Received media details:', data);
    },
  });

  return (
    <div>
      <Button leftSection={<IconArrowLeft size={14} />} onClick={() => navigate(-1)}>
        Back
      </Button>

      <h1>Media Details</h1>
      <p>Details about the selected media will be displayed here.</p>
    </div>
  );
};

export default MediaDetailsPage;
