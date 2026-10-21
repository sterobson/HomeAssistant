import { describe, expect, it } from 'vitest'
import { boilerSegments, minutesOn, stateLabel, timesOn } from './boilerHistory.js'

const from = new Date('2026-10-20T12:00:00Z')
const to = new Date('2026-10-20T20:00:00Z')
const ev = (at, on, appControlsBoiler = true) => ({ at, on, appControlsBoiler })
const shape = (segments) => segments.map(s => [s.start.toISOString().slice(11, 16), s.end.toISOString().slice(11, 16), s.state])

describe('boilerSegments', () => {
  it('starts from the state before the window and cuts at each change', () => {
    const segments = boilerSegments({
      before: ev('2026-10-20T11:00:00Z', false),
      events: [ev('2026-10-20T13:31:00Z', true), ev('2026-10-20T14:52:00Z', false), ev('2026-10-20T17:05:00Z', true), ev('2026-10-20T18:02:00Z', false)]
    }, from, to)

    expect(shape(segments)).toEqual([
      ['12:00', '13:31', 'off'],
      ['13:31', '14:52', 'on'],
      ['14:52', '17:05', 'off'],
      ['17:05', '18:02', 'on'],
      ['18:02', '20:00', 'off']
    ])
    expect(minutesOn(segments)).toBe(81 + 57)
    expect(timesOn(segments, from)).toBe(2)
  })

  it('is unknown before the first record, and sorts events it is given out of order', () => {
    const segments = boilerSegments({ before: null, events: [ev('2026-10-20T16:00:00Z', false), ev('2026-10-20T15:00:00Z', true)] }, from, to)
    expect(shape(segments)).toEqual([['12:00', '15:00', 'unknown'], ['15:00', '16:00', 'on'], ['16:00', '20:00', 'off']])
  })

  it('shows the boiler thermostat stretches, and merges repeats of the same state', () => {
    const segments = boilerSegments({
      before: ev('2026-10-20T09:00:00Z', false, false),
      events: [ev('2026-10-20T13:00:00Z', false, false), ev('2026-10-20T14:00:00Z', false)]
    }, from, to)
    expect(shape(segments)).toEqual([['12:00', '14:00', 'thermostat'], ['14:00', '20:00', 'off']])
  })

  it('counts an on that began before the window as on, but not as a time it was turned on', () => {
    const segments = boilerSegments({ before: ev('2026-10-20T11:00:00Z', true), events: [] }, from, to)
    expect(minutesOn(segments)).toBe(480)
    expect(timesOn(segments, from)).toBe(0)
  })

  it('ignores events after the window', () => {
    expect(shape(boilerSegments({ before: ev('2026-10-20T11:00:00Z', false), events: [ev('2026-10-20T21:00:00Z', true)] }, from, to)))
      .toEqual([['12:00', '20:00', 'off']])
  })
})

describe('inferred stretches', () => {
  it('keep their flag, and are not merged with the app\'s own record of the same state', () => {
    const segments = boilerSegments({
      before: { ...ev('2026-10-20T11:00:00Z', true), inferred: true },
      events: [ev('2026-10-20T14:00:00Z', true)]
    }, from, to)
    expect(segments.map(s => [s.state, s.inferred])).toEqual([['on', true], ['on', false]])
    expect(minutesOn(segments)).toBe(480)
  })
})

describe('stateLabel', () => {
  it('names each state in sentence case', () => {
    expect(stateLabel('thermostat')).toBe('Run by the boiler thermostat')
    expect(stateLabel('unknown')).toBe('Not recorded')
  })
})
