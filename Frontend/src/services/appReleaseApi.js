import { getHouseId } from '../utils/cookies.js'
import { reportApiError } from '../composables/useApiErrors.js'

const API_BASE_URL = import.meta.env.VITE_API_URL || 'http://localhost:7071/api'
const FUNCTION_KEY = import.meta.env.VITE_FUNCTION_KEY || ''
const LOCALHOST_KEY = import.meta.env.VITE_LOCALHOST_KEY || ''

function getCurrentHouseId() {
  const houseId = getHouseId()
  if (!houseId) {
    throw new Error('House ID not set. Please configure your House ID first.')
  }
  return houseId
}

function getApiHeaders() {
  const headers = {
    'Content-Type': 'application/json'
  }

  if (API_BASE_URL.includes('localhost')) {
    if (LOCALHOST_KEY) {
      headers['X-Localhost-Key'] = LOCALHOST_KEY
    }
  } else {
    if (FUNCTION_KEY) {
      headers['x-functions-key'] = FUNCTION_KEY
    }
  }

  return headers
}

export const appReleaseApi = {
  async getLatestVersion() {
    try {
      const response = await fetch(`${API_BASE_URL}/app-release/latest`, {
        headers: getApiHeaders()
      })

      if (!response.ok) {
        throw new Error(`Failed to fetch latest version: ${response.statusText}`)
      }

      return await response.json()
    } catch (error) {
      console.error('Error fetching latest version:', error)
      reportApiError('Failed to load latest version')
      throw error
    }
  },

  async getRunningVersion() {
    try {
      const houseId = getCurrentHouseId()
      const response = await fetch(`${API_BASE_URL}/app-release/running?houseId=${houseId}`, {
        headers: getApiHeaders()
      })

      if (!response.ok) {
        throw new Error(`Failed to fetch running version: ${response.statusText}`)
      }

      return await response.json()
    } catch (error) {
      console.error('Error fetching running version:', error)
      reportApiError('Failed to load running version')
      throw error
    }
  },

  async requestUpdate(version) {
    try {
      const houseId = getCurrentHouseId()
      const response = await fetch(`${API_BASE_URL}/app-release/desired?houseId=${houseId}&version=${version}`, {
        method: 'POST',
        headers: getApiHeaders()
      })

      if (!response.ok) {
        throw new Error(`Failed to request update: ${response.statusText}`)
      }

      return await response.json()
    } catch (error) {
      console.error('Error requesting update:', error)
      reportApiError('Failed to request update')
      throw error
    }
  }
}
