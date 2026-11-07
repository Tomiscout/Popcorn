import {
  ActionIcon,
  Avatar,
  Badge,
  Button,
  Collapse,
  Group,
  Loader,
  Stack,
  Text,
  Tooltip,
  UnstyledButton,
} from '@mantine/core';
import { IconChevronDown, IconRefresh } from '@tabler/icons-react';
import { ReactNode, useState } from 'react';
import { create } from 'zustand';
import { persist } from 'zustand/middleware';
import { PluginInfo } from '../model/plugin.contract';
import { resolveServerHost } from '../utils/url-resolver';
import type { ProviderResults } from './file-sources';

const PREVIEW_COUNT = 10;

/** What a result group shows of its provider: a plugin, or one file source of a plugin (`FileSource`). */
export type Provider = Pick<PluginInfo, 'id' | 'name' | 'iconUrl'>;

/** Collapsed provider groups, remembered across pages and restarts. Keys: `<section>:<provider id>`. */
const useCollapsedProviders = create<{ collapsed: Record<string, boolean>; toggle: (key: string) => void }>()(
  persist(
    (set) => ({
      collapsed: {},
      toggle: (key) => set((s) => ({ collapsed: { ...s.collapsed, [key]: !s.collapsed[key] } })),
    }),
    { name: 'popcorn-collapsed-providers' }
  )
);

export function PluginIcon({ plugin, size = 24 }: { plugin: Provider; size?: number }) {
  // Absolute: in dev the page comes from the Vite server, plugin files from Popcorn.Server
  const src = plugin.iconUrl && `${window.location.protocol}//${resolveServerHost()}${plugin.iconUrl}`;
  return (
    <Avatar src={src} size={size} radius="sm" color="gray" alt="">
      {plugin.name.slice(0, 1).toUpperCase()}
    </Avatar>
  );
}

/** Searches the provider again. */
export function RefreshButton({
  plugin,
  loading,
  onRefresh,
}: {
  plugin: Provider;
  loading: boolean;
  onRefresh: () => void;
}) {
  return (
    <Tooltip label={`Search ${plugin.name} again`}>
      <ActionIcon variant="subtle" color="gray" size="sm" onClick={onRefresh} disabled={loading} aria-label="Refresh">
        <IconRefresh size={16} />
      </ActionIcon>
    </Tooltip>
  );
}

/**
 * Results of one provider: a header (icon, name, count, optional refresh) that collapses the group, the first 10
 * items and a Show all / Show less toggle for the rest.
 */
export function ProviderGroup<T>({
  section,
  plugin,
  state,
  renderItem,
  onRefresh,
}: {
  /** Separates the collapsed state of the same plugin in different lists (e.g. "media", "files"). */
  section: string;
  plugin: Provider;
  state: ProviderResults<T>;
  renderItem: (item: T, index: number) => ReactNode;
  onRefresh?: () => void;
}) {
  const key = `${section}:${plugin.id}`;
  const collapsed = useCollapsedProviders((s) => !!s.collapsed[key]);
  const toggle = useCollapsedProviders((s) => s.toggle);
  const [expanded, setExpanded] = useState(false);
  const { loading, results, error } = state;
  const visible = expanded ? results : results.slice(0, PREVIEW_COUNT);

  return (
    <Stack gap="sm">
      <Group gap="xs">
        <UnstyledButton onClick={() => toggle(key)} aria-expanded={!collapsed}>
          <Group gap="sm">
            <IconChevronDown
              size={18}
              style={{ transform: collapsed ? 'rotate(-90deg)' : undefined, transition: 'transform 150ms ease' }}
            />
            <PluginIcon plugin={plugin} />
            <Text fw={700} size="lg">
              {plugin.name}
            </Text>
            {loading ? (
              <Loader size="xs" />
            ) : error ? (
              <Badge color="red" variant="light">
                Failed
              </Badge>
            ) : (
              <Badge color="gray" variant="light">
                {results.length}
              </Badge>
            )}
          </Group>
        </UnstyledButton>
        {onRefresh && <RefreshButton plugin={plugin} loading={loading} onRefresh={onRefresh} />}
      </Group>

      <Collapse in={!collapsed}>
        <Stack gap="sm">
          {error && <Text c="red">Search failed: {error}</Text>}
          {!loading && !error && results.length === 0 && <Text c="dimmed">No results</Text>}
          {visible.map(renderItem)}
          {results.length > PREVIEW_COUNT && (
            <Button variant="subtle" onClick={() => setExpanded((e) => !e)} style={{ alignSelf: 'center' }}>
              {expanded ? 'Show less' : `Show all ${results.length}`}
            </Button>
          )}
        </Stack>
      </Collapse>
    </Stack>
  );
}
