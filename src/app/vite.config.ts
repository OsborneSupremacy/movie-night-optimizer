import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

// There's no local API. In development, /api is proxied to the deployed one, so `npm run dev`
// talks to real data and POST /api/nights sends real mail.
export default defineConfig({
  plugins: [react()],
  server: {
    proxy: {
      '/api': {
        target: 'https://movienight.osbornesupremacy.com',
        changeOrigin: true,
      },
    },
  },
})
