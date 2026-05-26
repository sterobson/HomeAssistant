import { describe, it, expect } from 'vitest'
import { distribute, buildPreviewPoints } from './energyDistribution.js'

describe('distribute', () => {
  describe('proportional path (comparison sums to non-zero)', () => {
    it('reproduces the comparison when target equals comparison sum', () => {
      expect(distribute(10, [1, 2, 7], 4)).toEqual([1, 2, 7])
    })

    it('scales each cell by target/sum when target differs', () => {
      expect(distribute(100, [1, 2, 7], 4)).toEqual([10, 20, 70])
    })

    it('preserves sign when target and comparison sum share a sign', () => {
      // sum = 10, factor = 1 → original shape
      expect(distribute(10, [2, -1, 9], 4)).toEqual([2, -1, 9])
    })

    it('inverts signs when target sign differs from comparison sum sign', () => {
      // sum = 10, factor = -1 → all signs flip
      expect(distribute(-10, [2, -1, 9], 4)).toEqual([-2, 1, -9])
    })

    it('returns all zeros when target is zero, regardless of comparison shape', () => {
      expect(distribute(0, [5, 10, 15], 4)).toEqual([0, 0, 0])
    })

    it('sums very close to target after rounding (24 cells, fractional)', () => {
      const result = distribute(1, new Array(24).fill(1), 4)
      const sum = result.reduce((s, v) => s + v, 0)
      // Each cell rounded to 4dp; worst-case drift is 24 × 5e-5 = 1.2e-3
      expect(Math.abs(sum - 1)).toBeLessThanOrEqual(0.0012)
    })
  })

  describe('zero-sum fallback (even distribution)', () => {
    it('spreads target evenly when comparison values are all zero', () => {
      expect(distribute(8, [0, 0, 0, 0], 4)).toEqual([2, 2, 2, 2])
    })

    it('spreads target evenly when positives and negatives cancel', () => {
      // Comparison shape exists but sum=0 — there's no proportional shape to follow
      expect(distribute(12, [1, -1, 0, 0], 4)).toEqual([3, 3, 3, 3])
    })

    it('returns zeros when target and comparison are both zero', () => {
      expect(distribute(0, [0, 0, 0], 4)).toEqual([0, 0, 0])
    })

    it('handles an empty comparison array by returning an empty array', () => {
      expect(distribute(10, [], 4)).toEqual([])
    })
  })

  describe('rounding', () => {
    it('rounds to 4 decimal places by default-ish (1/3 ≈ 0.3333)', () => {
      expect(distribute(1, [1, 1, 1], 4)).toEqual([0.3333, 0.3333, 0.3333])
    })

    it('honours a custom decimals parameter (2 dp for pence)', () => {
      expect(distribute(1, [1, 1, 1], 2)).toEqual([0.33, 0.33, 0.33])
    })

    it('rounds half to even / nearest at the requested precision', () => {
      // 5/4 = 1.25 → 2dp keeps it as 1.25
      expect(distribute(5, [1, 1, 1, 1], 2)).toEqual([1.25, 1.25, 1.25, 1.25])
    })
  })
})

describe('buildPreviewPoints', () => {
  function blankHour(h) {
    return {
      hour: h,
      gridKwh: 0,
      batteryKwh: 0,
      solarKwh: 0,
      houseKwh: 0,
      importCostPence: 0,
      exportCostPence: 0
    }
  }

  function blankDay() {
    return new Array(24).fill(null).map((_, h) => blankHour(h))
  }

  it('returns an empty array when the comparison is not exactly 24 hours', () => {
    expect(buildPreviewPoints({ gridKwh: 10 }, [])).toEqual([])
    expect(buildPreviewPoints({ gridKwh: 10 }, new Array(23).fill(blankHour(0)))).toEqual([])
    expect(buildPreviewPoints({ gridKwh: 10 }, new Array(25).fill(blankHour(0)))).toEqual([])
  })

  it('returns an empty array when comparisonHours is not an array', () => {
    expect(buildPreviewPoints({ gridKwh: 10 }, null)).toEqual([])
    expect(buildPreviewPoints({ gridKwh: 10 }, undefined)).toEqual([])
  })

  it('produces 24 ordered points with hour 0..23', () => {
    const comparison = blankDay()
    comparison[0].solarKwh = 1
    const points = buildPreviewPoints({ solarKwh: 1 }, comparison)
    expect(points.length).toBe(24)
    points.forEach((p, h) => expect(p.hour).toBe(h))
  })

  it('distributes each component independently using its own shape', () => {
    const comparison = blankDay()
    comparison[0].gridKwh = 1
    comparison[1].gridKwh = 1
    comparison[0].batteryKwh = 2
    comparison[1].batteryKwh = -1
    comparison[1].solarKwh = 5

    const points = buildPreviewPoints({
      gridKwh: 4,
      batteryKwh: 1,
      solarKwh: 10,
      houseKwh: 0,
      importCostPence: 0,
      exportCostPence: 0
    }, comparison)

    // Grid: 4 across [1,1,0...] → shares [0.5,0.5,0...] → [2,2,0,...]
    expect(points[0].gridKwh).toBe(2)
    expect(points[1].gridKwh).toBe(2)
    expect(points[2].gridKwh).toBe(0)

    // Battery: 1 across [2,-1,0...] sum=1, factor=1 → [2,-1,0...]
    expect(points[0].batteryKwh).toBe(2)
    expect(points[1].batteryKwh).toBe(-1)

    // Solar: 10 across [0,5,0...] sum=5, factor=2 → [0,10,0...]
    expect(points[0].solarKwh).toBe(0)
    expect(points[1].solarKwh).toBe(10)
  })

  it('uses 2dp for the cost fields and 4dp for kWh fields', () => {
    const comparison = blankDay()
    comparison.forEach(h => {
      h.gridKwh = 1
      h.importCostPence = 1
    })

    const points = buildPreviewPoints({
      gridKwh: 1, batteryKwh: 0, solarKwh: 0, houseKwh: 0,
      importCostPence: 1, exportCostPence: 0
    }, comparison)

    // 1/24 → kWh rounds to 0.0417, pence rounds to 0.04
    expect(points[0].gridKwh).toBe(0.0417)
    expect(points[0].importCostPence).toBe(0.04)
  })

  it('falls back to even distribution per component when that component sums to zero', () => {
    const comparison = blankDay()
    // No solar in comparison day (all zeros), but user enters a solar total
    comparison[0].gridKwh = 1
    comparison[1].gridKwh = 1

    const points = buildPreviewPoints({
      gridKwh: 0,
      batteryKwh: 0,
      solarKwh: 4.8, // 4.8 / 24 = 0.2 per hour
      houseKwh: 0,
      importCostPence: 0,
      exportCostPence: 0
    }, comparison)

    points.forEach(p => expect(p.solarKwh).toBe(0.2))
  })

  it('treats missing component fields on the comparison as zero', () => {
    // Hour records without solarKwh / batteryKwh — buildPreviewPoints should not NaN
    const comparison = new Array(24).fill(null).map((_, h) => ({ hour: h, gridKwh: 1 }))
    const points = buildPreviewPoints({
      gridKwh: 24, batteryKwh: 0, solarKwh: 0, houseKwh: 0,
      importCostPence: 0, exportCostPence: 0
    }, comparison)
    expect(points.every(p => p.gridKwh === 1)).toBe(true)
    expect(points.every(p => p.solarKwh === 0)).toBe(true)
    expect(points.every(p => Number.isFinite(p.gridKwh))).toBe(true)
  })

  it('treats missing totals fields as zero', () => {
    const comparison = blankDay()
    comparison[0].solarKwh = 5
    // totals object is intentionally sparse
    const points = buildPreviewPoints({ solarKwh: 10 }, comparison)
    expect(points[0].solarKwh).toBe(10)
    expect(points[0].gridKwh).toBe(0)
    expect(points[0].batteryKwh).toBe(0)
    expect(points[0].importCostPence).toBe(0)
  })
})
