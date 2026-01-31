import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react-swc';
import { resolve as _resolve } from 'path';
import { viteStaticCopy } from 'vite-plugin-static-copy';

// https://vite.dev/config/
export default defineConfig({
  plugins: [
    react(),
    viteStaticCopy({
      targets: [
        {
          src: 'node_modules/libpgs/dist/libpgs.worker.js',
          dest: 'lib',
        },
      ],
    }),
  ],
  server: {
    host: true, // == 0.0.0.0
    port: 5173, // optional, default shown
  },
  resolve: {
    alias: {
      '@': '/src',
    },
  },
});
