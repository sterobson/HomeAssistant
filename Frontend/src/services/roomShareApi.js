// Owner-side API for sharing a heating zone by QR code. Needs the house ID, like the other services.
// The guest side lives in guestApi.js and never touches the house ID.

import { getHouseId } from '../utils/cookies.js'

const API_BASE_URL = import.meta.env.VITE_API_URL || 'http://localhost:7071/api'
const FUNCTION_KEY = import.meta.env.VITE_FUNCTION_KEY || ''
const LOCALHOST_KEY = import.meta.env.VITE_LOCALHOST_KEY || ''

const getApiHeaders = () => {
  const headers = { 'Content-Type': 'application/json' }
  if (API_BASE_URL.includes('localhost')) {
    if (LOCALHOST_KEY) headers['X-Localhost-Key'] = LOCALHOST_KEY
  } else if (FUNCTION_KEY) {
    headers['x-functions-key'] = FUNCTION_KEY
  }
  return headers
}

const getCurrentHouseId = () => {
  const houseId = getHouseId()
  if (!houseId) {
    throw new Error('House ID not set. Please configure your House ID first.')
  }
  return houseId
}

const request = async (path, method, failureMessage) => {
  const response = await fetch(`${API_BASE_URL}/${path}`, { method, headers: getApiHeaders() })
  if (!response.ok) {
    throw new Error(`${failureMessage}: ${response.statusText || response.status}`)
  }
  return response.json()
}

// A share is for a zone ({ roomId }) or a layout group ({ groupId }); a bare number means a zone.
const targetQuery = (target) => {
  if (target && typeof target === 'object' && target.groupId) {
    return `groupId=${encodeURIComponent(target.groupId)}`
  }
  const roomId = typeof target === 'object' ? target.roomId : target
  return `roomId=${encodeURIComponent(roomId)}`
}

export const roomShareApi = {
  /**
   * Every zone and group that already has a share link.
   * @returns {Promise<{ roomId: number|null, groupId: string|null, token: string, createdAt: string }[]>}
   */
  async getShares() {
    const houseId = encodeURIComponent(getCurrentHouseId())
    return request(`room-shares?houseId=${houseId}`, 'GET', 'Failed to load share links')
  },

  /**
   * The zone's or group's share link, created if it does not have one yet.
   * @param {number|{ roomId?: number, groupId?: string }} target
   */
  async createShare(target) {
    const houseId = encodeURIComponent(getCurrentHouseId())
    return request(`room-shares?houseId=${houseId}&${targetQuery(target)}`, 'POST', 'Failed to create the share link')
  },

  /**
   * Replace the share link. The old one stops working; this returns the new one.
   * @param {number|{ roomId?: number, groupId?: string }} target
   */
  async revokeShare(target) {
    const houseId = encodeURIComponent(getCurrentHouseId())
    return request(`room-shares/revoke?houseId=${houseId}&${targetQuery(target)}`, 'POST', 'Failed to make a new share link')
  }
}
