import { fileURLToPath, URL } from 'node:url'

import { defineConfig } from 'vite'
import vue from '@vitejs/plugin-vue'

// https://vite.dev/config/
export default defineConfig({
  plugins: [vue()],
  resolve: {
    alias: {
      '@': fileURLToPath(new URL('./src', import.meta.url)),
    },
  },
  server: {
    port: 5173,
    // In development the API runs on its own port; the cookie stays first-party through this proxy.
    proxy: {
      '/api': { target: process.env.VITE_API_URL ?? 'http://localhost:5080', changeOrigin: false },
    },
  },
})
