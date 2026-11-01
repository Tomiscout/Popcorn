import React, { useState } from 'react';
import { DragDropContext, Draggable, Droppable, DropResult } from '@hello-pangea/dnd';
import {
  ActionIcon,
  Badge,
  Button,
  Code,
  Group,
  HoverCard,
  Modal,
  Paper,
  Stack,
  Switch,
  Text,
  TextInput,
  Tooltip,
} from '@mantine/core';
import { IconGripVertical, IconHelpCircle, IconPencil, IconPlus, IconTerminal2, IconTrash } from '@tabler/icons-react';
import { PLAYER_ARGS_PLACEHOLDER, PlayerOption } from '../../model/settings.contract';
import { playerDisplayName } from '../../utils/player-options';

interface PlayerListProps {
  players: PlayerOption[];
  /** OS default video player name, `undefined` while loading, `''` if none is associated. */
  defaultPlayerName?: string;
  onChange: (players: PlayerOption[]) => void;
  onAddProgram: () => void;
}

interface ScriptDraft {
  /** Id of the script being edited, `null` when adding a new one. */
  id: string | null;
  name: string;
  command: string;
}

const typeLabels: Record<PlayerOption['type'], string> = {
  Popcorn: 'Built-in',
  Default: 'System',
  Program: 'Program',
  Script: 'Script',
};

// crypto.randomUUID needs a secure context, which LAN access over plain http isn't.
export const newPlayerId = () => `${Date.now().toString(36)}-${Math.random().toString(36).slice(2, 10)}`;

const PlayerList: React.FC<PlayerListProps> = ({ players, defaultPlayerName, onChange, onAddProgram }) => {
  const [scriptDraft, setScriptDraft] = useState<ScriptDraft | null>(null);

  const onDragEnd = ({ source, destination }: DropResult) => {
    if (!destination || destination.index === source.index) return;
    const next = [...players];
    const [moved] = next.splice(source.index, 1);
    next.splice(destination.index, 0, moved);
    onChange(next);
  };

  const update = (id: string, patch: Partial<PlayerOption>) =>
    onChange(players.map((p) => (p.id === id ? { ...p, ...patch } : p)));

  const remove = (id: string) => onChange(players.filter((p) => p.id !== id));

  const saveScript = () => {
    if (!scriptDraft || !scriptDraft.command.trim()) return;
    const name = scriptDraft.name.trim() || null;
    const command = scriptDraft.command.trim();

    if (scriptDraft.id) {
      update(scriptDraft.id, { name, command });
    } else {
      onChange([...players, { id: newPlayerId(), type: 'Script', name, command, enabled: true }]);
    }
    setScriptDraft(null);
  };

  const noneEnabled = !players.some((p) => p.enabled);

  return (
    <Stack gap="xs">
      <DragDropContext onDragEnd={onDragEnd}>
        <Droppable droppableId="players">
          {(dropProvided) => (
            <div ref={dropProvided.innerRef} {...dropProvided.droppableProps}>
              {players.map((player, index) => {
                const isBuiltIn = player.type === 'Popcorn' || player.type === 'Default';
                const detail =
                  player.type === 'Program' ? player.path : player.type === 'Script' ? player.command : null;

                return (
                  <Draggable key={player.id} draggableId={player.id} index={index}>
                    {(dragProvided, snapshot) => (
                      // Spacing via margin, not Stack gap: the library measures item boxes while dragging.
                      <Paper
                        ref={dragProvided.innerRef}
                        {...dragProvided.draggableProps}
                        withBorder
                        p="xs"
                        mb="xs"
                        radius="md"
                        shadow={snapshot.isDragging ? 'md' : undefined}
                      >
                        <Group wrap="nowrap" gap="sm">
                          <div
                            {...dragProvided.dragHandleProps}
                            aria-label="Drag to reorder"
                            style={{ display: 'flex', cursor: 'grab', color: 'var(--mantine-color-dimmed)' }}
                          >
                            <IconGripVertical size={18} stroke={1.5} />
                          </div>

                          <Stack gap={2} style={{ flex: 1, minWidth: 0, opacity: player.enabled ? 1 : 0.55 }}>
                            <Group gap="xs" wrap="nowrap">
                              <Text fw={500} truncate>
                                {playerDisplayName(player, defaultPlayerName)}
                              </Text>
                              <Badge size="xs" variant="light" color={isBuiltIn ? 'gray' : 'blue'}>
                                {typeLabels[player.type]}
                              </Badge>
                            </Group>
                            {detail && (
                              <Text size="xs" c="dimmed" ff="monospace" truncate title={detail}>
                                {detail}
                              </Text>
                            )}
                          </Stack>

                          {player.type === 'Script' && (
                            <Tooltip label="Edit">
                              <ActionIcon
                                variant="subtle"
                                onClick={() =>
                                  setScriptDraft({
                                    id: player.id,
                                    name: player.name ?? '',
                                    command: player.command ?? '',
                                  })
                                }
                                aria-label="Edit script"
                              >
                                <IconPencil size={18} />
                              </ActionIcon>
                            </Tooltip>
                          )}
                          {!isBuiltIn && (
                            <Tooltip label="Remove">
                              <ActionIcon
                                variant="subtle"
                                color="red"
                                onClick={() => remove(player.id)}
                                aria-label="Remove player"
                              >
                                <IconTrash size={18} />
                              </ActionIcon>
                            </Tooltip>
                          )}
                          <Switch
                            checked={player.enabled}
                            onChange={(e) => update(player.id, { enabled: e.currentTarget.checked })}
                            aria-label="Enabled"
                          />
                        </Group>
                      </Paper>
                    )}
                  </Draggable>
                );
              })}
              {dropProvided.placeholder}
            </div>
          )}
        </Droppable>
      </DragDropContext>

      {noneEnabled && (
        <Text size="sm" c="red">
          No player is enabled, files cannot be opened.
        </Text>
      )}

      <Group gap="xs">
        <Button variant="light" leftSection={<IconPlus size={16} />} onClick={onAddProgram}>
          Add program
        </Button>
        <Button
          variant="light"
          leftSection={<IconTerminal2 size={16} />}
          onClick={() => setScriptDraft({ id: null, name: '', command: '' })}
        >
          Add custom script
        </Button>
      </Group>

      <Modal
        opened={scriptDraft !== null}
        onClose={() => setScriptDraft(null)}
        title={scriptDraft?.id ? 'Edit custom script' : 'Add custom script'}
        size="lg"
      >
        {scriptDraft && (
          <Stack gap="sm">
            <TextInput
              label="Name"
              placeholder="e.g. mpv (flatpak)"
              value={scriptDraft.name}
              onChange={(e) => setScriptDraft({ ...scriptDraft, name: e.currentTarget.value })}
            />
            <TextInput
              label={
                <Group gap={4} component="span">
                  Command
                  <HoverCard width={420} shadow="md" withArrow openDelay={100} position="top">
                    <HoverCard.Target>
                      <IconHelpCircle
                        size={16}
                        stroke={1.5}
                        aria-label="Command help"
                        style={{ cursor: 'help', color: 'var(--mantine-color-dimmed)' }}
                      />
                    </HoverCard.Target>
                    <HoverCard.Dropdown>
                      <Stack gap={6}>
                        <Text size="sm">
                          Runs through the system shell (<Code>sh -c</Code> on Linux, <Code>cmd /c</Code> on Windows).
                        </Text>
                        <Text size="sm">
                          <Code>{PLAYER_ARGS_PLACEHOLDER}</Code> is replaced with the quoted path of the video file, so
                          don't add quotes around it. If it is left out, the path is appended at the end.
                        </Text>
                        <Text size="sm">Quote program paths that contain spaces.</Text>
                        <Text size="sm">Examples:</Text>
                        <Code block>
                          {`mpv --fs ${PLAYER_ARGS_PLACEHOLDER}
flatpak run org.videolan.VLC --fullscreen ${PLAYER_ARGS_PLACEHOLDER}
"C:\\Program Files\\MPC-HC\\mpc-hc64.exe" ${PLAYER_ARGS_PLACEHOLDER} /fullscreen`}
                        </Code>
                      </Stack>
                    </HoverCard.Dropdown>
                  </HoverCard>
                </Group>
              }
              placeholder={`/path/to/program --fullscreen ${PLAYER_ARGS_PLACEHOLDER}`}
              ff="monospace"
              value={scriptDraft.command}
              onChange={(e) => setScriptDraft({ ...scriptDraft, command: e.currentTarget.value })}
              onKeyDown={(e) => e.key === 'Enter' && saveScript()}
              data-autofocus
              description={
                <>
                  Example: <Code>mpv --fs {PLAYER_ARGS_PLACEHOLDER}</Code>
                </>
              }
            />
            <Group justify="flex-end" gap="xs" mt="xs">
              <Button variant="default" onClick={() => setScriptDraft(null)}>
                Cancel
              </Button>
              <Button onClick={saveScript} disabled={!scriptDraft.command.trim()}>
                {scriptDraft.id ? 'Save' : 'Add'}
              </Button>
            </Group>
          </Stack>
        )}
      </Modal>
    </Stack>
  );
};

export default PlayerList;
