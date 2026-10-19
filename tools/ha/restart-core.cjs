// Restart Home Assistant Core, safely.
//
//   node tools/ha/restart-core.cjs
//
// Checks the configuration first and refuses to restart if it is invalid (an invalid config can stop
// HA coming back). Then restarts and waits until the REST API answers again and reports RUNNING.
// Devices keep their state; automations pause and the NetDaemon add-on reconnects by itself.
// Remember gotcha 3: every entity's last_changed is rewritten by the restart.
const { rest, run } = require('./lib/ha.cjs');

const sleep = (ms) => new Promise((r) => setTimeout(r, ms));

run(async () => {
  const check = await rest('POST', '/config/core/check_config');
  if (check.result !== 'valid') throw new Error(`config check: ${check.result}\n${check.errors || ''}`);
  console.log('config check: valid');

  console.log('restarting...');
  try { await rest('POST', '/services/homeassistant/restart', {}); } catch { /* the connection drops */ }

  await sleep(15000);
  for (let i = 0; i < 60; i++) {
    try {
      const config = await rest('GET', '/config');
      if (config.state === 'RUNNING') { console.log(`back: ${config.state}, version ${config.version}`); return; }
      console.log(`starting: ${config.state}`);
    } catch { /* not up yet */ }
    await sleep(5000);
  }
  throw new Error('Home Assistant did not report RUNNING within 5 minutes');
});
