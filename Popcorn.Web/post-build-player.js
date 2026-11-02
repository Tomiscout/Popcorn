// After `vite build --mode player`: dist-player/player.html -> index.html + index-chrome.html (<base> for the
// http://app.local/ scheme the native players serve the bundle from), then copy the bundle next to the Linux player.
import { promises as fs } from 'fs';
import path from 'path';
import { fileURLToPath } from 'url';

const __dirname = path.dirname(fileURLToPath(import.meta.url));
const distDir = path.join(__dirname, 'dist-player');
const linuxDir = path.join(__dirname, '../Popcorn.Player.Linux/web-player');

async function main() {
  const html = await fs.readFile(path.join(distDir, 'player.html'), 'utf8');
  await fs.writeFile(path.join(distDir, 'index.html'), html, 'utf8');
  await fs.writeFile(
    path.join(distDir, 'index-chrome.html'),
    html.replace('<head>', '<head><base href="http://app.local/">'),
    'utf8'
  );
  await fs.rm(path.join(distDir, 'player.html'));
  console.log('Wrote index.html and index-chrome.html (<base> http://app.local/)');

  // Replace, not merge: the old overlay bundle (Popcorn.Player.Web) left other files here.
  await fs.rm(linuxDir, { recursive: true, force: true });
  await fs.cp(distDir, linuxDir, { recursive: true });
  console.log('Copied bundle to', linuxDir);
}

main().catch((error) => {
  console.error('Player post-build failed:', error);
  process.exit(1);
});
