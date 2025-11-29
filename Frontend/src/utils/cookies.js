/**
 * Cookie utilities for managing house ID
 */

const HOUSE_ID_COOKIE = 'homeassistant_house_id'
const COOKIE_MAX_AGE = 365 * 24 * 60 * 60 // 1 year in seconds

/**
 * Get the house ID from cookie
 * @returns {string|null} House ID or null if not set
 */
export function getHouseId() {
  const cookies = document.cookie.split(';')
  for (const cookie of cookies) {
    const [name, value] = cookie.trim().split('=')
    if (name === HOUSE_ID_COOKIE) {
      return decodeURIComponent(value)
    }
  }
  return null
}

/**
 * Set the house ID in a cookie
 * @param {string} houseId - The house ID to store
 */
export function setHouseId(houseId) {
  if (!houseId || typeof houseId !== 'string') {
    throw new Error('House ID must be a non-empty string')
  }

  document.cookie = `${HOUSE_ID_COOKIE}=${encodeURIComponent(houseId)}; max-age=${COOKIE_MAX_AGE}; path=/; SameSite=Strict`
}

/**
 * Clear the house ID cookie
 */
export function clearHouseId() {
  document.cookie = `${HOUSE_ID_COOKIE}=; max-age=0; path=/`
}

/**
 * Check if a house ID exists in the cookie
 * @returns {boolean}
 */
export function hasHouseId() {
  return getHouseId() !== null
}
