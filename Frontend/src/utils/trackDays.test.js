import { describe, expect, it } from 'vitest'
import { dayFlag, runsOn } from './trackDays.js'

// 2026-10-19 was a Monday.
const monday = new Date(2026, 9, 19, 12, 0)
const sunday = new Date(2026, 9, 25, 23, 59)

describe('dayFlag', () => {
  it('numbers Monday 1 through Sunday 64, like the app', () => {
    expect(dayFlag(monday)).toBe(1)
    expect(dayFlag(new Date(2026, 9, 21))).toBe(4)
    expect(dayFlag(sunday)).toBe(64)
  })
})

describe('runsOn', () => {
  it('runs every day when no days are set', () => {
    expect(runsOn(0, monday)).toBe(true)
    expect(runsOn(undefined, sunday)).toBe(true)
    expect(runsOn(127, sunday)).toBe(true)
  })

  it('runs only on the days chosen', () => {
    expect(runsOn(31, monday)).toBe(true) // weekdays
    expect(runsOn(31, sunday)).toBe(false)
    expect(runsOn(96, sunday)).toBe(true) // weekends
    expect(runsOn(96, monday)).toBe(false)
  })
})
