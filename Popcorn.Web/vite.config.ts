import { defineConfig, UserConfig } from 'vite';
import react from '@vitejs/plugin-react-swc';
import { resolve } from 'path';
import { viteStaticCopy } from 'vite-plugin-static-copy';

const shared: UserConfig = {
  server: {
    host: true, // == 0.0.0.0
    port: 5173, // optional, default shown
  },
  resolve: {
    alias: {
      '@': '/src',
    },
  },
};

// https://vite.dev/config/
export default defineConfig(({ mode }) => {
  // `vite build --mode player`: the native players' overlay (player.html -> dist-player/), loaded from disk by
  // Popcorn.Player.Linux (CEF, http://app.local/) and Popcorn.Player.Windows (WebView2), so asset URLs are relative.
  if (mode === 'player') {
    return {
      ...shared,
      plugins: [react()],
      base: './',
      publicDir: false, // public/ is the main UI's (images, libpgs worker for browser subtitles)
      build: {
        outDir: 'dist-player',
        emptyOutDir: true,
        rollupOptions: { input: resolve(__dirname, 'player.html') },
      },
    };
  }

  return {
    ...shared,
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
  };
});
