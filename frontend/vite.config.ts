import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

// https://vite.dev/config/
const backendTarget = process.env.BACKEND_URL || (process.env.NODE_ENV === 'production' ? 'http://backend:8000' : 'http://backend:8000')

export default defineConfig({
  plugins: [react()],
  server: {
    host: '0.0.0.0',
    port: 3000,
    proxy: {
      '/api': {
        target: process.env.VITE_BACKEND_URL || process.env.BACKEND_URL || 'http://backend:8000',
        changeOrigin: true,
        secure: false,
      },
    },
  },
})
