import { Button, Flex, Loader, Pill, TextInput, Text, Title } from '@mantine/core';
import { IconSearch } from '@tabler/icons-react';
import React, { useEffect, useState } from 'react';
import '../../components/styles/search-text-input.css';
import { useWebSocket } from '../../state/websocket-context';
import { SocketRequests } from '../../socket/socket-requests';
import debounce from 'lodash.debounce';
import { MediaSearchRequest, MovieDbMediaType, MovieDbSearchResult } from '../../model/search.contract';
import './media-search.page.css';
import { useNavigate } from 'react-router-dom';

const mediaTypeTextMap: Record<MovieDbMediaType, { text: string; backgroundColor: string }> = {
  [MovieDbMediaType.Movie]: { text: 'Movie', backgroundColor: '#4caf50' },
  [MovieDbMediaType.TvShow]: { text: 'TV Show', backgroundColor: '#2196f3' },
  [MovieDbMediaType.Person]: { text: 'Person', backgroundColor: '#ff9800' },
};

const MediaSearchPage: React.FC = () => {
  const { client, isClientReady } = useWebSocket();
  const [searchText, setSearchText] = useState('');
  const [isLoading, setIsLoading] = useState(false);
  const [searchResults, setSearchResults] = useState<MovieDbSearchResult[]>([]);

  const navigate = useNavigate();

  const debouncedSearch = React.useMemo(
    () =>
      debounce((value: MediaSearchRequest) => {
        if (isClientReady) {
          setIsLoading(true);
          client.sendEvent(SocketRequests.MediaSearch, value);
        }
      }, 300),
    [client, isClientReady]
  );

  useEffect(() => {
    return () => {
      debouncedSearch.cancel();
    };
  }, [debouncedSearch]);

  const handleSearch = () => {
    const query: MediaSearchRequest = {
      searchText,
      isTvShow: false,
    };
    debouncedSearch(query);
  };

  useEffect(() => {
    if (!isClientReady) return;

    const handleSearchResults = (response: MovieDbSearchResult[]) => {
      setIsLoading(false);
      setSearchResults(response);
      console.log('Search results:', response);
    };

    client.onEvent<any>(SocketRequests.MediaSearch, handleSearchResults);

    return () => {
      client.onEventUnregiser(SocketRequests.MediaSearch);
    };
  }, [client, isClientReady]);

  function renderSearchResult(result: MovieDbSearchResult) {
    const handleClick = () => {
      if (result.mediaType === MovieDbMediaType.Person) return;

      navigate(`/media-details/${result.id}`);
    };

    return (
      <Flex
        key={result.id}
        direction="row"
        align="center"
        gap="md"
        p="sm"
        style={{
          border: '1px solid #ccc',
          borderRadius: '8px',
          cursor: 'pointer',
          transition: 'background-color 0.3s',
        }}
        className="media-result-card"
        onClick={handleClick}
      >
        <img
          src={result.posterPath ? `https://image.tmdb.org/t/p/w200${result.posterPath}` : '/images/default-media.jpg'}
          alt={result.title}
          style={{ width: '170px', height: '275px', objectFit: 'cover', borderRadius: '4px' }}
        />
        <Flex direction="column" gap="sm" h={280}>
          <Title order={2}>{result.title}</Title>
          <Flex align="center" gap="sm">
            {renderMediaTypePill(result.mediaType)}
            <Text size="md">
              Release Date: {result.releaseDate ? new Date(result.releaseDate).toLocaleDateString() : 'N/A'}
            </Text>
          </Flex>
          <Text size="sm" style={{ flexGrow: 1 }}>
            {result.description}
          </Text>
        </Flex>
      </Flex>
    );
  }

  function renderMediaTypePill(mediaType: MovieDbMediaType) {
    const { text, backgroundColor } = mediaTypeTextMap[mediaType];
    return <Pill style={{ backgroundColor, color: 'white' }}>{text}</Pill>;
  }

  return (
    <Flex direction="column" w="100%">
      <Title order={1} mb={8}>
        Media search
      </Title>

      <Flex w="100%" align="center">
        <TextInput
          value={searchText}
          onChange={(event) => setSearchText(event.currentTarget.value)}
          onKeyDown={(event) => {
            if (event.key === 'Enter') {
              handleSearch();
            }
          }}
          variant="filled"
          radius="xl"
          leftSectionPointerEvents="none"
          leftSection={<IconSearch size="16px" />}
          placeholder="Search"
          classNames={{ input: 'searchInput' }}
          style={{ flexGrow: 1 }}
        />
        <Button ml="sm" radius="xl" onClick={handleSearch}>
          Search
        </Button>
      </Flex>
      {isLoading && <Loader />}
      <Flex direction="column" mt="md" gap="sm">
        {searchResults.map((result) => renderSearchResult(result))}
      </Flex>
    </Flex>
  );
};

export default MediaSearchPage;
