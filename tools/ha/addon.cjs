// Read or restart a Home Assistant add-on through the Supervisor.
//
//   node tools/ha/addon.cjs <slug>                       state and version
//   node tools/ha/addon.cjs <slug> log [regex] [--lines 80] [--from 100]   the log, optionally only matching lines
//   node tools/ha/addon.cjs <slug> restart               restart it, then wait until it reports started again
//   node tools/ha/addon.cjs <slug> stop | start          stop or start it, and wait for that (stop the NetDaemon app before
//                                                        copying a new build over it: DEPLOYMENT.md)
//
// Slugs: c6a2317c_netdaemon6 (the NetDaemon app), core_samba (the config share).
// State and restart go through the websocket's `supervisor/api` command, because the REST /api/hassio proxy
// returns 401 for them with a long-lived token (see DEPLOYMENT.md → "The Samba share, and restarting add-ons").
// The log is the other way round: REST `/api/hassio/addons/<slug>/logs` serves it as plain text, while the
// websocket command fails on it (it expects JSON). Without asking, the Supervisor returns only the last 100
// journal lines, a few hours of the NetDaemon app; `--from N` asks for the last N (a `Range: entries=:-N:` header
// plus `?lines=N`), and that spans add-on restarts. 40000 reached back about two days on 2026-10-21.
const { connect, restText, parseArgs, run } = require('./lib/ha.cjs');

run(async () => {
  const args = parseArgs();
  const [slug, action, pattern] = args._;
  if (!slug) throw new Error('usage: node tools/ha/addon.cjs <slug> [log [regex] [--lines 80] [--from 40000] | restart | stop | start]');

  if (action === 'log') {
    const from = Number(args.from ?? 100);
    const text = await restText('GET', `/hassio/addons/${slug}/logs?lines=${from}`, undefined, { Range: `entries=:-${from}:`, Accept: 'text/plain' });
    const filter = pattern ? new RegExp(pattern, 'i') : null;
    const lines = text.split('\n').map((l) => l.replace(/\x1b\[[0-9;]*m/g, '')).filter((l) => !filter || filter.test(l));
    for (const line of lines.slice(-Number(args.lines ?? 80))) console.log(line);
    return;
  }

  const ha = await connect();
  const info = () => ha.call({ type: 'supervisor/api', endpoint: `/addons/${slug}/info`, method: 'get' });
  const show = (i) => console.log(`${slug}: ${i.state}, version ${i.version}`);

  show(await info());
  // Each waits until the Supervisor reports the state it leads to.
  const actions = { restart: 'started', start: 'started', stop: 'stopped' };
  if (actions[action]) {
    console.log(`${action}...`);
    await ha.call({ type: 'supervisor/api', endpoint: `/addons/${slug}/${action}`, method: 'post', timeout: null });
    for (let i = 0; i < 30; i++) {
      const now = await info();
      if (now.state === actions[action]) { show(now); break; }
      await new Promise((r) => setTimeout(r, 2000));
    }
  } else if (action) {
    throw new Error(`unknown action: ${action}`);
  }
  ha.close();
});
