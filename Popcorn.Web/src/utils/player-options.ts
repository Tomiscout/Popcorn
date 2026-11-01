import { PlayerOption } from '../model/settings.contract';

const fileName = (path: string) => path.split(/[\\/]/).pop() || path;

export function playerDisplayName(player: PlayerOption, defaultPlayerName?: string): string {
  switch (player.type) {
    case 'Popcorn':
      return 'Popcorn Player';
    case 'Default':
      if (defaultPlayerName === undefined) return 'Default video player';
      return defaultPlayerName ? `Default video player (${defaultPlayerName})` : 'Default video player (none detected)';
    case 'Program':
      return player.name || fileName(player.path ?? '');
    case 'Script':
      return player.name || 'Custom script';
  }
}
