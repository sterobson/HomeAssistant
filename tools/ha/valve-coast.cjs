// Read-only. Each time the app shut a radiator valve (its setpoint moved from at or above the TRV's own reading to
// below it), how far the room thermometer climbed afterwards, up to the valve's next opening (3 h at most): the room's
// coast. "radiator" is the TRV's own reading minus the room's at the shut (how hot the radiator still was).
//
//   node tools/ha/valve-coast.cjs [--hours 240] [--only bedroom]
//
// Recorder retention is ~10 days (HOME-ASSISTANT.md gotcha 2). A room that overshoots by more than this shows was
// warmed while its valve was open, not by coasting (gotcha 30).
const { rest, parseArgs, run } = require('./lib/ha.cjs');

const VALVES = [
  ['climate.bedroom_1_radiator_thermostat', 'sensor.bedroom_1_climate_temperature', 'Bedroom 1'],
  ['climate.bedroom_2_radiator_thermostat', 'sensor.bedroom_2_climate_temperature', 'Bedroom 2'],
  ['climate.bedroom_3_radiator_thermostat', 'sensor.bedroom_3_climate_temperature', 'Bedroom 3'],
  ['climate.dining_room_radiator_thermostat', 'sensor.diningroom_temperature', 'Dining room'],
  ['climate.living_room_radiator_thermostat', 'sensor.livingroom_climate_temperature', 'Living room'],
  ['climate.den_radiator_1_thermostat', 'sensor.den_climate_temperature', 'Den radiator 1'],
  ['climate.den_radiator_2_thermostat', 'sensor.den_climate_temperature', 'Den radiator 2'],
];
const ms = (iso) => new Date(iso).getTime();
const local = (t) => new Date(t).toLocaleString('en-GB', { timeZone: 'Europe/London', day: '2-digit', month: 'short', hour: '2-digit', minute: '2-digit' });
const valueAt = (series, t) => { let v = null; for (const [time, value] of series) { if (time > t) break; v = value; } return v; };

run(async () => {
  const args = parseArgs();
  const hours = Number(args.hours || 240);
  const end = new Date();
  const start = new Date(end.getTime() - hours * 3600 * 1000);
  const q = (ids, attrs) => `/history/period/${encodeURIComponent(start.toISOString())}?filter_entity_id=${ids.join(',')}&end_time=${encodeURIComponent(end.toISOString())}${attrs ? '' : '&minimal_response&no_attributes'}`;
  const summary = [];
  for (const [valve, thermo, label] of VALVES) {
    if (args.only && !new RegExp(args.only, 'i').test(label)) continue;
    const [vrows] = await rest('GET', q([valve], true));
    const [trows] = await rest('GET', q([thermo], false));
    const room = trows.filter((r) => !isNaN(parseFloat(r.state))).map((r) => [ms(r.last_changed || r.last_updated), parseFloat(r.state)]);
    const pts = vrows.filter((r) => r.attributes?.current_temperature != null && r.attributes?.temperature != null)
      .map((r) => ({ t: ms(r.last_updated || r.last_changed), own: r.attributes.current_temperature, sp: r.attributes.temperature }));
    // An app "open" puts the setpoint 5 above the reading; "shut" 5 below. Use setpoint vs own reading.
    const opens = [], shuts = [];
    let wasOpen = null, openedAt = null;
    for (const p of pts) {
      const open = p.sp >= p.own;
      if (wasOpen === false && open) { opens.push(p.t); openedAt = p.t; }
      if (wasOpen === true && !open) shuts.push({ t: p.t, own: p.own, openedAt });
      wasOpen = open;
    }
    console.log(`\n=== ${label} ===`);
    console.log('valve shut          open for  radiator  room@shut  room max  +rise  mins to max');
    const rises = [];
    for (const s of shuts) {
      const nextOpen = opens.find((t) => t > s.t) ?? end.getTime();
      const windowEnd = Math.min(nextOpen, s.t + 3 * 3600 * 1000);
      const atShut = valueAt(room, s.t);
      if (atShut == null) continue;
      let max = atShut, maxT = s.t;
      for (const [t, v] of room) if (t > s.t && t <= windowEnd && v > max) { max = v; maxT = t; }
      const openMins = s.openedAt ? Math.round((s.t - s.openedAt) / 60000) : null;
      const hot = s.own - atShut;
      rises.push({ rise: max - atShut, hot });
      console.log(`${local(s.t).padEnd(19)} ${String(openMins ?? '?').padStart(7)}m  ${('+' + hot.toFixed(1)).padStart(7)}   ${atShut.toFixed(1).padStart(8)}  ${max.toFixed(1).padStart(8)}  ${('+' + (max - atShut).toFixed(1)).padStart(5)}  ${String(Math.round((maxT - s.t) / 60000)).padStart(11)}`);
    }
    const hotOnes = rises.filter((r) => r.hot >= 2).map((r) => r.rise).sort((a, b) => a - b);
    if (hotOnes.length) {
      const line = `${label}: shut with radiator >= 2 deg above room: n=${hotOnes.length}, mean rise +${(hotOnes.reduce((a, b) => a + b, 0) / hotOnes.length).toFixed(2)}, median +${hotOnes[Math.floor(hotOnes.length / 2)].toFixed(1)}, max +${hotOnes[hotOnes.length - 1].toFixed(1)}`;
      summary.push(line);
    }
  }
  console.log('\n' + summary.join('\n'));
});
