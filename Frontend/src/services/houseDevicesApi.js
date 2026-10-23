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
  const headers = { 'Content-Type': 'application/json' }
  if (API_BASE_URL.includes('localhost')) {
    if (LOCALHOST_KEY) headers['X-Localhost-Key'] = LOCALHOST_KEY
  } else if (FUNCTION_KEY) {
    headers['x-functions-key'] = FUNCTION_KEY
  }
  return headers
}

// The 3D house view's data: the device snapshot the NetDaemon app uploads, a request for live states, and commands.
// Live states, snapshot changes and command results arrive over SignalR (house-device-states,
// house-devices-changed, house-device-command-result).
export const houseDevicesApi = {
  async getDevices() {
    try {
      const response = await fetch(`${API_BASE_URL}/house-devices?houseId=${encodeURIComponent(getCurrentHouseId())}`, {
        headers: getApiHeaders()
      })
      // An API older than the 3D view has no such route. Not an error the user can act on here; the page says so.
      if (response.status === 404) return { devices: [], updatedAt: null, unsupported: true }
      if (!response.ok) throw new Error(`Failed to fetch devices: ${response.statusText}`)
      return await response.json()
    } catch (error) {
      console.error('Error fetching house devices:', error)
      reportApiError('Failed to load the devices')
      throw error
    }
  },

  /** Asks the house app to send live states for the next few minutes. Quiet on failure: the view still works. */
  async watch() {
    try {
      await fetch(`${API_BASE_URL}/house-devices/watch?houseId=${encodeURIComponent(getCurrentHouseId())}`, {
        method: 'POST',
        headers: getApiHeaders()
      })
    } catch (error) {
      console.warn('Could not ask for live device states:', error)
    }
  },

  /** Sends one command; resolves to its commandId, whose result comes back over SignalR. */
  async sendCommand(entityId, action) {
    const response = await fetch(`${API_BASE_URL}/house-devices/command?houseId=${encodeURIComponent(getCurrentHouseId())}`, {
      method: 'POST',
      headers: getApiHeaders(),
      body: JSON.stringify({ entityId, action })
    })
    if (!response.ok) throw new Error(`Failed to send the command: ${response.statusText}`)
    const body = await response.json()
    return body.commandId
  }
}
