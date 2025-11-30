// API service for heating schedules and room states
// Connects to Azure Functions backend

import { getHouseId } from '../utils/cookies.js'

// Configuration
const API_BASE_URL = import.meta.env.VITE_API_URL || 'http://localhost:7071'
const FUNCTION_KEY = import.meta.env.VITE_FUNCTION_KEY || ''
const LOCALHOST_KEY = import.meta.env.VITE_LOCALHOST_KEY || ''

/**
 * Get the current house ID from cookies
 * @returns {string} House ID
 * @throws {Error} If house ID is not set
 */
function getCurrentHouseId() {
  const houseId = getHouseId()
  if (!houseId) {
    throw new Error('House ID not set. Please configure your House ID first.')
  }
  return houseId
}

/**
 * Get appropriate headers for API requests
 * @returns {Object} Headers object
 */
function getApiHeaders() {
  const headers = {
    'Content-Type': 'application/json'
  }

  // If calling localhost, add the localhost key header
  if (API_BASE_URL.includes('localhost')) {
    if (LOCALHOST_KEY) {
      headers['X-Localhost-Key'] = LOCALHOST_KEY
    }
  } else {
    // For deployed environments, use Azure Function key
    if (FUNCTION_KEY) {
      headers['x-functions-key'] = FUNCTION_KEY
    }
  }

  return headers
}

// TODO: SignalR Connection - Add SignalR hub connection for real-time updates
// import * as signalR from '@microsoft/signalr'
// let hubConnection = null

export const heatingApi = {
  /**
   * Get all heating schedules for all rooms
   * @returns {Promise<Object>} Schedules response with rooms array
   */
  async getSchedules() {
    try {
      const houseId = getCurrentHouseId()
      const response = await fetch(`${API_BASE_URL}/api/schedules?houseId=${houseId}`, {
        headers: getApiHeaders()
      })

      if (!response.ok) {
        throw new Error(`Failed to fetch schedules: ${response.statusText}`)
      }

      return await response.json()
    } catch (error) {
      console.error('Error fetching schedules:', error)
      throw error
    }
  },

  /**
   * Save heating schedules for all rooms
   * @param {Object} schedules - Schedules object with rooms array
   * @returns {Promise<Object>} Success response
   */
  async setSchedules(schedules) {
    try {
      const houseId = getCurrentHouseId()
      const response = await fetch(`${API_BASE_URL}/api/schedules?houseId=${houseId}`, {
        method: 'POST',
        headers: getApiHeaders(),
        body: JSON.stringify(schedules)
      })

      if (!response.ok) {
        throw new Error(`Failed to save schedules: ${response.statusText}`)
      }

      console.log('Successfully saved schedules')
      return { success: true }
    } catch (error) {
      console.error('Error saving schedules:', error)
      throw error
    }
  },

  /**
   * Get current state of all rooms (temperature, heating status, active schedule)
   * This is separate from schedule configuration and is updated by the backend
   * @returns {Promise<Object>} Room states response with roomStates array
   */
  async getRoomStates() {
    try {
      const houseId = getCurrentHouseId()
      const response = await fetch(`${API_BASE_URL}/api/room-states?houseId=${houseId}`, {
        headers: getApiHeaders()
      })

      if (!response.ok) {
        throw new Error(`Failed to fetch room states: ${response.statusText}`)
      }

      return await response.json()
    } catch (error) {
      console.error('Error fetching room states:', error)
      throw error
    }
  },

  /**
   * Get house details (name, etc.)
   * @returns {Promise<Object>} House details response with name property
   */
  async getHouseDetails() {
    try {
      const houseId = getCurrentHouseId()
      const response = await fetch(`${API_BASE_URL}/api/house-details?houseId=${houseId}`, {
        headers: getApiHeaders()
      })

      if (!response.ok) {
        throw new Error(`Failed to fetch house details: ${response.statusText}`)
      }

      return await response.json()
    } catch (error) {
      console.error('Error fetching house details:', error)
      throw error
    }
  },

  /**
   * Save house details (name, etc.)
   * @param {Object} details - House details object with name property
   * @returns {Promise<Object>} Success response
   */
  async setHouseDetails(details) {
    try {
      const houseId = getCurrentHouseId()
      const response = await fetch(`${API_BASE_URL}/api/house-details?houseId=${houseId}`, {
        method: 'POST',
        headers: getApiHeaders(),
        body: JSON.stringify(details)
      })

      if (!response.ok) {
        throw new Error(`Failed to save house details: ${response.statusText}`)
      }

      console.log('Successfully saved house details')
      return { success: true }
    } catch (error) {
      console.error('Error saving house details:', error)
      throw error
    }
  }

  // TODO: SignalR Connection - Methods for SignalR real-time updates
  // async connectToSignalR() {
  //   if (hubConnection) {
  //     return
  //   }
  //
  //   hubConnection = new signalR.HubConnectionBuilder()
  //     .withUrl(`${API_BASE_URL}/api`)
  //     .withAutomaticReconnect()
  //     .build()
  //
  //   hubConnection.on('SchedulesUpdated', async () => {
  //     console.log('Received SchedulesUpdated notification')
  //     // Trigger UI refresh by calling getSchedules()
  //   })
  //
  //   hubConnection.on('RoomStatesUpdated', async () => {
  //     console.log('Received RoomStatesUpdated notification')
  //     // Trigger UI refresh by calling getRoomStates()
  //   })
  //
  //   await hubConnection.start()
  //   console.log('Connected to SignalR hub')
  // },
  //
  // async disconnectFromSignalR() {
  //   if (hubConnection) {
  //     await hubConnection.stop()
  //     hubConnection = null
  //     console.log('Disconnected from SignalR hub')
  //   }
  // }
}
