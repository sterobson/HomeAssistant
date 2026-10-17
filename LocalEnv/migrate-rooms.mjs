// Migrates the schedules blob to the split-zone layout, written as a `layout` tree
// (rooms and groups, to any depth) rather than a label on each room.
//
//   Dining room (container)   Den (container)
//     dining room  TRV          games desk  heater plug  <- was "Games room", id 2
//     dining desk  plug         den         no hardware yet
//
// DRY RUN BY DEFAULT. Prints the before/after and writes nothing.
// Pass --apply to POST it.
//
//   node migrate-rooms.mjs LOCAL-COPY                 # dry run against the LOCAL emulator
//   node migrate-rooms.mjs LOCAL-COPY --apply         # write to the LOCAL emulator
//
//   # Production: both flags are required, on purpose.
//   node migrate-rooms.mjs 1234567 --api https://homeassistant-prod-functions.azurewebsites.net/api
//   node migrate-rooms.mjs 1234567 --api https://homeassistant-prod-functions.azurewebsites.net/api --apply --i-mean-production
//
// Run this only AFTER the Functions app and NetDaemon app carrying the new room names are
// deployed. Before then the Functions app drops the unknown displayName/icon/layout fields, and
// the running NetDaemon app does not recognise "dining desk" / "games desk" / "den".

const flag = (name) => { const i = process.argv.indexOf(`--${name}`); return i > -1 ? process.argv[i + 1] : undefined; };
const API = flag('api') ?? 'http://localhost:7159/api'; // local unless told otherwise
const houseId = process.argv[2];
const apply = process.argv.includes('--apply');
const isLocal = /^http:\/\/(localhost|127\.0\.0\.1)[:/]/.test(API);

if (apply && !isLocal && !process.argv.includes('--i-mean-production')) {
  console.error(`REFUSING to write to ${API}: that is not localhost. If you really mean production, add --i-mean-production.`);
  process.exit(1);
}
console.log(`API: ${API}  (${isLocal ? 'local emulator' : 'REMOTE'})  house: ${houseId}  mode: ${apply ? 'APPLY' : 'dry run'}
`);

if (!houseId) {
  console.error('Usage: node migrate-rooms.mjs <houseId> [--api URL] [--apply] [--i-mean-production]');
  process.exit(1);
}

const RoomInUse = 4;
const RoomNotInUse = 8;

const res = await fetch(`${API}/schedules?houseId=${houseId}`);
if (!res.ok) {
  console.error('GET failed:', res.status, res.statusText);
  process.exit(1);
}
const data = await res.json();

const byName = (name) => data.rooms.find((r) => r.name.trim().toLowerCase() === name);
const dining = byName('dining room');
const games = byName('games room');

if (!dining || !games) {
  console.error('Expected to find both "Dining room" and "Games room". Found:', data.rooms.map((r) => r.name));
  process.exit(1);
}

const nextRoomId = Math.max(...data.rooms.map((r) => r.id)) + 1;

// The radiator zone cannot see desk presence, so a RoomInUse track there would silently never
// fire and a RoomNotInUse track would always fire. Carry over only the RoomNotInUse tracks,
// with the presence bit cleared, which is the baseline the radiator actually followed before.
const radiatorBaseline = dining.schedules
  .filter((t) => (t.conditions & RoomNotInUse) !== 0)
  .map((t, i) => ({ ...t, id: i + 1, conditions: t.conditions & ~RoomNotInUse & ~RoomInUse }));

const migrated = {
  houseOccupancyState: data.houseOccupancyState,
  rooms: data.rooms.flatMap((room) => {
    // The dining room keeps its id and its history, and narrows to the radiator.
    if (room.id === dining.id) {
      return [
        { ...room, name: 'dining room', displayName: 'Dining room', group: 'Dining room', schedules: radiatorBaseline },
        {
          id: nextRoomId,
          name: 'dining desk',
          displayName: "Ruth's desk",
          group: 'Dining room',
          boost: null,
          // The desk keeps the original schedules untouched, presence conditions and all.
          schedules: dining.schedules,
        },
      ];
    }

    // The games room was only ever the desk's plug and thermometer, so it becomes the desk.
    if (room.id === games.id) {
      return [
        { ...room, name: 'games desk', displayName: "Ste's desk", group: 'Den' },
        {
          id: nextRoomId + 1,
          name: 'den',
          // Named after the room, like the dining radiator, so inside the "Den" group it reads
          // as the room's own heating rather than as the lower-case join key "den".
          displayName: 'Den',
          group: 'Den',
          boost: null,
          schedules: [], // No TRVs fitted yet.
        },
      ];
    }

    // The three bedroom renames. Display names only: `name` stays "Bedroom N", the key the
    // heating code matches on to find each room's radiator and sensor.
    const bedroomNames = {
      'bedroom 1': "Ruth and Ste's room",
      'bedroom 2': "Joseph's room",
      'bedroom 3': "Ellie's room",
    };
    const bedroomName = bedroomNames[room.name.trim().toLowerCase()];

    return [{ ...room, displayName: bedroomName ?? room.displayName ?? null, group: room.group ?? null }];
  }),
};

// Turn the group labels used above into the saved form: an ordered tree of rooms and groups. A group
// sits where its first room is. Group ids are stable strings, so a group can be renamed later.
const layout = [];
const groupNodes = new Map();
for (const room of migrated.rooms) {
  if (!room.group) {
    layout.push({ roomId: room.id });
  } else {
    let node = groupNodes.get(room.group);
    if (!node) {
      node = { id: room.group.toLowerCase().replace(/\s+/g, '-'), name: room.group, icon: null, children: [] };
      groupNodes.set(room.group, node);
      layout.push(node);
    }
    node.children.push({ roomId: room.id });
  }
  delete room.group;
}
migrated.layout = layout;

const summarise = (rooms) =>
  rooms
    .map((r) => `  id=${r.id} name="${r.name}" display="${r.displayName ?? ''}" tracks=${r.schedules.length}`)
    .join('\n');

console.log('BEFORE:\n' + summarise(data.rooms));
console.log('\nAFTER:\n' + summarise(migrated.rooms));
const show = (nodes, depth = 1) => nodes.map((n) => '  '.repeat(depth) + (n.roomId !== undefined ? `room ${n.roomId}` : `[${n.name}]\n${show(n.children, depth + 1)}`)).join('\n');
console.log('\nLAYOUT:\n' + show(migrated.layout));
console.log('\nRadiator baseline carried over:');
for (const t of radiatorBaseline) {
  console.log(`  ${t.time}  ${t.temperature}°C  conditions=${t.conditions}`);
}

if (!apply) {
  console.log('\nDry run — nothing written. Re-run with --apply to POST this.');
  process.exit(0);
}

const post = await fetch(`${API}/schedules?houseId=${houseId}`, {
  method: 'POST',
  headers: { 'Content-Type': 'application/json' },
  body: JSON.stringify(migrated),
});
console.log('\nPOST', post.status, post.statusText, await post.text());
