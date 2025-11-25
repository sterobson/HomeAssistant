// API service for heating schedules and room states
// Connects to Azure Functions backend or uses mock data

import { mockSchedulesData, mockRoomStatesData } from './mockData.js'

// Configuration
const API_BASE_URL = import.meta.env.VITE_API_URL || 'http://localhost:7071'
const HOUSE_ID = import.meta.env.VITE_HOUSE_ID || '00000000-0000-0000-0000-000000000000'
const USE_MOCK_API = import.meta.env.VITE_USE_MOCK_API === 'true'

// TODO: SignalR Connection - Add SignalR hub connection for real-time updates
// import * as signalR from '@microsoft/signalr'
// let hubConnection = null

export const heatingApi = {
  /**
   * Get all heating schedules for all rooms
   * @returns {Promise<Object>} Schedules response with rooms array
   */
  async getSchedules() {
    // Use mock data if configured
    if (USE_MOCK_API) {
      console.log('Using mock data for schedules')
      return Promise.resolve(JSON.parse(JSON.stringify(mockSchedulesData)))
    }

    try {
      const response = await fetch(`${API_BASE_URL}/api/schedules?houseId=${HOUSE_ID}`)

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
    // Use mock data if configured
    if (USE_MOCK_API) {
      console.log('Mock API: Saving schedules (simulated)', schedules)
      // Update mock data in memory
      mockSchedulesData.rooms = JSON.parse(JSON.stringify(schedules.rooms))
      return Promise.resolve({ success: true })
    }

    try {
      const response = await fetch(`${API_BASE_URL}/api/schedules?houseId=${HOUSE_ID}`, {
        method: 'POST',
        headers: {
          'Content-Type': 'application/json'
        },
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
    // Use mock data if configured
    if (USE_MOCK_API) {
      console.log('Using mock data for room states')
      return Promise.resolve(JSON.parse(JSON.stringify(mockRoomStatesData)))
    }

    try {
      const response = await fetch(`${API_BASE_URL}/api/room-states?houseId=${HOUSE_ID}`)

      if (!response.ok) {
        throw new Error(`Failed to fetch room states: ${response.statusText}`)
      }

      return await response.json()
    } catch (error) {
      console.error('Error fetching room states:', error)
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
