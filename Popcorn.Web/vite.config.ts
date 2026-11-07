import { defineConfig, Plugin, UserConfig } from 'vite';
import react from '@vitejs/plugin-react-swc';
import { resolve } from 'path';
import { createRequire } from 'module';
import { viteStaticCopy } from 'vite-plugin-static-copy';
import { SHARED_MODULES } from './plugin-sdk/shared.js';

/**
 * Shares react / react-dom / @mantine/* with plugin UI modules (Docs/Plugins.md): every shared module gets a
 * virtual re-export entry (build: `shared/<name>-<hash>.js`, same chunks the app itself uses, so one instance), and
 * index.html gets an import map pointing the bare specifiers at them. Plugins are built with these external.
 */
function pluginSharedModules(): Plugin {
  const prefix = 'popcorn-shared:';
  const require = createRequire(import.meta.url);
  const entryName = (id: string) => `shared/${id.replace(/^@/, '').replace(/\//g, '-')}`;

  return {
    name: 'popcorn-plugin-shared-modules',
    config(_, { command }) {
      if (command !== 'build') return;
      return {
        build: {
          rollupOptions: {
            input: {
              index: resolve(__dirname, 'index.html'),
              ...Object.fromEntries(SHARED_MODULES.map((m) => [entryName(m.id), prefix + m.id])),
            },
            // Vite drops entry exports in app builds; the shared entries exist only for their exports
            preserveEntrySignatures: 'exports-only',
            output: {
              entryFileNames: (chunk) =>
                chunk.name.startsWith('shared/') ? '[name]-[hash].js' : 'assets/[name]-[hash].js',
            },
          },
        },
      };
    },
    resolveId(id) {
      return id.startsWith(prefix) ? '\0' + id : undefined;
    },
    load(id) {
      if (!id.startsWith('\0' + prefix)) return;
      const module = SHARED_MODULES.find((m) => '\0' + prefix + m.id === id)!;
      const spec = JSON.stringify(module.id);
      if (!module.cjs) return `export * from ${spec};`;
      // CommonJS (react): `export *` cannot see its exports, so list them
      const names = Object.keys(require(module.id)).filter((n) => n !== 'default' && /^[A-Za-z_$][\w$]*$/.test(n));
      return `import * as ns from ${spec};
const m = ns.default ?? ns;
export default m;
export const { ${names.join(', ')} } = m;`;
    },
    transformIndexHtml: {
      order: 'post',
      handler(_, ctx) {
        const imports: Record<string, string> = {};
        for (const m of SHARED_MODULES) {
          if (ctx.bundle) {
            const chunk = Object.values(ctx.bundle).find(
              (c) => c.type === 'chunk' && c.isEntry && c.name === entryName(m.id)
            );
            if (!chunk) throw new Error(`shared module entry for ${m.id} missing from the bundle`);
            imports[m.id] = '/' + chunk.fileName;
          } else {
            imports[m.id] = `/@id/__x00__${prefix}${m.id}`; // dev server URL of the virtual module
          }
        }
        return [
          { tag: 'script', attrs: { type: 'importmap' }, children: JSON.stringify({ imports }), injectTo: 'head-prepend' },
        ];
      },
    },
  };
}

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
      pluginSharedModules(),
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
