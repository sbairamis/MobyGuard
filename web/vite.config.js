import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

export default defineConfig({
  plugins: [react()],
  build: {
    outDir: '../MobyGuard/wwwroot',
    emptyOutDir: true,
    assetsInlineLimit: 0
  },
  server: {
    proxy: {
      '/api': {
        target: process.env.MOBYGUARD_API || 'http://localhost:5090',
        changeOrigin: false
      }
    }
  }
})
