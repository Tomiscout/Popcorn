import { Badge, Button, Container, Group, Stack, Title, Image, Text, BackgroundImage } from '@mantine/core';
import { IconArrowLeft } from '@tabler/icons-react';
import React, { useEffect, useState } from 'react';
import { useNavigate, useParams } from 'react-router-dom';
import { useSocketEvents } from '../../socket/useSocketEvents.hook';
import { SocketRequests } from '../../socket/socket-requests';
import { useWebSocket } from '../../state/websocket-context';
import { MediaSearchDetailsRequest, MediaSearchDetailsResponse, Movie, TVShow } from '../../model/search.contract';

const SearchDetailsPage: React.FC = () => {
  const navigate = useNavigate();
  const { client, isClientReady } = useWebSocket();
  const { id, type } = useParams();
  const [data, setData] = useState<Movie | TVShow | null>(null);

  useSocketEvents({
    [SocketRequests.MediaSearchDetails]: (response: MediaSearchDetailsResponse) => {
      const result = response.movie ?? response.tvShow ?? null;
      console.log('Received media details:', result);
      setData(result);
    },
  });

  useEffect(() => {
    if (!isClientReady) return;

    client.sendEvent<any>(SocketRequests.MediaSearchDetails, {
      id: parseInt(id || '0'),
      type: parseInt(type || '0'),
    } as MediaSearchDetailsRequest);
  }, [isClientReady]);

  if (!data) return null;

  const isMovie = (d: Movie | TVShow): d is Movie => (d as Movie).title !== undefined;

  const title = isMovie(data) ? data.title : data.name;
  const overview = data.overview;
  const tagline = isMovie(data) ? data.tagline : '';
  const backdrop = data.backdropPath || '';
  const poster = data.posterPath || '';
  const genres = data.genres?.map((g) => g.name) ?? [];

  return (
    <div>
      <BackgroundImage
        src={backdrop}
        style={{
          position: 'relative',
        }}
      >
        {/* Dark overlay */}
        <div
          style={{
            position: 'absolute',
            inset: 0,
            background: 'linear-gradient(90deg, rgba(0,0,0,0.85) 30%, rgba(0,0,0,0.4) 100%)',
          }}
        />

        <Container
          size="lg"
          style={{
            position: 'relative',
            paddingTop: 40,
            paddingBottom: 120,
          }}
        >
          <Button
            variant="subtle"
            color="gray"
            mb="xl"
            leftSection={<IconArrowLeft size={14} />}
            onClick={() => navigate(-1)}
          >
            Back
          </Button>

          <Group align="flex-start" gap="xl">
            <Image src={poster} w={300} radius="md" style={{ boxShadow: '0 20px 40px rgba(0,0,0,0.5)' }} />

            <Stack gap="sm" maw={600}>
              <Title order={1} c="white">
                {title}
              </Title>

              <Group gap="xs">
                {genres.map((g) => (
                  <Badge key={g} color="pink">
                    {g}
                  </Badge>
                ))}
              </Group>

              <Text c="gray.2" size="lg">
                {tagline}
              </Text>

              <Text c="gray.3" size="lg">
                {overview}
              </Text>
            </Stack>
          </Group>
        </Container>
      </BackgroundImage>
      {/* <Table></Table> */}
    </div>
  );
};

export default SearchDetailsPage;
