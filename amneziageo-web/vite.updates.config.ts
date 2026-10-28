import { defineConfig } from 'vite'
import type { Plugin } from 'vite'
import react from '@vitejs/plugin-react'
import tailwindcss from '@tailwindcss/vite'
import path from 'node:path'

const root = import.meta.dirname

// Names the page of the web interface of the service of the releases the way the service answers it.
function page(): Plugin {
  return {
    name: 'amneziageo-updates-page',
    apply: 'build',
    enforce: 'post',
    generateBundle(_, bundle) {
      const built = bundle['updates.html']
      if (built?.type !== 'asset') {
        return
      }

      delete bundle['updates.html']
      this.emitFile({ type: 'asset', fileName: 'index.html', source: built.source })
    },
  }
}

export default defineConfig({
  base: './',
  plugins: [react(), tailwindcss(), page()],
  resolve: {
    alias: { '@': path.resolve(root, 'src') },
  },
  build: {
    outDir: path.resolve(root, '../amneziageo-server/AmneziaGeo.Server.Updates.Host/wwwroot'),
    emptyOutDir: true,
    rolldownOptions: {
      input: path.resolve(root, 'updates.html'),
    },
  },
})
