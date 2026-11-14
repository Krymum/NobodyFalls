import react from '@vitejs/plugin-react';
import { defineConfig } from 'vite';

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
  },
});
