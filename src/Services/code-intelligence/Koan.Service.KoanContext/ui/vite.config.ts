import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';
import path from 'path';

// https://vitejs.dev/config/
export default defineConfig({
  plugins: [react()],

  // Static assets directory (copied to outDir during build)
  publicDir: 'public',

  build: {
    // Output to Service/wwwroot/ (ASP.NET Core static files directory)
    outDir: '../Service/wwwroot',

    // Empty wwwroot/ before each build (safe because it's 100% owned by Vite)
    emptyOutDir: true,

    // Enable source maps for debugging
    sourcemap: true,

    // Don't minify in development for faster builds
    minify: process.env.NODE_ENV === 'production',

    // Rollup options for clean asset naming
    rollupOptions: {
      output: {
        assetFileNames: 'assets/[name]-[hash][extname]',
        chunkFileNames: 'assets/[name]-[hash].js',
        entryFileNames: 'assets/[name]-[hash].js',
      },
    },
  },

  // Path aliases for cleaner imports
  resolve: {
    alias: {
      '@': path.resolve(__dirname, './src'),
    },
  },

  // Development server configuration
  server: {
    port: 5173,
    strictPort: false,

    // Proxy API requests to ASP.NET Core backend during development
    // (Not needed for single-server architecture, but kept for reference)
    // proxy: {
    //   '/api': 'http://localhost:27500',
    //   '/mcp': 'http://localhost:27500',
    // },
  },
});
