// After `vite build --mode player`: dist-player/player.html -> index.html, the page both native players load as
// http://app.local/index.html (their builds link dist-player/ in as web-player/).
import { promises as fs } from 'fs';
import path from 'path';
import { fileURLToPath } from 'url';

const distDir = path.join(path.dirname(fileURLToPath(import.meta.url)), 'dist-player');

fs.rename(path.join(distDir, 'player.html'), path.join(distDir, 'index.html')).catch((error) => {
  console.error('Player post-build failed:', error);
  process.exit(1);
});
