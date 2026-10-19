// Read-only. Current state plus real state transitions from the recorder, for one or more entities.
//
//   node tools/ha/entity-history.cjs <entity_id> [<entity_id> ...] [--hours 48]
//
// Use this, not the state object's last_changed, to answer "when did it really fail / come back"
// (gotcha 3: last_changed is rewritten on every HA restart and integration reload). end_time is
// always passed, because without it the history endpoint silently returns a one-day window (gotcha 1).
// Recorder retention is ~10 days (gotcha 2), so asking for more than ~240 hours gains nothing.
const { rest, parseArgs, run } = require('./lib/ha.cjs');

run(async () => {
  const args = parseArgs();
  const ids = args._;
  if (!ids.length) throw new Error('usage: entity-history.cjs <entity_id> [...] [--hours 48]');
  const hours = Number(args.hours || 48);

  for (const id of ids) {
    try {
      const s = await rest('GET', `/states/${id}`);
      const unit = s.attributes.unit_of_measurement || '';
      console.log(`${id}: ${s.state}${unit ? ' ' + unit : ''} | "${s.attributes.friendly_name || ''}" | last_changed ${s.last_changed}`);
    } catch (e) {
      console.log(`${id}: ${e.message}`);
    }
  }

  const end = new Date();
  const start = new Date(end.getTime() - hours * 3600 * 1000);
  const query = `filter_entity_id=${ids.join(',')}&end_time=${encodeURIComponent(end.toISOString())}&minimal_response&no_attributes`;
  const series = await rest('GET', `/history/period/${encodeURIComponent(start.toISOString())}?${query}`);

  console.log(`\n--- transitions, last ${hours} h (UTC) ---`);
  for (const rows of series) {
    if (!rows.length) continue;
    const id = rows[0].entity_id;
    console.log(`[${id}]`);
    let previous;
    for (const r of rows) {
      if (r.state === previous) continue;   // collapse repeats to real transitions
      console.log(`  ${r.last_changed}  ${r.state}`);
      previous = r.state;
    }
  }
  const missing = ids.filter((id) => !series.some((rows) => rows[0] && rows[0].entity_id === id));
  if (missing.length) console.log(`no history rows: ${missing.join(', ')}`);
});
