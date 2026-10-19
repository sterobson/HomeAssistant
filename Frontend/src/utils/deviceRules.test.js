import { describe, expect, it } from 'vitest'
import {
  drivableDevices,
  effectiveRules,
  firstSaveMap,
  normaliseRules,
  PRE_HEAT_OPTIONS,
  preHeatFor,
  presenceZones,
  summariseRules,
  validateWindow
} from './deviceRules.js'

const rules = (timeWindows, presence = null) => ({ timeWindows, presence })
const window = (start, end, days = 127) => ({ days, start, end })
const label = (name) => `<${name}>`

describe('effectiveRules', () => {
  const device = { entityId: 'switch.a', rules: rules([window('08:00', '09:00')]) }

  it('uses the device rules while the blob has never been saved', () => {
    expect(effectiveRules(device, null)).toBe(device.rules)
    expect(effectiveRules(device, undefined)).toBe(device.rules)
  })

  it('uses the blob entry once the blob exists', () => {
    const entry = rules([window('10:00', '11:00')])
    expect(effectiveRules(device, { 'switch.a': entry })).toBe(entry)
  })

  it('treats a device missing from an existing blob as having no rules', () => {
    expect(effectiveRules(device, { 'switch.other': rules([]) })).toBeNull()
  })

  it('copes with a device that has no rules', () => {
    expect(effectiveRules({ entityId: 'switch.b' }, null)).toBeNull()
  })
})

describe('summariseRules', () => {
  it('says any time with no presence for empty rules', () => {
    expect(summariseRules(rules([]), label)).toBe('Any time · no presence needed')
    expect(summariseRules(null, label)).toBe('Any time · no presence needed')
  })

  it('treats a presence rule with no sources as no presence', () => {
    expect(summariseRules(rules([], { match: 0, sources: [] }), label)).toBe('Any time · no presence needed')
  })

  it('spells out every day for a single window', () => {
    expect(summariseRules(rules([window('05:30', '22:00')], { match: 0, sources: ['house'] }), label))
      .toBe('05:30–22:00 every day · House')
  })

  it('lists several every-day windows without repeating the days', () => {
    const two = rules([window('08:00', '08:30'), window('21:00', '21:15')], { match: 0, sources: ['house'] })
    expect(summariseRules(two, label)).toBe('08:00–08:30, 21:00–21:15 · House')
  })

  it('names the days of a window that is not every day', () => {
    expect(summariseRules(rules([window('07:00', '09:00', 31)]), label)).toBe('07:00–09:00 Mon–Fri · no presence needed')
    expect(summariseRules(rules([window('07:00', '09:00', 96)]), label)).toBe('07:00–09:00 Sat–Sun · no presence needed')
    expect(summariseRules(rules([window('07:00', '09:00', 1 | 4)]), label)).toBe('07:00–09:00 Mon, Wed · no presence needed')
  })

  it('labels zones and joins them by the match mode', () => {
    expect(summariseRules(rules([], { match: 0, sources: ['den', 'house'] }), label)).toBe('Any time · <den> or House')
    expect(summariseRules(rules([], { match: 1, sources: ['den', 'living room', 'house'] }), label))
      .toBe('Any time · <den>, <living room> and House')
  })
})

describe('validateWindow', () => {
  it('accepts a normal window and one that wraps midnight', () => {
    expect(validateWindow(window('08:00', '17:00'))).toBeNull()
    expect(validateWindow(window('22:00', '06:00'))).toBeNull()
  })

  it('rejects bad times', () => {
    expect(validateWindow(window('', '17:00'))).toMatch(/start/i)
    expect(validateWindow(window('8:00', '17:00'))).toMatch(/start/i)
    expect(validateWindow(window('08:00', '24:00'))).toMatch(/end/i)
    expect(validateWindow(window('08:00', undefined))).toMatch(/end/i)
  })

  it('rejects a window with no days', () => {
    expect(validateWindow(window('08:00', '17:00', 0))).toMatch(/day/i)
  })
})

describe('firstSaveMap', () => {
  const plug = { entityId: 'switch.plug', role: 'heaterPlug', rules: rules([window('06:00', '21:00')], { match: 0, sources: ['house'] }) }
  const valve = { entityId: 'climate.valve', role: 'radiatorValve', rules: rules([]) }
  const sensor = { entityId: 'sensor.t', role: 'thermometer' }
  const bare = { entityId: 'switch.bare', role: 'heaterPlug', rules: null }

  it('keeps every other drivable device default and adds the new rules', () => {
    const next = rules([window('10:00', '11:00')])
    const map = firstSaveMap([plug, valve, sensor, bare], 'climate.valve', next)
    expect(map).toEqual({ 'switch.plug': plug.rules, 'climate.valve': next })
  })

  it('lets the new rules replace the device own default', () => {
    const next = rules([])
    expect(firstSaveMap([plug], 'switch.plug', next)).toEqual({ 'switch.plug': next })
  })

  it('counts a device once when it sits in two zones', () => {
    const map = firstSaveMap([plug, { ...plug }, valve], 'switch.new', rules([]))
    expect(Object.keys(map).sort()).toEqual(['climate.valve', 'switch.new', 'switch.plug'])
  })
})

describe('presenceZones', () => {
  it('keeps zones that can detect occupancy, by label in natural order', () => {
    const rooms = [
      { name: 'zone 10', capabilities: 3 },
      { name: 'zone 2', displayName: 'Zone 2', capabilities: 2 },
      { name: 'kitchen', capabilities: 1 },
      { name: 'den', displayName: 'The den', capabilities: 3 }
    ]
    expect(presenceZones(rooms).map(z => z.label)).toEqual(['The den', 'Zone 2', 'zone 10'])
    expect(presenceZones(rooms)[0].name).toBe('den')
  })
})

describe('drivableDevices', () => {
  it('collects heater plugs and valves once each', () => {
    const plug = { entityId: 'switch.p', role: 'heaterPlug' }
    const rooms = [
      { devices: [plug, { entityId: 'sensor.t', role: 'thermometer' }] },
      { devices: [plug, { entityId: 'climate.v', role: 'radiatorValve' }] }
    ]
    expect(drivableDevices(rooms).map(d => d.entityId)).toEqual(['switch.p', 'climate.v'])
  })
})

describe('pre-heat', () => {
  it('defaults to 30 minutes for a radiator valve and none for a heater plug', () => {
    expect(preHeatFor(null, 'radiatorValve')).toBe(30)
    expect(preHeatFor({}, 'radiatorValve')).toBe(30)
    expect(preHeatFor(null, 'heaterPlug')).toBe(0)
    expect(preHeatFor({}, 'heaterPlug')).toBe(0)
  })

  it('is clamped to 0–60', () => {
    expect(preHeatFor({ preHeatMinutes: 0 }, 'radiatorValve')).toBe(0)
    expect(preHeatFor({ preHeatMinutes: 45 }, 'heaterPlug')).toBe(45)
    expect(preHeatFor({ preHeatMinutes: 90 }, 'radiatorValve')).toBe(60)
    expect(preHeatFor({ preHeatMinutes: -5 }, 'heaterPlug')).toBe(0)
  })

  it('offers every 5 minutes from none to an hour', () => {
    expect(PRE_HEAT_OPTIONS[0]).toBe(0)
    expect(PRE_HEAT_OPTIONS.at(-1)).toBe(60)
    expect(PRE_HEAT_OPTIONS).toHaveLength(13)
  })

  it('is mentioned in the summary only when it is not the default for that kind of device', () => {
    expect(summariseRules(rules([]), label, 'radiatorValve')).toBe('Any time · no presence needed')
    expect(summariseRules(rules([]), label, 'heaterPlug')).toBe('Any time · no presence needed')
    expect(summariseRules({ ...rules([]), preHeatMinutes: 10 }, label, 'radiatorValve')).toBe('Any time · no presence needed · pre-heat 10 min')
    expect(summariseRules({ ...rules([], { match: 0, sources: ['house'] }), preHeatMinutes: 30 }, label, 'heaterPlug'))
      .toBe('Any time · House · pre-heat 30 min')
  })

  it('is kept when rules are tidied for saving, which would otherwise wipe it', () => {
    expect(normaliseRules({ ...rules([]), preHeatMinutes: 15 }, 'radiatorValve').preHeatMinutes).toBe(15)
    expect(normaliseRules(rules([]), 'radiatorValve').preHeatMinutes).toBe(30)
    expect(normaliseRules(rules([]), 'heaterPlug').preHeatMinutes).toBe(0)
  })
})

describe('normaliseRules', () => {
  it('drops presence with no sources', () => {
    expect(normaliseRules(rules([], { match: 1, sources: [] })).presence).toBeNull()
  })

  it('resets the match to Any for a single source', () => {
    expect(normaliseRules(rules([], { match: 1, sources: ['house'] })).presence).toEqual({ match: 0, sources: ['house'] })
  })

  it('keeps All for several sources and strips unknown fields', () => {
    const out = normaliseRules(rules([{ days: 1, start: '08:00', end: '09:00', extra: 1 }], { match: 1, sources: ['house', 'den'] }))
    expect(out.presence).toEqual({ match: 1, sources: ['house', 'den'] })
    expect(out.timeWindows).toEqual([{ days: 1, start: '08:00', end: '09:00' }])
  })
})
