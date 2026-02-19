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

// --- Multi-house localStorage helpers ---

const SAVED_HOUSES_KEY = 'homeassistant_houses'

/**
 * Get the list of saved houses from localStorage
 * @returns {Array<{id: string, name: string}>}
 */
export function getSavedHouses() {
  try {
    const raw = localStorage.getItem(SAVED_HOUSES_KEY)
    return raw ? JSON.parse(raw) : []
  } catch {
    return []
  }
}

/**
 * Add a house to the saved list (deduplicates by id)
 * @param {string} id
 * @param {string} name
 */
export function addSavedHouse(id, name) {
  const houses = getSavedHouses()
  const existing = houses.find(h => h.id === id)
  if (existing) {
    existing.name = name
  } else {
    houses.push({ id, name })
  }
  localStorage.setItem(SAVED_HOUSES_KEY, JSON.stringify(houses))
}

/**
 * Remove a house from the saved list
 * @param {string} id
 */
export function removeSavedHouse(id) {
  const houses = getSavedHouses().filter(h => h.id !== id)
  localStorage.setItem(SAVED_HOUSES_KEY, JSON.stringify(houses))
}

/**
 * Update the name for a saved house
 * @param {string} id
 * @param {string} name
 */
export function updateSavedHouseName(id, name) {
  const houses = getSavedHouses()
  const house = houses.find(h => h.id === id)
  if (house) {
    house.name = name
    localStorage.setItem(SAVED_HOUSES_KEY, JSON.stringify(houses))
  }
}
