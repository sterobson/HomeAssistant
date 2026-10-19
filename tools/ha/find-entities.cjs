// Read-only. Find entities whose ID or friendly name matches a regex, with state and last_updated.
//
//   node tools/ha/find-entities.cjs <regex>           e.g.  fingerbot|j4pdtz9v
//
// Matching friendly_name as well as entity_id matters: entity IDs keep the model prefix or an
// old room name after a rename (gotchas 9 and 10), so the ID alone often misses the device.
const { rest, parseArgs, run } = require('./lib/ha.cjs');

run(async () => {
  const pattern = parseArgs()._[0];
  if (!pattern) throw new Error('usage: find-entities.cjs <regex>');
  const re = new RegExp(pattern, 'i');
  const states = await rest('GET', '/states');
  const hits = states
    .filter((s) => re.test(s.entity_id) || re.test(s.attributes.friendly_name || ''))
    .sort((a, b) => a.entity_id.localeCompare(b.entity_id));
  for (const s of hits) {
    const unit = s.attributes.unit_of_measurement || '';
    console.log(`${s.entity_id} | ${s.state}${unit ? ' ' + unit : ''} | "${s.attributes.friendly_name || ''}" | ${s.last_updated}`);
  }
  console.log(`${hits.length} match(es)`);
});
