import { describe, expect, it } from 'vitest'
import {
  badgeTitle,
  clearedIssues,
  describeHistory,
  describeWhatWasSeen,
  flaggedIssues,
  flaggedValveIssues,
  formatWhen,
  valveIssueLabel
} from './valveHealth.js'

const leak = (extra = {}) => ({
  kind: 'leaksWhenShut',
  flagged: true,
  count: 1,
  lastSeenAt: '2026-10-23T14:40:00Z',
  lastSeenDetail: 'Rose from 20.1 °C to 27.1 °C while shut, with hot water flowing',
  fromTemperature: 20.1,
  toTemperature: 27.1,
  lastOkAt: null,
  ...extra
})

const cold = (extra = {}) => ({
  kind: 'doesNotWarm',
  flagged: true,
  count: 1,
  lastSeenAt: '2026-10-19T13:20:00Z',
  fromTemperature: 19.5,
  toTemperature: 19.5,
  minutes: 60,
  ...extra
})

const valve = (valveHealth, extra = {}) => ({ role: 'radiatorValve', entityId: 'climate.den_radiator_2_thermostat', name: 'Den radiator 2', valveHealth, ...extra })
const celsius = (value) => `${value.toFixed(1)}°C`

describe('flaggedIssues and clearedIssues', () => {
  it('splits a valve\'s findings into those standing and those a later run cleared', () => {
    const device = valve([leak(), cold({ flagged: false, lastOkAt: '2026-10-23T04:30:00Z' })])
    expect(flaggedIssues(device).map(i => i.kind)).toEqual(['leaksWhenShut'])
    expect(clearedIssues(device).map(i => i.kind)).toEqual(['doesNotWarm'])
  })

  it('leaves out a kind that was only ever checked and found fine', () => {
    expect(clearedIssues(valve([leak({ flagged: false, count: 0, lastSeenAt: null, lastOkAt: '2026-10-23T09:20:00Z' })]))).toEqual([])
  })

  it('has nothing for a device that is not a valve, or from an app too old to send it', () => {
    expect(flaggedIssues({ role: 'heaterPlug', valveHealth: [leak()] })).toEqual([])
    expect(flaggedIssues(valve(undefined))).toEqual([])
  })
})

describe('flaggedValveIssues', () => {
  it('lists every flagged fault in a room with its valve', () => {
    const radiator2 = valve([leak()])
    const radiator1 = valve([], { entityId: 'climate.den_radiator_1_thermostat' })
    const flagged = flaggedValveIssues([radiator1, radiator2, { role: 'thermometer' }])
    expect(flagged).toEqual([{ device: radiator2, issue: radiator2.valveHealth[0] }])
    expect(badgeTitle(flagged, d => d.name)).toBe('Den radiator 2: lets hot water through while shut')
  })
})

describe('formatWhen', () => {
  const now = new Date(2026, 9, 23, 18, 0)

  it('says today, yesterday, or the date', () => {
    expect(formatWhen(new Date(2026, 9, 23, 15, 5).toISOString(), now)).toBe('today at 15:05')
    expect(formatWhen(new Date(2026, 9, 22, 9, 0).toISOString(), now)).toBe('yesterday at 09:00')
    expect(formatWhen(new Date(2026, 9, 20, 9, 50).toISOString(), now)).toBe('on 20 Oct at 09:50')
  })

  it('uses the user\'s time format', () => {
    expect(formatWhen(new Date(2026, 9, 23, 15, 5).toISOString(), now, () => '3:05 PM')).toBe('today at 3:05 PM')
  })
})

describe('describeWhatWasSeen', () => {
  it('puts a leak in the user\'s unit', () => {
    expect(describeWhatWasSeen(leak(), celsius))
      .toBe('Its radiator warmed from 20.1°C to 27.1°C while the valve was shut and hot water was flowing.')
    expect(describeWhatWasSeen(leak(), v => `${(v * 9 / 5 + 32).toFixed(0)}°F`))
      .toBe('Its radiator warmed from 68°F to 81°F while the valve was shut and hot water was flowing.')
  })

  it('says how long a cold valve was open and whether it moved at all', () => {
    expect(describeWhatWasSeen(cold(), celsius))
      .toBe('It was open for 1 h while another radiator warmed, but its radiator stayed at 19.5°C.')
    expect(describeWhatWasSeen(cold({ toTemperature: 20, minutes: 85 }), celsius))
      .toBe('It was open for 1 h 25 min while another radiator warmed, but its radiator only got from 19.5°C to 20.0°C.')
  })

  it('falls back to the app\'s own words without the numbers', () => {
    expect(describeWhatWasSeen(leak({ fromTemperature: null }), celsius))
      .toBe('Rose from 20.1 °C to 27.1 °C while shut, with hot water flowing.')
  })
})

describe('describeHistory', () => {
  const when = (iso) => `at ${iso.slice(11, 16)}`

  it('counts the sightings of a flagged fault', () => {
    expect(describeHistory(leak({ count: 2 }), when)).toBe('Seen twice, last at 14:40.')
  })

  it('mentions a clean run that came before the latest sighting', () => {
    expect(describeHistory(leak({ lastOkAt: '2026-10-20T16:20:00Z' }), when))
      .toBe('Seen once, last at 14:40. It looked fine at 16:20, before that.')
  })

  it('says a cleared fault is not flagged now', () => {
    expect(describeHistory(leak({ flagged: false, count: 3, lastOkAt: '2026-10-24T08:00:00Z' }), when))
      .toBe('Seen 3 times, last at 14:40. Looked fine at 08:00, so it is not flagged now.')
  })
})

describe('valveIssueLabel', () => {
  it('names each kind in sentence case', () => {
    expect(valveIssueLabel('leaksWhenShut')).toBe('Lets hot water through while shut')
    expect(valveIssueLabel('doesNotWarm')).toBe('Stays cold while open')
  })
})
