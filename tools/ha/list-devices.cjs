// Read-only. Devices from the registry with model, area, and every entity with its live state.
//
//   node tools/ha/list-devices.cjs [--model <regex>] [--name <regex>] [--area <regex>] [--areas]
//
//   --model SNZB-02          every room thermometer (model comes from the registry, not the entity ID)
//   --name "Den - Climate"   one device by its display name, e.g. to check a pairing afterwards
//   --areas                  just list the areas and their IDs
//
// This is the authoritative way to see which hardware an entity is on and how existing devices are
// named before naming a new one (gotchas 10 and 11).
const { connect, snapshot, parseArgs, run } = require('./lib/ha.cjs');

run(async () => {
  const args = parseArgs(['areas']);
  const ha = await connect();
  try {
    const snap = await snapshot(ha);

    if (args.areas) {
      snap.areas
        .sort((a, b) => a.name.localeCompare(b.name, undefined, { numeric: true, sensitivity: 'base' }))
        .forEach((a) => console.log(`${a.area_id} | ${a.name}`));
      return;
    }

    const test = (flag, value) => !args[flag] || new RegExp(args[flag], 'i').test(value || '');
    const devices = snap.devices
      .filter((d) => test('model', d.model)
        && test('name', d.name_by_user || d.name)
        && test('area', snap.areaName[d.area_id] || d.area_id))
      .sort((a, b) => (a.name_by_user || a.name || '').localeCompare(b.name_by_user || b.name || '', undefined, { numeric: true, sensitivity: 'base' }));

    for (const d of devices) {
      console.log(`\n${d.name_by_user || d.name} | ${d.manufacturer} ${d.model} | area=${snap.areaName[d.area_id] || d.area_id || '(none)'} | device_id=${d.id}`);
      snap.entities
        .filter((e) => e.device_id === d.id)
        .sort((a, b) => a.entity_id.localeCompare(b.entity_id))
        .forEach((e) => {
          const s = snap.stateOf[e.entity_id];
          const value = s ? `${s.state}${s.attributes.unit_of_measurement ? ' ' + s.attributes.unit_of_measurement : ''}` : '(no state)';
          const flags = [e.name ? `name="${e.name}"` : '', e.disabled_by ? `disabled by ${e.disabled_by}` : '', e.hidden_by ? `hidden by ${e.hidden_by}` : '']
            .filter(Boolean).join(', ');
          console.log(`    ${e.entity_id} | ${value}${flags ? ' | ' + flags : ''}`);
        });
    }
    console.log(`\n${devices.length} device(s)`);
  } finally {
    ha.close();
  }
});
