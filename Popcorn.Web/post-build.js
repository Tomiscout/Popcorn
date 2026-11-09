// After `vite build`: copy the bundle to Popcorn.Server/web, which the server serves (and its build packs).
import { promises as fs } from 'fs';
import path from 'path';
import { fileURLToPath } from 'url';

const __dirname = path.dirname(fileURLToPath(import.meta.url));
const distDir = path.join(__dirname, 'dist');
const serverDir = path.join(__dirname, '../Popcorn.Server/web');

async function main() {
  // Replace, not merge: file names are content-hashed, old bundles would pile up.
  await fs.rm(serverDir, { recursive: true, force: true });
  await fs.cp(distDir, serverDir, { recursive: true });
  console.log('Copied bundle to', serverDir);
}

main().catch((error) => {
  console.error('Post-build failed:', error);
  process.exit(1);
});
