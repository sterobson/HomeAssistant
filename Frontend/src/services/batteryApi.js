import { getHouseId } from '../utils/cookies.js'
import { reportApiError } from '../composables/useApiErrors.js'

const API_BASE_URL = import.meta.env.VITE_API_URL || 'http://localhost:7071'
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

export const batteryApi = {
  async getRules() {
    try {
      const houseId = getCurrentHouseId()
      const response = await fetch(`${API_BASE_URL}/api/battery-rules?houseId=${houseId}`, {
        headers: getApiHeaders()
      })

      if (!response.ok) {
        throw new Error(`Failed to fetch battery rules: ${response.statusText}`)
      }

      return await response.json()
    } catch (error) {
      console.error('Error fetching battery rules:', error)
      reportApiError('Failed to load battery rules')
      throw error
    }
  },

  async setRules(rules) {
    try {
      const houseId = getCurrentHouseId()
      const response = await fetch(`${API_BASE_URL}/api/battery-rules?houseId=${houseId}`, {
        method: 'POST',
        headers: getApiHeaders(),
        body: JSON.stringify(rules)
      })

      if (!response.ok) {
        throw new Error(`Failed to save battery rules: ${response.statusText}`)
      }

      return await response.json()
    } catch (error) {
      console.error('Error saving battery rules:', error)
      reportApiError('Failed to save battery rules')
      throw error
    }
  },

  async getPricing(date) {
    try {
      const houseId = getCurrentHouseId()
      const response = await fetch(`${API_BASE_URL}/api/battery-pricing?houseId=${houseId}&date=${date}`, {
        headers: getApiHeaders()
      })

      if (!response.ok) {
        throw new Error(`Failed to fetch battery pricing: ${response.statusText}`)
      }

      return await response.json()
    } catch (error) {
      console.error('Error fetching battery pricing:', error)
      reportApiError('Failed to load energy pricing')
      throw error
    }
  },

  async setBatteryState(state) {
    try {
      const houseId = getCurrentHouseId()
      const response = await fetch(`${API_BASE_URL}/api/battery-state?houseId=${houseId}`, {
        method: 'POST',
        headers: getApiHeaders(),
        body: JSON.stringify(state)
      })

      if (!response.ok) {
        throw new Error(`Failed to save battery state: ${response.statusText}`)
      }

      return await response.json()
    } catch (error) {
      console.error('Error saving battery state:', error)
      reportApiError('Failed to save battery state')
      throw error
    }
  },

  async getHistory(date) {
    try {
      const houseId = getCurrentHouseId()
      const response = await fetch(`${API_BASE_URL}/api/battery-history?houseId=${houseId}&date=${date}`, {
        headers: getApiHeaders()
      })

      if (!response.ok) {
        throw new Error(`Failed to fetch battery history: ${response.statusText}`)
      }

      return await response.json()
    } catch (error) {
      console.error('Error fetching battery history:', error)
      reportApiError('Failed to load battery history')
      throw error
    }
  },

  async requestBackfill(date) {
    try {
      const houseId = getCurrentHouseId()
      const response = await fetch(`${API_BASE_URL}/api/battery-history-backfill?houseId=${houseId}&date=${date}`, {
        method: 'POST',
        headers: getApiHeaders()
      })

      if (!response.ok) {
        throw new Error(`Failed to request battery history backfill: ${response.statusText}`)
      }

      return await response.json()
    } catch (error) {
      console.error('Error requesting battery history backfill:', error)
      reportApiError('Failed to request history backfill')
      throw error
    }
  }
}
