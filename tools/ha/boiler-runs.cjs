// Read-only. Looks at the boiler relay's runs (switch.den_boiler_relay, the app's real boiler switch since 2026-10-31)
// against the radiator valves and the room thermometers, to answer two design questions:
//
//   A. COASTING: when the relay goes off, how much further does each room climb? (--coast)
//   B. BATCHING: how often does a run start for a room that was only a little under its target during the run before,
//      so that opening its valve then would have saved a run? (--batch)
//   Both are shown by default; the run list is --runs.
//
//   node tools/ha/boiler-runs.cjs [--hours 240] [--runs] [--coast] [--batch] [--window 60]
//
// What is estimated, and how roughly (read this before believing a number):
//   - A valve is "open" when its setpoint is at or above its own reading (the app opens one with reading + 5, shuts with
//     reading - 5), as valve-coast.cjs reads it. Valves are neither opened nor shut while the boiler is off, so a valve
//     open at a run's start is either left open from before (its room was still "wanting heat": hysteresis runs to 0.2
//     above the target) or was opened in that run's first minutes (a room that was calling).
//   - Targets are not in the recorder. For batching, a room's target around a run is guessed as its temperature when that
//     run ended (the boiler goes off once no room is calling, so a room that was calling has just reached it). A schedule
//     change between two runs makes that wrong, so deficits are shown in bands, not as exact answers.
//   - Coast is the room thermometer's rise after the relay goes off, up to --window minutes or the next run, whichever is
//     first, for rooms whose valve was open at that moment. Sun, cooking and doors add to it; rooms whose valve was shut
//     are shown as a baseline for that noise.
//
// Recorder retention is ~10 days (HOME-ASSISTANT.md gotcha 2). Writes nothing.
const { rest, parseArgs, run } = require('./lib/ha.cjs');

const RELAY = 'switch.den_boiler_relay';
// Room -> its thermometer and its radiator valves (NamedEntities.cs; same wiring as valve-coast.cjs).
const ROOMS = [
  ['Bedroom 1', 'sensor.bedroom_1_climate_temperature', ['climate.bedroom_1_radiator_thermostat']],
  ['Bedroom 2', 'sensor.bedroom_2_climate_temperature', ['climate.bedroom_2_radiator_thermostat']],
  ['Bedroom 3', 'sensor.bedroom_3_climate_temperature', ['climate.bedroom_3_radiator_thermostat']],
  ['Dining room', 'sensor.diningroom_temperature', ['climate.dining_room_radiator_thermostat']],
  ['Living room', 'sensor.livingroom_climate_temperature', ['climate.living_room_radiator_thermostat']],
  ['Den', 'sensor.den_climate_temperature', ['climate.den_radiator_1_thermostat', 'climate.den_radiator_2_thermostat']],
];
const MIN = 60 * 1000;
const ms = (iso) => new Date(iso).getTime();
const local = (t) => new Date(t).toLocaleString('en-GB', { timeZone: 'Europe/London', day: '2-digit', month: 'short', hour: '2-digit', minute: '2-digit' });
const valueAt = (series, t) => { let v = null; for (const [time, value] of series) { if (time > t) break; v = value; } return v; };
const pct = (xs, p) => { const s = [...xs].sort((a, b) => a - b); return s.length ? s[Math.min(s.length - 1, Math.floor(p * s.length))] : NaN; };
const f1 = (x) => (Number.isNaN(x) ? '  -  ' : (x >= 0 ? '+' : '') + x.toFixed(1));
const mins = (x) => Math.round(x / MIN);

run(async () => {
  const args = parseArgs(['runs', 'coast', 'batch']);
  const hours = Number(args.hours || 240);
  const windowMin = Number(args.window || 60);
  const showAll = !args.runs && !args.coast && !args.batch;
  const end = new Date();
  const start = new Date(end.getTime() - hours * 3600 * 1000);
  const q = (id, attrs) => `/history/period/${encodeURIComponent(start.toISOString())}?filter_entity_id=${id}&end_time=${encodeURIComponent(end.toISOString())}${attrs ? '' : '&minimal_response&no_attributes'}`;

  // The relay's complete runs.
  const [relayRows] = await rest('GET', q(RELAY, false));
  const edges = relayRows.filter((r) => r.state === 'on' || r.state === 'off').map((r) => ({ t: ms(r.last_changed || r.last_updated), on: r.state === 'on' }));
  const runs = [];
  let openedAt = null;
  for (const e of edges) {
    if (e.on && openedAt === null) openedAt = e.t;
    else if (!e.on && openedAt !== null) { runs.push({ on: openedAt, off: e.t }); openedAt = null; }
  }
  if (runs.length < 2) throw new Error(`only ${runs.length} complete relay run(s) in the last ${hours} h`);

  // Per room: its thermometer series, and whether any of its valves is open (a step series of 0/1).
  const rooms = [];
  for (const [label, thermo, valves] of ROOMS) {
    const [trows] = await rest('GET', q(thermo, false));
    const temp = trows.filter((r) => !isNaN(parseFloat(r.state))).map((r) => [ms(r.last_changed || r.last_updated), parseFloat(r.state)]);
    const valveSeries = [];
    for (const v of valves) {
      const [vrows] = await rest('GET', q(v, true));
      valveSeries.push(vrows
        .filter((r) => r.attributes?.current_temperature != null && r.attributes?.temperature != null)
        .map((r) => [ms(r.last_updated || r.last_changed), r.attributes.temperature >= r.attributes.current_temperature ? 1 : 0]));
    }
    rooms.push({ label, temp, valveSeries });
  }
  const isOpen = (room, t) => room.valveSeries.some((s) => valueAt(s, t) === 1);
  const openedBetween = (room, a, b) => room.valveSeries.some((s) => s.some(([t, v], i) => t > a && t <= b && v === 1 && (i === 0 || s[i - 1][1] === 0)));
  const openAnyTime = (room, a, b) => isOpen(room, a) || room.valveSeries.some((s) => s.some(([t, v]) => t > a && t <= b && v === 1));

  const first = new Date(runs[0].on).getTime();
  console.log(`${runs.length} complete boiler runs, ${local(first)} to ${local(runs[runs.length - 1].off)} (recorder history; ${hours} h asked for)`);

  // Annotate each run: who was open when it started, split into left open and newly opened.
  runs.forEach((r, i) => {
    r.gap = i > 0 ? r.on - runs[i - 1].off : null;
    r.leftOpen = rooms.filter((room) => isOpen(room, r.on - 1000)).map((x) => x.label);
    r.newlyOpened = rooms.filter((room) => !isOpen(room, r.on - 1000) && openedBetween(room, r.on - 1000, r.on + 3 * MIN)).map((x) => x.label);
  });

  // ---- Runs ----
  const durations = runs.map((r) => (r.off - r.on) / MIN);
  const gaps = runs.slice(1).map((r) => r.gap / MIN);
  // Counts per band: edgesList are the bands' lower bounds; the first band starts at zero.
  const bucket = (xs, edgesList) => edgesList.map((lo, i) => {
    const hi = edgesList[i + 1];
    return `${hi === undefined ? lo + '+' : lo + '-' + hi}: ${xs.filter((x) => x >= lo && (hi === undefined || x < hi)).length}`;
  });
  console.log(`\n=== RUNS ===`);
  console.log(`total on-time ${(durations.reduce((a, b) => a + b, 0) / 60).toFixed(1)} h; duration median ${Math.round(pct(durations, 0.5))} min`);
  console.log(`duration (min): ${bucket(durations, [0, 10, 20, 60, 120]).join('  ')}`);
  console.log(`gap since previous run (min): ${bucket(gaps, [0, 15, 30, 60, 120, 240]).join('  ')}`);
  // Runs that served one room only, by that room: the short cycles batching could not help.
  const short = runs.filter((r) => r.off - r.on < 25 * MIN);
  const shortOne = short.filter((r) => new Set([...r.leftOpen, ...r.newlyOpened]).size === 1);
  const byRoom = {};
  for (const r of shortOne) byRoom[[...r.leftOpen, ...r.newlyOpened][0]] = (byRoom[[...r.leftOpen, ...r.newlyOpened][0]] || 0) + 1;
  console.log(`runs under 25 min: ${short.length}; of those, with a single room's valve open: ${shortOne.length} ${JSON.stringify(byRoom)}`);
  const withNew = runs.filter((r) => r.newlyOpened.length > 0);
  console.log(`valves already open at start (left from before): ${(runs.filter((r) => r.leftOpen.length > 0).length)} of ${runs.length} runs; runs that opened at least one room's valve: ${withNew.length}; rooms newly opened per such run, median ${pct(withNew.map((r) => r.newlyOpened.length), 0.5)}`);
  if (args.runs || showAll) {
    console.log('\nrun start          mins   gap  newly opened                       already open at start');
    for (const r of runs) {
      console.log(`${local(r.on).padEnd(17)} ${String(mins(r.off - r.on)).padStart(5)} ${String(r.gap == null ? '' : mins(r.gap)).padStart(5)}  ${(r.newlyOpened.join(', ') || '-').padEnd(34)} ${r.leftOpen.join(', ') || '-'}`);
    }
  }

  // ---- A. Coast ----
  if (args.coast || showAll) {
    console.log(`\n=== A. COAST: room thermometer rise after the relay goes off (to ${windowMin} min or the next run) ===`);
    console.log('room          valve open at off: n  median  p90   max   mins to peak (median)  | valve shut at off: n  median  max (noise baseline)');
    const allOpen = [];
    for (const room of rooms) {
      const withValve = [], withoutValve = [];
      runs.forEach((r, i) => {
        const nextOn = runs[i + 1]?.on ?? end.getTime();
        const windowEnd = Math.min(nextOn, r.off + windowMin * MIN);
        if (windowEnd - r.off < 20 * MIN) return;
        const t0 = valueAt(room.temp, r.off);
        if (t0 == null) return;
        let max = t0, maxT = r.off;
        for (const [t, v] of room.temp) if (t > r.off && t <= windowEnd && v > max) { max = v; maxT = t; }
        const sample = { rise: max - t0, toPeak: (maxT - r.off) / MIN, runMins: (r.off - r.on) / MIN, at: r.off };
        (isOpen(room, r.off) ? withValve : withoutValve).push(sample);
      });
      allOpen.push(...withValve.map((s) => ({ ...s, room: room.label })));
      const rs = withValve.map((s) => s.rise);
      const ws = withoutValve.map((s) => s.rise);
      console.log(`${room.label.padEnd(13)} ${String(withValve.length).padStart(22)}  ${f1(pct(rs, 0.5)).padStart(6)} ${f1(pct(rs, 0.9)).padStart(5)} ${f1(Math.max(...rs, -99)).padStart(5)}   ${String(Math.round(pct(withValve.map((s) => s.toPeak), 0.5))).padStart(10)}              | ${String(withoutValve.length).padStart(22)}  ${f1(pct(ws, 0.5)).padStart(6)} ${f1(Math.max(...ws, -99)).padStart(5)}`);
    }
    // Does coast grow with the length of the run? (The radiator is hotter after a long run.)
    const short = allOpen.filter((s) => s.runMins < 30).map((s) => s.rise);
    const mid = allOpen.filter((s) => s.runMins >= 30 && s.runMins < 90).map((s) => s.rise);
    const long = allOpen.filter((s) => s.runMins >= 90).map((s) => s.rise);
    console.log(`\nrise by length of the run before (rooms with the valve open): <30 min n=${short.length} median ${f1(pct(short, 0.5))} p90 ${f1(pct(short, 0.9))};  30-90 min n=${mid.length} median ${f1(pct(mid, 0.5))} p90 ${f1(pct(mid, 0.9))};  90+ min n=${long.length} median ${f1(pct(long, 0.5))} p90 ${f1(pct(long, 0.9))}`);
    const big = allOpen.filter((s) => s.rise >= 0.4).sort((a, b) => b.rise - a.rise).slice(0, 8);
    if (big.length) {
      console.log('largest coasts (>= +0.4):');
      for (const s of big) console.log(`  ${local(s.at)}  ${s.room.padEnd(12)} ${f1(s.rise)} after a ${Math.round(s.runMins)} min run, peak ${Math.round(s.toPeak)} min later`);
    }
  }

  // ---- B. Batching ----
  if (args.batch || showAll) {
    console.log('\n=== B. BATCHING: rooms that opened at a run, and how they stood during the run before ===');
    console.log('target guess = the room\'s temperature when the run ended. Deficit = that guess minus its temperature when the run before it began,');
    console.log('only for rooms whose valve was NOT open at any time during the run before (a room already served is not a rider).');
    const bands = { 'above guess (<= 0)': 0, '0 to 0.2 under': 0, '0.2 to 0.4 under': 0, '0.4 to 0.7 under': 0, 'over 0.7 under': 0 };
    let calledRooms = 0, servedBefore = 0, tooFar = 0;
    let runsAllRiders = 0, runsWithCallers = 0;
    const lines = [];
    runs.forEach((r, i) => {
      if (i === 0 || r.newlyOpened.length === 0) return;
      const prev = runs[i - 1];
      if (r.on - prev.off > 4 * 3600 * 1000) { tooFar += r.newlyOpened.length; return; }
      runsWithCallers++;
      let allRiders = true;
      const parts = [];
      for (const label of r.newlyOpened) {
        const room = rooms.find((x) => x.label === label);
        calledRooms++;
        if (openAnyTime(room, prev.on, prev.off)) { servedBefore++; parts.push(`${label}: open in the run before`); allRiders = false; continue; }
        const target = valueAt(room.temp, r.off);
        const then = valueAt(room.temp, prev.on);
        if (target == null || then == null) { allRiders = false; continue; }
        const deficit = target - then;
        const key = deficit <= 0 ? 'above guess (<= 0)' : deficit <= 0.2 ? '0 to 0.2 under' : deficit <= 0.4 ? '0.2 to 0.4 under' : deficit <= 0.7 ? '0.4 to 0.7 under' : 'over 0.7 under';
        bands[key]++;
        parts.push(`${label} ${deficit.toFixed(1)} under`);
        if (!(deficit > 0 && deficit <= 0.4)) allRiders = false;
      }
      if (allRiders) runsAllRiders++;
      lines.push(`${local(r.on).padEnd(17)} gap ${String(mins(r.gap)).padStart(4)}m  ${allRiders ? '*' : ' '} ${parts.join('; ')}`);
    });
    console.log(`\nrooms opened at a run that followed another within 4 h: ${calledRooms} (${tooFar} more followed a longer gap and are ignored)`);
    console.log(`  already served by the run before (valve open then): ${servedBefore}`);
    for (const [k, v] of Object.entries(bands)) console.log(`  ${k.padEnd(20)} ${v}`);
    console.log(`runs with at least one such room: ${runsWithCallers}; of those, runs where EVERY room that opened was 0-0.4 under during the run before (marked *, the runs batching could plausibly have folded into the one before): ${runsAllRiders}`);
    if (args.runs || showAll) console.log('\n' + lines.join('\n'));
  }
});
