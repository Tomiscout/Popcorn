import { SHARED_MODULES } from './shared.js';

/**
 * Vite config for a plugin UI module: an ES library `dist/index.js` with the shared modules (react, react-dom,
 * @mantine/*) left as bare imports, resolved by the Popcorn app's import map at runtime.
 * Use together with @vitejs/plugin-react(-swc).
 */
export function popcornPluginConfig({ entry = 'src/index.tsx', outDir = 'dist' } = {}) {
  const shared = SHARED_MODULES.map((m) => m.id);
  return {
    define: { 'process.env.NODE_ENV': JSON.stringify('production') },
    build: {
      outDir,
      emptyOutDir: true,
      lib: { entry, formats: ['es'], fileName: () => 'index.js' },
      rollupOptions: {
        external: (id) => shared.some((s) => id === s || id.startsWith(`${s}/`)),
      },
    },
  };
}
