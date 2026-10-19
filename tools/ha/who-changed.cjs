// Who or what changed an entity: each state change in HA's logbook with what caused it (a user, an
// automation, a script, or a device reporting by itself).
//
//   node tools/ha/who-changed.cjs <entity_id> [--hours 6]
//
// Read-only. Written 2026-10-19 to find out what pressed the boiler fingerbot twice at 13:17 BST.
// The logbook keeps the "context" HA attaches to every change: context_user_id for a person (UI, app or
// a long-lived token - including this repo's scripts and the NetDaemon app, which all share the owner's
// token), context_domain/context_service for the service call, and context_entity_id when an automation
// or script did it. A change with no context is the device reporting on its own.
const { rest, connect, parseArgs, run } = require('./lib/ha.cjs');

run(async () => {
  const args = parseArgs();
  const [entityId] = args._;
  if (!entityId) throw new Error('usage: node tools/ha/who-changed.cjs <entity_id> [--hours 6]');
  const hours = Number(args.hours ?? 6);
  const start = new Date(Date.now() - hours * 3600 * 1000).toISOString();

  // Names for user ids, when the token is allowed to list users (admin only).
  const names = {};
  try {
    const ha = await connect();
    for (const u of await ha.call({ type: 'config/auth/list' })) names[u.id] = u.name;
    ha.close();
  } catch {
    // Not an admin token: user ids are shown as they are.
  }

  const entries = await rest('GET', `/logbook/${encodeURIComponent(start)}?entity=${encodeURIComponent(entityId)}`);
  if (!entries.length) {
    console.log(`no logbook entries for ${entityId} in the last ${hours} h`);
    return;
  }
  for (const e of entries) {
    const by = [];
    if (e.context_user_id) by.push(`user ${names[e.context_user_id] ?? e.context_user_id}`);
    if (e.context_entity_id) by.push(`via ${e.context_entity_id}${e.context_entity_id_name ? ` (${e.context_entity_id_name})` : ''}`);
    if (e.context_domain || e.context_service) by.push(`service ${e.context_domain ?? '?'}.${e.context_service ?? '?'}`);
    if (e.context_event_type && !e.context_service) by.push(`event ${e.context_event_type}`);
    console.log(`${e.when}  ${e.state ?? e.message ?? ''}  <- ${by.length ? by.join(', ') : 'no context (the device reported it)'}`);
  }
});
