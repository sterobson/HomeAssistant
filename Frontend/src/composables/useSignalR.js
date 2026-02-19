import { ref, onMounted, onUnmounted } from 'vue'
import * as signalR from '@microsoft/signalr'

const connection = ref(null)
const isConnected = ref(false)
const connectionError = ref(null)

// Module-level map of all registered listeners across all useSignalR instances.
// When the connection is (re-)established, every handler here gets attached.
const allListeners = new Map()

export function useSignalR(houseId) {
  // Track which event names this instance owns, so off() / cleanup only
  // removes this instance's registrations.
  const ownedEvents = new Set()
  const lastHiddenTime = ref(null)

  async function connect() {
    if (connection.value && isConnected.value) {
      console.log('SignalR already connected')
      return
    }

    // Don't attempt to connect if there's no house ID
    if (!houseId) {
      console.warn('Cannot connect to SignalR: No house ID provided')
      connectionError.value = new Error('No house ID provided')
      return
    }

    try {
      // Get the API base URL from environment or use default
      const apiBaseUrl = import.meta.env.VITE_API_URL || 'http://localhost:7071/api'

      // First, get connection info from the negotiate endpoint
      // Pass houseId as query parameter so the connection is associated with this house
      const negotiateUrl = `${apiBaseUrl}/signalr/negotiate${houseId ? `?houseId=${encodeURIComponent(houseId)}` : ''}`
      const negotiateResponse = await fetch(negotiateUrl, {
        method: 'POST'
      })

      if (!negotiateResponse.ok) {
        throw new Error(`Negotiate failed: ${negotiateResponse.statusText}`)
      }

      const connectionInfo = await negotiateResponse.json()
      console.log('SignalR negotiate successful for house:', houseId)

      // Now connect to Azure SignalR Service using the connection info
      connection.value = new signalR.HubConnectionBuilder()
        .withUrl(connectionInfo.url, {
          accessTokenFactory: () => connectionInfo.accessToken
        })
        .withAutomaticReconnect()
        .configureLogging(signalR.LogLevel.Error)
        .build()

      // Set up event handlers
      connection.value.onclose((error) => {
        isConnected.value = false
        if (error) {
          console.error('SignalR connection closed with error:', error)
          connectionError.value = error
        }
      })

      connection.value.onreconnecting((error) => {
        isConnected.value = false
        console.warn('SignalR reconnecting:', error)
      })

      connection.value.onreconnected(async (connectionId) => {
        isConnected.value = true
        console.log('SignalR reconnected:', connectionId)

        // Re-add to house group after reconnection
        try {
          const addToGroupUrl = `${apiBaseUrl}/signalr/add-to-group?houseId=${encodeURIComponent(houseId)}&connectionId=${encodeURIComponent(connectionId)}`
          const groupResponse = await fetch(addToGroupUrl, {
            method: 'POST'
          })

          if (groupResponse.ok) {
            console.log('Successfully re-added to house group after reconnection:', houseId)
          } else {
            console.warn('Failed to re-add to house group after reconnection:', await groupResponse.text())
          }
        } catch (groupError) {
          console.error('Error re-adding to house group after reconnection:', groupError)
        }
      })

      // Attach all pending listeners before starting so they're ready
      // when the first messages arrive
      allListeners.forEach((handler, eventName) => {
        connection.value.on(eventName, handler)
      })

      await connection.value.start()
      isConnected.value = true
      connectionError.value = null
      console.log('SignalR connected, connection ID:', connection.value.connectionId)

      // Add connection to the house group so we receive group messages
      try {
        const addToGroupUrl = `${apiBaseUrl}/signalr/add-to-group?houseId=${encodeURIComponent(houseId)}&connectionId=${encodeURIComponent(connection.value.connectionId)}`
        const groupResponse = await fetch(addToGroupUrl, {
          method: 'POST'
        })

        if (groupResponse.ok) {
          console.log('Successfully added to house group:', houseId)
        } else {
          console.warn('Failed to add to house group:', await groupResponse.text())
        }
      } catch (groupError) {
        console.error('Error adding to house group:', groupError)
        // Don't fail the connection if group add fails
      }
    } catch (error) {
      console.error('Error connecting to SignalR:', error)
      connectionError.value = error
      isConnected.value = false
    }
  }


  function on(eventName, handler) {
    // Register on the connection if it exists (SignalR allows registering
    // handlers regardless of connection state)
    if (connection.value) {
      connection.value.on(eventName, handler)
    }
    // Store globally so any future connect/reconnect will re-attach
    allListeners.set(eventName, handler)
    ownedEvents.add(eventName)
  }

  function off(eventName) {
    if (connection.value) {
      connection.value.off(eventName)
    }
    allListeners.delete(eventName)
    ownedEvents.delete(eventName)
  }

  async function disconnect() {
    if (connection.value) {
      // Only clear this instance's listeners from the shared map
      ownedEvents.forEach(eventName => {
        allListeners.delete(eventName)
      })
      ownedEvents.clear()
      await connection.value.stop()
      connection.value = null
      isConnected.value = false
    }
  }

  // Check if connection is healthy
  function isConnectionHealthy() {
    return connection.value &&
           isConnected.value &&
           connection.value.state === signalR.HubConnectionState.Connected
  }

  // Force reconnect (useful when tab becomes visible)
  async function ensureConnected() {
    if (!isConnectionHealthy()) {
      console.log('Connection not healthy, reconnecting...')
      await disconnect()
      await connect()
      return true // Reconnected
    }
    return false // Already connected
  }

  return {
    connection,
    isConnected,
    connectionError,
    lastHiddenTime,
    connect,
    disconnect,
    on,
    off,
    isConnectionHealthy,
    ensureConnected
  }
}
