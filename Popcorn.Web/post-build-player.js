// After `vite build --mode player`: dist-player/player.html -> index.html (the page both native players load as
// http://app.local/index.html), then copy the bundle next to the Linux player.
import { promises as fs } from 'fs';
import path from 'path';
import { fileURLToPath } from 'url';

const __dirname = path.dirname(fileURLToPath(import.meta.url));
const distDir = path.join(__dirname, 'dist-player');
const linuxDir = path.join(__dirname, '../Popcorn.Player.Linux/web-player');

async function main() {
  await fs.rename(path.join(distDir, 'player.html'), path.join(distDir, 'index.html'));
  console.log('Renamed player.html to index.html');

  // Replace, not merge: file names are content-hashed, old bundles would pile up.
  await fs.rm(linuxDir, { recursive: true, force: true });
  await fs.cp(distDir, linuxDir, { recursive: true });
  console.log('Copied bundle to', linuxDir);
}

main().catch((error) => {
  console.error('Player post-build failed:', error);
  process.exit(1);
});
