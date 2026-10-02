import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';
import markdown from './plugins/vite-plugin-markdown.js';

// GitHub Pages sirve este repo en https://<user>.github.io/jaravi/, asi que el
// sitio vive bajo /jaravi/. Se puede reapuntar sin tocar codigo:
//   JARAVI_BASE=/ npm run build      (dominio propio o raiz del dominio)
const base = process.env.JARAVI_BASE ?? '/jaravi/';

export default defineConfig({
  base,
  plugins: [react(), markdown()],
  build: {
    // El artefacto se publica desde docs/, que es lo que GitHub Pages sirve.
    // emptyOutDir: false porque ahi conviven el paper (jaravi.pdf/tex) y el
    // propio codigo fuente del sitio.
    outDir: '../',
    emptyOutDir: false,
    assetsDir: 'assets',
    cssCodeSplit: false,
    manifest: true,
    rollupOptions: {
      output: {
        entryFileNames: 'assets/[name].[hash].js',
        chunkFileNames: 'assets/[name].[hash].js',
        assetFileNames: 'assets/[name].[hash][extname]',
      },
    },
  },
});
