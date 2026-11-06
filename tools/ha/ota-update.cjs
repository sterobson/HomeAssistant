// Follow, and optionally start, a ZHA firmware update, and ask the device itself what it runs.
//
//   node tools/ha/ota-update.cjs <update entity_id>                 read-only: versions, progress, and what the device says
//   node tools/ha/ota-update.cjs <update entity_id> --watch         follow an update already running, until it ends
//   node tools/ha/ota-update.cjs <update entity_id> --install        WRITES: start the update, then follow it
//
// Things this exists for (HOME-ASSISTANT.md gotcha 34):
//  - the install service blocks until the update ends, and Node's fetch drops that call after about 5 minutes with
//    "fetch failed" while HA carries on, so the call is fired without waiting and watched through the entity;
//  - a battery device (a button, a TRV) stops part-way again and again, and each retry resumes where the last stopped, so
//    "STOPPED" is not the end: run --install again, with the device awake (press its button every 10 to 15 seconds);
//  - when it finishes the update entity goes unavailable for a few seconds and comes back still showing the OLD version
//    for a minute or more, so the end is judged by asking the device (OTA cluster 0x0019, attribute 0x0002), not the entity.
// Reaches the device only while it is awake, so the last step retries for up to 90 s.
const { connect, rest, parseArgs, run } = require('./lib/ha.cjs');

const sleep = (ms) => new Promise((r) => setTimeout(r, ms));
const time = () => new Date().toLocaleTimeString();
const hex = (n) => `0x${Number(n).toString(16).padStart(8, '0')}`;

const describe = (s) => {
  const a = s.attributes;
  return `${time()} state=${s.state} installed=${a.installed_version} latest=${a.latest_version} in_progress=${JSON.stringify(a.in_progress)} percentage=${a.update_percentage ?? ''}`;
};

// The device's own report of the firmware it runs. Needs the device awake.
async function askDevice(ha, entityId) {
  const entities = await ha.call({ type: 'config/entity_registry/list' });
  const entity = entities.find((e) => e.entity_id === entityId);
  if (!entity || !entity.device_id) return null;
  const devices = await ha.call({ type: 'config/device_registry/list' });
  const device = devices.find((d) => d.id === entity.device_id);
  const ieee = ((device && device.identifiers) || []).find((i) => i[0] === 'zha');
  if (!ieee) return null;
  const deadline = Date.now() + 90000;
  while (Date.now() < deadline) {
    try {
      const value = await ha.call({
        type: 'zha/devices/clusters/attributes/value',
        ieee: ieee[1], endpoint_id: 1, cluster_id: 25, cluster_type: 'out', attribute: 2,
      });
      return { ieee: ieee[1], version: Number(value), registry: device.sw_version };
    } catch { await sleep(6000); }
  }
  return { ieee: ieee[1], version: null, registry: device.sw_version };
}

run(async () => {
  const args = parseArgs(['watch', 'install']);
  const entityId = args._[0];
  if (!entityId || !entityId.startsWith('update.')) throw new Error('usage: ota-update.cjs <update.entity_id> [--watch | --install]');

  const ha = await connect();
  let state = await rest('GET', `/states/${entityId}`);
  console.log(describe(state));
  const startVersion = state.attributes.installed_version;

  if (!args.watch && !args.install) {
    const device = await askDevice(ha, entityId);
    if (device) console.log(`${device.ieee}: device says ${device.version === null ? 'nothing (asleep?)' : hex(device.version)}; registry says ${device.registry}`);
    ha.close();
    return;
  }

  if (args.install) {
    rest('POST', '/services/update/install', { entity_id: entityId })
      .then(() => console.log(`${time()} install call returned OK`))
      .catch((e) => console.log(`${time()} install call ended: ${e.message || e}`));
    console.log('install requested');
  }

  const deadline = Date.now() + 150 * 60 * 1000;
  let seenRunning = false;
  let last = '';
  while (Date.now() < deadline) {
    await sleep(20000);
    try { state = await rest('GET', `/states/${entityId}`); } catch (e) { console.log('state read failed:', e.message || e); continue; }
    const a = state.attributes;
    if (state.state === 'unavailable') { console.log(`${time()} entity unavailable`); continue; }
    const running = a.in_progress !== false && a.in_progress !== undefined && a.in_progress !== null;
    if (running) seenRunning = true;
    const key = `${state.state}|${a.in_progress}|${Math.floor(a.update_percentage ?? -1)}|${a.installed_version}`;
    if (key !== last) { console.log(describe(state)); last = key; }
    if (a.installed_version !== startVersion && !running) { console.log(`FINISHED: the entity now shows ${a.installed_version}`); break; }
    if (seenRunning && !running) {
      console.log(`${time()} no longer in progress; entity still shows ${a.installed_version}. Asking the device (press its button)...`);
      const device = await askDevice(ha, entityId);
      if (device && device.version !== null && hex(device.version) !== startVersion) console.log(`FINISHED: the device runs ${hex(device.version)} (registry still says ${device.registry})`);
      else console.log(`STOPPED: the device ${device && device.version !== null ? `still runs ${hex(device.version)}` : 'did not answer'}. Run --install again to resume.`);
      break;
    }
  }
  ha.close();
});
