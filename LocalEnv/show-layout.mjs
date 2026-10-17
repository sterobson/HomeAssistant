// Prints the saved layout tree of the LOCAL copy, with ids and icons. Read-only.
// Usage: node show-layout.mjs [houseId]
const house = process.argv[2] ?? 'LOCAL-COPY';
const data = await (await fetch(`http://localhost:7159/api/schedules?houseId=${house}`)).json();
const names = new Map(data.rooms.map((r) => [r.id, r.displayName || r.name]));

const show = (nodes, depth = 0) => {
  for (const n of nodes) {
    const pad = '  '.repeat(depth);
    if (n.roomId !== undefined && n.roomId !== null) {
      const room = data.rooms.find((r) => r.id === n.roomId);
      console.log(`${pad}room ${n.roomId} "${names.get(n.roomId) ?? '?'}"${room?.icon ? ' ' + room.icon : ''}`);
    } else {
      console.log(`${pad}[${n.name}] id=${n.id}${n.icon ? ' ' + n.icon : ''}`);
      show(n.children ?? [], depth + 1);
    }
  }
};
show(data.layout ?? []);
