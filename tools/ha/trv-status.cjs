// Read-only. Is each radiator TRV heating right now? Mode, action, setpoint and the TRV's own reading.
//
//   node tools/ha/trv-status.cjs
//
// hvac_action is the answer to "is it on": `heating` means the valve is open, `idle` means it is shut
// because the TRV's own sensor is at or above its setpoint. The mode is `heat` for all of them
// whatever they are doing, so it says nothing. The reading is the TRV's onboard sensor, not the room
// thermometer the schedules use (CLAUDE.md, "Two temperature sources per room"). Even a heating TRV
// does not mean the boiler is firing: in boiler-thermostat mode the app never drives it.
const { rest, run } = require('./lib/ha.cjs');

run(async () => {
  const states = await rest('GET', '/states');
  const trvs = states
    .filter((s) => /^climate\..*radiator.*thermostat$/.test(s.entity_id))
    .sort((a, b) => a.entity_id.localeCompare(b.entity_id));
  for (const s of trvs) {
    const a = s.attributes;
    console.log(
      `${s.entity_id} | mode=${s.state} | action=${a.hvac_action} | target=${a.temperature} | current=${a.current_temperature}`,
    );
  }
  const heating = trvs.filter((s) => s.attributes.hvac_action === 'heating').length;
  console.log(`${heating} of ${trvs.length} heating`);
});
