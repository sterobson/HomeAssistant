// Run the heating's monthly boiler and radiator valve exercise now, instead of waiting for 08:00 on the 1st.
//
//   node tools/ha/valve-exercise.cjs
//
// WRITES (through the NetDaemon app): it fires the Home Assistant event the app listens for
// (HeatingControlService.ValveExerciseEvent). If the boiler is believed off, the app presses it on, flips every
// radiator valve to the opposite of what it is, puts each back after 2½ minutes, and hands the boiler back to the
// normal rules after 5. If the boiler is already on it skips, exactly as the monthly run does. HA returns 200
// whether or not the app is listening: read the add-on log or the TRVs back (trv-status.cjs) to see it happen.
const { rest, run } = require('./lib/ha.cjs');

const EVENT = 'home_heating_valve_exercise';

run(async () => {
  const result = await rest('POST', `/events/${EVENT}`, {});
  console.log(`fired ${EVENT}: ${result?.message ?? 'ok'}`);
});
