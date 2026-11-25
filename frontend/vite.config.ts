import { defineConfig } from 'vitest/config'
import react from '@vitejs/plugin-react'

// https://vite.dev/config/
const backendTarget = process.env.VITE_BACKEND_URL || process.env.BACKEND_URL || 'http://backend:8000'

export default defineConfig({
  plugins: [react()],
  test: {
    // Tesztlefedettség (npm run test:coverage): a CI összesítőjébe és artifactként is kikerül.
    coverage: {
      provider: 'v8',
      reporter: ['text-summary', 'json-summary', 'html'],
      reportsDirectory: './coverage',
      include: ['src/**/*.{ts,tsx}'],
      exclude: ['src/**/*.test.{ts,tsx}', 'src/main.tsx', 'src/vite-env.d.ts'],
    },
  },
  server: {
    host: '0.0.0.0',
    port: 3000,
    proxy: {
      '/api': {
        target: backendTarget,
        changeOrigin: true,
        secure: false,
      },
    },
  },
})
