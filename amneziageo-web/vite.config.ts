import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'
import tailwindcss from '@tailwindcss/vite'
import path from 'node:path'

const root = import.meta.dirname
const panel = process.env.AMNEZIAGEO_PANEL ?? 'http://localhost:8443'

export default defineConfig({
  base: './',
  plugins: [react(), tailwindcss()],
  resolve: {
    alias: { '@': path.resolve(root, 'src') },
  },
  build: {
    outDir: path.resolve(root, '../amneziageo-server/AmneziaGeo.Server.Api/wwwroot'),
    emptyOutDir: true,
  },
  server: {
    host: true,
    port: 5173,
    proxy: {
      '/api': { target: panel, changeOrigin: true, secure: false },
    },
  },
})
