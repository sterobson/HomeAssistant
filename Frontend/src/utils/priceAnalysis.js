// Pure functions for analysing pricing data arrays.
// Each entry: { timeMinutes, importPrice, exportPrice }

/**
 * Check whether price `a` exceeds price `b` by the given threshold.
 * threshold: { type: 'absolute', value: £ } or { type: 'percent', value: % } or null.
 * Prices are in p/kWh; absolute threshold value is in £ (multiply by 100 for pence).
 */
function exceedsWithThreshold(a, b, threshold) {
  if (!threshold || !threshold.type) return a > b
  if (threshold.type === 'absolute') return a > b + threshold.value * 100
  if (threshold.type === 'percent') return a > b * (1 + threshold.value / 100)
  return a > b
}

/**
 * Find points where export price crosses above import price (with optional threshold).
 * Returns the slot where export first exceeds import.
 */
export function findExportExceedsImportCrossovers(data, threshold) {
  const crossovers = []
  for (let i = 1; i < data.length; i++) {
    const prev = data[i - 1]
    const curr = data[i]
    if (!exceedsWithThreshold(prev.exportPrice, prev.importPrice, threshold)
        && exceedsWithThreshold(curr.exportPrice, curr.importPrice, threshold)) {
      crossovers.push(curr)
    }
  }
  return crossovers
}

/**
 * Find points where import price crosses above export price (with optional threshold).
 * Returns the slot where import first exceeds export.
 */
export function findImportExceedsExportCrossovers(data, threshold) {
  const crossovers = []
  for (let i = 1; i < data.length; i++) {
    const prev = data[i - 1]
    const curr = data[i]
    if (!exceedsWithThreshold(prev.importPrice, prev.exportPrice, threshold)
        && exceedsWithThreshold(curr.importPrice, curr.exportPrice, threshold)) {
      crossovers.push(curr)
    }
  }
  return crossovers
}

/**
 * Find local maxima (peaks) for a given price key ('importPrice' or 'exportPrice').
 * Handles plateau data: a flat run of equal values counts as a peak if the values
 * on both sides are lower. Returns the middle slot of each peak plateau.
 */
export function findPeaks(data, priceKey) {
  if (data.length < 2) return []

  const peaks = []
  let i = 0

  while (i < data.length) {
    // Find the end of the current plateau (consecutive equal values)
    let j = i
    while (j < data.length - 1 && data[j + 1][priceKey] === data[i][priceKey]) {
      j++
    }

    const currVal = data[i][priceKey]
    const prevVal = i > 0 ? data[i - 1][priceKey] : null
    const nextVal = j < data.length - 1 ? data[j + 1][priceKey] : null

    // A plateau is a peak if it's higher than both neighbours.
    // Both neighbours must exist — edge-of-data plateaus are not peaks.
    if (prevVal !== null && nextVal !== null && currVal > prevVal && currVal > nextVal) {
      const midIndex = Math.floor((i + j) / 2)
      peaks.push(data[midIndex])
    }

    i = j + 1
  }

  return peaks
}

/**
 * Find local minima (troughs) for a given price key.
 * Handles plateau data: a flat run of equal values counts as a trough if the values
 * on both sides are higher. Returns the middle slot of each trough plateau.
 */
export function findTroughs(data, priceKey) {
  if (data.length < 2) return []

  const troughs = []
  let i = 0

  while (i < data.length) {
    let j = i
    while (j < data.length - 1 && data[j + 1][priceKey] === data[i][priceKey]) {
      j++
    }

    const currVal = data[i][priceKey]
    const prevVal = i > 0 ? data[i - 1][priceKey] : null
    const nextVal = j < data.length - 1 ? data[j + 1][priceKey] : null

    // A plateau is a trough if it's lower than both neighbours.
    // Both neighbours must exist — edge-of-data plateaus are not troughs.
    if (prevVal !== null && nextVal !== null && currVal < prevVal && currVal < nextVal) {
      const midIndex = Math.floor((i + j) / 2)
      troughs.push(data[midIndex])
    }

    i = j + 1
  }

  return troughs
}

/**
 * Find boundaries where an upward trend starts/ends for a given price key.
 * "Start" = transition from flat/decreasing to increasing.
 * "End" = transition from increasing to flat/decreasing.
 */
export function findIncreaseBoundaries(data, priceKey) {
  const boundaries = []
  for (let i = 1; i < data.length; i++) {
    const prevDelta = i >= 2 ? data[i - 1][priceKey] - data[i - 2][priceKey] : 0
    const currDelta = data[i][priceKey] - data[i - 1][priceKey]
    // Start of increase: previous was not increasing, current is
    if (prevDelta <= 0 && currDelta > 0) {
      boundaries.push({ ...data[i - 1], boundaryType: 'start' })
    }
    // End of increase: previous was increasing, current is not
    if (prevDelta > 0 && currDelta <= 0) {
      boundaries.push({ ...data[i - 1], boundaryType: 'end' })
    }
  }
  return boundaries
}

/**
 * Find boundaries where a downward trend starts/ends for a given price key.
 */
export function findDecreaseBoundaries(data, priceKey) {
  const boundaries = []
  for (let i = 1; i < data.length; i++) {
    const prevDelta = i >= 2 ? data[i - 1][priceKey] - data[i - 2][priceKey] : 0
    const currDelta = data[i][priceKey] - data[i - 1][priceKey]
    if (prevDelta >= 0 && currDelta < 0) {
      boundaries.push({ ...data[i - 1], boundaryType: 'start' })
    }
    if (prevDelta < 0 && currDelta >= 0) {
      boundaries.push({ ...data[i - 1], boundaryType: 'end' })
    }
  }
  return boundaries
}

/**
 * Calculate duration-weighted average price across a full day (0–1440 minutes).
 * Each slot's price applies from its timeMinutes until the next slot starts.
 * The first slot's price back-fills to minute 0; the last slot's price forward-fills to 1440.
 * Returns null if data is empty.
 */
export function calculateAveragePrice(data, priceKey) {
  if (!data || data.length === 0) return null

  // Filter to today-only slots (0–1440) and sort by time
  const slots = data
    .filter(d => d.timeMinutes >= 0 && d.timeMinutes <= 1440)
    .sort((a, b) => a.timeMinutes - b.timeMinutes)

  if (slots.length === 0) return null

  let weightedSum = 0
  for (let i = 0; i < slots.length; i++) {
    const start = i === 0 ? 0 : slots[i].timeMinutes
    const end = i < slots.length - 1 ? slots[i + 1].timeMinutes : 1440
    const duration = end - start
    weightedSum += slots[i][priceKey] * duration
  }

  return weightedSum / 1440
}

/**
 * Find start/end of below-average price regions.
 * A region is "cheap" when the price drops to 25%+ below the day's average
 * (i.e. price <= average × 0.75).
 * Returns pairs of { ...slot, boundaryType: 'start'|'end' } boundaries.
 */
export function findMinimaRegionBoundaries(data, priceKey) {
  const avg = calculateAveragePrice(data, priceKey)
  if (avg === null) return []

  const threshold = avg * 0.75
  const boundaries = []
  let inRegion = false

  for (let i = 0; i < data.length; i++) {
    const cheap = data[i][priceKey] <= threshold
    if (cheap && !inRegion) {
      boundaries.push({ ...data[i], boundaryType: 'start' })
      inRegion = true
    } else if (!cheap && inRegion) {
      boundaries.push({ ...data[i], boundaryType: 'end' })
      inRegion = false
    }
  }

  // If still in a cheap region at end of data, synthesize end at 1440
  if (inRegion) {
    boundaries.push({ ...data[data.length - 1], timeMinutes: 1440, boundaryType: 'end' })
  }

  return boundaries
}

/**
 * Find start/end of above-average price regions.
 * A region is "expensive" when the price rises to 25%+ above the day's average
 * (i.e. price >= average × 1.25).
 * Returns pairs of { ...slot, boundaryType: 'start'|'end' } boundaries.
 */
export function findMaximaRegionBoundaries(data, priceKey) {
  const avg = calculateAveragePrice(data, priceKey)
  if (avg === null) return []

  const threshold = avg * 1.25
  const boundaries = []
  let inRegion = false

  for (let i = 0; i < data.length; i++) {
    const expensive = data[i][priceKey] >= threshold
    if (expensive && !inRegion) {
      boundaries.push({ ...data[i], boundaryType: 'start' })
      inRegion = true
    } else if (!expensive && inRegion) {
      boundaries.push({ ...data[i], boundaryType: 'end' })
      inRegion = false
    }
  }

  // If still in an expensive region at end of data, synthesize end at 1440
  if (inRegion) {
    boundaries.push({ ...data[data.length - 1], timeMinutes: 1440, boundaryType: 'end' })
  }

  return boundaries
}

/**
 * Given a time in minutes and pricing data, determine which rule types
 * are contextually available (i.e. there's a matching event within tolerance).
 * Returns an array of { type, label, description, matchCount } objects.
 */
export function getAvailableRuleTypes(timeMinutes, data) {
  const TOLERANCE = 15 // minutes

  const isNearTime = (slot) => Math.abs(slot.timeMinutes - timeMinutes) <= TOLERANCE

  const ruleTypes = [
    { type: 'fixed-time', label: 'Fixed time', description: 'Set charge at this specific time every day', matchCount: 1 }
  ]

  // Export exceeds import crossovers
  const exportCrossovers = findExportExceedsImportCrossovers(data)
  if (exportCrossovers.some(isNearTime)) {
    ruleTypes.push({
      type: 'export-exceeds-import',
      label: 'Export exceeds import',
      description: 'Trigger when export price crosses above import price',
      matchCount: exportCrossovers.length
    })
  }

  // Import exceeds export crossovers
  const importCrossovers = findImportExceedsExportCrossovers(data)
  if (importCrossovers.some(isNearTime)) {
    ruleTypes.push({
      type: 'import-exceeds-export',
      label: 'Import exceeds export',
      description: 'Trigger when import price crosses above export price',
      matchCount: importCrossovers.length
    })
  }

  // Import minima region boundaries (cheap import)
  const importMinimaRegions = findMinimaRegionBoundaries(data, 'importPrice')
  const importMinimaStarts = importMinimaRegions.filter(b => b.boundaryType === 'start')
  const importMinimaEnds = importMinimaRegions.filter(b => b.boundaryType === 'end')

  if (importMinimaStarts.some(isNearTime)) {
    ruleTypes.push({
      type: 'local-extrema-boundary',
      label: 'Start of cheap import',
      description: 'Trigger at the start of each below-average import price region',
      matchCount: importMinimaStarts.length,
      priceType: 'import',
      regionType: 'minima',
      extremaType: 'start'
    })
  }
  if (importMinimaEnds.some(isNearTime)) {
    ruleTypes.push({
      type: 'local-extrema-boundary',
      label: 'End of cheap import',
      description: 'Trigger at the end of each below-average import price region',
      matchCount: importMinimaEnds.length,
      priceType: 'import',
      regionType: 'minima',
      extremaType: 'end'
    })
  }

  // Import maxima region boundaries (expensive import)
  const importMaximaRegions = findMaximaRegionBoundaries(data, 'importPrice')
  const importMaximaStarts = importMaximaRegions.filter(b => b.boundaryType === 'start')
  const importMaximaEnds = importMaximaRegions.filter(b => b.boundaryType === 'end')

  if (importMaximaStarts.some(isNearTime)) {
    ruleTypes.push({
      type: 'local-extrema-boundary',
      label: 'Start of expensive import',
      description: 'Trigger at the start of each above-average import price region',
      matchCount: importMaximaStarts.length,
      priceType: 'import',
      regionType: 'maxima',
      extremaType: 'start'
    })
  }
  if (importMaximaEnds.some(isNearTime)) {
    ruleTypes.push({
      type: 'local-extrema-boundary',
      label: 'End of expensive import',
      description: 'Trigger at the end of each above-average import price region',
      matchCount: importMaximaEnds.length,
      priceType: 'import',
      regionType: 'maxima',
      extremaType: 'end'
    })
  }

  // Export minima region boundaries (low export)
  const exportMinimaRegions = findMinimaRegionBoundaries(data, 'exportPrice')
  const exportMinimaStarts = exportMinimaRegions.filter(b => b.boundaryType === 'start')
  const exportMinimaEnds = exportMinimaRegions.filter(b => b.boundaryType === 'end')

  if (exportMinimaStarts.some(isNearTime)) {
    ruleTypes.push({
      type: 'local-extrema-boundary',
      label: 'Start of low export',
      description: 'Trigger at the start of each below-average export price region',
      matchCount: exportMinimaStarts.length,
      priceType: 'export',
      regionType: 'minima',
      extremaType: 'start'
    })
  }
  if (exportMinimaEnds.some(isNearTime)) {
    ruleTypes.push({
      type: 'local-extrema-boundary',
      label: 'End of low export',
      description: 'Trigger at the end of each below-average export price region',
      matchCount: exportMinimaEnds.length,
      priceType: 'export',
      regionType: 'minima',
      extremaType: 'end'
    })
  }

  // Export maxima region boundaries (high export)
  const exportMaximaRegions = findMaximaRegionBoundaries(data, 'exportPrice')
  const exportMaximaStarts = exportMaximaRegions.filter(b => b.boundaryType === 'start')
  const exportMaximaEnds = exportMaximaRegions.filter(b => b.boundaryType === 'end')

  if (exportMaximaStarts.some(isNearTime)) {
    ruleTypes.push({
      type: 'local-extrema-boundary',
      label: 'Start of high export',
      description: 'Trigger at the start of each above-average export price region',
      matchCount: exportMaximaStarts.length,
      priceType: 'export',
      regionType: 'maxima',
      extremaType: 'start'
    })
  }
  if (exportMaximaEnds.some(isNearTime)) {
    ruleTypes.push({
      type: 'local-extrema-boundary',
      label: 'End of high export',
      description: 'Trigger at the end of each above-average export price region',
      matchCount: exportMaximaEnds.length,
      priceType: 'export',
      regionType: 'maxima',
      extremaType: 'end'
    })
  }

  return ruleTypes
}

/**
 * Returns all possible rule types with match counts from today's data.
 * Unlike getAvailableRuleTypes, this does not filter by time proximity.
 */
export function getAllRuleTypes(data) {
  const ruleTypes = [
    { type: 'fixed-time', label: 'Fixed time', description: 'Trigger at a specific time every day', matchCount: 1 }
  ]

  // Filter to today only (timeMinutes < 1440) so match counts aren't inflated
  // by the duplicated next-day data appended for cross-midnight zone detection.
  const todayData = data.filter(d => d.timeMinutes < 1440)

  const exportCrossovers = findExportExceedsImportCrossovers(todayData)
  ruleTypes.push({
    type: 'export-exceeds-import',
    label: 'Export exceeds import',
    description: 'Trigger when export price crosses above import price',
    matchCount: exportCrossovers.length,
    priceType: 'export'
  })

  const importCrossovers = findImportExceedsExportCrossovers(todayData)
  ruleTypes.push({
    type: 'import-exceeds-export',
    label: 'Import exceeds export',
    description: 'Trigger when import price crosses above export price',
    matchCount: importCrossovers.length,
    priceType: 'import'
  })

  const importMinimaRegions = findMinimaRegionBoundaries(todayData, 'importPrice')
  ruleTypes.push({
    type: 'local-extrema-boundary', label: 'Start of cheap import',
    description: 'Trigger at the start of each below-average import price region',
    matchCount: importMinimaRegions.filter(b => b.boundaryType === 'start').length,
    priceType: 'import', regionType: 'minima', extremaType: 'start'
  })
  ruleTypes.push({
    type: 'local-extrema-boundary', label: 'End of cheap import',
    description: 'Trigger at the end of each below-average import price region',
    matchCount: importMinimaRegions.filter(b => b.boundaryType === 'end').length,
    priceType: 'import', regionType: 'minima', extremaType: 'end'
  })

  const importMaximaRegions = findMaximaRegionBoundaries(todayData, 'importPrice')
  ruleTypes.push({
    type: 'local-extrema-boundary', label: 'Start of expensive import',
    description: 'Trigger at the start of each above-average import price region',
    matchCount: importMaximaRegions.filter(b => b.boundaryType === 'start').length,
    priceType: 'import', regionType: 'maxima', extremaType: 'start'
  })
  ruleTypes.push({
    type: 'local-extrema-boundary', label: 'End of expensive import',
    description: 'Trigger at the end of each above-average import price region',
    matchCount: importMaximaRegions.filter(b => b.boundaryType === 'end').length,
    priceType: 'import', regionType: 'maxima', extremaType: 'end'
  })

  const exportMinimaRegions = findMinimaRegionBoundaries(todayData, 'exportPrice')
  ruleTypes.push({
    type: 'local-extrema-boundary', label: 'Start of low export',
    description: 'Trigger at the start of each below-average export price region',
    matchCount: exportMinimaRegions.filter(b => b.boundaryType === 'start').length,
    priceType: 'export', regionType: 'minima', extremaType: 'start'
  })
  ruleTypes.push({
    type: 'local-extrema-boundary', label: 'End of low export',
    description: 'Trigger at the end of each below-average export price region',
    matchCount: exportMinimaRegions.filter(b => b.boundaryType === 'end').length,
    priceType: 'export', regionType: 'minima', extremaType: 'end'
  })

  const exportMaximaRegions = findMaximaRegionBoundaries(todayData, 'exportPrice')
  ruleTypes.push({
    type: 'local-extrema-boundary', label: 'Start of high export',
    description: 'Trigger at the start of each above-average export price region',
    matchCount: exportMaximaRegions.filter(b => b.boundaryType === 'start').length,
    priceType: 'export', regionType: 'maxima', extremaType: 'start'
  })
  ruleTypes.push({
    type: 'local-extrema-boundary', label: 'End of high export',
    description: 'Trigger at the end of each above-average export price region',
    matchCount: exportMaximaRegions.filter(b => b.boundaryType === 'end').length,
    priceType: 'export', regionType: 'maxima', extremaType: 'end'
  })

  return ruleTypes
}
