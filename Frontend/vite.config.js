import { defineConfig } from 'vite'
import vue from '@vitejs/plugin-vue'

export default defineConfig({
  plugins: [vue()],
  server: {
    port: 3000
  },
  // Set base path for GitHub Pages deployment
  // Change 'HomeAssistant' to your actual repository name if different
  base: process.env.GITHUB_PAGES === 'true' ? '/HomeAssistant/' : '/'
})
