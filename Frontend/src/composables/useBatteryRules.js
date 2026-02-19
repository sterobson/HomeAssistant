// Battery zone-rule management with API persistence (localStorage fallback)
import { ref, computed, watch } from 'vue'
import { batteryApi } from '../services/batteryApi.js'
import { getHouseId } from '../utils/cookies.js'
import { getCache, setCache } from '../utils/apiCache.js'
import {
  findExportExceedsImportCrossovers,
  findImportExceedsExportCrossovers,
  findPeaks,
  findTroughs,
  findMinimaRegionBoundaries,
  findMaximaRegionBoundaries
} from '../utils/priceAnalysis.js'

/**
 * Check if a minute value falls within a zone, handling zones where endMinutes > 1440 (midnight wrap).
 */
export function isMinuteInZone(currentMinutes, startMinutes, endMinutes) {
  if (endMinutes <= 1440) {
    return currentMinutes >= startMinutes && currentMinutes < endMinutes
  }
  // Wrapped zone
  return currentMinutes >= startMinutes || currentMinutes < (endMinutes - 1440)
}

/**
 * Get elapsed minutes since zone start, handling zones where endMinutes > 1440 (midnight wrap).
 */
export function getElapsedMinutes(currentMinutes, startMinutes, endMinutes) {
  if (currentMinutes >= startMinutes) {
    return currentMinutes - startMinutes
  }
  // Morning portion of a wrapped zone
  return (1440 - startMinutes) + currentMinutes
}

const LEGACY_STORAGE_KEY = 'battery-zone-rules'
const cachedHouseId = getHouseId()

function generateId() {
  return Date.now().toString(36) + Math.random().toString(36).substring(2, 9)
}

function migrateActions(rules) {
  for (const rule of rules) {
    if (rule.action === 'charge') rule.action = 'import'
    if (rule.action === 'discharge') rule.action = 'export'
  }
  return rules
}

function loadFromLocalStorage() {
  // Try houseId-scoped cache first
  if (cachedHouseId) {
    const cached = getCache(cachedHouseId, 'rules', 'current')
    if (cached) return migrateActions(cached)
  }

  // Fall back to legacy flat key
  try {
    const stored = localStorage.getItem(LEGACY_STORAGE_KEY)
    if (stored) {
      const rules = JSON.parse(stored)
      return migrateActions(rules)
    }
  } catch (error) {
    console.error('Failed to load battery zone rules from localStorage:', error)
  }
  return []
}

// Reactive rules array (singleton)
const rules = ref(loadFromLocalStorage())
const loading = ref(false)
const saveError = ref(null)
const savingRuleIds = ref(new Set())

let saveTimeout = null
let _preChangeSnapshot = null

function snapshotRules() {
  if (!_preChangeSnapshot) {
    _preChangeSnapshot = JSON.parse(JSON.stringify(rules.value))
  }
}

function markSaving(ruleId) {
  snapshotRules()
  savingRuleIds.value = new Set([...savingRuleIds.value, ruleId])
}

async function loadFromApi() {
  if (!cachedHouseId) return
  try {
    loading.value = true
    const response = await batteryApi.getRules()
    if (response && response.rules) {
      rules.value = response.rules
      // Update houseId-scoped cache and remove legacy key
      if (cachedHouseId) {
        setCache(cachedHouseId, 'rules', 'current', response.rules)
      }
      localStorage.removeItem(LEGACY_STORAGE_KEY)
    }
  } catch (error) {
    console.warn('Failed to load battery rules from API, using localStorage fallback:', error)
    rules.value = loadFromLocalStorage()
  } finally {
    loading.value = false
  }
}

function saveToApi() {
  if (!cachedHouseId) return
  clearTimeout(saveTimeout)
  saveTimeout = setTimeout(async () => {
    const snapshot = _preChangeSnapshot
    _preChangeSnapshot = null
    try {
      saveError.value = null
      await batteryApi.setRules({ rules: rules.value })
      // Update cache on successful save
      if (cachedHouseId) {
        setCache(cachedHouseId, 'rules', 'current', rules.value)
      }
    } catch (error) {
      console.error('Failed to save battery rules to API:', error)
      saveError.value = 'Failed to save, changes reverted'
      // Revert to snapshot
      if (snapshot) {
        rules.value = snapshot
      }
      // Fallback: save snapshot to localStorage
      try {
        localStorage.setItem(LEGACY_STORAGE_KEY, JSON.stringify(rules.value))
      } catch (e) {
        console.error('Failed to save to localStorage fallback:', e)
      }
    } finally {
      savingRuleIds.value = new Set()
    }
  }, 500)
}

// Auto-save on changes (skip saves triggered by API load)
watch(rules, () => {
  if (loading.value) return
  saveToApi()
}, { deep: true })

// Load from API on init
loadFromApi()

/**
 * Resolve a single time definition against pricing data.
 * Returns an array of matching minutes values.
 */
function resolveTimeDefinition(timeDef, pricingData) {
  switch (timeDef.type) {
    case 'fixed-time':
      return [timeDef.fixedTimeMinutes]

    case 'export-exceeds-import': {
      const threshold = timeDef.thresholdType ? { type: timeDef.thresholdType, value: timeDef.thresholdValue } : null
      const crossovers = findExportExceedsImportCrossovers(pricingData, threshold)
      return crossovers.map(slot => slot.timeMinutes)
    }

    case 'import-exceeds-export': {
      const threshold = timeDef.thresholdType ? { type: timeDef.thresholdType, value: timeDef.thresholdValue } : null
      const crossovers = findImportExceedsExportCrossovers(pricingData, threshold)
      return crossovers.map(slot => slot.timeMinutes)
    }

    case 'local-peak-trough': {
      const priceKey = timeDef.priceType === 'export' ? 'exportPrice' : 'importPrice'
      const matches = timeDef.extremaType === 'peak'
        ? findPeaks(pricingData, priceKey)
        : findTroughs(pricingData, priceKey)
      return matches.map(slot => slot.timeMinutes)
    }

    case 'local-extrema-boundary': {
      const priceKey = timeDef.priceType === 'export' ? 'exportPrice' : 'importPrice'
      const boundaries = timeDef.regionType === 'maxima'
        ? findMaximaRegionBoundaries(pricingData, priceKey)
        : findMinimaRegionBoundaries(pricingData, priceKey)
      const filtered = boundaries.filter(b => b.boundaryType === timeDef.extremaType)
      return filtered.map(slot => slot.timeMinutes)
    }

    default:
      console.warn('Unknown time definition type:', timeDef.type)
      return []
  }
}

/**
 * Evaluate a single zone rule against pricing data.
 * Returns an array of resolved zones (one rule can produce multiple zones
 * when dynamic start/end times match multiple events).
 */
function evaluateZoneRule(rule, pricingData) {
  const startTimes = [...new Set(
    resolveTimeDefinition(rule.startTime, pricingData)
      .map(t => t >= 1440 ? t - 1440 : t)
  )].sort((a, b) => a - b)
  const endTimes = resolveTimeDefinition(rule.endTime, pricingData).sort((a, b) => a - b)

  if (startTimes.length === 0 || endTimes.length === 0) return []

  // Greedy pairing: for each start, find the nearest forward end (each end used once)
  const usedEnds = new Set()
  const zones = []

  for (const start of startTimes) {
    let bestEnd = null
    let bestEndIndex = -1
    for (let i = 0; i < endTimes.length; i++) {
      if (usedEnds.has(i)) continue
      if (endTimes[i] > start) {
        if (bestEnd === null || endTimes[i] < bestEnd) {
          bestEnd = endTimes[i]
          bestEndIndex = i
        }
      }
    }
    if (bestEnd !== null) {
      usedEnds.add(bestEndIndex)
      zones.push({
        ruleId: rule.id,
        startMinutes: start,
        endMinutes: bestEnd,
        action: rule.action,
        targetPercent: rule.targetPercent,
        rateMode: rule.rateMode || 'by-end',
        startTimeType: rule.startTime?.type || 'fixed-time',
        endTimeType: rule.endTime?.type || 'fixed-time'
      })
    }
  }

  // Second pass: wrap around midnight for unmatched starts
  // Emit a single zone with endMinutes > 1440 — consumers use isMinuteInZone/getElapsedMinutes
  const matchedStarts = new Set(zones.map(z => z.startMinutes))
  for (const start of startTimes) {
    if (matchedStarts.has(start)) continue

    let bestEnd = null
    let bestEndIndex = -1
    for (let i = 0; i < endTimes.length; i++) {
      if (usedEnds.has(i)) continue
      const wrapped = endTimes[i] + 1440
      if (wrapped > start) {
        if (bestEnd === null || wrapped < bestEnd) {
          bestEnd = wrapped
          bestEndIndex = i
        }
      }
    }
    if (bestEnd !== null) {
      usedEnds.add(bestEndIndex)
      zones.push({
        ruleId: rule.id,
        startMinutes: start,
        endMinutes: bestEnd,
        action: rule.action,
        targetPercent: rule.targetPercent,
        rateMode: rule.rateMode || 'by-end',
        startTimeType: rule.startTime?.type || 'fixed-time',
        endTimeType: rule.endTime?.type || 'fixed-time'
      })
    }
  }

  return zones
}

/**
 * Expand a zone into 1-2 [start, end) segments within [0, 1440).
 */
function getSegments(zone) {
  if (zone.endMinutes <= 1440) {
    return [{ start: zone.startMinutes, end: zone.endMinutes }]
  }
  // Wrapped zone: split into evening + morning
  return [
    { start: zone.startMinutes, end: 1440 },
    { start: 0, end: zone.endMinutes - 1440 }
  ]
}

/**
 * Check if a proposed zone overlaps any existing resolved zones.
 */
function hasOverlap(newZone, existingZones) {
  const newSegs = getSegments(newZone)
  for (const zone of existingZones) {
    // Skip zones from the same rule (when editing)
    if (newZone.ruleId && zone.ruleId === newZone.ruleId) continue
    const existingSegs = getSegments(zone)
    for (const a of newSegs) {
      for (const b of existingSegs) {
        if (a.start < b.end && a.end > b.start) {
          return true
        }
      }
    }
  }
  return false
}

export function useBatteryRules() {
  /**
   * All resolved zones computed from current rules + pricing data.
   */
  function getResolvedZones(pricingData) {
    return computed(() => {
      const allZones = []
      const data = pricingData.value || pricingData
      for (const rule of rules.value) {
        const ruleZones = evaluateZoneRule(rule, data)
        allZones.push(...ruleZones)
      }
      return allZones.sort((a, b) => a.startMinutes - b.startMinutes)
    })
  }

  function addRule(ruleData) {
    const rule = {
      id: generateId(),
      ...ruleData
    }
    markSaving(rule.id)
    rules.value.push(rule)
    return rule
  }

  function updateRule(ruleId, ruleData) {
    const index = rules.value.findIndex(r => r.id === ruleId)
    if (index !== -1) {
      markSaving(ruleId)
      rules.value[index] = { id: ruleId, ...ruleData }
    }
  }

  function deleteRule(ruleId) {
    markSaving(ruleId)
    rules.value = rules.value.filter(r => r.id !== ruleId)
  }

  function getRuleById(ruleId) {
    return rules.value.find(r => r.id === ruleId) || null
  }

  function clearAllRules() {
    rules.value = []
  }

  return {
    rules,
    loading,
    saveError,
    savingRuleIds,
    getResolvedZones,
    addRule,
    updateRule,
    deleteRule,
    getRuleById,
    clearAllRules,
    hasOverlap
  }
}
