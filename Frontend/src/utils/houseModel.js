// The house as the 3D view draws it, and how each Home Assistant device is placed and summarised in it.
//
// The plan was worked out from the Zigbee mesh on 2026-10-21 and corrected by the owner: the den is on the left
// (seen from the front) and runs from the front to the back wall. Room sizes, doors and the rooms without a plug
// (bathroom, bedroom 2, hallway) are still guesses. Plan units: x left to right as seen from the road, y from the
// back wall (0) to the front (70); one unit is PLAN_UNIT_METRES.

// The den is 9 m from front to back (owner, 2026-10-22), and 70 units.
export const PLAN_UNIT_METRES = 9 / 70

/** A real-world length in plan units, for sizing furniture. */
const m = (metres) => metres / PLAN_UNIT_METRES

/** Gives each item an id from its type ("door-3", "chair-2") unless it has one, so the editor can name it. */
function withIds(items) {
  const counts = {}
  return items.map((item) => {
    counts[item.type] = (counts[item.type] ?? 0) + 1
    return item.id ? item : { id: `${item.type}-${counts[item.type]}`, ...item }
  })
}
export const STOREY_HEIGHT_METRES = 2.7
export const WALL_HEIGHT_METRES = 2.4

export const FLOORS = {
  ground: { id: 'ground', label: 'Ground floor', level: 0 },
  first: { id: 'first', label: 'First floor', level: 1 },
}

// `free` is where devices without a fixed spot (DEVICE_SPOTS) are spread, clear of the furniture. `inner` rooms sit
// inside another room's floor (they get walls and a label but no floor of their own); `nested` ones only a label.
export const ROOMS = [
  // 9 m long and 3.6 m across at its widest (owner).
  { id: 'den', floor: 'ground', name: 'Den / games room', x: -4, y: 0, w: 28, h: 70, free: { x: 0, y: 19, w: 15, h: 22 } },
  { id: 'boiler', floor: 'ground', name: 'Boiler cupboard', x: 18, y: 0, w: 6, h: 6, inner: true },
  { id: 'wc', floor: 'ground', name: 'Toilet', x: 17, y: 6, w: 7, h: 11, inner: true },
  { id: 'kitchen', floor: 'ground', name: 'Kitchen', x: 24, y: 0, w: 20, h: 34, free: { x: 29, y: 21, w: 10, h: 8 } },
  { id: 'dining', floor: 'ground', name: 'Dining room', x: 44, y: 0, w: 28, h: 34, free: { x: 66, y: 2, w: 5, h: 13 } },
  { id: 'hall', floor: 'ground', name: 'Hallway', x: 24, y: 34, w: 16, h: 36, stairs: true, free: { x: 32, y: 40, w: 7, h: 26 } },
  { id: 'understairs', floor: 'ground', name: 'Under the stairs', x: 24, y: 34, w: 7, h: 10, nested: true },
  { id: 'living', floor: 'ground', name: 'Living room', x: 40, y: 34, w: 32, h: 36, free: { x: 43, y: 40, w: 20, h: 16 } },
  { id: 'porch', floor: 'ground', name: 'Porch', x: 24, y: 70, w: 16, h: 9 },
  { id: 'bath', floor: 'first', name: 'Bathroom', x: 24, y: 0, w: 22, h: 30 },
  { id: 'bed2', floor: 'first', name: 'Bedroom 2', x: 46, y: 0, w: 26, h: 36 },
  { id: 'landing', floor: 'first', name: 'Landing', x: 24, y: 30, w: 18, h: 16, stairs: true },
  { id: 'bed3', floor: 'first', name: 'Bedroom 3', x: 24, y: 46, w: 18, h: 24 },
  { id: 'bed1', floor: 'first', name: 'Bedroom 1', x: 42, y: 36, w: 30, h: 34 },
]

// Doors, windows and open-plan gaps, as stretches of a wall line: axis 'h' runs along x at y = at, axis 'v' along y
// at x = at. A door's leaf hangs from `from` and opens towards +y ('h') or +x ('v') when swing is 1, the other way
// when -1. `external` doors are the outside ones; every internal door is wooden with four panels, and the one from
// the hallway to the living room slides (owner). From the owner's description, 2026-10-22; nothing upstairs yet.
export const OPENINGS = withIds([
  { id: "window-1", floor: "ground", axis: "h", at: 0, from: -2, to: 10.5, type: "window" },
  { id: "door-1", floor: "ground", axis: "h", at: 0, from: 11.5, to: 17.5, type: "door", swing: 1, external: true, hinge: "end" },
  { id: "door-2", floor: "ground", axis: "h", at: 70, from: 18.5, to: 24, type: "door", swing: -1, external: true },
  { id: "door-3", floor: "ground", axis: "v", at: 24, from: 27, to: 33, type: "door", swing: 1 },
  { id: "door-4", floor: "ground", axis: "v", at: 17, from: 6.06, to: 11.86, type: "door", swing: -1 },
  { id: "door-5", floor: "ground", axis: "v", at: 18, from: 0.5, to: 5.5, type: "door", swing: -1 },
  { id: "window-2", floor: "ground", axis: "h", at: 0, from: 28, to: 39, type: "window" },
  { id: "door-6", floor: "ground", axis: "h", at: 34, from: 32.5, to: 38.5, type: "door", swing: 1 },
  { id: "door-8", floor: "ground", axis: "h", at: 0, from: 45, to: 51.5, type: "door", swing: 1, external: true },
  { id: "window-3", floor: "ground", axis: "h", at: 0, from: 53, to: 71, type: "window" },
  { id: "open-1", floor: "ground", axis: "h", at: 34, from: 56, to: 72, type: "open" },
  { id: "window-4", floor: "ground", axis: "h", at: 70, from: 46, to: 66, type: "window" },
  { id: "door-9", floor: "ground", axis: "v", at: 40, from: 45, to: 51.5, type: "door", sliding: true, swing: 1 },
  { id: "door-10", floor: "ground", axis: "h", at: 70, from: 28.12, to: 34.62, type: "door", swing: -1, external: true, hinge: "end" },
  { id: "open-2", floor: "ground", axis: "h", at: 79, from: 25.5, to: 38.5, type: "open" },
  { id: "open-3", floor: "ground", axis: "v", at: 44, from: 19, to: 24.5, type: "open" },
])

// Furniture and fittings, in plan units with heights in metres. `facing` is the way a seat, screen or heater points:
// 'back' (towards the back wall), 'front', 'left' or 'right'. `litBy` names a device (by its Home Assistant name)
// whose being on makes the item glow, so the stick lights and the heaters show when their plugs are on.
// TVs and monitors take their screen size from `inches` (and `aspect`); `rotation` turns an item about its middle,
// anticlockwise seen from above. Arranged by the owner in the 3D page's arrange mode on 2026-10-22, then tidied (wall
// items flush to their walls, the den sofa's two halves rejoined) with the desks' monitors, keyboards and printer added.
export const FURNITURE = withIds([
  { id: "desk-1", floor: "ground", type: "desk", x: -3.7, y: 0.3, w: 10.89, h: 5.44 },
  { id: "desk-2", floor: "ground", type: "desk", x: -3.7, y: 5.74, w: 5.44, h: 7 },
  { id: "chair-1", floor: "ground", type: "chair", x: 2.2, y: 6.8, w: 4.28, h: 4.28, facing: "back", style: "swivel", rotation: 45 },
  { id: "heater-1", floor: "ground", type: "heater", x: 13.35, y: 11.4, w: 2.33, h: 1.56, facing: "back", litBy: "Games Room - Plug - Heater" },
  { id: "boiler-1", floor: "ground", type: "boiler", x: 19.3, y: 0.3, w: 3.42, h: 2.33 },
  { id: "toilet-1", floor: "ground", type: "toilet", x: 18.94, y: 6.6, w: 5.06, h: 2.96, facing: "left" },
  { id: "radiator-1", floor: "ground", type: "radiator", x: 16.22, y: 11.79, w: 0.78, h: 6.22 },
  { id: "cabinet-1", floor: "ground", type: "cabinet", x: -3.8, y: 45.11, w: 3.11, h: 12.44 },
  { id: "tv-1", floor: "ground", type: "tv", x: -3.6, y: 45.92, w: 0.39, h: 11.2, inches: 65, facing: "right" },
  { id: "sofa-1", floor: "ground", type: "sofa", x: 9.28, y: 45, w: 7.39, h: 16.37, facing: "left", noArm: "front", colour: "duckEgg", style: "euphoria" },
  { id: "sofa-2", floor: "ground", type: "sofa", x: 4.28, y: 61.37, w: 12.39, h: 7.39, facing: "back", noArm: "right", colour: "duckEgg", style: "euphoria" },
  { id: "radiator-2", floor: "ground", type: "radiator", x: 5.5, y: 69.22, w: 9.33, h: 0.78 },
  { id: "counter-1", floor: "ground", type: "counter", x: 24, y: 0, w: 20, h: 4.67 },
  { id: "counter-2", floor: "ground", type: "counter", x: 39.33, y: 4.67, w: 4.67, h: 10.83 },
  { id: "counter-3", floor: "ground", type: "counter", x: 24, y: 4.67, w: 4.67, h: 6.22 },
  { id: "sink-1", floor: "ground", type: "sink", x: 31, y: 0.6, w: 6.22, h: 3.5 },
  { id: "hob-1", floor: "ground", type: "hob", x: 24.4, y: 5.47, w: 3.89, h: 4.51 },
  { id: "tall-1", floor: "ground", type: "tall", x: 24, y: 10.89, w: 4.67, h: 4.67, label: "oven" },
  { id: "tall-2", floor: "ground", type: "tall", x: 24, y: 15.56, w: 4.67, h: 4.67, label: "fridge" },
  { id: "bar-1", floor: "ground", type: "bar", x: 33.11, y: 15.5, w: 10.89, h: 3.5 },
  { id: "heater-2", floor: "ground", type: "heater", x: 38.61, y: 31.23, w: 2.33, h: 1.56, facing: "back", litBy: "Kitchen plug - Heater" },
  { id: "table-1", floor: "ground", type: "table", x: 55, y: 9, w: 7, h: 14 },
  { id: "chair-2", floor: "ground", type: "chair", x: 51.11, y: 9.74, w: 3.5, h: 3.5, facing: "right" },
  { id: "chair-3", floor: "ground", type: "chair", x: 51.11, y: 14.25, w: 3.5, h: 3.5, facing: "right" },
  { id: "chair-4", floor: "ground", type: "chair", x: 51.11, y: 18.76, w: 3.5, h: 3.5, facing: "right" },
  { id: "chair-5", floor: "ground", type: "chair", x: 62.39, y: 9.74, w: 3.5, h: 3.5, facing: "left" },
  { id: "chair-6", floor: "ground", type: "chair", x: 62.39, y: 14.25, w: 3.5, h: 3.5, facing: "left" },
  { id: "chair-7", floor: "ground", type: "chair", x: 62.39, y: 18.76, w: 3.5, h: 3.5, facing: "left" },
  { id: "desk-3", floor: "ground", type: "desk", x: 49.74, y: 28.26, w: 5.45, h: 5.44 },
  { id: "desk-4", floor: "ground", type: "desk", x: 44.3, y: 24.37, w: 5.44, h: 9.33 },
  { id: "chair-8", floor: "ground", type: "chair", x: 50.67, y: 23.59, w: 4.28, h: 4.28, facing: "left", style: "swivel" },
  { id: "heater-3", floor: "ground", type: "heater", x: 70.04, y: 25.5, w: 1.56, h: 2.33, facing: "left", litBy: "Dining Room - Plug - Heater" },
  { id: "radiator-3", floor: "ground", type: "radiator", x: 44, y: 3.5, w: 0.78, h: 9.33 },
  { id: "cabinet-2", floor: "ground", type: "cabinet", x: 41.5, y: 34.2, w: 9.33, h: 3.11 },
  { id: "tv-2", floor: "ground", type: "tv", x: 41.95, y: 34.8, w: 8.44, h: 0.39, inches: 49, facing: "front" },
  { id: "stickLight-1", floor: "ground", type: "stickLight", x: 53.4, y: 34.6, w: 0.8, h: 0.8, litBy: "LivingRoomLightTreePlug" },
  { id: "stickLight-2", floor: "ground", type: "stickLight", x: 55.4, y: 34.6, w: 0.8, h: 0.8, litBy: "LivingRoomLightTreePlug" },
  { id: "sofa-3", floor: "ground", type: "sofa", x: 47.5, y: 62.1, w: 16.33, h: 7, facing: "back", colour: "plum" },
  { id: "sofa-4", floor: "ground", type: "sofa", x: 64.6, y: 41, w: 7, h: 16.33, facing: "left", colour: "plum" },
  { id: "radiator-4", floor: "ground", type: "radiator", x: 50.5, y: 69.22, w: 10.89, h: 0.78 },
  { id: "radiator-5", floor: "ground", type: "radiator", x: 39.22, y: 35, w: 0.78, h: 7.78 },
  { id: "monitor-1", floor: "ground", type: "monitor", x: -4.55, y: 3.31, w: 8.11, h: 0.39, inches: 43, aspect: "32:10", facing: "front", rotation: 45 },
  { id: "keyboard-1", floor: "ground", type: "keyboard", x: -0.56, y: 4.61, w: 3.42, h: 1.09, rotation: 45 },
  { id: "mouse-1", floor: "ground", type: "mouse", x: 2.57, y: 3.11, w: 0.47, h: 0.78, rotation: 45 },
  { id: "monitor-2", floor: "ground", type: "monitor", x: 44.93, y: 24.82, w: 0.39, h: 4.13, inches: 24, facing: "right" },
  { id: "monitor-3", floor: "ground", type: "monitor", x: 44.93, y: 29.11, w: 0.39, h: 4.13, inches: 24, facing: "right" },
  { id: "keyboard-2", floor: "ground", type: "keyboard", x: 47.27, y: 25.93, w: 1.09, h: 3.42 },
  { id: "mouse-2", floor: "ground", type: "mouse", x: 47.5, y: 24.76, w: 0.47, h: 0.78 },
  { id: "printer-1", floor: "ground", type: "printer", x: 51.53, y: 30.66, w: 3.27, h: 2.96 },
  // The household (owner, 2026-10-22), each on the chair they sit at, or standing. A person's footprint is centred
  // on their seat; `person` is how they're drawn (houseFigures.js).
  { id: "person-ste", floor: "ground", type: "person", x: 4.34 - m(0.25), y: 8.94 - m(0.25), w: m(0.5), h: m(0.5), facing: "back", rotation: 45,
    person: { name: "Ste", pose: "sit", height: 1.88, build: "slender", hair: "short", hairColour: "#2b211b", glasses: true, beard: true, top: "#2f4a6d", bottom: "#38455c" } },
  { id: "person-ellie", floor: "ground", type: "person", x: 64.14 - m(0.25), y: 20.51 - m(0.25), w: m(0.5), h: m(0.5), facing: "left",
    person: { name: "Ellie", pose: "sit", height: 1.65, build: "slender", hair: "short", hairColour: "#271c17", top: "#3f7a78", bottom: "#2e3440" } },
  { id: "person-ruth", floor: "ground", type: "person", x: 52.81 - m(0.25), y: 25.73 - m(0.25), w: m(0.5), h: m(0.5), facing: "left",
    person: { name: "Ruth", pose: "sit", height: 1.65, hair: "shoulder", hairColour: "#d9bb7a", top: "#a55a5a", bottom: "#3a3a45" } },
  { id: "person-joseph", floor: "first", type: "person", x: 52 - m(0.25), y: 18 - m(0.25), w: m(0.5), h: m(0.5), facing: "right",
    person: { name: "Joseph", pose: "stand", height: 1.76, build: "slender", hair: "curly", hairColour: "#3b2a20", top: "#5d6470", bottom: "#33415a" } },
])

// Devices whose place in the house is known, by Home Assistant device name (any case), or `id:<device id>` where two
// devices share a name. `height` is metres above the floor and `rotation` turns the marker. Placed by the owner in
// arrange mode (2026-10-22), then tidied: plugs on the floor, wall-mounted devices flush to their wall, anything over
// a desk, bar or cabinet on its top, radiator valves at their radiators' ends. A device not listed here is spread
// across its room's free area. The fingerbot has no Home Assistant area, so this puts it in the den.
export const DEVICE_SPOTS = {
  "8e:60:52:ee:88:75": { room: "unplaced", x: 84.8, y: 5.6, height: 0.09, rotation: 0 },
  "65\" oled": { room: "unplaced", x: 93.3, y: 5.6, height: 1, rotation: 0 },
  "all speakers": { room: "unplaced", x: 84.8, y: 11.8, height: 1, rotation: 0 },
  "backup": { room: "unplaced", x: 93.3, y: 11.8, height: 0.9, rotation: 0 },
  "bathroom - climate": { room: "bath", x: 35, y: 15, height: 1.4, rotation: 0 },
  "bedroom 1 - climate": { room: "bed1", x: 50.8, y: 45.8, height: 1.4, rotation: 0 },
  "bedroom 1 - radiator": { room: "bed1", x: 63.3, y: 45.8, height: 0.35, rotation: 0 },
  "bedroom 1 plug - heater": { room: "bed1", x: 50.8, y: 60.3, height: 0.09, rotation: 0 },
  "bedroom 2 - climate": { room: "bed2", x: 59, y: 7.7, height: 1.4, rotation: 0 },
  "bedroom 2 - radiator": { room: "bed2", x: 59, y: 18, height: 0.35, rotation: 0 },
  "bedroom 3 - climate": { room: "bed3", x: 29.8, y: 53.3, height: 1.4, rotation: 0 },
  "bedroom 3 - radiator": { room: "bed3", x: 36.3, y: 53.3, height: 0.35, rotation: 0 },
  "bedroom speaker": { room: "unplaced", x: 84.8, y: 17.9, height: 1, rotation: 0 },
  "bedroom3lampplug": { room: "bed3", x: 29.8, y: 62.8, height: 0.09, rotation: 0 },
  "bedroom3shelfplug": { room: "bed3", x: 36.3, y: 62.8, height: 0.09, rotation: 0 },
  "boiler-fingerbot": { room: "boiler", x: 21, y: 2.8, height: 1.3, rotation: 0 },
  "bookcase light strip": { room: "dining", x: 68.5, y: 4.2, height: 1.9, rotation: 0 },
  // A Velop mesh node, on Ste's desk against the back wall (owner, 2026-10-22).
  "child node 1": { room: "den", x: 3.2, y: 0.8, height: 0.88, rotation: 0 },
  "child node 2": { room: "kitchen", x: 40.7, y: 16, height: 1.18, rotation: 0 },
  "christmas tree": { room: "living", x: 43.7, y: 68, height: 0.09, rotation: 0 },
  "clock temperature and humidity": { room: "bed1", x: 63.3, y: 60.3, height: 1.4, rotation: 0 },
  "den - climate": { room: "den", x: -3.65, y: 58.5, height: 1.4, rotation: 0 },
  "den - plug - motion lights": { room: "den", x: -3.65, y: 34.9, height: 0.09, rotation: 0 },
  "den - plug - tv": { room: "den", x: -3.65, y: 62, height: 0.09, rotation: 0 },
  "den - radiator 1": { room: "den", x: 16.61, y: 18.41, height: 0.2, rotation: 0 },
  "den - radiator 2": { room: "den", x: 15.23, y: 69.61, height: 0.2, rotation: 0 },
  // A tall standing lamp in the front left corner, between the TV and the sofa (owner, 2026-10-23).
  "den tall lamp": { room: "den", x: -2.4, y: 67.6, height: 1.9, rotation: 0 },
  "dining room": { room: "unplaced", x: 84.8, y: 24.1, height: 1, rotation: 0 },
  "dining room - bookshelf - button": { room: "dining", x: 68.5, y: 8.5, height: 1, rotation: 0 },
  "dining room - climate": { room: "dining", x: 44.35, y: 26.7, height: 0.84, rotation: 0 },
  "dining room - desk - button": { room: "dining", x: 50.52, y: 31, height: 0.79, rotation: 0 },
  "dining room - desk - plug": { room: "dining", x: 46, y: 33.65, height: 0.09, rotation: 0 },
  "dining room - plug - bookshelf": { room: "dining", x: 71.65, y: 18.5, height: 0.09, rotation: 0 },
  "dining room - plug - heater": { room: "dining", x: 71.65, y: 29, height: 0.09, rotation: 0 },
  "dining room - radiator": { room: "dining", x: 44.39, y: 13.23, height: 0.2, rotation: 0 },
  "dining room plug - dehumidifier": { room: "dining", x: 71.65, y: 23.5, height: 0.09, rotation: 0 },
  "downstairs speakers": { room: "unplaced", x: 93.3, y: 24.1, height: 1, rotation: 0 },
  "electricity meter": { room: "understairs", x: 27.5, y: 36.4, height: 0.9, rotation: 0 },
  "electricity meter export": { room: "unplaced", x: 84.8, y: 30.3, height: 0.9, rotation: 0 },
  "ellie's phone": { room: "unplaced", x: 93.3, y: 30.3, height: 0.9, rotation: 0 },
  "forecast": { room: "unplaced", x: 84.8, y: 36.4, height: 0.9, rotation: 0 },
  "games room - desk - button": { room: "den", x: 3, y: 2.6, height: 0.79, rotation: 0 },
  "games room - desk - climate": { room: "den", x: -1.4, y: 9.5, height: 0.84, rotation: 0 },
  "games room - desk - plug": { room: "den", x: -1, y: 3, height: 0.09, rotation: 0 },
  "games room - plug - heater": { room: "den", x: 16.65, y: 12.6, height: 0.09, rotation: 0 },
  "games room desk lamp": { room: "den", x: 5.5, y: 2.4, height: 0.91, rotation: 0 },
  "gas meter": { room: "understairs", x: 27.5, y: 39, height: 0.9, rotation: 0 },
  "greenness forecast (a-98d9d332)": { room: "unplaced", x: 93.3, y: 36.4, height: 0.9, rotation: 0 },
  "hallway - climate": { room: "hall", x: 39.65, y: 36.9, height: 1.4, rotation: 0 },
  "hypervolt charger": { room: "porch", x: 28.8, y: 70.35, height: 1.3, rotation: 0 },
  "id:8f783974b90f7ff023369066a68c7aa0": { room: "unplaced", x: 93.3, y: 42.6, height: 0.09, rotation: 0 },
  "id:9c310e67f191dacfca07f5cfda82b0e3": { room: "unplaced", x: 84.8, y: 42.6, height: 0.09, rotation: 0 },
  "kevin's phone": { room: "unplaced", x: 84.8, y: 48.8, height: 0.9, rotation: 0 },
  "kitchen - sink - washing machine - plug": { room: "kitchen", x: 33, y: 0.35, height: 0.09, rotation: 0 },
  "kitchen flood detector": { room: "kitchen", x: 35.6, y: 1.8, height: 0.05, rotation: 0 },
  "kitchen plug - heater": { room: "kitchen", x: 40, y: 33.65, height: 0.09, rotation: 0 },
  "kitchen speaker": { room: "den", x: 20.2, y: 21, height: 1, rotation: 0 },
  "kitchen temperature and humidity": { room: "kitchen", x: 43.65, y: 16.3, height: 1.14, rotation: 0 },
  "lamperion bane of shadows": { room: "bed2", x: 59, y: 28.3, height: 1.9, rotation: 0 },
  "lego village": { room: "dining", x: 68.5, y: 14.1, height: 0.09, rotation: 0 },
  "living room - christmas tree - button": { room: "living", x: 49.4, y: 36.6, height: 0.48, rotation: 0 },
  "living room - climate": { room: "living", x: 66.1, y: 67.2, height: 1.4, rotation: 0 },
  "living room - plug - tv": { room: "living", x: 51.4, y: 34.35, height: 0.09, rotation: 0 },
  "living room - radiator": { room: "living", x: 61.79, y: 69.61, height: 0.2, rotation: 0 },
  "living room google home": { room: "living", x: 66.5, y: 66, height: 1, rotation: 0 },
  "living room lamp": { room: "living", x: 67.4, y: 64.9, height: 1.9, rotation: 0 },
  "living room tv": { room: "unplaced", x: 93.3, y: 48.8, height: 1, rotation: 0 },
  "livingroomlighttreeplug": { room: "living", x: 54.4, y: 34.35, height: 0.09, rotation: 0 },
  "loft shelf": { room: "understairs", x: 27.5, y: 41.6, height: 1.9, rotation: 0 },
  "main speakers": { room: "living", x: 44, y: 37.4, height: 1, rotation: 0 },
  "mg mg4 electric": { room: "porch", x: 35.3, y: 74.5, height: 0.6, rotation: 0 },
  "motion-sensor-02": { room: "dining", x: 71.65, y: 13.2, height: 2, rotation: 0 },
  "octoplus (a-98d9d332)": { room: "unplaced", x: 84.8, y: 54.9, height: 0.9, rotation: 0 },
  "porch light": { room: "porch", x: 24.35, y: 74, height: 2, rotation: 0 },
  "porch motion detector": { room: "porch", x: 26.3, y: 70.35, height: 2, rotation: 0 },
  "router": { room: "living", x: 66.1, y: 67, height: 0.9, rotation: 0 },
  "ruth phone": { room: "unplaced", x: 93.3, y: 54.9, height: 0.9, rotation: 0 },
  "solar production forecast": { room: "unplaced", x: 84.8, y: 61.1, height: 0.9, rotation: 0 },
  "solax energy dashboard": { room: "unplaced", x: 93.3, y: 61.1, height: 0.09, rotation: 0 },
  "solax inverter": { room: "unplaced", x: 84.8, y: 67.3, height: 1.4, rotation: 0 },
  "sonoff zbdongle-e": { room: "living", x: 69.1, y: 63.8, height: 0.9, rotation: 0 },
  "ste - phone - samsung galaxy a54": { room: "unplaced", x: 93.3, y: 67.3, height: 0.9, rotation: 0 },
  "sun": { room: "unplaced", x: 84.8, y: 73.4, height: 0.9, rotation: 0 },
}

/** Where devices with no Home Assistant area, or one the plan doesn't know, are put: a pad beside the house. */
export const UNPLACED = { id: 'unplaced', floor: 'ground', name: 'Not in a room', x: 78, y: 0, w: 22, h: 79, outside: true }

/** The plan's extent, for centring the model. */
export const PLAN_BOUNDS = { minX: -4, maxX: UNPLACED.x + UNPLACED.w, minY: 0, maxY: 79 }

// Home Assistant area name → plan room. Matched without regard to case or spacing.
const AREA_ROOMS = {
  'living room': 'living',
  'dining room': 'dining',
  kitchen: 'kitchen',
  'games room': 'den',
  den: 'den',
  hallway: 'hall',
  porch: 'porch',
  'under the stairs': 'understairs',
  'bedroom 1': 'bed1',
  'bedroom 2': 'bed2',
  'bedroom 3': 'bed3',
  bathroom: 'bath',
}

const byName = (a, b) => (a.name ?? '').localeCompare(b.name ?? '', undefined, { numeric: true, sensitivity: 'base' })

export function roomById(id) {
  return id === UNPLACED.id ? UNPLACED : ROOMS.find((room) => room.id === id) ?? null
}

/** The plan room a Home Assistant area belongs in, or the unplaced pad. */
export function roomForArea(area) {
  const key = (area ?? '').trim().toLowerCase().replace(/\s+/g, ' ')
  return roomById(AREA_ROOMS[key]) ?? UNPLACED
}

export function domainOf(entityId) {
  return (entityId ?? '').split('.')[0]
}

const UNAVAILABLE = new Set(['unavailable', 'unknown'])

/** An entity's name without its device's name in front ("Den - Plug - TV Power" → "Power"). */
export function entityLabel(entity, device) {
  const name = (entity?.name ?? '').trim()
  const deviceName = (device?.name ?? '').trim()
  if (deviceName && name.toLowerCase().startsWith(deviceName.toLowerCase())) {
    const rest = name.slice(deviceName.length).replace(/^[\s\-–:]+/, '')
    return rest ? rest.charAt(0).toUpperCase() + rest.slice(1) : ''
  }
  return name || entity?.entityId || ''
}

/**
 * The entity that stands for the device: its own on/off switch or light (the one named after the device), a
 * radiator's thermostat, a sensor's main reading.
 */
export function primaryEntity(device) {
  const entities = device?.entities ?? []
  const named = (e) => entityLabel(e, device) === ''
  const of = (domain) => entities.filter((e) => domainOf(e.entityId) === domain)
  const pick = (list) => list.find(named) ?? list[0]
  return pick(of('light'))
    ?? pick(of('switch'))
    ?? of('climate')[0]
    ?? of('binary_sensor').find((e) => ['motion', 'occupancy', 'moisture', 'door', 'window'].includes(e.deviceClass))
    ?? of('sensor').find((e) => e.deviceClass === 'temperature')
    ?? of('media_player')[0]
    ?? of('binary_sensor')[0]
    ?? of('sensor').find((e) => e.deviceClass !== 'battery' && !/signal|rssi|lqi/i.test(e.entityId))
    ?? entities[0]
    ?? null
}

/** What the model draws for the device. */
export function deviceKind(device) {
  const model = (device?.model ?? '').toLowerCase()
  const name = (device?.name ?? '').toLowerCase()
  const domains = new Set((device?.entities ?? []).map((e) => domainOf(e.entityId)))
  const classes = new Set((device?.entities ?? []).map((e) => e.deviceClass).filter(Boolean))
  if (model.includes('zbdongle') || name.includes('zbdongle')) return 'hub'
  if (name.includes('fingerbot')) return 'fingerbot'
  if (domains.has('climate') && (model.includes('trv') || name.includes('radiator'))) return 'radiator'
  if (domains.has('climate') || domains.has('lock')) return 'car'
  if (domains.has('light')) return 'light'
  if (domains.has('media_player')) return name.includes('tv') || model.includes('oled') || model.includes('chromecast') ? 'tv' : 'speaker'
  if (classes.has('motion') || classes.has('occupancy')) return 'motion'
  if (classes.has('moisture')) return 'flood'
  if (domains.has('switch')) return 'plug'
  if (classes.has('temperature')) return 'thermometer'
  if (domains.has('button') && model.startsWith('snzb-01')) return 'button'
  if (domains.has('device_tracker')) return 'phone'
  return 'other'
}

/** How high above its floor each kind of device sits, in metres. */
export const KIND_HEIGHT = {
  radiator: 0.35, plug: 0.3, flood: 0.08, car: 0.6, hub: 0.9, fingerbot: 1.2, thermometer: 1.4, motion: 2.0,
  light: 1.9, button: 1.0, tv: 1.0, speaker: 1.0, phone: 0.9, other: 0.9,
}

function stateText(entity) {
  if (!entity) return ''
  const state = entity.state ?? ''
  if (UNAVAILABLE.has(state)) return 'Unavailable'
  const domain = domainOf(entity.entityId)
  if (domain === 'switch' || domain === 'light' || domain === 'input_boolean') return state === 'on' ? 'On' : 'Off'
  if (domain === 'binary_sensor') {
    const on = state === 'on'
    switch (entity.deviceClass) {
      case 'motion':
      case 'occupancy': return on ? 'Motion' : 'No motion'
      case 'moisture': return on ? 'Wet' : 'Dry'
      case 'door':
      case 'window': return on ? 'Open' : 'Closed'
      default: return on ? 'On' : 'Off'
    }
  }
  const number = Number(state)
  if (state !== '' && Number.isFinite(number)) {
    const shown = Math.abs(number) >= 100 ? Math.round(number).toString() : (Math.round(number * 10) / 10).toString()
    return entity.unit ? `${shown} ${entity.unit}` : shown
  }
  return state.charAt(0).toUpperCase() + state.slice(1).replace(/_/g, ' ')
}

export { stateText as entityStateText }

/**
 * A one-line status for the device and a tone for its colour: 'on', 'off', 'alert' (unavailable, wet, motion) or
 * 'reading' (a sensor with a value).
 */
export function deviceStatus(device) {
  const primary = primaryEntity(device)
  if (!primary) return { text: '', tone: 'reading' }
  const text = stateText(primary)
  if (UNAVAILABLE.has(primary.state ?? '')) return { text, tone: 'alert' }
  const domain = domainOf(primary.entityId)
  if (domain === 'switch' || domain === 'light') {
    const power = (device.entities ?? []).find((e) => e.deviceClass === 'power' && !UNAVAILABLE.has(e.state ?? ''))
    const on = primary.state === 'on'
    return { text: on && power ? `${text} · ${stateText(power)}` : text, tone: on ? 'on' : 'off' }
  }
  if (domain === 'binary_sensor') {
    return { text, tone: primary.state === 'on' && primary.deviceClass === 'moisture' ? 'alert' : primary.state === 'on' ? 'on' : 'off' }
  }
  if (primary.deviceClass === 'temperature') {
    const humidity = (device.entities ?? []).find((e) => e.deviceClass === 'humidity' && !UNAVAILABLE.has(e.state ?? ''))
    return { text: humidity ? `${text} · ${stateText(humidity)}` : text, tone: 'reading' }
  }
  return { text, tone: 'reading' }
}

const spotKey = (name) => (name ?? '').trim().toLowerCase().replace(/\s+/g, ' ')

/**
 * The key a device's spot is stored under: its name in lower case, or `id:<device id>` when another device has the
 * same name (two Hypervolt chargers do), so each can have its own spot.
 */
export function deviceKey(device, devices) {
  const key = spotKey(device?.name)
  const clash = (devices ?? []).some((other) => other !== device && other.id !== device.id && spotKey(other.name) === key)
  return clash ? `id:${device.id}` : key
}

/** The device's spot: an arrangement made in the page (`overrides`) first, then DEVICE_SPOTS, else null. */
export function deviceSpot(device, overrides = null, devices = null) {
  const key = devices ? deviceKey(device, devices) : spotKey(device?.name)
  return overrides?.[key] ?? overrides?.[`id:${device?.id}`] ?? DEVICE_SPOTS[`id:${device?.id}`] ?? DEVICE_SPOTS[key] ?? null
}

/**
 * The room a plan position falls in on a floor: a room inside another (the toilet, the boiler cupboard, under the
 * stairs) before the room around it, and the patio beside the house; null outside all of them.
 */
export function roomAt(floor, x, y) {
  const inside = (r) => x >= r.x && x <= r.x + r.w && y >= r.y && y <= r.y + r.h
  const candidates = ROOMS.filter((r) => r.floor === floor && inside(r))
  const room = candidates.find((r) => r.inner || r.nested) ?? candidates[0]
  if (room) return room
  return floor === UNPLACED.floor && inside(UNPLACED) ? UNPLACED : null
}

const round = (value, places) => Math.round(value * 10 ** places) / 10 ** places

/**
 * Every device's spot as the arrangement editor prints it, in DEVICE_SPOTS' own shape: key → { room, x, y, height,
 * rotation }, alphabetical. Pasting it into DEVICE_SPOTS reproduces the arrangement.
 */
export function arrangement(devices, overrides = null) {
  const entries = placeDevices(devices, overrides).map(({ device, room, x, y, height, rotation }) => [
    deviceKey(device, devices),
    {
      room: room.id,
      x: round(x, 1),
      y: round(y, 1),
      height: round(height ?? KIND_HEIGHT[deviceKind(device)] ?? 0.9, 2),
      rotation: ((Math.round(rotation ?? 0) % 360) + 360) % 360,
    },
  ])
  entries.sort(([a], [b]) => a.localeCompare(b, undefined, { numeric: true, sensitivity: 'base' }))
  return Object.fromEntries(entries)
}

/** The device a FURNITURE item's `litBy` names, from a list of devices, or null. */
export function deviceNamed(devices, name) {
  const key = spotKey(name)
  return (devices ?? []).find((device) => spotKey(device.name) === key && device.entities?.length) ?? null
}

/**
 * Where each device goes: its known spot if it has one, otherwise its room, on a grid across the room's free area,
 * alphabetical by name. Returns [{ device, room, x, y, height, rotation }] in plan units, x and y being the centre;
 * height (metres) is null when the device's kind decides it.
 */
export function placeDevices(devices, overrides = null) {
  const placed = []
  const byRoom = new Map()
  for (const device of devices ?? []) {
    const spot = deviceSpot(device, overrides, devices)
    if (spot && roomById(spot.room)) {
      placed.push({ device, room: roomById(spot.room), x: spot.x, y: spot.y, height: spot.height ?? null, rotation: spot.rotation ?? 0 })
      continue
    }
    const room = roomForArea(device.area)
    if (!byRoom.has(room.id)) byRoom.set(room.id, [])
    byRoom.get(room.id).push(device)
  }

  for (const [roomId, list] of byRoom) {
    const room = roomById(roomId)
    const area = room.free ?? room
    const sorted = [...list].sort(byName)
    const inset = room.free ? 0 : Math.min(2.5, area.w / 6, area.h / 6)
    const w = area.w - inset * 2
    const h = area.h - inset * 2
    const columns = Math.max(1, Math.round(Math.sqrt((sorted.length * w) / h)))
    const rows = Math.ceil(sorted.length / columns)
    sorted.forEach((device, index) => {
      const column = index % columns
      const row = Math.floor(index / columns)
      placed.push({
        device,
        room,
        x: area.x + inset + (w * (column + 0.5)) / columns,
        y: area.y + inset + (h * (row + 0.5)) / rows,
        height: null,
        rotation: 0,
      })
    })
  }
  return placed
}

// ---- The layout editor (owner, 2026-10-22): arrange mode on the 3D page edits a copy of FURNITURE and OPENINGS and
// device spots, and prints all three as JSON for pasting back here.

/** Walls are the union of every room's edges on a line, so a wall two rooms share is one segment. */
export function wallSegments(rooms) {
  const lines = new Map()
  const add = (key, from, to) => {
    if (!lines.has(key)) lines.set(key, [])
    lines.get(key).push([Math.min(from, to), Math.max(from, to)])
  }
  for (const r of rooms) {
    add(`h:${r.y}`, r.x, r.x + r.w)
    add(`h:${r.y + r.h}`, r.x, r.x + r.w)
    add(`v:${r.x}`, r.y, r.y + r.h)
    add(`v:${r.x + r.w}`, r.y, r.y + r.h)
  }
  const segments = []
  for (const [key, spans] of lines) {
    const [axis, at] = [key[0], Number(key.slice(2))]
    spans.sort((a, b) => a[0] - b[0])
    let [start, end] = spans[0]
    for (const [s, e] of spans.slice(1)) {
      if (s <= end) end = Math.max(end, e)
      else { segments.push({ axis, at, start, end }); [start, end] = [s, e] }
    }
    segments.push({ axis, at, start, end })
  }
  return segments
}

/** Every wall on a floor (the cupboard under the stairs has none of its own). */
export function floorWalls(floor) {
  return wallSegments(ROOMS.filter((r) => r.floor === floor && !r.nested))
}

/** A fresh, editable copy of the house's furniture and openings. */
export function defaultLayout() {
  return { furniture: structuredClone(FURNITURE), openings: structuredClone(OPENINGS) }
}

/** How far one press of an editor button changes things: 45° turns and 5 cm in size. */
export const EDIT_STEPS = { rotation: 45, size: m(0.05) }

/** The wall nearest a plan position on a floor, with how far along it the position falls. */
export function nearestWall(floor, x, y) {
  let best = null
  for (const seg of floorWalls(floor)) {
    const [along, across] = seg.axis === 'h' ? [x, y] : [y, x]
    const clamped = Math.min(seg.end, Math.max(seg.start, along))
    const distance = Math.hypot(along - clamped, across - seg.at)
    if (!best || distance < best.distance) best = { ...seg, along: clamped, distance }
  }
  return best
}

/** An opening moved to the wall nearest (x, y), centred there as far as that wall allows. */
export function placeOpening(opening, x, y) {
  const wall = nearestWall(opening.floor, x, y)
  if (!wall) return opening
  const width = Math.min(opening.to - opening.from, wall.end - wall.start)
  const from = Math.min(wall.end - width, Math.max(wall.start, wall.along - width / 2))
  return { ...opening, axis: wall.axis, at: wall.at, from, to: from + width }
}

/** An opening made wider (delta > 0) or narrower about its middle; never under 20 cm. */
export function resizeOpening(opening, delta) {
  const width = Math.max(m(0.2), opening.to - opening.from + delta)
  const middle = (opening.from + opening.to) / 2
  return { ...opening, from: middle - width / 2, to: middle + width / 2 }
}

/** A door hung from its other edge: `hinge` 'start' (the from end, the default) or 'end'. */
export function flipHinge(opening) {
  return { ...opening, hinge: opening.hinge === 'end' ? 'start' : 'end' }
}

/** A door opening into the room on the other side of its wall (or a sliding door running on the other face). */
export function flipSwing(opening) {
  return { ...opening, swing: -(opening.swing ?? 1) }
}

const normaliseDegrees = (degrees) => (((Math.round(degrees) % 360) + 360) % 360)

/** A piece of furniture with its footprint's middle moved to (x, y). */
export function moveFurniture(item, x, y) {
  return { ...item, x: x - item.w / 2, y: y - item.h / 2 }
}

/** A piece of furniture turned about its middle; positive is anticlockwise seen from above. */
export function rotateFurniture(item, degrees) {
  return { ...item, rotation: normaliseDegrees((item.rotation ?? 0) + degrees) }
}

/**
 * A piece of furniture made longer or shorter (along its longer side) and deeper or shallower (the other side),
 * about its middle; never under 5 cm either way.
 */
export function resizeFurniture(item, longer, deeper) {
  const alongX = item.w >= item.h
  const w = Math.max(m(0.05), item.w + (alongX ? longer : deeper))
  const h = Math.max(m(0.05), item.h + (alongX ? deeper : longer))
  return { ...item, x: item.x + (item.w - w) / 2, y: item.y + (item.h - h) / 2, w, h }
}

/** A copy of an item with a new id, offset a little so it can be seen and dragged into place. */
export function duplicateItem(items, item) {
  let n = items.filter((i) => i.type === item.type).length + 1
  while (items.some((i) => i.id === `${item.type}-${n}`)) n++
  const copy = { ...structuredClone(item), id: `${item.type}-${n}` }
  if ('axis' in item) {
    const width = item.to - item.from
    return placeOpening({ ...copy, from: item.to + m(0.2), to: item.to + m(0.2) + width }, ...(item.axis === 'h' ? [item.to + m(0.2) + width / 2, item.at] : [item.at, item.to + m(0.2) + width / 2]))
  }
  return { ...copy, x: item.x + m(0.3), y: item.y + m(0.3) }
}

const tidy = (item) => Object.fromEntries(Object.entries(item).map(([key, value]) => [key, typeof value === 'number' ? round(value, 2) : value]))

/** Everything arrange mode can change, as the JSON it prints: device spots, furniture and openings. */
export function layoutJson(devices, overrides, layout) {
  return {
    devices: arrangement(devices, overrides),
    furniture: layout.furniture.map(tidy),
    openings: layout.openings.map(tidy),
  }
}

/** The wall pieces left once doors, windows and open-plan gaps are cut out of a wall line's [start, end]. */
export function cutOpenings(segment, openings) {
  const cuts = openings
    .filter((o) => o.axis === segment.axis && o.at === segment.at && o.to > segment.start && o.from < segment.end)
    .sort((a, b) => a.from - b.from)
  const pieces = []
  let start = segment.start
  for (const cut of cuts) {
    if (cut.from > start) pieces.push({ ...segment, start, end: cut.from })
    start = Math.max(start, cut.to)
  }
  if (start < segment.end) pieces.push({ ...segment, start, end: segment.end })
  return { pieces, cuts }
}

/** The devices with a batch of state changes applied, as a new array (devices that changed are new objects). */
export function applyStateChanges(devices, changes) {
  const byEntity = new Map((changes ?? []).map((change) => [change.entityId, change.state]))
  if (byEntity.size === 0) return devices
  return (devices ?? []).map((device) => {
    if (!device.entities.some((e) => byEntity.has(e.entityId))) return device
    return {
      ...device,
      entities: device.entities.map((e) => (byEntity.has(e.entityId) ? { ...e, state: byEntity.get(e.entityId) } : e)),
    }
  })
}

/** The device's entities split for its panel: what can be used, what it reads, and the rest. */
export function groupEntities(device) {
  const controls = []
  const readings = []
  const other = []
  for (const entity of device?.entities ?? []) {
    const domain = domainOf(entity.entityId)
    if (['switch', 'light', 'button', 'input_boolean'].includes(domain)) controls.push(entity)
    else if (domain === 'sensor' || domain === 'binary_sensor') readings.push(entity)
    else other.push(entity)
  }
  const primary = primaryEntity(device)
  const firstPrimary = (a, b) => (a === primary ? -1 : b === primary ? 1 : 0)
  return { controls: controls.sort(firstPrimary), readings, other }
}
