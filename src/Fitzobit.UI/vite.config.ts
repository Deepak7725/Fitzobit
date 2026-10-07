import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';

// Dev: the UI runs on :5173 and proxies /api to the ASP.NET Core host on :5080.
// Build: output goes to Fitzobit.Web/wwwroot so the API host serves the app on :5080.
export default defineConfig({
  plugins: [react()],
  server: {
    port: 5173,
    proxy: { '/api': 'http://localhost:5080' },
  },
  build: {
    outDir: '../Fitzobit.Web/wwwroot',
    emptyOutDir: true,
  },
});
