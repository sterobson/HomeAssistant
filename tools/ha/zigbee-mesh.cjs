// The Zigbee mesh as ZHA last saw it: each router (mains plug) with its link quality to the coordinator (both
// directions; "-" means that side does not list the other), its best router neighbours, and each battery device
// with the router or coordinator it hangs off (its parent) and that link's LQI. Read-only: it uses the neighbour
// tables ZHA already holds from its periodic scans (refresh them from ZHA's settings page if they look stale).
//
//   node tools/ha/zigbee-mesh.cjs            the table
//   node tools/ha/zigbee-mesh.cjs --json     raw devices with neighbours, for drawing
//
// LQI is 0-255. As a rough guide: 150+ strong, 100-149 fair, 60-99 weak, below 60 poor.
const { connect, parseArgs, run } = require('./lib/ha.cjs');

run(async () => {
  const args = parseArgs(['json']);
  const ha = await connect();
  const areas = new Map((await ha.call({ type: 'config/area_registry/list' })).map((a) => [a.area_id, a.name]));
  const devices = (await ha.call({ type: 'zha/devices' })).map((d) => ({
    ieee: d.ieee,
    name: d.user_given_name || d.name,
    type: d.device_type,
    area: areas.get(d.area_id) ?? null,
    lqi: d.lqi ?? null,
    available: d.available,
    lastSeen: d.last_seen ?? null,
    neighbors: (d.neighbors ?? []).map((n) => ({ ieee: n.ieee, lqi: Number(n.lqi), relationship: n.relationship }))
  }));
  ha.close();

  if (args.json) {
    console.log(JSON.stringify(devices, null, 1));
    return;
  }

  const byIeee = new Map(devices.map((d) => [d.ieee, d]));
  const name = (ieee) => byIeee.get(ieee)?.name ?? `(unknown ${ieee.slice(-5)})`;
  const area = (d) => d.area ?? '(no area)';
  const byArea = (a, b) => area(a).localeCompare(area(b), undefined, { numeric: true, sensitivity: 'base' })
    || a.name.localeCompare(b.name, undefined, { numeric: true, sensitivity: 'base' });

  // A battery device's parent is whichever router lists it as a child (the best such link if several do).
  const parents = new Map();
  for (const d of devices) {
    for (const n of d.neighbors.filter((n) => /child/i.test(n.relationship))) {
      if (!parents.has(n.ieee) || n.lqi > parents.get(n.ieee).lqi) parents.set(n.ieee, { parent: d.ieee, lqi: n.lqi });
    }
  }

  const coordinator = devices.find((d) => d.type === 'Coordinator');
  const link = (from, to) => from?.neighbors.find((n) => n.ieee === to?.ieee)?.lqi;

  console.log(`Coordinator: ${coordinator?.name} (${area(coordinator ?? {})})\n`);
  console.log('Routers: hub->router / router->hub, then best router neighbours');
  for (const r of devices.filter((d) => d.type === 'Router').sort(byArea)) {
    const best = r.neighbors
      .filter((n) => n.ieee !== coordinator?.ieee && byIeee.get(n.ieee)?.type === 'Router')
      .sort((a, b) => b.lqi - a.lqi).slice(0, 2).map((n) => `${name(n.ieee)} ${n.lqi}`).join(', ');
    const offline = r.available ? '' : `  [OFFLINE, last seen ${r.lastSeen}]`;
    console.log(`  ${area(r).padEnd(14)} ${r.name.padEnd(40)} ${String(link(coordinator, r) ?? '-').padStart(3)} / ${String(link(r, coordinator) ?? '-').padEnd(3)}  ${best}${offline}`);
  }

  console.log('\nBattery devices: parent (LQI of that link)');
  for (const d of devices.filter((x) => x.type === 'EndDevice').sort(byArea)) {
    const p = parents.get(d.ieee);
    const offline = d.available ? '' : `  [OFFLINE, last seen ${d.lastSeen}]`;
    console.log(`  ${area(d).padEnd(14)} ${d.name.padEnd(40)} ${p ? `${name(p.parent)} (${p.lqi})` : 'no parent listed'}${offline}`);
  }
});
