// Rename an EXISTING device and its entities the house way. Dry run by default; writes only with --apply.
//
//   node tools/ha/rename-device.cjs <device_id> --name "Living Room - Plug - TV" --prefix living_room_plug_tv --main-suffix switch
//   node tools/ha/rename-device.cjs <device_id> --name ... --prefix ... --main-suffix switch --apply
//   node tools/ha/rename-device.cjs <device_id> --name "Den tall lamp" --prefix den_tall_lamp --platform wiz --apply
//
// Same naming rules as pair-zigbee.cjs (see lib/naming.cjs and the README). Only entities of the device's own
// integration are renamed: ZHA unless --platform says otherwise (wiz for a WiZ bulb). Entities other integrations
// attach to the device (e.g. the Octopus cost tracker) are listed but never renamed. Stops without
// writing anything if a target entity ID is already taken.
//
// Renaming an entity ID breaks anything that refers to the OLD ID: HA automations using entity
// triggers, dashboards, and Backend/App's NamedEntities. Device triggers refer to the device and
// survive. Check before applying.
const { connect, parseArgs, run } = require('./lib/ha.cjs');
const { plan, skipped, entitiesOf, problems, apply, show } = require('./lib/naming.cjs');

run(async () => {
  const args = parseArgs(['apply']);
  const deviceId = args._[0];
  if (!deviceId || !args.name || !args.prefix) {
    throw new Error('usage: rename-device.cjs <device_id> --name "<Device name>" --prefix <prefix> [--main-suffix switch] [--label <Room>] [--area <area_id>] [--platform zha] [--apply]');
  }
  const ha = await connect();
  try {
    const device = (await ha.call({ type: 'config/device_registry/list' })).find((d) => d.id === deviceId);
    if (!device) throw new Error(`no device ${deviceId}`);
    console.log(`${device.name_by_user || device.name} (${device.manufacturer} ${device.model})  ->  "${args.name}"${args.area ? `, area ${args.area}` : ''}`);

    const entities = await entitiesOf(ha, deviceId);
    const platform = args.platform || 'zha';
    const steps = plan(entities, { prefix: args.prefix, label: args.label, mainSuffix: args['main-suffix'], platform });
    steps.forEach((s) => console.log(show(s)));
    skipped(entities, platform).forEach((e) => console.log(`  (left alone, belongs to ${e.platform}: ${e.entity_id})`));

    const issues = await problems(ha, steps);
    if (issues.length) { console.log(`Stopping, nothing written:\n${issues.join('\n')}`); return; }
    if (!args.apply) { console.log('Dry run. Add --apply to write.'); return; }

    await apply(ha, deviceId, steps, { name: args.name, area: args.area });
    console.log(`Done. Check it with: node tools/ha/list-devices.cjs --name "${args.name}"`);
  } finally {
    ha.close();
  }
});
