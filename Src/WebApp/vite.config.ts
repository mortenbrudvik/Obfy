import { defineConfig } from 'vitest/config'
import react from '@vitejs/plugin-react'
import { contentSecurityPolicy } from './build/contentSecurityPolicy.ts'

export default defineConfig({
  plugins: [react(), contentSecurityPolicy()],
  server: {
    port: 5250,
    // Fail loudly when the port is taken instead of moving to another one: the app loads exactly this address.
    strictPort: true,
  },
  build: {
    outDir: 'dist',
    emptyOutDir: true,
    // No source maps in production: nothing but the page's own files ships next to the app.
    sourcemap: false,
    // Every asset is a file of its own, so the CSP never has to allow data: URIs.
    assetsInlineLimit: 0,
    rollupOptions: {
      output: {
        entryFileNames: 'index.js',
        chunkFileNames: '[name]-[hash].js',
      },
    },
  },
  test: {
    environment: 'jsdom',
    globals: true,
    passWithNoTests: true,
    setupFiles: ['./src/test/setup.ts'],
    // A test may chain several asynchronous reads; five seconds is tight for a busy machine or a small CI runner.
    testTimeout: 15000,
  },
})
