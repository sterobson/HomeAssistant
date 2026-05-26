// Pure functions used by the manual-import page to spread daily totals
// across 24 hours using another day's hourly shape. Kept dependency-free
// (no Vue imports) so they can be unit-tested in isolation.

/**
 * Distribute a target total across N hours, weighted by a "comparison"
 * array of per-hour values for the same component.
 *
 *  - When the comparison sums to non-zero, each output hour gets
 *    `targetTotal * (comparisonValue / comparisonSum)`. Signs are
 *    preserved when target and comparison-sum share a sign, and inverted
 *    when they don't (mathematically consistent — the user is asking for
 *    an inverted day).
 *
 *  - When the comparison sums to zero (whether because all values are
 *    zero, or because positives and negatives cancel), there is no
 *    shape to follow, so the target is spread evenly across the hours.
 *
 * Each output cell is rounded to `decimals` decimal places. The sum of
 * the rounded outputs may differ from `targetTotal` by up to ~½ × 10^-decimals
 * per cell.
 *
 * @param {number} targetTotal       The number the outputs should (approximately) sum to.
 * @param {number[]} comparisonValues The per-hour comparison values.
 * @param {number} decimals          Decimal places to round each output to.
 * @returns {number[]}
 */
export function distribute(targetTotal, comparisonValues, decimals = 4) {
  const factor = Math.pow(10, decimals)
  const sum = comparisonValues.reduce((s, v) => s + v, 0)

  if (sum === 0) {
    const n = comparisonValues.length
    if (n === 0) return []
    const each = targetTotal / n
    const rounded = Math.round(each * factor) / factor
    return comparisonValues.map(() => rounded)
  }

  return comparisonValues.map(v => Math.round(targetTotal * (v / sum) * factor) / factor)
}

/**
 * Build 24 hourly energy points by distributing each daily total across
 * the comparison day's hourly shape. The comparison array must be exactly
 * 24 entries (callers should zero-fill missing hours first).
 *
 * @param {object} totals - { gridKwh, batteryKwh, solarKwh, houseKwh, importCostPence, exportCostPence }
 * @param {object[]} comparisonHours - 24 entries with the same field names
 * @returns {object[]} 24 entries with { hour, gridKwh, ..., exportCostPence }
 */
export function buildPreviewPoints(totals, comparisonHours) {
  if (!Array.isArray(comparisonHours) || comparisonHours.length !== 24) {
    return []
  }

  const compGrid = comparisonHours.map(p => p.gridKwh || 0)
  const compBattery = comparisonHours.map(p => p.batteryKwh || 0)
  const compSolar = comparisonHours.map(p => p.solarKwh || 0)
  const compHouse = comparisonHours.map(p => p.houseKwh || 0)
  const compImportCost = comparisonHours.map(p => p.importCostPence || 0)
  const compExportCost = comparisonHours.map(p => p.exportCostPence || 0)

  const gridOut = distribute(totals.gridKwh || 0, compGrid, 4)
  const batteryOut = distribute(totals.batteryKwh || 0, compBattery, 4)
  const solarOut = distribute(totals.solarKwh || 0, compSolar, 4)
  const houseOut = distribute(totals.houseKwh || 0, compHouse, 4)
  const importCostOut = distribute(totals.importCostPence || 0, compImportCost, 2)
  const exportCostOut = distribute(totals.exportCostPence || 0, compExportCost, 2)

  const points = []
  for (let h = 0; h < 24; h++) {
    points.push({
      hour: h,
      gridKwh: gridOut[h],
      batteryKwh: batteryOut[h],
      solarKwh: solarOut[h],
      houseKwh: houseOut[h],
      importCostPence: importCostOut[h],
      exportCostPence: exportCostOut[h]
    })
  }
  return points
}
