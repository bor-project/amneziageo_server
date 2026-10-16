import { defineConfig } from 'vite'
import type { Plugin } from 'vite'
import react from '@vitejs/plugin-react'
import tailwindcss from '@tailwindcss/vite'
import { readFileSync } from 'node:fs'
import path from 'node:path'

const root = import.meta.dirname

// Folds the script and the styles of the page of a subscription into the page itself, which the server fills and
// answers with.
function page(): Plugin {
  return {
    name: 'amneziageo-subscription-page',
    apply: 'build',
    enforce: 'post',
    generateBundle(_, bundle) {
      let script = ''
      let style = ''
      for (const [name, item] of Object.entries(bundle)) {
        if (item.type === 'chunk') {
          script += item.code
        } else if (name.endsWith('.css')) {
          style += typeof item.source === 'string' ? item.source : new TextDecoder().decode(item.source)
        }

        delete bundle[name]
      }

      const shell = readFileSync(path.resolve(root, 'src/subscription/page.html'), 'utf8')
      this.emitFile({
        type: 'asset',
        fileName: 'subscription.html',
        source: shell
          .replace('/*style*/', () => style.replaceAll('</style', '<\\/style'))
          .replace('/*page*/', () => script.replaceAll('</script', '<\\/script')),
      })
    },
  }
}

export default defineConfig({
  plugins: [react(), tailwindcss(), page()],
  resolve: {
    alias: { '@': path.resolve(root, 'src') },
  },
  define: {
    'process.env.NODE_ENV': JSON.stringify('production'),
  },
  build: {
    outDir: path.resolve(root, '../amneziageo-server/AmneziaGeo.Server.Api/wwwroot'),
    emptyOutDir: false,
    copyPublicDir: false,
    cssCodeSplit: false,
    lib: {
      entry: path.resolve(root, 'src/subscription/page.tsx'),
      formats: ['iife'],
      name: 'subscription',
      fileName: () => 'subscription.js',
    },
  },
})
