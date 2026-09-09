import path from 'node:path'
import tailwindcss from '@tailwindcss/vite'
import react from '@vitejs/plugin-react'
import { defineConfig } from 'vite'

// https://vite.dev/config/
export default defineConfig({
  plugins: [react(), tailwindcss()],
  resolve: {
    alias: {
      '@': path.resolve(import.meta.dirname, './src'),
    },
  },
  server: {
    proxy: {
      '/v1': {
        target: 'http://localhost:5243',
        changeOrigin: true,
      },
      // Local-dev-only: routes direct-to-storage uploads through this same origin so
      // the browser never makes a cross-origin request to MinIO -- sidesteps this
      // MinIO version's CORS-configuration limitation (`mc cors set` rejects every
      // AllowedHeader/AllowedOrigin combination tried with
      // "functionality that is not implemented") without weakening anything real S3
      // would need in production, where the frontend's real origin would be
      // configured directly on the bucket's CORS policy instead of proxied like this.
      '/gweb-documents-local': {
        target: 'http://localhost:9000',
        changeOrigin: true,
      },
    },
  },
})
