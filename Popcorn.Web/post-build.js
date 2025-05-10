import { promises as fs } from 'fs';
import path from 'path';
import { fileURLToPath } from 'url';

const indexPath = './dist/index.html';
const linuxPath = './dist/index-linux.html';

async function patchAndCopy() {
  try {
    // Step 1: Read the original index.html
    let data = await fs.readFile(indexPath, 'utf8');

    // Step 2: Patch index.html by adding <base> tag
    let patchedData = data.replace('<head>', '<head><base href="http://app.local/">');

    // Step 3: Write the patched content back to index.html
    await fs.writeFile(indexPath, patchedData, 'utf8');
    console.log('Patched index.html with <base> http://app.local/');

    // Step 4: Copy patched index.html to index-linux.html
    await fs.writeFile(linuxPath, patchedData, 'utf8');
    console.log('Copied to index-linux.html');

    // Step 5: Replace http://app.local with app.local:// in index-linux.html
    let linuxData = await fs.readFile(linuxPath, 'utf8');
    let finalData = linuxData.replace(/http:\/\/app\.local/g, 'app.local:/');

    // Step 6: Write the modified data to index-linux.html
    await fs.writeFile(linuxPath, finalData, 'utf8');
    console.log('Replaced http://app.local with app.local:// in index-linux.html');
  } catch (error) {
    console.error('Error during file operations:', error);
  }
}

async function copyBundle() {
  const __filename = fileURLToPath(import.meta.url);
  const __dirname = path.dirname(__filename);

  const srcDir = path.join(__dirname, 'dist');
  const destDir = path.join(__dirname, '../Popcorn.Linux/web');

  await fs.cp(srcDir, destDir, { recursive: true });
}

patchAndCopy();
copyBundle();
