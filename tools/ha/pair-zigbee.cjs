// WRITES to Home Assistant. Pair a new Zigbee device through ZHA, then name it and its entities the
// house way. Opening the join window lets any Zigbee device join for up to 254 s; nothing physical
// moves. The renames only touch the newly joined device.
//
//   Dry run (writes nothing): show the rename an existing device would get under the new names
//     node tools/ha/pair-zigbee.cjs --plan <existing_device_id> --prefix den_climate --label Den
//
//   Pair for real: open the join window, wait for a new device of that model, rename it
//     node tools/ha/pair-zigbee.cjs --model SNZB-02D --name "Den - Climate" --area games_room --prefix den_climate --label Den
//
// Naming conventions (see HOME-ASSISTANT.md → Device inventory):
//   room thermometer  device "{Room} - Climate",  entities {room}_climate_*,  --label {Room}
//   radiator TRV      device "{Room} - Radiator", entities {room}_radiator_*
//   smart plug        device "{Room} - Plug - {Purpose}", entities {room}_plug_{purpose}_*, --main-suffix switch
// Every entity becomes {domain}.{prefix}_{snake(original_name)}. The device's main entity has no
// original_name, so it becomes {domain}.{prefix}[_{main-suffix}]. --label, when given, is set as the
// entity name of the temperature and humidity sensors, as on the bedroom thermometers.
// Area IDs: node tools/ha/list-devices.cjs --areas
//
// Start it, then put the device into pairing mode (a new SNZB-02D joins on first power-up; a TRVZB
// needs the sequence in HOME-ASSISTANT.md). It stops without renaming anything if a target entity ID
// is already taken, and ignores any other model that joins in the window.
const { connect, parseArgs, run } = require('./lib/ha.cjs');

const JOIN_SECONDS = 254;           // ZHA's maximum
const EXPECT_SETTLED_MS = 20000;    // entities appear as the interview runs; wait until the count is stable this long
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));
const snake = (s) => s.toLowerCase().replace(/[^a-z0-9]+/g, '_').replace(/^_|_$/g, '');
const time = () => new Date().toLocaleTimeString();

function plan(entities, prefix, label, mainSuffix) {
  return entities.map((e) => {
    const domain = e.entity_id.split('.')[0];
    // A device's main entity (a plug's on/off switch) has no original_name; it takes the prefix
    // itself, plus --main-suffix if given (the heater plugs are switch.{room}_plug_heater_switch).
    const to = e.original_name
      ? `${domain}.${prefix}_${snake(e.original_name)}`
      : `${domain}.${prefix}${mainSuffix ? `_${snake(mainSuffix)}` : ''}`;
    const labelled = label && domain === 'sensor' && /^(temperature|humidity)$/i.test(e.original_name || '');
    return { from: e.entity_id, to, name: labelled ? label : undefined };
  });
}

async function entitiesOf(ha, deviceId) {
  return (await ha.call({ type: 'config/entity_registry/list' })).filter((e) => e.device_id === deviceId);
}

async function problems(ha, steps) {
  const taken = new Set((await ha.call({ type: 'config/entity_registry/list' })).map((e) => e.entity_id));
  const targets = steps.map((s) => s.to);
  return [
    ...targets.filter((t, i) => targets.indexOf(t) !== i).map((t) => `duplicate target ${t}`),
    ...steps.filter((s) => s.from !== s.to && taken.has(s.to)).map((s) => `already taken: ${s.to}`),
  ];
}

const show = (s) => `  ${s.from}  ->  ${s.to}${s.name ? `   (name "${s.name}")` : ''}`;

async function dryRun(ha, deviceId, prefix, label, mainSuffix) {
  const steps = plan(await entitiesOf(ha, deviceId), prefix, label, mainSuffix);
  steps.forEach((s) => console.log(show(s)));
  const issues = await problems(ha, steps);
  console.log(issues.length ? issues.join('\n') : `${steps.length} entities, no clashes`);
}

async function pair(ha, { model, name, area, prefix, label, 'main-suffix': mainSuffix }) {
  const modelRe = new RegExp(model, 'i');
  const before = new Set((await ha.call({ type: 'config/device_registry/list' })).map((d) => d.id));
  await ha.call({ type: 'call_service', domain: 'zha', service: 'permit', service_data: { duration: JOIN_SECONDS } });
  console.log(`${time()} ZHA join window open for ${JOIN_SECONDS} s. Put the ${model} into pairing mode now.`);

  const deadline = Date.now() + (JOIN_SECONDS + 60) * 1000;
  const reported = new Set();
  let device;
  while (!device && Date.now() < deadline) {
    await sleep(3000);
    const fresh = (await ha.call({ type: 'config/device_registry/list' })).filter((d) => !before.has(d.id));
    fresh.filter((d) => !modelRe.test(d.model || '') && !reported.has(d.id)).forEach((d) => {
      reported.add(d.id);
      console.log(`  ignoring a different new device: ${d.name} (${d.model})`);
    });
    device = fresh.find((d) => modelRe.test(d.model || ''));
  }
  if (!device) { console.log(`No new ${model} joined before the window closed. Nothing renamed.`); return; }
  console.log(`${time()} Joined: ${device.manufacturer} ${device.model} device_id=${device.id}`);

  let count = -1;
  let stableSince = Date.now();
  while (Date.now() - stableSince < EXPECT_SETTLED_MS) {
    await sleep(2000);
    const n = (await entitiesOf(ha, device.id)).length;
    if (n !== count) { count = n; stableSince = Date.now(); console.log(`  entities so far: ${n}`); }
  }

  const steps = plan(await entitiesOf(ha, device.id), prefix, label, mainSuffix);
  const issues = await problems(ha, steps);
  if (issues.length) {
    console.log(`Stopping before renaming anything:\n${issues.join('\n')}`);
    return;
  }

  const update = { type: 'config/device_registry/update', device_id: device.id, name_by_user: name };
  if (area) update.area_id = area;
  await ha.call(update);
  console.log(`Device named "${name}"${area ? `, area ${area}` : ''}`);
  for (const s of steps) {
    const msg = { type: 'config/entity_registry/update', entity_id: s.from };
    if (s.from !== s.to) msg.new_entity_id = s.to;
    if (s.name) msg.name = s.name;
    if (msg.new_entity_id || msg.name) await ha.call(msg);
    console.log(show(s));
  }
  console.log(`Done: ${steps.length} entities. Check it with: node tools/ha/list-devices.cjs --name "${name}"`);
}

run(async () => {
  const args = parseArgs();
  if (!args.prefix) throw new Error('--prefix is required (e.g. den_climate). See the header of this file.');
  const ha = await connect();
  try {
    if (args.plan) await dryRun(ha, args.plan, args.prefix, args.label, args['main-suffix']);
    else if (args.model && args.name) await pair(ha, args);
    else throw new Error('usage: --plan <device_id> --prefix ...  |  --model <regex> --name "<Device name>" --prefix ... [--area <area_id>] [--label <Room>] [--main-suffix switch]');
  } finally {
    ha.close();
  }
});
