// Read or restart a Home Assistant add-on through the Supervisor, over the websocket.
//
//   node tools/ha/addon.cjs <slug>            state and version
//   node tools/ha/addon.cjs <slug> restart    restart it, then wait until it reports started again
//
// Slugs: c6a2317c_netdaemon6 (the NetDaemon app), core_samba (the config share).
// The REST /api/hassio proxy returns 401 for a long-lived token, so this goes through the websocket's
// `supervisor/api` command instead (see DEPLOYMENT.md → "The Samba share, and restarting add-ons").
const { connect, parseArgs, run } = require('./lib/ha.cjs');

run(async () => {
  const args = parseArgs();
  const [slug, action] = args._;
  if (!slug) throw new Error('usage: node tools/ha/addon.cjs <slug> [restart]');

  const ha = await connect();
  const info = () => ha.call({ type: 'supervisor/api', endpoint: `/addons/${slug}/info`, method: 'get' });
  const show = (i) => console.log(`${slug}: ${i.state}, version ${i.version}`);

  show(await info());
  if (action === 'restart') {
    console.log('restarting...');
    await ha.call({ type: 'supervisor/api', endpoint: `/addons/${slug}/restart`, method: 'post', timeout: null });
    for (let i = 0; i < 30; i++) {
      const now = await info();
      if (now.state === 'started') { show(now); break; }
      await new Promise((r) => setTimeout(r, 2000));
    }
  } else if (action) {
    throw new Error(`unknown action: ${action}`);
  }
  ha.close();
});
