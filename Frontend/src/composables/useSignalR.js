import { ref, onMounted, onUnmounted } from 'vue'
import * as signalR from '@microsoft/signalr'

const connection = ref(null)
const isConnected = ref(false)
const connectionError = ref(null)

export function useSignalR(houseId) {
  const listeners = new Map()

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
      const negotiateUrl = `${apiBaseUrl}/api/signalr/negotiate${houseId ? `?houseId=${encodeURIComponent(houseId)}` : ''}`
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
        .configureLogging(signalR.LogLevel.Information)
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

      connection.value.onreconnected((connectionId) => {
        isConnected.value = true
        console.log('SignalR reconnected:', connectionId)
      })

      await connection.value.start()
      isConnected.value = true
      connectionError.value = null
      console.log('SignalR connected, connection ID:', connection.value.connectionId)

      // Re-attach any existing listeners
      listeners.forEach((handler, eventName) => {
        connection.value.on(eventName, handler)
      })
    } catch (error) {
      console.error('Error connecting to SignalR:', error)
      connectionError.value = error
      isConnected.value = false
    }
  }


  function on(eventName, handler) {
    if (connection.value && isConnected.value) {
      connection.value.on(eventName, handler)
    }
    // Store the listener for when we reconnect
    listeners.set(eventName, handler)
  }

  function off(eventName) {
    if (connection.value) {
      connection.value.off(eventName)
    }
    listeners.delete(eventName)
  }

  async function disconnect() {
    if (connection.value) {
      listeners.clear()
      await connection.value.stop()
      connection.value = null
      isConnected.value = false
    }
  }

  return {
    connection,
    isConnected,
    connectionError,
    connect,
    disconnect,
    on,
    off
  }
}
