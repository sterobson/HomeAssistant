// Press the boiler once without the NetDaemon app counting it, to bring the boiler back in line with the app's belief.
//
//   node tools/ha/realign-boiler.cjs            dry run: the add-on's state and the fingerbot's
//   node tools/ha/realign-boiler.cjs --apply    stop the add-on, press the fingerbot once, start the add-on
//
// WRITES: stops and starts the NetDaemon add-on, and presses the boiler's button (one fingerbot toggle, which turns
// the boiler on if it was off and off if it was on). Use it only when you know the boiler and the app disagree (the
// radiators say the boiler is running while the heating page says off, or the reverse; HOME-ASSISTANT.md gotchas 23
// and 24). While the app runs, any fingerbot movement it did not command counts as an outside press and flips its
// belief along with the boiler, so they stay inverted. With the add-on stopped nothing watches the fingerbot, and the
// app reads it fresh at startup without counting the change. Its saved belief (in /data, kept across the restart) is
// then right again. The add-on is started again even if the press fails.
const { connect, parseArgs, run } = require('./lib/ha.cjs');

const SLUG = 'c6a2317c_netdaemon6';
const FINGERBOT = 'switch.tz3210_j4pdtz9v_ts0001';

const sleep = (ms) => new Promise((r) => setTimeout(r, ms));

run(async () => {
  const args = parseArgs(['apply']);
  const ha = await connect();
  const addonState = async () => (await ha.call({ type: 'supervisor/api', endpoint: `/addons/${SLUG}/info`, method: 'get' })).state;
  const fingerbot = async () => (await ha.call({ type: 'get_states' })).find((s) => s.entity_id === FINGERBOT)?.state;
  const waitFor = async (read, wanted, seconds) => {
    for (let i = 0; i < seconds; i++) {
      const value = await read();
      if (value === wanted) return value;
      await sleep(1000);
    }
    return read();
  };

  const before = await fingerbot();
  console.log(`add-on ${SLUG}: ${await addonState()}`);
  console.log(`fingerbot ${FINGERBOT}: ${before}`);
  if (!args.apply) {
    console.log('dry run: pass --apply to stop the add-on, press the boiler once and start it again');
    ha.close();
    return;
  }
  if (before !== 'on' && before !== 'off') throw new Error(`the fingerbot is ${before}, so it cannot be pressed`);

  console.log('stopping the add-on...');
  await ha.call({ type: 'supervisor/api', endpoint: `/addons/${SLUG}/stop`, method: 'post', timeout: null });
  console.log(`add-on: ${await waitFor(addonState, 'stopped', 30)}`);

  try {
    const wanted = before === 'on' ? 'off' : 'on';
    console.log(`pressing: switch.turn_${wanted}`);
    try {
      await ha.call({ type: 'call_service', domain: 'switch', service: `turn_${wanted}`, target: { entity_id: FINGERBOT } });
    } catch (e) {
      console.log(`Home Assistant answered with an error: ${e.message}`);
    }
    const after = await waitFor(fingerbot, wanted, 15);
    console.log(after === wanted ? `fingerbot moved: ${before} -> ${after} (one press)` : `FINGERBOT DID NOT MOVE: still ${after}; the boiler was not pressed`);
  } finally {
    console.log('starting the add-on...');
    await ha.call({ type: 'supervisor/api', endpoint: `/addons/${SLUG}/start`, method: 'post', timeout: null });
    console.log(`add-on: ${await waitFor(addonState, 'started', 60)}`);
    ha.close();
  }
});
