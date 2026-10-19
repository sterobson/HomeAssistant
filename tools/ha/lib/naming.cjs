// House-style naming for a device's entities, shared by pair-zigbee.cjs and rename-device.cjs.
// See ../README.md → Pairing and naming.
//
// Every entity becomes {domain}.{prefix}_{snake(original_name)}. A device's main entity (a plug's
// on/off switch) has no original_name, so it becomes {domain}.{prefix}[_{mainSuffix}]. When `label` is
// given it is set as the entity name of the temperature and humidity sensors.
//
// Only entities of the device's own integration (`platform`, default zha) are renamed. Other
// integrations can attach entities to the same device — the Octopus cost tracker does — and those
// are not this device's to rename.

const snake = (s) => s.toLowerCase().replace(/[^a-z0-9]+/g, '_').replace(/^_|_$/g, '');

function plan(entities, { prefix, label, mainSuffix, platform = 'zha' }) {
  return entities
    .filter((e) => e.platform === platform)
    .map((e) => {
      const domain = e.entity_id.split('.')[0];
      const to = e.original_name
        ? `${domain}.${prefix}_${snake(e.original_name)}`
        : `${domain}.${prefix}${mainSuffix ? `_${snake(mainSuffix)}` : ''}`;
      const labelled = label && domain === 'sensor' && /^(temperature|humidity)$/i.test(e.original_name || '');
      return { from: e.entity_id, to, name: labelled ? label : undefined };
    });
}

// Entities on the device that the plan leaves alone, so the caller can say so.
function skipped(entities, platform = 'zha') {
  return entities.filter((e) => e.platform !== platform);
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

// Writes: names the device (and area, if given), then renames each entity in the plan.
async function apply(ha, deviceId, steps, { name, area }) {
  const update = { type: 'config/device_registry/update', device_id: deviceId, name_by_user: name };
  if (area) update.area_id = area;
  await ha.call(update);
  for (const s of steps) {
    const msg = { type: 'config/entity_registry/update', entity_id: s.from };
    if (s.from !== s.to) msg.new_entity_id = s.to;
    if (s.name) msg.name = s.name;
    if (msg.new_entity_id || msg.name) await ha.call(msg);
  }
}

const show = (s) => `  ${s.from}  ->  ${s.to}${s.name ? `   (name "${s.name}")` : ''}`;

module.exports = { snake, plan, skipped, entitiesOf, problems, apply, show };
