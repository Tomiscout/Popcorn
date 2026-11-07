import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react-swc';
import { popcornPluginConfig } from '@popcorn/plugin-sdk/vite';

// dist/index.js; Popcorn.Plugin.Tmdb.csproj copies dist/ into the plugin's web/ folder
export default defineConfig({
  plugins: [react()],
  ...popcornPluginConfig(),
});
