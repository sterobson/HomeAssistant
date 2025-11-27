import { defineConfig } from 'vite'
import vue from '@vitejs/plugin-vue'

// Determine base path for GitHub Pages deployment
const getBasePath = () => {
  if (process.env.GITHUB_PAGES !== 'true') {
    return '/'
  }

  // Use environment variable to set the base path
  // For development branch: /HomeAssistant/dev/
  // For release branch: /HomeAssistant/
  const deployPath = process.env.DEPLOY_PATH || ''
  return `/HomeAssistant${deployPath}`
}

export default defineConfig({
  plugins: [vue()],
  server: {
    port: 3000
  },
  base: getBasePath()
})
