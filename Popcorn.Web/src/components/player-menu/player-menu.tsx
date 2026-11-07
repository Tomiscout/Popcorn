import React, { useEffect, useState } from 'react';
import { Menu, Text } from '@mantine/core';
import { IconBrowser, IconDeviceDesktop, IconFileCode, IconPlayerPlay, IconTerminal2 } from '@tabler/icons-react';
import { PlayerOption, Settings } from '../../model/settings.contract';
import { SocketRequests } from '../../socket/socket-requests';
import { useWebSocket } from '../../state/websocket-context';
import { isDesktopApp } from '../../utils/platform';
import { playerDisplayName } from '../../utils/player-options';

/** Player list from settings plus the detected OS default player name, kept fresh while mounted. */
export function usePlayerOptions() {
  const { client, isClientReady } = useWebSocket();
  const [players, setPlayers] = useState<PlayerOption[]>([]);
  const [defaultPlayerName, setDefaultPlayerName] = useState<string>();

  useEffect(() => {
    if (!isClientReady) return;

    const unsubs = [
      client.onEvent<Settings>(SocketRequests.GetSettings, (s) => setPlayers(s.players ?? [])),
      client.onEvent<string>(SocketRequests.DefaultPlayerName, setDefaultPlayerName),
    ];
    client.sendEvent(SocketRequests.GetSettings);
    client.sendEvent(SocketRequests.DefaultPlayerName);

    return () => unsubs.forEach((unsub) => unsub());
  }, [isClientReady]);

  return { players, defaultPlayerName };
}

/** Pseudo player id for the in-browser web player (not part of the server list). */
export const WEB_PLAYER_ID = 'web';

const icons: Record<PlayerOption['type'], React.ReactNode> = {
  Popcorn: <IconPlayerPlay size={16} />,
  Default: <IconDeviceDesktop size={16} />,
  Program: <IconFileCode size={16} />,
  Script: <IconTerminal2 size={16} />,
};

interface PlayerMenuItemsProps {
  players: PlayerOption[];
  defaultPlayerName?: string;
  /** Called with a server player id, or {@link WEB_PLAYER_ID}. */
  onSelect: (playerId: string) => void;
}

/** `Menu.Item`s for every player; render inside a Mantine `Menu.Dropdown`. */
export const PlayerMenuItems: React.FC<PlayerMenuItemsProps> = ({ players, defaultPlayerName, onSelect }) => (
  <>
    {!isDesktopApp && (
      <>
        <Menu.Item leftSection={<IconBrowser size={16} />} onClick={() => onSelect(WEB_PLAYER_ID)}>
          Web player
        </Menu.Item>
        <Menu.Divider />
        <Menu.Label>On server machine</Menu.Label>
      </>
    )}
    {isDesktopApp && <Menu.Label>Play with</Menu.Label>}
    {players.map((player) => (
      <Menu.Item key={player.id} leftSection={icons[player.type]} onClick={() => onSelect(player.id)}>
        <Text size="sm" c={player.enabled ? undefined : 'dimmed'} truncate maw={320}>
          {playerDisplayName(player, defaultPlayerName)}
        </Text>
      </Menu.Item>
    ))}
  </>
);
