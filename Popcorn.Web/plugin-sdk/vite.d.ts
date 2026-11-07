// Structural (no `vite` import): this package is linked from Popcorn.Web, whose vite types would clash with the
// plugin's own vite install.
export interface PopcornPluginConfig {
  define: Record<string, string>;
  build: {
    outDir: string;
    emptyOutDir: boolean;
    lib: { entry: string; formats: ['es']; fileName: () => string };
    rollupOptions: { external: (id: string) => boolean };
  };
}

export declare function popcornPluginConfig(options?: { entry?: string; outDir?: string }): PopcornPluginConfig;
