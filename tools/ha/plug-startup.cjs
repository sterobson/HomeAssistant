// WRITES to Home Assistant with --set. A smart plug's start-up behaviour: what the plug does when its
// power comes back after a cut (On, Off, Toggle, or its previous state). Changing it moves nothing now;
// it only decides what the plug does after the next power cut.
//
//   Show the current setting and the options (writes nothing):
//     node tools/ha/plug-startup.cjs select.den_plug_sideboard_lamp_power_on_behaviour
//
//   Change it, then read it back (gotcha 5: a 200 does not mean the plug took it):
//     node tools/ha/plug-startup.cjs select.den_plug_sideboard_lamp_power_on_behaviour --set On
//
// The entity is the plug's `select.*_start_up_behaviour` or `select.*_power_on_behaviour` (ZHA has
// named it both ways on S60ZBTPGs); list-devices.cjs --name "<device>" shows which.
const { rest, parseArgs, run } = require('./lib/ha.cjs');

const sleep = (ms) => new Promise((r) => setTimeout(r, ms));

run(async () => {
  const args = parseArgs();
  const entityId = args._[0];
  if (!entityId || !entityId.startsWith('select.')) throw new Error('Usage: plug-startup.cjs <select entity_id> [--set <option>]');

  const state = await rest('GET', `/states/${entityId}`);
  const options = state.attributes.options || [];
  console.log(`${entityId}: ${state.state}   (options: ${options.join(', ')})`);
  if (args.set === undefined) return;

  const option = options.find((o) => o.toLowerCase() === String(args.set).toLowerCase());
  if (!option) throw new Error(`"${args.set}" is not one of: ${options.join(', ')}`);
  if (state.state === option) { console.log('Already set. Nothing sent.'); return; }

  await rest('POST', '/services/select/select_option', { entity_id: entityId, option });
  for (let i = 0; i < 10; i++) {
    await sleep(1000);
    const now = await rest('GET', `/states/${entityId}`);
    if (now.state === option) { console.log(`Now ${now.state}.`); return; }
  }
  throw new Error(`Sent "${option}", but it still reads "${(await rest('GET', `/states/${entityId}`)).state}" after 10 s`);
});
