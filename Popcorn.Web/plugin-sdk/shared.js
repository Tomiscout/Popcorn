// Modules the Popcorn web app shares with plugin UI modules through an import map (one React / Mantine instance,
// so hooks, context and the Mantine theme work inside plugin components). Plugins must mark these external
// (vite.js does); the host build emits one entry per module (Popcorn.Web/vite.config.ts).
// `cjs`: CommonJS package, its named exports are enumerated at build time.
/** @type {import('./shared').SharedModule[]} */
export const SHARED_MODULES = [
  { id: 'react', cjs: true },
  { id: 'react/jsx-runtime', cjs: true },
  { id: 'react-dom', cjs: true },
  { id: '@mantine/core', cjs: false },
  { id: '@mantine/hooks', cjs: false },
];
