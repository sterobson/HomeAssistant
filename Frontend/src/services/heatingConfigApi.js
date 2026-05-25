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

export const heatingConfigApi = {
  async getConfig() {
    try {
      const houseId = getCurrentHouseId()
      const response = await fetch(`${API_BASE_URL}/heating-config?houseId=${houseId}`, {
        headers: getApiHeaders()
      })

      if (!response.ok) {
        throw new Error(`Failed to fetch heating config: ${response.statusText}`)
      }

      return await response.json()
    } catch (error) {
      console.error('Error fetching heating config:', error)
      reportApiError('Failed to load heating configuration')
      throw error
    }
  },

  async saveConfig(config) {
    try {
      const houseId = getCurrentHouseId()
      const response = await fetch(`${API_BASE_URL}/heating-config?houseId=${houseId}`, {
        method: 'POST',
        headers: getApiHeaders(),
        body: JSON.stringify(config)
      })

      if (!response.ok) {
        throw new Error(`Failed to save heating config: ${response.statusText}`)
      }

      return await response.json()
    } catch (error) {
      console.error('Error saving heating config:', error)
      reportApiError('Failed to save heating configuration')
      throw error
    }
  }
}
