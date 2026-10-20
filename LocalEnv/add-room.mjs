// Adds a room to the schedules blob and puts it in a layout group. The heating page cannot add rooms, so this is how
// one gets in. The room starts with no schedules; its `name` is the canonical key the heating code matches on
// (see CLAUDE.md → Heating), so the app that knows the name must be deployed first.
//
// DRY RUN BY DEFAULT. Prints the before/after and writes nothing. Pass --apply to POST it.
//
//   node add-room.mjs LOCAL-COPY --name hallway --display Hallway --group g3             # dry run, local emulator
//   node add-room.mjs LOCAL-COPY --name hallway --display Hallway --group g3 --apply     # write to the local emulator
//
//   # Production: both flags are required, on purpose. Back the blob up first (a GET of /schedules).
//   node add-room.mjs 1234567 --api https://homeassistant-prod-functions.azurewebsites.net/api --name hallway --display Hallway --group g3
//   node add-room.mjs 1234567 --api https://homeassistant-prod-functions.azurewebsites.net/api --name hallway --display Hallway --group g3 --apply --i-mean-production
//
// --group is a layout group's id (see show-layout.mjs); leave it out to add the room at the top level.
// The POST replaces the whole blob, so the rest is sent back exactly as read. Refuses a name already in use.

const flag = (name) => { const i = process.argv.indexOf(`--${name}`); return i > -1 ? process.argv[i + 1] : undefined; };
const API = flag('api') ?? 'http://localhost:7159/api'; // local unless told otherwise
const houseId = process.argv[2];
const name = flag('name');
const displayName = flag('display') ?? null;
const icon = flag('icon') ?? null;
const groupId = flag('group');
const apply = process.argv.includes('--apply');
const isLocal = /^http:\/\/(localhost|127\.0\.0\.1)[:/]/.test(API);

if (!houseId || !name) {
  console.error('Usage: node add-room.mjs <houseId> --name <canonical name> [--display <name>] [--icon <emoji>] [--group <layout group id>] [--api URL] [--apply] [--i-mean-production]');
  process.exit(1);
}
if (apply && !isLocal && !process.argv.includes('--i-mean-production')) {
  console.error(`REFUSING to write to ${API}: that is not localhost. If you really mean production, add --i-mean-production.`);
  process.exit(1);
}
console.log(`API: ${API}  (${isLocal ? 'local emulator' : 'REMOTE'})  house: ${houseId}  mode: ${apply ? 'APPLY' : 'dry run'}\n`);

const res = await fetch(`${API}/schedules?houseId=${houseId}`);
if (!res.ok) {
  console.error('GET failed:', res.status, res.statusText);
  process.exit(1);
}
const data = await res.json();

if (data.rooms.some((r) => r.name.trim().toLowerCase() === name.trim().toLowerCase())) {
  console.error(`A room named "${name}" already exists. Nothing to do.`);
  process.exit(1);
}

const room = {
  id: Math.max(0, ...data.rooms.map((r) => r.id)) + 1,
  name,
  displayName,
  icon,
  boost: null,
  schedules: [],
};

const findGroup = (nodes) => {
  for (const node of nodes ?? []) {
    if (node.id === groupId) return node;
    const inside = findGroup(node.children);
    if (inside) return inside;
  }
  return null;
};

const layout = data.layout ?? [];
const entry = { roomId: room.id, id: null, name: null, icon: null, children: null };
if (groupId) {
  const group = findGroup(layout);
  if (!group) {
    console.error(`No layout group with id "${groupId}". Groups:`, layout.filter((n) => n.id).map((n) => `${n.id} (${n.name})`).join(', '));
    process.exit(1);
  }
  group.children.push(entry);
} else {
  layout.push(entry);
}

const updated = { ...data, layout, rooms: [...data.rooms, room] };

console.log(`Adding room id=${room.id} name="${room.name}" display="${room.displayName ?? ''}" ${groupId ? `to group "${groupId}"` : 'at the top level'}`);
console.log(`Rooms before: ${data.rooms.length}, after: ${updated.rooms.length}`);

if (!apply) {
  console.log('\nDry run — nothing written. Re-run with --apply to POST this.');
  process.exit(0);
}

const post = await fetch(`${API}/schedules?houseId=${houseId}`, {
  method: 'POST',
  headers: { 'Content-Type': 'application/json' },
  body: JSON.stringify(updated),
});
console.log('\nPOST', post.status, post.statusText, await post.text());
