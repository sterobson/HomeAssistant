import { describe, it, expect } from 'vitest'
import {
  DEFAULT_LIMITS,
  clampDuration,
  clampTemperature,
  durationOptions,
  formatDuration,
  formatEndTime,
  formatTemperature,
  isOverrideActive,
  stepTemperature,
  validateOverride
} from './guestOverride.js'

describe('durationOptions', () => {
  it('runs from 30 minutes to 6 hours in 30-minute steps', () => {
    const options = durationOptions(DEFAULT_LIMITS)
    expect(options).toHaveLength(12)
    expect(options[0]).toEqual({ minutes: 30, label: '30 minutes' })
    expect(options[1]).toEqual({ minutes: 60, label: '1 hour' })
    expect(options[2].label).toBe('1 hour 30 minutes')
    expect(options.at(-1)).toEqual({ minutes: 360, label: '6 hours' })
  })

  it('follows a different server limit', () => {
    expect(durationOptions({ maxMinutes: 120 }).map(o => o.minutes)).toEqual([30, 60, 90, 120])
  })

  it('uses the defaults without limits', () => {
    expect(durationOptions()).toHaveLength(12)
  })
})

describe('formatDuration', () => {
  it('uses singular and plural correctly', () => {
    expect(formatDuration(30)).toBe('30 minutes')
    expect(formatDuration(60)).toBe('1 hour')
    expect(formatDuration(120)).toBe('2 hours')
    expect(formatDuration(150)).toBe('2 hours 30 minutes')
  })
})

describe('clampTemperature', () => {
  it('keeps values inside the limits', () => {
    expect(clampTemperature(10)).toBe(16)
    expect(clampTemperature(30)).toBe(24)
    expect(clampTemperature(20)).toBe(20)
  })

  it('rounds to the nearest half degree', () => {
    expect(clampTemperature(19.8)).toBe(20)
    expect(clampTemperature(19.2)).toBe(19)
    expect(clampTemperature(19.3)).toBe(19.5)
  })

  it('falls back to 20 for anything that is not a number', () => {
    expect(clampTemperature(NaN)).toBe(20)
    expect(clampTemperature(undefined)).toBe(20)
    expect(clampTemperature('21')).toBe(20)
  })

  it('respects custom limits', () => {
    expect(clampTemperature(20, { minTemperature: 17, maxTemperature: 19 })).toBe(19)
  })
})

describe('stepTemperature', () => {
  it('moves by half a degree and stops at the limits', () => {
    expect(stepTemperature(20, 1)).toBe(20.5)
    expect(stepTemperature(20, -1)).toBe(19.5)
    expect(stepTemperature(24, 1)).toBe(24)
    expect(stepTemperature(16, -1)).toBe(16)
  })
})

describe('clampDuration', () => {
  it('stays between 30 minutes and the limit', () => {
    expect(clampDuration(0)).toBe(30)
    expect(clampDuration(1000)).toBe(360)
  })

  it('snaps to a 30-minute step', () => {
    expect(clampDuration(70)).toBe(60)
    expect(clampDuration(80)).toBe(90)
  })

  it('falls back to one hour for anything that is not a number', () => {
    expect(clampDuration(NaN)).toBe(60)
    expect(clampDuration(null)).toBe(60)
  })
})

describe('validateOverride', () => {
  it('accepts values on the edges of the limits', () => {
    expect(validateOverride(16, 30)).toBeNull()
    expect(validateOverride(24, 360)).toBeNull()
  })

  it('rejects a temperature outside the limits', () => {
    expect(validateOverride(15.5, 60)).toBe('The temperature must be between 16 and 24 °C.')
    expect(validateOverride(24.5, 60)).toBe('The temperature must be between 16 and 24 °C.')
  })

  it('rejects a duration that is too short or too long', () => {
    expect(validateOverride(20, 0)).toBe('Choose how long it should last.')
    expect(validateOverride(20, 390)).toBe('It can last at most 6 hours.')
  })

  it('rejects values that are not numbers', () => {
    expect(validateOverride(NaN, 60)).toBe('Choose a temperature.')
    expect(validateOverride(20, undefined)).toBe('Choose how long it should last.')
  })
})

describe('formatTemperature', () => {
  it('drops a trailing .0 and keeps a half', () => {
    expect(formatTemperature(20)).toBe('20 °C')
    expect(formatTemperature(17.5)).toBe('17.5 °C')
  })

  it('shows a dash when there is no reading', () => {
    expect(formatTemperature(null)).toBe('–')
    expect(formatTemperature(undefined)).toBe('–')
  })
})

describe('formatEndTime', () => {
  // Built from local components so the result does not depend on the machine's time zone.
  const now = new Date(2026, 9, 19, 17, 0)

  it('shows just the clock time when it ends today', () => {
    expect(formatEndTime(new Date(2026, 9, 19, 18, 30), now)).toBe('18:30')
  })

  it('says tomorrow when it ends after midnight', () => {
    expect(formatEndTime(new Date(2026, 9, 20, 1, 5), now)).toBe('Tomorrow 01:05')
  })

  it('accepts an ISO string', () => {
    const iso = new Date(2026, 9, 19, 19, 0).toISOString()
    expect(formatEndTime(iso, now)).toBe('19:00')
  })

  it('returns an empty string for a bad date', () => {
    expect(formatEndTime('not a date', now)).toBe('')
    expect(formatEndTime(undefined, now)).toBe('')
  })
})

describe('isOverrideActive', () => {
  const now = new Date(2026, 9, 19, 17, 0)

  it('is active until the end time', () => {
    expect(isOverrideActive({ endTime: new Date(2026, 9, 19, 17, 1).toISOString() }, now)).toBe(true)
    expect(isOverrideActive({ endTime: new Date(2026, 9, 19, 16, 59).toISOString() }, now)).toBe(false)
  })

  it('is not active without an override', () => {
    expect(isOverrideActive(null, now)).toBe(false)
    expect(isOverrideActive({}, now)).toBe(false)
  })
})
