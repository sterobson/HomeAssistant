// Read-only. Estimates how fast each radiator valve and electric heater has warmed its room, from the recorder's
// history, the way the NetDaemon app now measures it live (Backend/App/Services/Climate/WarmUpRates.cs): the valve open
// with the boiler on, 30+ minutes, or the heater plug on, 5+ minutes, taking the room from where it started to where it
// got to.
//
//   node tools/ha/warmup-history.cjs [--hours 240] [--min 30] [--heater-min 5] [--only valves|heaters] [--runs]
//                                    [--seed Backend/App/warm-up-seed.json]
//
// --seed writes each device's last 5 usable episodes in the app's own sample format, merged into the file: devices this
// run measured are replaced, every other entry is kept (so --only heaters leaves the radiators' estimates alone). The app
// ships that file and merges it with what it has measured itself (WarmUpRates), so a device starts with a rate instead
// of its fallback.
//
// Heater plugs (since 2026-10-21) are simpler: a plug heats the moment it is on, with no boiler to wait for, so an
// episode is the plug switching on until it switches off, and the rate is the room thermometer's rise over it. A plug
// the app switched off because its rules stopped passing (nobody at the desk, say) ends an episode before the room
// reached its target; that still measures how fast it was warming, so it is kept, unlike in the live measurement.
//
// This is an ESTIMATE, rougher than the live measurement. The history never recorded when the boiler was on (the app
// believes it only from 2026-10-19, and that belief is not in HA), and "valve open" alone is useless: a valve sits open
// for hours once its room is warm (31 hours seen). The valve's own temperature sensor sits against the radiator, so it
// shows what the boiler did: when the valve opens and hot water arrives it climbs about 0.5 degrees every few minutes,
// peaks, and falls once the boiler goes off. So an episode is read like this:
//   - it starts when the valve opens (hvac_action "heating" after idle; the app opens a valve by setting its setpoint to
//     reading + 5), and counts only if the valve's own sensor then rises by at least --hot degrees (hot water arrived);
//   - if that rise only begins more than --late minutes after the valve opened, the boiler came on later, so the episode
//     starts at the rise instead (this is what happened before the app drove the boiler);
//   - it ends at the sensor's peak if the sensor then falls by 1 degree or more before the valve shuts (the boiler went off,
//     which the app does only when no room is still calling for heat, so this room had reached its target), or else when
//     the valve shuts (the room reached its target with the boiler still on).
// The valve's target is not recorded (its setpoint is just reading +/- 5), so "reached the target" cannot be checked. The
// rate is the rise in the room thermometer over the episode, which can read a little fast (the lag before hot water
// arrives is dropped when a start is moved) or slow (the live measurement stops at the first reading at the target). Read
// it as a rough figure, good for the order of magnitude and for comparing radiators.
//
// Recorder retention is ~10 days (HOME-ASSISTANT.md gotcha 2). Writes nothing.
const { rest, parseArgs, run } = require('./lib/ha.cjs');

// Valve -> the room thermometer the heating app reads for it (NamedEntities.cs), and the zone it belongs to.
const VALVES = [
  ['climate.bedroom_1_radiator_thermostat', 'sensor.bedroom_1_climate_temperature', 'Bedroom 1'],
  ['climate.bedroom_2_radiator_thermostat', 'sensor.bedroom_2_climate_temperature', 'Bedroom 2'],
  ['climate.bedroom_3_radiator_thermostat', 'sensor.bedroom_3_climate_temperature', 'Bedroom 3'],
  ['climate.dining_room_radiator_thermostat', 'sensor.diningroom_temperature', 'Dining room'],
  ['climate.living_room_radiator_thermostat', 'sensor.livingroom_climate_temperature', 'Living room'],
  ['climate.den_radiator_1_thermostat', 'sensor.den_climate_temperature', 'Den radiator 1'],
  ['climate.den_radiator_2_thermostat', 'sensor.den_climate_temperature', 'Den radiator 2'],
];
// Heater plug -> the room thermometer its zone reads, and a label. Fan heaters (NamedEntities.cs, room states' devices).
const HEATERS = [
  ['switch.kitchen_plug_heater_switch', 'sensor.kitchen_temperature_and_humidity_temperature', 'Kitchen heater'],
  ['switch.bedroom_1_plug_heater_switch', 'sensor.bedroom_1_climate_temperature', 'Bedroom 1 heater'],
  ['switch.games_room_plug_heater_switch', 'sensor.sonoff_snzb_02d_temperature', "Ste's desk heater"],
  ['switch.dining_room_plug_heater_switch', 'sensor.diningroom_temperature', "Ruth's desk heater"],
];
const SAMPLES_KEPT = 5; // the app averages the last five

const ms = (iso) => new Date(iso).getTime();
const local = (t) => new Date(t).toLocaleString('en-GB', { day: '2-digit', month: 'short', hour: '2-digit', minute: '2-digit' });
const avg = (xs) => xs.reduce((a, b) => a + b, 0) / xs.length;

// The value of a step series (sorted [time, value]) at a time: the last reading at or before it.
const valueAt = (series, t) => {
  let found = null;
  for (const [time, value] of series) {
    if (time > t) break;
    found = value;
  }
  return found;
};

// Each time the valve opens (idle -> heating), the episode that followed; see the header for how it is read.
function episodes(rows, { hot, late }) {
  const points = rows
    .filter((r) => r.state !== 'unavailable' && r.attributes?.current_temperature != null)
    .map((r) => ({
      t: ms(r.last_updated),
      open: r.attributes.hvac_action === 'heating',
      trv: Number(r.attributes.current_temperature),
    }));
  const found = [];
  for (let i = 1; i < points.length; i++) {
    if (!(points[i].open && !points[i - 1].open)) continue;
    const opened = points[i].t;
    let closeIndex = points.length; // the valve shutting; a run still open at the end of the window is left out
    for (let j = i + 1; j < points.length; j++) if (!points[j].open) { closeIndex = j; break; }
    if (closeIndex === points.length) continue;
    const span = points.slice(i, closeIndex + 1);
    const base = span[0].trv;

    // Peak of the valve's own sensor while it was open, and the first time it had risen a little (hot water arriving).
    let peak = span[0];
    for (const p of span) if (p.trv > peak.trv) peak = p;
    const onset = span.find((p) => p.trv >= base + 0.5);
    if (!onset || peak.trv - base < hot) { found.push({ opened, skipped: 'radiator not shown hot' }); continue; }

    const begin = onset.t - opened > late * 60000 ? onset.t : opened;
    const closed = points[closeIndex].t;
    const after = span.filter((p) => p.t > peak.t);
    const fell = after.length > 0 && peak.trv - Math.min(...after.map((p) => p.trv)) >= 1;
    found.push({ opened, begin, end: fell ? peak.t : closed, boilerWentOff: fell, closed });
  }
  return found;
}

// Each time the heater plug switched on, until it switched off. A run still on at the end of the window is left out.
function heaterEpisodes(rows) {
  const points = rows
    .filter((r) => r.state === 'on' || r.state === 'off')
    .map((r) => ({ t: ms(r.last_changed ?? r.last_updated), on: r.state === 'on' }));
  const found = [];
  for (let i = 0; i < points.length; i++) {
    if (!points[i].on || (i > 0 && points[i - 1].on)) continue;
    const off = points.slice(i + 1).find((p) => !p.on);
    if (off) found.push({ opened: points[i].t, begin: points[i].t, end: off.t, closed: off.t });
  }
  return found;
}

// The usable episodes as the app's WarmUpSample records (PascalCase), so the file can be read straight back as seed data.
const toSamples = (runs) => runs.map((r) => ({
  DegreesPerHour: Math.round(r.rate * 1000) / 1000,
  StartedAt: new Date(r.begin).toISOString(),
  Minutes: Math.round(r.minutes),
  StartTemperature: r.roomStart,
  ReachedTemperature: r.roomEnd,
}));

run(async () => {
  const args = parseArgs(['runs']);
  const hours = Number(args.hours || 240);
  const minMinutes = Number(args.min || 30);
  const hot = Number(args.hot || 1.5);      // degrees the valve's own sensor must rise for hot water to have arrived
  const late = Number(args.late || 20);     // minutes after the valve opens beyond which the boiler is taken to have come on later
  const end = new Date();
  const start = new Date(end.getTime() - hours * 3600 * 1000);
  const history = (ids, extra = '') =>
    rest('GET', `/history/period/${encodeURIComponent(start.toISOString())}?filter_entity_id=${ids}&end_time=${encodeURIComponent(end.toISOString())}${extra}`);

  const seeds = {};
  const only = args.only && args.only !== true ? String(args.only) : null;
  const heaterMinutes = Number(args['heater-min'] || 5);
  console.log(`Estimated from the last ${hours} h of recorder history (UTC window ends ${end.toISOString().slice(0, 16)}).`);
  if (only !== 'heaters') console.log(`A valve episode: the valve opens and its own sensor rises ${hot}°C+ (hot water arrived); it ends when the sensor peaks and falls (boiler off) or the valve shuts.\n`);

  for (const [valveId, thermometerId, label] of only === 'heaters' ? [] : VALVES) {
    const [valveRows = []] = await history(valveId);
    const [thermRows = []] = await history(thermometerId, '&minimal_response&no_attributes');
    const room = thermRows.filter((r) => r.state !== 'unavailable' && r.state !== 'unknown' && !Number.isNaN(Number(r.state)))
      .map((r) => [ms(r.last_changed ?? r.last_updated), Number(r.state)]);
    const valveTemp = valveRows.filter((r) => r.attributes?.current_temperature != null)
      .map((r) => [ms(r.last_updated), Number(r.attributes.current_temperature)]);

    const all = episodes(valveRows, { hot, late });
    const runs = all.filter((e) => !e.skipped).map((e) => {
      const minutes = (e.end - e.begin) / 60000;
      const roomStart = valueAt(room, e.begin);
      const roomEnd = valueAt(room, e.end);
      const rise = roomStart != null && roomEnd != null ? roomEnd - roomStart : null;
      return { ...e, minutes, roomStart, roomEnd, rise, rate: rise != null && rise > 0 ? rise / (minutes / 60) : null, hot: true };
    });

    const counted = runs.filter((r) => r.minutes >= minMinutes && r.rate != null);
    const lastFive = counted.slice(-SAMPLES_KEPT);
    // The shape the app's WarmUpSample record serialises to (PascalCase), so it can be read straight back as seed data.
    if (lastFive.length) {
      seeds[valveId] = toSamples(lastFive);
    }
    const long = runs.filter((r) => r.minutes >= minMinutes);
    console.log(`${label} (${valveId})`);
    console.log(`  ${all.length} valve openings: ${all.length - runs.length} with no hot water, ${runs.length} warm-ups, ${long.length} of ${minMinutes}+ min, ${counted.length} usable`);
    if (lastFive.length) {
      console.log(`  estimate: ${avg(lastFive.map((r) => r.rate)).toFixed(2)} °C an hour (average of the last ${lastFive.length}; range ${Math.min(...lastFive.map((r) => r.rate)).toFixed(2)}–${Math.max(...lastFive.map((r) => r.rate)).toFixed(2)})`);
    } else {
      console.log('  estimate: none (no episode qualifies)');
    }
    if (args.runs) {
      for (const r of runs) {
        const how = r.boilerWentOff ? 'boiler went off' : 'valve shut';
        const moved = r.begin !== r.opened ? ' (boiler later)' : '';
        console.log(`    ${local(r.begin)}  ${r.minutes.toFixed(0).padStart(4)} min  ${r.roomStart ?? '?'}→${r.roomEnd ?? '?'}°C  ${r.rate == null ? '   —   ' : (r.rate.toFixed(2) + ' °C/h')}  ${how}${moved}${r.minutes < minMinutes ? '  [under ' + minMinutes + ' min: not counted]' : ''}`);
      }
    }
    console.log('');
  }

  if (only !== 'valves') console.log(`A heater episode: the plug on until it switches off, ${heaterMinutes}+ minutes, with the room warmer at the end.\n`);
  for (const [plugId, thermometerId, label] of only === 'valves' ? [] : HEATERS) {
    const [plugRows = []] = await history(plugId, '&minimal_response&no_attributes');
    const [thermRows = []] = await history(thermometerId, '&minimal_response&no_attributes');
    const room = thermRows.filter((r) => r.state !== 'unavailable' && r.state !== 'unknown' && !Number.isNaN(Number(r.state)))
      .map((r) => [ms(r.last_changed ?? r.last_updated), Number(r.state)]);

    const runs = heaterEpisodes(plugRows).map((e) => {
      const minutes = (e.end - e.begin) / 60000;
      const roomStart = valueAt(room, e.begin);
      const roomEnd = valueAt(room, e.end);
      const rise = roomStart != null && roomEnd != null ? roomEnd - roomStart : null;
      return { ...e, minutes, roomStart, roomEnd, rise, rate: rise != null && rise > 0 ? rise / (minutes / 60) : null };
    });
    const counted = runs.filter((r) => r.minutes >= heaterMinutes && r.rate != null);
    const lastFive = counted.slice(-SAMPLES_KEPT);
    if (lastFive.length) seeds[plugId] = toSamples(lastFive);

    console.log(`${label} (${plugId})`);
    console.log(`  ${runs.length} times on, ${runs.filter((r) => r.minutes >= heaterMinutes).length} of ${heaterMinutes}+ min, ${counted.length} usable (room warmer at the end)`);
    console.log(lastFive.length
      ? `  estimate: ${avg(lastFive.map((r) => r.rate)).toFixed(2)} °C an hour (average of the last ${lastFive.length}; range ${Math.min(...lastFive.map((r) => r.rate)).toFixed(2)}–${Math.max(...lastFive.map((r) => r.rate)).toFixed(2)})`
      : '  estimate: none (no episode qualifies)');
    if (args.runs) {
      for (const r of runs) {
        console.log(`    ${local(r.begin)}  ${r.minutes.toFixed(0).padStart(4)} min  ${r.roomStart ?? '?'}→${r.roomEnd ?? '?'}°C  ${r.rate == null ? '   —   ' : (r.rate.toFixed(2) + ' °C/h')}${r.minutes < heaterMinutes ? '  [under ' + heaterMinutes + ' min: not counted]' : ''}`);
      }
    }
    console.log('');
  }

  if (args.seed && args.seed !== true) {
    const fs = require('fs');
    const existing = fs.existsSync(args.seed) ? JSON.parse(fs.readFileSync(args.seed, 'utf8')) : {};
    const merged = { ...existing, ...seeds };
    fs.writeFileSync(args.seed, JSON.stringify(merged, null, 2) + '\n');
    console.log(`Wrote ${Object.values(seeds).reduce((n, s) => n + s.length, 0)} samples for ${Object.keys(seeds).length} devices to ${args.seed}, keeping ${Object.keys(existing).filter((k) => !(k in seeds)).length} other entries`);
  }
});
