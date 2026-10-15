import { defineConfig } from 'vite'
import type { Plugin } from 'vite'
import { readFileSync } from 'node:fs'
import path from 'node:path'

const root = import.meta.dirname

// Folds the script of the page of a subscription into the page itself, which the server fills and answers with.
function page(): Plugin {
  return {
    name: 'amneziageo-subscription-page',
    apply: 'build',
    generateBundle(_, bundle) {
      let script = ''
      for (const [name, item] of Object.entries(bundle)) {
        if (item.type === 'chunk') {
          script += item.code
        }

        delete bundle[name]
      }

      const shell = readFileSync(path.resolve(root, 'src/subscription/page.html'), 'utf8')
      this.emitFile({
        type: 'asset',
        fileName: 'subscription.html',
        source: shell.replace('/*page*/', () => script.replaceAll('</script', '<\\/script')),
      })
    },
  }
}

export default defineConfig({
  plugins: [page()],
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
    lib: {
      entry: path.resolve(root, 'src/subscription/page.ts'),
      formats: ['iife'],
      name: 'subscription',
      fileName: () => 'subscription.js',
    },
  },
})
