/**
 * Generic localStorage cache utility for API responses, scoped by houseId.
 *
 * Key format: cache:{houseId}:{category}:{key}
 * Values: JSON-stringified API response data.
 */

function buildKey(houseId, category, key) {
  return `cache:${houseId}:${category}:${key}`
}

/**
 * Read a cached value.
 * @param {string} houseId
 * @param {string} category - e.g. 'pricing', 'history', 'rules'
 * @param {string} key - e.g. '2026-02-16' or 'current'
 * @returns {any|null} Parsed JSON or null if missing / corrupt
 */
export function getCache(houseId, category, key) {
  try {
    const raw = localStorage.getItem(buildKey(houseId, category, key))
    return raw ? JSON.parse(raw) : null
  } catch {
    return null
  }
}

/**
 * Write a value to the cache.
 * @param {string} houseId
 * @param {string} category
 * @param {string} key
 * @param {any} data - Will be JSON-stringified
 */
export function setCache(houseId, category, key, data) {
  try {
    localStorage.setItem(buildKey(houseId, category, key), JSON.stringify(data))
  } catch {
    // localStorage full or unavailable — silently ignore
  }
}

/**
 * Remove all cache entries for a house.
 * @param {string} houseId
 */
export function clearCache(houseId) {
  const prefix = `cache:${houseId}:`
  const keysToRemove = []
  for (let i = 0; i < localStorage.length; i++) {
    const k = localStorage.key(i)
    if (k && k.startsWith(prefix)) {
      keysToRemove.push(k)
    }
  }
  for (const k of keysToRemove) {
    localStorage.removeItem(k)
  }
}

/**
 * Remove cache entries for a category that are not in the keepKeys list.
 * Useful for dropping old date-keyed entries (e.g. pricing older than 7 days).
 * @param {string} houseId
 * @param {string} category
 * @param {string[]} keepKeys - Keys to retain
 */
export function pruneCache(houseId, category, keepKeys) {
  const prefix = `cache:${houseId}:${category}:`
  const keepSet = new Set(keepKeys.map(k => buildKey(houseId, category, k)))
  const keysToRemove = []
  for (let i = 0; i < localStorage.length; i++) {
    const k = localStorage.key(i)
    if (k && k.startsWith(prefix) && !keepSet.has(k)) {
      keysToRemove.push(k)
    }
  }
  for (const k of keysToRemove) {
    localStorage.removeItem(k)
  }
}
