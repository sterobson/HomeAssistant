// Backfills the boiler's on/off history (the Functions app's `boilerhistory` table, shown by the heating page's boiler
// chart) for the days before it was recorded, by working out from the radiator valves when the boiler evidently ran
// (owner, 2026-10-21). Rows it writes are marked Inferred, and the chart draws them hatched, apart from the app's own
// record.
//
//   node tools/ha/boiler-history-backfill.cjs --house 1234567 [--days 7] [--runs] [--why]  dry run: prints what it would write
//                                                                                        (--why: each valve's hot stretches behind each run)
//   node tools/ha/boiler-history-backfill.cjs --house 1234567 [--days 7] --apply         replaces earlier inferred rows, writes these
//
// How the boiler is read from the valves. Each TRV's own sensor sits against its radiator, so hot water arriving shows as
// that sensor climbing (about 0.5 °C every few minutes) and the boiler stopping as it falling again (HOME-ASSISTANT.md,
// and tools/ha/warmup-history.cjs, which reads warm-ups the same way):
//   - a valve is "hot" from the first reading of a climb that gains at least --rise degrees within 45 minutes (a slow
//     creep over hours is the room warming, not hot water) until the climb's last rise, a climb ending once its sensor
//     has been flat for 30 minutes or falls. A valve that shuts on its own setpoint also stops climbing, so that alone
//     is no proof the boiler went off;
//   - the boiler is taken to be on while any valve is hot, and through a gap between hot stretches of up to --bridge
//     minutes (default 120) UNLESS a valve cooled by --fall degrees or more during the gap while it stayed open
//     (hvac_action "heating"): an open valve cooling is the boiler off. One that cooled while shut proves nothing. (2026-10-20 overnight: Bedroom 3 cycled on its own setpoint for hours
//     while the boiler ran, and the bridge is what keeps that from reading as the boiler cycling.)
//   - runs shorter than 5 minutes are dropped.
// The times are rough: a valve's sensor lags the boiler by a few minutes at each end, and a stretch with every valve
// shut shows nothing. It says what the boiler did, whoever ran it (the boiler thermostat or the app), never why.
//
// Writes go to the production storage account (ScheduleStorageConnectionString in the git-ignored
// Backend/HomeAssistant.Functions/local.settings.json; the key is never printed), table `boilerhistory`, partition = house
// ID. It never writes at or after the oldest row the app recorded itself, and --apply first deletes every inferred row,
// so re-running replaces a backfill rather than adding to it.
const crypto = require('crypto');
const fs = require('fs');
const path = require('path');
const { rest, parseArgs, run } = require('./lib/ha.cjs');

const TRVS = [
  'climate.bedroom_1_radiator_thermostat',
  'climate.bedroom_2_radiator_thermostat',
  'climate.bedroom_3_radiator_thermostat',
  'climate.dining_room_radiator_thermostat',
  'climate.living_room_radiator_thermostat',
  'climate.den_radiator_1_thermostat',
  'climate.den_radiator_2_thermostat',
];
const TABLE = 'boilerhistory';
const MIN_RUN_MS = 5 * 60000;

const ms = (iso) => new Date(iso).getTime();
const local = (t) => new Date(t).toLocaleString('en-GB', { weekday: 'short', day: '2-digit', month: 'short', hour: '2-digit', minute: '2-digit' });

// ---- Reading the valves ---------------------------------------------------------------------------------------------

/** Each valve's readings, oldest first: { t, open, temp }. */
function readings(rows) {
  return rows
    .filter((r) => r.attributes?.current_temperature != null && r.state !== 'unavailable')
    .map((r) => ({ t: ms(r.last_updated), open: r.attributes.hvac_action === 'heating', temp: Number(r.attributes.current_temperature) }))
    .sort((a, b) => a.t - b.t);
}

// Hot water lifts a radiator's sensor fast (about 0.5 °C every few minutes); a room warming slowly creeps it by the same
// amount over hours. So a climb counts only if it gains `rise` within RISE_WITHIN_MS of starting, and it ends once the
// sensor has gone FLAT_MS without climbing further (the last rise is its end).
const RISE_WITHIN_MS = 45 * 60000;
const FLAT_MS = 30 * 60000;

/** The stretches where a valve's sensor climbed fast by `rise` or more: [start, end of climb]. */
function hotStretches(points, rise) {
  const out = [];
  let i = 1;
  while (i < points.length) {
    if (points[i].temp <= points[i - 1].temp) { i++; continue; }
    // Climbing: follow it up while it keeps rising, allowing flat readings for up to FLAT_MS between rises.
    const base = points[i - 1].temp;
    const start = points[i];
    let top = points[i];
    let j = i + 1;
    while (j < points.length && points[j].temp >= top.temp && (points[j].temp > top.temp || points[j].t - top.t <= FLAT_MS)) {
      if (points[j].temp > top.temp) top = points[j];
      j++;
    }
    const fastEnough = points.slice(i, j).some((p) => p.temp - base >= rise && p.t - start.t <= RISE_WITHIN_MS);
    if (fastEnough) out.push([start.t, top.t]);
    i = Math.max(j, i + 1);
  }
  return out;
}

/**
 * True when some valve cooled by `fall` or more between two times WHILE IT STAYED OPEN: evidence the boiler was off. A
 * valve that cooled while shut and then opened proves nothing (it shut on its own setpoint), so the peak is only
 * measured from readings taken while the valve was open, and starts again each time it opens.
 */
function openValveCooled(allPoints, from, to, fall) {
  for (const points of allPoints) {
    let openPeak = null;
    let wasOpen = false;
    for (const p of points) {
      if (p.t > to) break;
      if (!p.open) { openPeak = null; wasOpen = false; continue; }
      openPeak = wasOpen ? Math.max(openPeak, p.temp) : p.temp;
      wasOpen = true;
      if (p.t >= from && openPeak - p.temp >= fall) return true;
    }
  }
  return false;
}

/** The boiler's inferred runs: [start, end] in ms, oldest first; `end` is null for a run still going at `now`. */
function boilerRuns(allPoints, { rise, fall, bridgeMs, now }) {
  const stretches = allPoints.flatMap((points, index) => hotStretches(points, rise).map((s) => [...s, index])).sort((a, b) => a[0] - b[0]);
  const runs = [];
  for (const [start, end, valve] of stretches) {
    const last = runs.at(-1);
    if (last && start <= last[1]) { last[1] = Math.max(last[1], end); last.why.push([start, end, valve]); continue; }
    if (last && start - last[1] <= bridgeMs && !openValveCooled(allPoints, last[1], start, fall)) {
      last[1] = Math.max(last[1], end);
      last.why.push([start, end, valve, 'bridged']);
      continue;
    }
    const run = [start, end];
    run.why = [[start, end, valve]];
    runs.push(run);
  }
  // The last run is still going (end null) if it would be bridged to now: recent enough, and no open valve has cooled since.
  return runs
    .map((r, index) => {
      const stillOn = index === runs.length - 1 && now - r[1] <= bridgeMs && !openValveCooled(allPoints, r[1], now, fall);
      return Object.assign([r[0], stillOn ? null : r[1]], { why: r.why });
    })
    .filter(([s, e]) => e == null || e - s >= MIN_RUN_MS);
}

// ---- Azure Table storage (REST, SharedKeyLite) ----------------------------------------------------------------------

function storage() {
  const settingsPath = path.resolve(__dirname, '..', '..', 'Backend', 'HomeAssistant.Functions', 'local.settings.json');
  // JSONC (comments, trailing commas), so the value is matched rather than parsed.
  const match = fs.readFileSync(settingsPath, 'utf8').match(/"ScheduleStorageConnectionString"\s*:\s*"([^"]+)"/);
  if (!match) throw new Error(`No ScheduleStorageConnectionString in ${settingsPath}`);
  const parts = Object.fromEntries(match[1].split(';').filter(Boolean)
    .map((kv) => [kv.slice(0, kv.indexOf('=')), kv.slice(kv.indexOf('=') + 1)]));
  const account = parts.AccountName;
  const key = Buffer.from(parts.AccountKey, 'base64');
  const base = `https://${account}.table.${parts.EndpointSuffix || 'core.windows.net'}`;

  async function call(method, resource, { body, headers = {}, query = '' } = {}) {
    const date = new Date().toUTCString();
    const canonical = `/${account}/${resource.split('?')[0]}`;
    const signature = crypto.createHmac('sha256', key).update(`${date}\n${canonical}`, 'utf8').digest('base64');
    const res = await fetch(`${base}/${resource}${query}`, {
      method,
      headers: {
        'x-ms-date': date,
        'x-ms-version': '2019-02-02',
        Authorization: `SharedKeyLite ${account}:${signature}`,
        Accept: 'application/json;odata=nometadata',
        DataServiceVersion: '3.0;NetFx',
        ...(body ? { 'Content-Type': 'application/json' } : {}),
        ...headers,
      },
      body: body ? JSON.stringify(body) : undefined,
    });
    const text = await res.text();
    if (!res.ok && !(method === 'POST' && resource === 'Tables' && res.status === 409)) {
      throw new Error(`${method} ${resource} -> ${res.status} ${text.slice(0, 300)}`);
    }
    return text ? JSON.parse(text) : null;
  }

  const entityPath = (pk, rk) => `${TABLE}(PartitionKey='${encodeURIComponent(pk)}',RowKey='${encodeURIComponent(rk)}')`;
  return {
    account,
    ensureTable: () => call('POST', 'Tables', { body: { TableName: TABLE } }),
    // One page (up to 1000 rows): a house's boiler history changes a few dozen times a day, and the backfill only needs
    // the inferred rows and the oldest recorded one, which sort last and first. Raise this if that ever stops holding.
    async list(houseId) {
      const filter = encodeURIComponent(`PartitionKey eq '${houseId.replace(/'/g, "''")}'`);
      const page = await call('GET', `${TABLE}()`, { query: `?$filter=${filter}` });
      return page?.value ?? [];
    },
    upsert: (row) => call('PUT', entityPath(row.PartitionKey, row.RowKey), { body: row }),
    remove: (row) => call('DELETE', entityPath(row.PartitionKey, row.RowKey), { headers: { 'If-Match': '*' } }),
  };
}

// The Functions app's row key: inverted .NET ticks, 19 digits, so the newest row sorts first.
const TICKS_AT_UNIX_EPOCH = 621355968000000000n;
const rowKey = (t) => (9223372036854775807n - (BigInt(t) * 10000n + TICKS_AT_UNIX_EPOCH)).toString().padStart(19, '0');

// ---- Main -----------------------------------------------------------------------------------------------------------

run(async () => {
  const args = parseArgs(['apply', 'runs', 'why']);
  const houseId = args.house && args.house !== true ? String(args.house) : null;
  if (!houseId) throw new Error('usage: boiler-history-backfill.cjs --house <houseId> [--days 7] [--runs] [--apply]');
  const days = Number(args.days || 7);
  const rise = Number(args.rise || 1);
  const fall = Number(args.fall || 1);
  const bridgeMs = Number(args.bridge || 120) * 60000;
  const now = Date.now();
  const from = now - days * 86400000;

  // HOME-ASSISTANT.md gotcha 1: without end_time the history API returns one day.
  const allPoints = [];
  for (const id of TRVS) {
    const [rows = []] = await rest('GET', `/history/period/${encodeURIComponent(new Date(from).toISOString())}?filter_entity_id=${id}&end_time=${encodeURIComponent(new Date(now).toISOString())}`);
    allPoints.push(readings(rows));
  }

  const runs = boilerRuns(allPoints, { rise, fall, bridgeMs, now });

  // The app's own record wins: nothing inferred at or after its oldest row.
  const table = storage();
  const existing = await table.list(houseId).catch((err) => {
    if (/TableNotFound|404/.test(String(err.message))) return [];
    throw err;
  });
  const recorded = existing.filter((r) => !r.Inferred).map((r) => ms(r.At)).sort((a, b) => a - b);
  const storedInferred = existing.filter((r) => r.Inferred).map((r) => ms(r.At)).sort((a, b) => a - b);
  console.log(`Stored now: ${recorded.length} rows recorded by the app, ${storedInferred.length} inferred`
    + (storedInferred.length ? ` (${local(storedInferred[0])} to ${local(storedInferred.at(-1))})` : '') + '.');
  const cutoff = recorded.length ? recorded[0] : now;

  const events = [{ t: from, on: false, start: true }];
  for (const [start, end] of runs) {
    if (start >= cutoff) break;
    events.push({ t: start, on: true });
    if (end != null && end < cutoff) events.push({ t: end, on: false });
  }

  let onMs = 0;
  for (const [start, end] of runs) onMs += Math.min(end ?? now, cutoff) - Math.max(start, from);
  console.log(`Boiler runs worked out from ${TRVS.length} radiator valves over the last ${days} days (rise ${rise} °C, fall ${fall} °C, bridge ${bridgeMs / 60000} min).`);
  console.log(`${runs.length} runs, ${(onMs / 3600000).toFixed(1)} h on in all; ${events.length} rows to write${recorded.length ? `, stopping at the app's first own row (${local(cutoff)})` : ''}.`);
  if (args.runs || args.why) {
    for (const r of runs) {
      const [start, end] = r;
      console.log(`  ${local(start)}  to  ${end == null ? 'now (still on)' : local(end)}  ${((((end ?? now) - start) / 60000)).toFixed(0).padStart(4)} min`);
      if (args.why) {
        for (const [s, e, valve, how] of r.why) {
          console.log(`      ${TRVS[valve].replace('climate.', '').replace('_thermostat', '').padEnd(28)} ${local(s)} to ${local(e)}${how ? '  (' + how + ')' : ''}`);
        }
      }
    }
  }

  if (!args.apply) {
    console.log('\nDry run: nothing written. Add --apply to write these to the production table.');
    return;
  }

  await table.ensureTable();
  const stale = existing.filter((r) => r.Inferred);
  for (const row of stale) await table.remove(row);
  for (const e of events) {
    await table.upsert({
      PartitionKey: houseId,
      RowKey: rowKey(e.t),
      At: new Date(e.t).toISOString(),
      'At@odata.type': 'Edm.DateTime',
      On: e.on,
      AppControlsBoiler: true,
      Inferred: true,
    });
  }
  console.log(`\nDeleted ${stale.length} earlier inferred rows and wrote ${events.length} to ${table.account}/${TABLE}, partition ${houseId}.`);
});
