// Guest-side API for the QR code page (/r/:token).
//
// This file must never import the house ID cookie helpers: a guest knows only the token, and the
// requests carry nothing else that identifies the house.

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

/** The link is unknown or has been replaced. */
export class InvalidLinkError extends Error {
  constructor() {
    super('This link is no longer valid')
    this.name = 'InvalidLinkError'
  }
}

/** The server turned the request down (for example a temperature outside the limits). */
export class GuestRequestError extends Error {
  constructor(message) {
    super(message)
    this.name = 'GuestRequestError'
  }
}

const send = async (path, token, method, body) => {
  const response = await fetch(`${API_BASE_URL}/${path}?token=${encodeURIComponent(token)}`, {
    method,
    headers: getApiHeaders(),
    body: body === undefined ? undefined : JSON.stringify(body)
  })

  if (response.status === 404) {
    throw new InvalidLinkError()
  }

  if (!response.ok) {
    let message = 'Something went wrong. Try again.'
    try {
      const payload = await response.json()
      if (payload?.error) message = payload.error
    } catch {
      // No JSON body; keep the generic message.
    }
    throw new GuestRequestError(message)
  }

  return response.json()
}

export const guestApi = {
  /**
   * The zone behind the link, with its current state and the limits for an override.
   * @param {string} token
   * @returns {Promise<{
   *   name: string, icon?: string, currentTemperature?: number, targetTemperature?: number,
   *   heatingActive: boolean,
   *   override: { temperature: number, endTime: string } | null,
   *   limits: { minTemperature: number, maxTemperature: number, maxMinutes: number }
   * }>}
   */
  getRoom(token) {
    return send('guest/room', token, 'GET')
  },

  /**
   * Hold the zone at a temperature for a while.
   * @param {string} token
   * @param {number} temperature
   * @param {number} durationMinutes
   */
  startOverride(token, temperature, durationMinutes) {
    return send('guest/override', token, 'POST', { temperature, durationMinutes })
  },

  /**
   * Stop the override and go back to the schedule.
   * @param {string} token
   */
  cancelOverride(token) {
    return send('guest/override/cancel', token, 'POST')
  }
}
