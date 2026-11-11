// Read-only. What the router lists as online (sensor.mesh_online_devices -> devices), the list the heating app's
// phone detectors read, plus the trackers HA keeps for the same devices.
//
//   node tools/ha/router-devices.cjs                 every online device: name, router id, IP, Velop unit
//   node tools/ha/router-devices.cjs <regex>         only devices whose name, id, IP or unit match, e.g.  android|unknown
//
// Android phones use a random Wi-Fi address per network, and a phone that gets a new one shows up here as a new
// device with a new router id; the app's phone detector (PresenceService, *PhoneRouterId) then reads "away" until
// that id is updated (HOME-ASSISTANT.md, router note). Compare the ids here with the ones in PresenceService.cs.
const { rest, parseArgs, run } = require('./lib/ha.cjs');

run(async () => {
  const pattern = parseArgs()._[0];
  const re = pattern ? new RegExp(pattern, 'i') : null;

  const sensor = await rest('GET', '/states/sensor.mesh_online_devices');
  console.log(`sensor.mesh_online_devices = ${sensor.state} | last_updated ${sensor.last_updated}`);

  const devices = Array.isArray(sensor.attributes.devices) ? sensor.attributes.devices : [];
  const shown = devices.filter((d) => !re || re.test(JSON.stringify(d)));
  for (const d of shown) console.log(JSON.stringify(d));
  console.log(`${shown.length} of ${devices.length} device(s)`);

  const states = await rest('GET', '/states');
  const trackers = states
    .filter((s) => s.entity_id.startsWith('device_tracker.') && (!re || re.test(s.entity_id) || re.test(s.attributes.friendly_name || '')))
    .sort((a, b) => a.entity_id.localeCompare(b.entity_id));
  console.log('\ndevice trackers:');
  for (const s of trackers) {
    console.log(`${s.entity_id} | ${s.state} | "${s.attributes.friendly_name || ''}" | ${s.last_updated}`);
  }
});
