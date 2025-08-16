import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react-swc';
import { fileURLToPath, URL } from 'url';
import { resolve as _resolve } from 'path';

// emulate __dirname
const __dirname = fileURLToPath(new URL('.', import.meta.url));

// https://vite.dev/config/
export default defineConfig({
  plugins: [react()],
  resolve: {
    alias: {
      '@': '/src',
      '@tabler/icons-react': '@tabler/icons-react/dist/esm/icons/index.mjs', // Solved all tabler icons loading in dev mode https://github.com/tabler/tabler-icons/issues/1344
    },
  },
});
