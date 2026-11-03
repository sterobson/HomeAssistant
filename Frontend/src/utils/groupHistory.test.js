import { describe, expect, it } from 'vitest'
import { mergeGroupHistory } from './groupHistory.js'

const point = (time, currentTemperature, targetTemperature = null) => ({ timestamp: `2026-11-03T${time}:00Z`, currentTemperature, targetTemperature })
const at = (merged, time) => merged.find(p => p.timestamp === `2026-11-03T${time}:00.000Z`)

describe('mergeGroupHistory', () => {
  it('takes the lowest and highest temperature and target across the zones at each moment', () => {
    const merged = mergeGroupHistory([
      [point('06:00', 18, 20), point('08:00', 20, 20)],
      [point('06:00', 21, 22), point('09:00', 19, 18)]
    ])

    expect(merged.map(p => p.timestamp.slice(11, 16))).toEqual(['06:00', '08:00', '09:00'])
    expect(at(merged, '06:00')).toMatchObject({ currentTemperature: 18, currentTemperatureMax: 21, targetTemperature: 20, targetTemperatureMax: 22 })
    // The second zone's 06:00 reading holds until its next point.
    expect(at(merged, '08:00')).toMatchObject({ currentTemperature: 20, currentTemperatureMax: 21, targetTemperature: 20, targetTemperatureMax: 22 })
    expect(at(merged, '09:00')).toMatchObject({ currentTemperature: 19, currentTemperatureMax: 20, targetTemperature: 18, targetTemperatureMax: 20 })
  })

  it('gives the mean of the zones that had a reading', () => {
    const merged = mergeGroupHistory([[point('06:00', 18)], [point('06:00', 22)]])
    expect(merged[0].currentTemperatureMean).toBe(20)
  })

  it('leaves a zone out until it has reported, and where it has no value', () => {
    const merged = mergeGroupHistory([
      [point('06:00', 18, 20)],
      [point('07:00', 22, null)] // no target, like a thermometer-only zone
    ])
    expect(at(merged, '06:00')).toMatchObject({ currentTemperature: 18, currentTemperatureMax: 18 })
    expect(at(merged, '07:00')).toMatchObject({ currentTemperature: 18, currentTemperatureMax: 22, targetTemperature: 20, targetTemperatureMax: 20 })
  })

  it('has no range where no zone has a value', () => {
    const merged = mergeGroupHistory([[point('06:00', null, null)]])
    expect(merged[0]).toMatchObject({ currentTemperature: null, currentTemperatureMax: null, currentTemperatureMean: null, targetTemperature: null, targetTemperatureMax: null })
  })

  it('copes with zones with no history at all, and unsorted points', () => {
    expect(mergeGroupHistory([])).toEqual([])
    expect(mergeGroupHistory([[], []])).toEqual([])
    const merged = mergeGroupHistory([[point('08:00', 20), point('06:00', 18)]])
    expect(merged.map(p => p.currentTemperature)).toEqual([18, 20])
  })
})
