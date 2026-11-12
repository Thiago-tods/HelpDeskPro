import { defineConfig } from 'vitest/config';
import react from '@vitejs/plugin-react';
import tailwindcss from '@tailwindcss/vite';
import path from 'node:path';

const apiTarget = process.env.API_URL ?? 'http://localhost:5080';
const port = Number(process.env.VITE_PORT ?? 5173);

// A API é acessada pelo mesmo origin (proxy): o cookie de refresh fica same-origin.
// Vale tanto para `vite` (desenvolvimento) quanto para `vite preview` (build servido, usado pelo E2E).
const proxy = {
  '/api': apiTarget,
  '/hubs': { target: apiTarget, ws: true },
};

export default defineConfig({
  plugins: [react(), tailwindcss()],
  resolve: { alias: { '@': path.resolve(import.meta.dirname, 'src') } },
  server: { port, strictPort: true, proxy },
  preview: { port, strictPort: true, proxy },
  test: {
    environment: 'jsdom',
    globals: true,
    setupFiles: ['./src/test-setup.ts'],
    css: false,
    exclude: ['e2e/**', 'node_modules/**', 'dist/**'],
  },
});
