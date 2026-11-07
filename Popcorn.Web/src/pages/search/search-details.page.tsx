import { Alert, Button, Loader, Center, Container, Group, Title } from '@mantine/core';
import { IconArrowLeft, IconSearch } from '@tabler/icons-react';
import React, { useEffect, useMemo, useState } from 'react';
import { useNavigate, useParams } from 'react-router-dom';
import type { FileQuery, MediaDetails } from '@popcorn/plugin-sdk';
import { useSocketEvents } from '../../socket/useSocketEvents.hook';
import { SocketRequests } from '../../socket/socket-requests';
import { useWebSocket } from '../../state/websocket-context';
import { PluginDetailsRequest, PluginDetailsResponse } from '../../model/plugin.contract';
import { pluginDetailsPath } from '../../plugins/plugins.hook';
import { PluginDetails } from '../../plugins/plugin-components';
import { FileSearch, LinkMedia, useFileSearch } from '../../plugins/file-sources';
import { FileResults } from '../../plugins/file-results';

/** Files section: every file source searched for one media item (its `fileQuery`, or its title), on demand. */
function ItemFiles({ query, media }: { query: FileQuery; media: LinkMedia }) {
  const [search, setSearch] = useState<FileSearch | null>(null);
  const { sources, start, refresh } = useFileSearch(search, setSearch);
  if (!sources?.length) return null;
  return (
    <Container size="lg" pb="xl">
      <Group mb="sm">
        <Title order={3}>Files</Title>
        {!search && (
          <Button variant="light" leftSection={<IconSearch size={16} />} onClick={() => start(query)}>
            Search {sources.length === 1 ? sources[0].name : `${sources.length} file sources`}
          </Button>
        )}
      </Group>
      {search && <FileResults section="item-files" sources={sources} search={search} media={media} onRefresh={refresh} />}
    </Container>
  );
}

type DetailsState = { key: string; details: MediaDetails | null; error: string | null };

/** /media-details/:pluginId/:id — details of a plugin search result, rendered by the plugin's Details slot. */
const SearchDetailsPage: React.FC = () => {
  const navigate = useNavigate();
  const { client, isClientReady } = useWebSocket();
  const { pluginId = '', id = '' } = useParams();
  const key = `${pluginId}\n${id}`;
  const [state, setState] = useState<DetailsState | null>(null);

  useSocketEvents(
    useMemo(
      () => ({
        [SocketRequests.PluginDetails]: (r: PluginDetailsResponse) => {
          if (r.pluginId === pluginId && r.id === id) setState({ key, details: r.details, error: r.error });
        },
      }),
      [pluginId, id, key]
    )
  );

  useEffect(() => {
    if (!isClientReady) return;
    client.sendEvent<PluginDetailsRequest>(SocketRequests.PluginDetails, { pluginId, id });
  }, [client, isClientReady, pluginId, id]);

  const current = state?.key === key ? state : null; // ignore the previous item's state while the next one loads

  return (
    <div style={{ position: 'relative', minHeight: '100%' }}>
      <Button
        variant="subtle"
        color="gray"
        leftSection={<IconArrowLeft size={14} />}
        onClick={() => navigate(-1)}
        style={{ position: 'absolute', top: 16, left: 16, zIndex: 1 }}
      >
        Back
      </Button>

      {!current && (
        <Center pt={96}>
          <Loader />
        </Center>
      )}
      {current?.error && (
        <Alert color="red" title="Failed to load details" m="md" mt={72}>
          {current.error}
        </Alert>
      )}
      {current?.details && (
        <>
          <PluginDetails
            key={key}
            pluginId={pluginId}
            details={current.details}
            onOpenCard={(card) => navigate(pluginDetailsPath(pluginId, card.id))}
          />
          <ItemFiles
            key={`files\n${key}`}
            query={current.details.fileQuery ?? { text: current.details.title }}
            media={{ title: current.details.title, posterUrl: current.details.posterUrl }}
          />
        </>
      )}
    </div>
  );
};

export default SearchDetailsPage;
