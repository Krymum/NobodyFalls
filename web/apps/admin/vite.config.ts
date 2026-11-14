import react from '@vitejs/plugin-react';
import { defineConfig } from 'vite';

// The API (docker compose or `dotnet run`) serves /api, /auth and /dev on port 8080.
const api = 'http://localhost:8080';

export default defineConfig({
  base: '/admin/',
  plugins: [react()],
  server: {
    port: 5174,
    proxy: { '/api': api, '/auth': api, '/dev': api },
  },
  build: {
    // Built after the game, which empties wwwroot (see the workspace build script).
    outDir: '../../../src/NobodyFalls.Api/wwwroot/admin',
    emptyOutDir: true,
    target: 'es2022',
  },
});
