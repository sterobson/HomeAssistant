// WRITES to Home Assistant. Create or replace automations from JSON files, then read them back.
//
//   node tools/ha/put-automation.cjs tools/ha/automations/<file>.json [...]
//   node tools/ha/put-automation.cjs --check tools/ha/automations/<file>.json [...]   compare only, write nothing
//   node tools/ha/put-automation.cjs --delete <automation id> --backup <file.json>    save its config, then delete it
//
// Each file is an automation config exactly as HA stores it, with an "id" field. Saving goes through
// the same config API as the UI editor: HA writes automations.yaml and reloads automations itself.
// An automation that already exists under that id is replaced, so the repo copy wins; anyone who
// edits one in the HA UI should copy it back here (run with --check to spot drift).
//
// Saving reloads automations. Threshold triggers fire only on a *crossing*, so they do nothing on
// save, but an automation with an `automation_reloaded` trigger (the den pair has one) runs straight
// away — so a save can switch real devices.
const fs = require('fs');
const { rest, parseArgs, run } = require('./lib/ha.cjs');

const sleep = (ms) => new Promise((r) => setTimeout(r, ms));
// Key-order-independent JSON, so HA reordering fields on save does not read as drift.
const canonical = (v) => Array.isArray(v) ? `[${v.map(canonical).join(',')}]`
  : v && typeof v === 'object' ? `{${Object.keys(v).sort().map((k) => `${JSON.stringify(k)}:${canonical(v[k])}`).join(',')}}`
  : JSON.stringify(v);

run(async () => {
  const args = parseArgs(['check']);

  if (args.delete) {
    // The automation's numeric `id` attribute, not its entity ID. Its config is written out first so
    // a deletion can be undone by putting that file back.
    if (!args.backup) throw new Error('--delete needs --backup <file.json> to save the config to first');
    const config = await rest('GET', `/config/automation/config/${args.delete}`);
    fs.writeFileSync(args.backup, JSON.stringify(config, null, 2));
    console.log(`saved "${config.alias}" to ${args.backup}`);
    await rest('DELETE', `/config/automation/config/${args.delete}`);
    await sleep(1500);
    const states = await rest('GET', '/states');
    const still = states.find((s) => s.entity_id.startsWith('automation.') && s.attributes.id === String(args.delete));
    console.log(still ? `NOT deleted: ${still.entity_id} still exists` : `deleted automation ${args.delete}`);
    return;
  }

  const files = args._;
  if (!files.length) throw new Error('usage: put-automation.cjs [--check] <automation.json> [...]');

  for (const file of files) {
    const wanted = JSON.parse(fs.readFileSync(file, 'utf8'));
    if (!wanted.id) throw new Error(`${file}: missing "id"`);

    let current = null;
    try { current = await rest('GET', `/config/automation/config/${wanted.id}`); } catch { /* 404: new */ }
    const same = current && canonical(current) === canonical(wanted);
    console.log(`${wanted.id}: ${current ? (same ? 'matches HA' : 'differs from HA') : 'not in HA yet'}`);
    if (args.check || same) continue;

    await rest('POST', `/config/automation/config/${wanted.id}`, wanted);
    await sleep(1500);   // HA reloads automations after the save
    const saved = await rest('GET', `/config/automation/config/${wanted.id}`);
    const states = await rest('GET', '/states');
    const entity = states.find((s) => s.entity_id.startsWith('automation.') && s.attributes.id === wanted.id);
    console.log(`  saved: ${saved.alias} | entity ${entity ? `${entity.entity_id} (${entity.state})` : 'NOT FOUND — check the HA log'}`);
  }
});
