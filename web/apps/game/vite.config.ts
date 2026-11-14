import react from '@vitejs/plugin-react';
import { defineConfig } from 'vite';

/**
 * Modules loaded on demand (SignalR after the first screen, "Ideas and contact", dev
 * pages). Everything else forms one "game" chunk: without this group, modules shared by
 * the first screen and a lazy one are split into extra chunks, each with its own
 * overhead, and the first load grows (ADR 0017: the game must not grow).
 */
const LAZY = /[\\/](src[\\/](feedback|dev)|@microsoft[\\/]signalr)[\\/]/;

// The API (docker compose or `dotnet run`) serves /api, /auth, /hubs and /dev on port 8080.
const api = 'http://localhost:8080';

export default defineConfig({
  plugins: [react()],
  server: {
    port: 5173,
    proxy: {
      '/api': api,
      '/auth': api,
      '/dev': api,
      '/hubs': { target: api, ws: true },
    },
  },
  build: {
    outDir: '../../../src/NobodyFalls.Api/wwwroot',
    emptyOutDir: true,
    target: 'es2022',
    rolldownOptions: {
      output: {
        codeSplitting: {
          groups: [{ name: 'game', test: (id: string) => !LAZY.test(id) }],
        },
      },
    },
  },
});
