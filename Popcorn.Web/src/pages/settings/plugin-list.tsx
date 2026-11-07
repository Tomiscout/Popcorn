import React, { useCallback, useState } from 'react';
import { DragDropContext, Draggable, Droppable, DropResult } from '@hello-pangea/dnd';
import {
  ActionIcon,
  Badge,
  Button,
  FileButton,
  Group,
  Paper,
  Skeleton,
  Stack,
  Switch,
  Text,
  Tooltip,
} from '@mantine/core';
import { notifications } from '@mantine/notifications';
import { IconGripVertical, IconSettings, IconTrash, IconUpload } from '@tabler/icons-react';
import { PluginSetEnabledRequest, PluginSetOrderRequest, PluginUninstallRequest } from '../../model/plugin.contract';
import { PluginIcon } from '../../plugins/provider-group';
import PluginSettingsDialog from './plugin-settings-dialog';
import { usePluginStore } from '../../state/plugin.store';
import { usePluginEntries } from '../../plugins/plugins.hook';
import { SocketRequests } from '../../socket/socket-requests';
import { useWebSocket } from '../../state/websocket-context';
import { resolveServerHost } from '../../utils/url-resolver';

/** Settings section: order (drag; search results follow it), enable/disable, install (zip upload) and uninstall plugins. */
const PluginList: React.FC = () => {
  const { client } = useWebSocket();
  const plugins = usePluginEntries();
  const setPlugins = usePluginStore((s) => s.setPlugins);
  const [installing, setInstalling] = useState(false);
  const [settingsOf, setSettingsOf] = useState<string | null>(null);
  const settingsPlugin = plugins?.find((p) => p.id === settingsOf && p.hasSettings);
  const closeSettings = useCallback(() => setSettingsOf(null), []);

  const onDragEnd = ({ source, destination }: DropResult) => {
    if (!plugins || !destination || destination.index === source.index) return;
    const next = [...plugins];
    const [moved] = next.splice(source.index, 1);
    next.splice(destination.index, 0, moved);
    setPlugins(next); // the server answers with the same list
    client.sendEvent<PluginSetOrderRequest>(SocketRequests.PluginSetOrder, { pluginIds: next.map((p) => p.id) });
  };

  const refresh = () => client.sendEvent(SocketRequests.PluginList);

  const setEnabled = (pluginId: string, enabled: boolean) =>
    client.sendEvent<PluginSetEnabledRequest>(SocketRequests.PluginSetEnabled, { pluginId, enabled });

  const uninstall = (pluginId: string, name: string) => {
    if (!window.confirm(`Uninstall plugin ${name}?`)) return;
    client.sendEvent<PluginUninstallRequest>(SocketRequests.PluginUninstall, { pluginId });
  };

  const install = async (file: File | null) => {
    if (!file) return;
    setInstalling(true);
    try {
      const body = new FormData();
      body.append('file', file);
      const response = await fetch(`${window.location.protocol}//${resolveServerHost()}/api/plugins`, {
        method: 'POST',
        body,
      });
      if (!response.ok) throw new Error((await response.text()) || response.statusText);
      const { id } = (await response.json()) as { id: string };
      notifications.show({ color: 'green', message: `Plugin ${id} installed` });
    } catch (error) {
      notifications.show({
        color: 'red',
        title: 'Plugin install failed',
        message: String((error as Error).message ?? error),
      });
    } finally {
      setInstalling(false);
      refresh();
    }
  };

  return (
    <Stack gap="xs">
      {plugins === null ? (
        <Skeleton height={60} radius="md" />
      ) : plugins.length === 0 ? (
        <Text size="sm" c="dimmed">
          No plugins installed.
        </Text>
      ) : (
        <DragDropContext onDragEnd={onDragEnd}>
          <Droppable droppableId="plugins">
            {(dropProvided) => (
              <div ref={dropProvided.innerRef} {...dropProvided.droppableProps}>
                {plugins.map((plugin, index) => (
                  <Draggable key={plugin.id} draggableId={plugin.id} index={index}>
                    {(dragProvided, snapshot) => (
                      // Spacing via margin, not Stack gap: the library measures item boxes while dragging.
                      <Paper
                        ref={dragProvided.innerRef}
                        {...dragProvided.draggableProps}
                        withBorder
                        p="sm"
                        mb="xs"
                        radius="md"
                        shadow={snapshot.isDragging ? 'md' : undefined}
                      >
                        <Group justify="space-between" wrap="nowrap">
                          <Group gap="sm" wrap="nowrap" style={{ minWidth: 0 }}>
                            <div
                              {...dragProvided.dragHandleProps}
                              aria-label="Drag to reorder"
                              style={{ display: 'flex', cursor: 'grab', color: 'var(--mantine-color-dimmed)' }}
                            >
                              <IconGripVertical size={18} stroke={1.5} />
                            </div>
                            <PluginIcon plugin={plugin} />
                            <Stack gap={2} style={{ minWidth: 0 }}>
                              <Group gap="xs">
                                <Text fw={500}>{plugin.name}</Text>
                                {plugin.version && (
                                  <Text size="xs" c="dimmed">
                                    {plugin.version}
                                  </Text>
                                )}
                                <Badge size="xs" variant="light" color={plugin.bundled ? 'gray' : 'blue'}>
                                  {plugin.bundled ? 'Built-in' : 'Installed'}
                                </Badge>
                                {plugin.imageKinds?.map((kind) => (
                                  <Badge key={kind} size="xs" variant="light" color="grape">
                                    {kind === 'Poster' ? 'Posters' : 'Backdrops'}
                                  </Badge>
                                ))}
                                {plugin.error && (
                                  <Badge size="xs" variant="light" color="red">
                                    Error
                                  </Badge>
                                )}
                              </Group>
                              {plugin.error && (
                                <Text size="xs" c="red" style={{ overflowWrap: 'anywhere' }}>
                                  {plugin.error}
                                </Text>
                              )}
                            </Stack>
                          </Group>
                          <Group gap="xs" wrap="nowrap">
                            {plugin.hasSettings && (
                              <Tooltip label="Settings">
                                <ActionIcon
                                  variant="subtle"
                                  color="gray"
                                  aria-label={`${plugin.name} settings`}
                                  onClick={() => setSettingsOf(plugin.id)}
                                >
                                  <IconSettings size={18} />
                                </ActionIcon>
                              </Tooltip>
                            )}
                            <Tooltip label={plugin.enabled ? 'Unload plugin' : 'Load plugin'}>
                              <Switch
                                checked={plugin.enabled}
                                disabled={!plugin.enabled && !!plugin.error} // unreadable plugin.json
                                onChange={(e) => setEnabled(plugin.id, e.currentTarget.checked)}
                              />
                            </Tooltip>
                            {!plugin.bundled && (
                              <Tooltip label="Uninstall">
                                <ActionIcon
                                  variant="subtle"
                                  color="red"
                                  onClick={() => uninstall(plugin.id, plugin.name)}
                                >
                                  <IconTrash size={18} />
                                </ActionIcon>
                              </Tooltip>
                            )}
                          </Group>
                        </Group>
                      </Paper>
                    )}
                  </Draggable>
                ))}
                {dropProvided.placeholder}
              </div>
            )}
          </Droppable>
        </DragDropContext>
      )}
      <Group>
        <FileButton onChange={install} accept=".zip,application/zip">
          {(props) => (
            <Button {...props} leftSection={<IconUpload size={16} />} loading={installing} variant="light">
              Install plugin (.zip)
            </Button>
          )}
        </FileButton>
      </Group>
      {settingsPlugin && <PluginSettingsDialog plugin={settingsPlugin} onClose={closeSettings} />}
    </Stack>
  );
};

export default PluginList;
