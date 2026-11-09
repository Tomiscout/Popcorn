import { Badge, Button, Flex, Loader, Stack, Tabs, Text, TextInput, Title } from '@mantine/core';
import { IconSearch } from '@tabler/icons-react';
import React, { useEffect, useMemo } from 'react';
import { useNavigate } from 'react-router-dom';
import '../../components/styles/search-text-input.css';
import { useWebSocket } from '../../state/websocket-context';
import { SocketRequests } from '../../socket/socket-requests';
import { useSocketEvents } from '../../socket/useSocketEvents.hook';
import { SearchTab, useSearchStore } from '../../state/search.store';
import { PluginSearchResponse } from '../../model/plugin.contract';
import { pluginDetailsPath, usePlugins } from '../../plugins/plugins.hook';
import { PluginSearchCard } from '../../plugins/plugin-components';
import { loadingResults, ProviderResults, useFileSearch } from '../../plugins/file-sources';
import { SourceFilter } from '../../plugins/source-filter';
import { FileResults } from '../../plugins/file-results';
import { ProviderGroup } from '../../plugins/provider-group';

/** Total result count of a tab, or a loader while any provider is still searching. */
function TabCount({ results }: { results: Record<string, ProviderResults<unknown>> | undefined }) {
  if (!results) return null;
  const states = Object.values(results);
  if (states.length === 0) return null;
  if (states.some((s) => s.loading)) return <Loader size={12} />;
  return (
    <Badge size="sm" variant="light" color="gray">
      {states.reduce((n, s) => n + s.results.length, 0)}
    </Badge>
  );
}

/**
 * Searches every plugin with one query: media information sources (Media tab, grouped per plugin) and file sources
 * (Files tab). File sources are only asked once the Files tab is open.
 */
const SearchPage: React.FC = () => {
  const { client, isClientReady } = useWebSocket();
  const plugins = usePlugins();
  const { searchQuery, setSearchQuery, tab, setTab, submittedQuery, media, files, startSearch } = useSearchStore();
  const setMediaResults = useSearchStore((s) => s.setMediaResults);
  const setFiles = useSearchStore((s) => s.setFiles);
  const navigate = useNavigate();
  const {
    sources,
    enabled,
    setEnabled,
    start: startFileSearch,
    refresh: refreshFiles,
  } = useFileSearch(files, setFiles);

  const mediaSources = useMemo(() => plugins?.filter((p) => p.media) ?? [], [plugins]);
  const fileSources = sources ?? [];

  // File search of the submitted query, once the Files tab is open
  useEffect(() => {
    if (tab === 'files' && submittedQuery && !files) startFileSearch({ text: submittedQuery });
  }, [tab, submittedQuery, files, startFileSearch]);

  useSocketEvents(
    useMemo(
      () => ({
        [SocketRequests.PluginSearch]: (r: PluginSearchResponse) =>
          setMediaResults(r.pluginId, r.query, r.results, r.error),
      }),
      [setMediaResults]
    )
  );

  const handleSearch = () => {
    const query = searchQuery.trim();
    if (!isClientReady || !plugins?.length || !query) return;
    for (const plugin of mediaSources) {
      client.sendEvent(SocketRequests.PluginSearch, { pluginId: plugin.id, query });
    }
    startSearch(query, loadingResults(mediaSources.map((p) => p.id))); // resets files: searched again if on Files
  };

  const noSources = (kind: string) =>
    plugins !== null && (
      <Text c="dimmed">
        No {kind} source plugins installed. Put a plugin folder into <code>~/.local/share/Popcorn/plugins/</code> or
        install one in Settings → Plugins (see Docs/Plugins.md).
      </Text>
    );

  return (
    <Flex direction="column" w="100%">
      <Title order={1} mb={8}>
        Search
      </Title>

      <Flex w="100%" align="center">
        <TextInput
          value={searchQuery}
          onChange={(event) => setSearchQuery(event.currentTarget.value)}
          onKeyDown={(event) => event.key === 'Enter' && handleSearch()}
          variant="filled"
          radius="xl"
          leftSectionPointerEvents="none"
          leftSection={<IconSearch size="16px" />}
          placeholder="Search"
          classNames={{ input: 'searchInput' }}
          style={{ flexGrow: 1 }}
          disabled={plugins?.length === 0}
        />
        <Button ml="sm" radius="xl" onClick={handleSearch} disabled={!plugins?.length}>
          Search
        </Button>
      </Flex>

      <Tabs value={tab} onChange={(value) => value && setTab(value as SearchTab)} mt="md">
        <Tabs.List>
          <Tabs.Tab value="media" rightSection={<TabCount results={media} />}>
            Media
          </Tabs.Tab>
          <Tabs.Tab value="files" rightSection={<TabCount results={files?.results} />}>
            Files
          </Tabs.Tab>
        </Tabs.List>

        <Tabs.Panel value="media">
          <Stack mt="md" gap="xl">
            {mediaSources.length === 0 && noSources('media')}
            {mediaSources.map(
              (plugin) =>
                media[plugin.id] && (
                  <ProviderGroup
                    key={`${plugin.id}\n${submittedQuery}`}
                    section="media"
                    plugin={plugin}
                    state={media[plugin.id]}
                    renderItem={(card) => (
                      <PluginSearchCard
                        key={card.id}
                        pluginId={plugin.id}
                        card={card}
                        onOpen={card.hasDetails ? () => navigate(pluginDetailsPath(plugin.id, card.id)) : undefined}
                      />
                    )}
                  />
                )
            )}
          </Stack>
        </Tabs.Panel>
        <Tabs.Panel value="files">
          <Stack mt="md">
            {fileSources.length === 0 && noSources('file')}
            <SourceFilter sources={fileSources} enabled={enabled ?? []} onChange={setEnabled} />
            {files && enabled && enabled.length > 0 && (
              <FileResults section="files" sources={enabled} search={files} onRefresh={refreshFiles} />
            )}
          </Stack>
        </Tabs.Panel>
      </Tabs>
    </Flex>
  );
};

export default SearchPage;
