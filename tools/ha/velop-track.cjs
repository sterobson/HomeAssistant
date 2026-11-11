// Linksys Velop device trackers: which router devices the integration tracks, and swapping one for another.
//
//   node tools/ha/velop-track.cjs                                   the tracked devices and each one's tracker entity (read-only)
//   node tools/ha/velop-track.cjs --swap <old-id> <new-id> --entity device_tracker.x --name "Ellie's phone (Wi-Fi)"
//                                                                   dry run: what the swap would do
//   node tools/ha/velop-track.cjs --swap ... --apply                WRITES: see below
//
// Use the swap when a phone takes a new random Wi-Fi address and so gets a new router id (HOME-ASSISTANT.md, router note;
// find the new id with router-devices.cjs). --apply changes the integration's tracked list, which reloads the integration
// (its entities are unavailable for a few seconds), then removes the old device's tracker from the entity registry and gives
// the new one the old one's entity ID and name, so anything that refers to the entity keeps working.
const { connect, rest, parseArgs, run } = require('./lib/ha.cjs');

const argv = process.argv.slice(2);

// Starts the integration's options flow at its device tracker step. Returns { flow, tracked }.
async function openTrackerForm(entryId) {
  const menu = await rest('POST', '/config/config_entries/options/flow', { handler: entryId });
  const form = await rest('POST', `/config/config_entries/options/flow/${menu.flow_id}`, { next_step_id: 'device_trackers' });
  const field = (form.data_schema || []).find((f) => f.name === 'tracked');
  if (!field) throw new Error('The options flow has no "tracked" field: this integration version differs from the one this script was written for');
  return { flow: form.flow_id, tracked: field.default || [] };
}

const abort = (flow) => rest('DELETE', `/config/config_entries/options/flow/${flow}`).catch(() => {});

run(async () => {
  const swapAt = argv.indexOf('--swap');
  const apply = argv.includes('--apply');
  const flagValue = (name) => { const i = argv.indexOf(name); return i >= 0 ? argv[i + 1] : undefined; };

  const ws = await connect();
  try {
    const entries = await ws.call({ type: 'config_entries/get', domain: 'linksys_velop' });
    if (entries.length !== 1) throw new Error(`expected one linksys_velop config entry, found ${entries.length}`);
    const entryId = entries[0].entry_id;

    const registry = await ws.call({ type: 'config/entity_registry/list' });
    const trackers = registry.filter((e) => e.platform === 'linksys_velop' && e.entity_id.startsWith('device_tracker.'));
    const trackerFor = (routerId) => trackers.find((e) => e.unique_id.endsWith(`-${routerId}`));

    const { flow, tracked } = await openTrackerForm(entryId);
    console.log('Tracked router devices:');
    for (const id of tracked) console.log(`  ${id}  ->  ${trackerFor(id)?.entity_id ?? '(no tracker entity yet)'}`);

    if (swapAt < 0) { await abort(flow); return; }

    const oldId = argv[swapAt + 1];
    const newId = argv[swapAt + 2];
    const entity = flagValue('--entity');
    const name = flagValue('--name');
    if (!oldId || !newId || !entity || !name) throw new Error('usage: --swap <old-id> <new-id> --entity <entity_id> --name <friendly name> [--apply]');
    if (!tracked.includes(oldId)) throw new Error(`${oldId} is not tracked`);
    if (tracked.includes(newId)) throw new Error(`${newId} is already tracked`);

    const oldTracker = trackerFor(oldId);
    console.log(`\nSwap: ${oldId} -> ${newId}`);
    console.log(`  remove the old tracker ${oldTracker?.entity_id ?? '(none registered)'} from the entity registry`);
    console.log(`  name the new tracker ${entity} ("${name}")`);
    if (!apply) { console.log('\nDry run: nothing changed. Add --apply to do it.'); await abort(flow); return; }

    const next = tracked.map((id) => (id === oldId ? newId : id));
    // The flow does not end at the tracked list: it goes on through the UI device, events, entity and logging steps, and
    // only the last returns create_entry (which is when everything is saved). Each later step is submitted with its own
    // default, which is the value already stored, so nothing else changes. Leaving the flow part-way saves nothing.
    let result = await rest('POST', `/config/config_entries/options/flow/${flow}`, { tracked: next });
    for (let i = 0; i < 8 && result.type === 'form'; i++) {
      if (result.errors) throw new Error(`the options flow rejected step ${result.step_id}: ${JSON.stringify(result.errors)}`);
      const defaults = {};
      for (const field of result.data_schema || []) if (field.default !== undefined) defaults[field.name] = field.default;
      result = await rest('POST', `/config/config_entries/options/flow/${flow}`, defaults);
    }
    if (result.type !== 'create_entry') throw new Error(`the options flow did not finish: ${JSON.stringify(result).slice(0, 300)}`);
    console.log('\nTracked list saved; waiting for the integration to reload and register the new tracker...');

    let created;
    for (let i = 0; i < 40 && !created; i++) {
      await new Promise((resolve) => setTimeout(resolve, 3000));
      const now = await ws.call({ type: 'config/entity_registry/list' });
      created = now.find((e) => e.platform === 'linksys_velop' && e.unique_id.endsWith(`-${newId}`));
    }
    if (!created) throw new Error('the new tracker did not appear within two minutes: the tracked list is saved, so finish by hand in the entity registry');

    if (oldTracker) await ws.call({ type: 'config/entity_registry/remove', entity_id: oldTracker.entity_id });
    await ws.call({ type: 'config/entity_registry/update', entity_id: created.entity_id, new_entity_id: entity, name });

    await new Promise((resolve) => setTimeout(resolve, 3000));
    const state = await rest('GET', `/states/${entity}`);
    console.log(`\nDone: ${entity} = ${state.state} ("${state.attributes.friendly_name}")`);
  } finally {
    ws.close();
  }
});
