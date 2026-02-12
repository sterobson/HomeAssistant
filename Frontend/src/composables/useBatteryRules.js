// Battery zone-rule management with API persistence (localStorage fallback)
import { ref, computed, watch } from 'vue'
import { batteryApi } from '../services/batteryApi.js'
import {
  findExportExceedsImportCrossovers,
  findImportExceedsExportCrossovers,
  findPeaks,
  findTroughs,
  findMinimaRegionBoundaries,
  findMaximaRegionBoundaries
} from '../utils/priceAnalysis.js'

const STORAGE_KEY = 'battery-zone-rules'

function generateId() {
  return Date.now().toString(36) + Math.random().toString(36).substring(2, 9)
}

function loadFromLocalStorage() {
  try {
    const stored = localStorage.getItem(STORAGE_KEY)
    if (stored) {
      const rules = JSON.parse(stored)
      for (const rule of rules) {
        if (rule.action === 'charge') rule.action = 'import'
        if (rule.action === 'discharge') rule.action = 'export'
      }
      return rules
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

let saveTimeout = null

async function loadFromApi() {
  try {
    loading.value = true
    const response = await batteryApi.getRules()
    if (response && response.rules) {
      rules.value = response.rules
      // Clear localStorage after successful API load
      localStorage.removeItem(STORAGE_KEY)
    }
  } catch (error) {
    console.warn('Failed to load battery rules from API, using localStorage fallback:', error)
    rules.value = loadFromLocalStorage()
  } finally {
    loading.value = false
  }
}

function saveToApi() {
  clearTimeout(saveTimeout)
  saveTimeout = setTimeout(async () => {
    try {
      saveError.value = null
      await batteryApi.setRules({ rules: rules.value })
    } catch (error) {
      console.error('Failed to save battery rules to API:', error)
      saveError.value = error.message
      // Fallback: save to localStorage
      try {
        localStorage.setItem(STORAGE_KEY, JSON.stringify(rules.value))
      } catch (e) {
        console.error('Failed to save to localStorage fallback:', e)
      }
    }
  }, 500)
}

// Auto-save on changes
watch(rules, () => {
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
  const startTimes = resolveTimeDefinition(rule.startTime, pricingData).sort((a, b) => a - b)
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
        rateKw: rule.rateKw,
        startTimeType: rule.startTime?.type || 'fixed-time',
        endTimeType: rule.endTime?.type || 'fixed-time'
      })
    }
  }

  // Second pass: wrap around midnight for unmatched starts
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
      const actualEnd = endTimes[bestEndIndex]
      const baseZone = {
        ruleId: rule.id,
        action: rule.action,
        targetPercent: rule.targetPercent,
        rateMode: rule.rateMode || 'by-end',
        rateKw: rule.rateKw,
        startTimeType: rule.startTime?.type || 'fixed-time',
        endTimeType: rule.endTime?.type || 'fixed-time',
        _fullZoneStart: start,
        _fullZoneEnd: bestEnd
      }
      // Evening segment: start → midnight
      zones.push({ ...baseZone, startMinutes: start, endMinutes: 1440 })
      // Morning segment: midnight → end
      if (actualEnd > 0) {
        zones.push({ ...baseZone, startMinutes: 0, endMinutes: actualEnd })
      }
    }
  }

  return zones
}

/**
 * Check if a proposed zone overlaps any existing resolved zones.
 */
function hasOverlap(newZone, existingZones) {
  for (const zone of existingZones) {
    // Skip zones from the same rule (when editing)
    if (newZone.ruleId && zone.ruleId === newZone.ruleId) continue
    if (newZone.startMinutes < zone.endMinutes && newZone.endMinutes > zone.startMinutes) {
      return true
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
    rules.value.push(rule)
    return rule
  }

  function updateRule(ruleId, ruleData) {
    const index = rules.value.findIndex(r => r.id === ruleId)
    if (index !== -1) {
      rules.value[index] = { id: ruleId, ...ruleData }
    }
  }

  function deleteRule(ruleId) {
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
    getResolvedZones,
    addRule,
    updateRule,
    deleteRule,
    getRuleById,
    clearAllRules,
    hasOverlap
  }
}
