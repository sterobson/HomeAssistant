// Where Ruth and Ste are drawn in the 3D house, worked out from the house itself (owner, 2026-10-22):
//   - only while their phone is home (the house app's phone detectors: on the home Wi-Fi, from the router);
//   - at their desk if its plug is switched on;
//   - otherwise on the sofa in front of a TV that is on (≥ 30 W, as the heating's TV detectors count it);
//   - otherwise in the kitchen between 07:00 and 21:00;
//   - otherwise in their bedroom (Bedroom 1 for Ruth and Ste, Bedroom 3 for Ellie, who has no desk).
// Plan units and floors as in houseModel.js. People without rules here (Joseph) stay where FURNITURE puts them.
import { PLAN_UNIT_METRES, primaryEntity, domainOf } from './houseModel.js'

const TV_ON_WATTS = 30

const DEN_TV = 'Den - Plug - TV'
const LIVING_ROOM_TV = 'Living Room - Plug - TV'

export const HOUSEHOLD_RULES = {
  'person-ruth': {
    phone: 'ruth-phone-home',
    desk: 'Dining Room - Desk - Plug',
    den: { floor: 'ground', x: 13.5, y: 54.5, facing: 'left', pose: 'sit' },
    living: { floor: 'ground', x: 57.5, y: 66, facing: 'back', pose: 'sit' },
    kitchen: { floor: 'ground', x: 31, y: 24, facing: 'back', pose: 'stand' },
    bedroom: { floor: 'first', x: 54, y: 52, facing: 'front', pose: 'stand' },
  },
  'person-ste': {
    phone: 'ste-phone-home',
    desk: 'Games Room - Desk - Plug',
    den: { floor: 'ground', x: 13.5, y: 49.5, facing: 'left', pose: 'sit' },
    living: { floor: 'ground', x: 52.5, y: 66, facing: 'back', pose: 'sit' },
    kitchen: { floor: 'ground', x: 35.5, y: 26, facing: 'left', pose: 'stand' },
    bedroom: { floor: 'first', x: 59, y: 52, facing: 'front', pose: 'stand' },
  },
  // Ellie has no desk; she is at the dining table (her place in FURNITURE) on weekdays from 08:00 to 15:00, and her
  // room is Bedroom 3 (owner, 2026-10-22).
  'person-ellie': {
    phone: 'ellie-phone-home',
    usualPlaceAt: { days: [1, 2, 3, 4, 5], from: 8, to: 15 },
    den: { floor: 'ground', x: 13.5, y: 59, facing: 'left', pose: 'sit' },
    living: { floor: 'ground', x: 61.5, y: 66, facing: 'back', pose: 'sit' },
    kitchen: { floor: 'ground', x: 28.5, y: 27, facing: 'back', pose: 'stand' },
    bedroom: { floor: 'first', x: 33, y: 58, facing: 'front', pose: 'stand' },
  },
}

const sameName = (a, b) => (a ?? '').trim().toLowerCase() === (b ?? '').trim().toLowerCase()
const named = (devices, name) => (devices ?? []).find((d) => sameName(d.name, name) && d.entities?.length)

/** Whether a plug's own switch is on. */
export function plugOn(devices, name) {
  const entity = primaryEntity(named(devices, name))
  return domainOf(entity?.entityId) === 'switch' && entity.state === 'on'
}

/** Whether a TV is on, by its plug's power draw. */
export function tvOn(devices, name) {
  const power = (named(devices, name)?.entities ?? []).find((e) => e.deviceClass === 'power')
  return Number(power?.state) >= TV_ON_WATTS
}

/**
 * Every presence detector's state from the room states the house app publishes (each room lists them all):
 * detector id → active. Empty until room states have arrived.
 */
export function detectorStates(roomStates) {
  const states = new Map()
  for (const room of roomStates ?? []) {
    for (const detector of room.occupancy?.available ?? []) states.set(detector.id, detector.active === true)
  }
  return states
}

/**
 * Where one person is: 'hidden' (phone not home, or not known yet), 'desk' (their usual place in FURNITURE: a desk,
 * or Ellie's seat at the dining table), or one of their rule's places ('den', 'living', 'kitchen', 'bedroom').
 */
export function whereIs(rule, { devices, detectors, now }) {
  if (detectors.get(rule.phone) !== true) return 'hidden'
  // Set hours at their usual place come first (Ellie at the dining table on weekdays), then a desk that is on.
  const hours = rule.usualPlaceAt
  if (hours && hours.days.includes(now.getDay()) && now.getHours() >= hours.from && now.getHours() < hours.to) return 'desk'
  if (rule.desk && plugOn(devices, rule.desk)) return 'desk'
  if (tvOn(devices, DEN_TV)) return 'den'
  if (tvOn(devices, LIVING_ROOM_TV)) return 'living'
  const hour = now.getHours()
  return hour >= 7 && hour < 21 ? 'kitchen' : 'bedroom'
}

/** Where everyone with rules is: { 'person-ruth': 'desk', … }. */
export function whereEveryoneIs(context) {
  return Object.fromEntries(Object.entries(HOUSEHOLD_RULES).map(([id, rule]) => [id, whereIs(rule, context)]))
}

/** The furniture with Ruth and Ste moved to where they are now, or left out while they're away. */
export function placeHousehold(furniture, context) {
  return placeHouseholdAt(furniture, whereEveryoneIs(context))
}

/** As placeHousehold, from whereEveryoneIs's answer, so a caller can redraw only when someone has moved. */
export function placeHouseholdAt(furniture, whereById) {
  const result = []
  for (const item of furniture) {
    const rule = HOUSEHOLD_RULES[item.id]
    if (!rule) { result.push(item); continue }
    const where = whereById[item.id] ?? 'desk'
    if (where === 'hidden') continue
    if (where === 'desk') { result.push(item); continue }
    const place = rule[where]
    const half = 0.25 / PLAN_UNIT_METRES
    result.push({
      ...item,
      floor: place.floor,
      x: place.x - half,
      y: place.y - half,
      facing: place.facing,
      rotation: 0,
      person: { ...item.person, pose: place.pose },
    })
  }
  return result
}
