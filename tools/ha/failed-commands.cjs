// Read-only: reads add-on log text from a file and counts Zigbee commands HA did not carry out per day, both ways it
// reports them ("device did not respond" and "timed out"), leaving out non-Zigbee devices. Log lines carry only a
// time, so dates are worked out backwards from the last line, stepping back a day each time the clock jumps forward by
// more than 6 hours when read in reverse. Retries each count, as in HOME-ASSISTANT.md's earlier counts.
// Usage: node failed-by-day.cjs <addon.log> <last line's date, YYYY-MM-DD> [first day to show]
const fs = require('fs');
const [file, lastDate, firstDay = '2026-10-18'] = process.argv.slice(2);
const lines = fs.readFileSync(file, 'utf8').split('\n');
const seconds = (t) => { const [h, m, s] = t.split(':').map(Number); return h * 3600 + m * 60 + s; };
const NOT_ZIGBEE = /solax|hypervolt|mg4|notify/i;
let date = new Date(`${lastDate}T12:00:00Z`);
let previous = null;
const events = [];
const commandsPerDay = new Map();
for (let i = lines.length - 1; i >= 0; i--) {
  const line = lines[i];
  const match = /\[(\d\d:\d\d:\d\d)/.exec(line);
  if (!match) continue;
  const now = seconds(match[1]);
  if (previous !== null && now - previous > 6 * 3600) date = new Date(date.getTime() - 86400000);
  previous = now;
  const day = date.toISOString().slice(0, 10);
  if (/thermostat turned (on|off)|Boiler turned/.test(line)) commandsPerDay.set(day, (commandsPerDay.get(day) ?? 0) + 1);
  const failed = /device did not respond/.test(line) ? 'noResponse' : /has timed out|timed out/i.test(line) ? 'timedOut' : null;
  if (!failed) continue;
  const entity = /"entity_id":\["([^"]+)"/.exec(line)?.[1] ?? /(?:carry out|send) [a-z_.]+ on ([a-z_]+\.[a-z0-9_]+)/.exec(line)?.[1];
  if (!entity || NOT_ZIGBEE.test(entity)) continue;
  events.push({ day, time: match[1], entity, failed });
}
const short = (k) => k.replace(/^(climate|switch)\./, '').replace('_radiator_thermostat', ' TRV').replace('tz3210_j4pdtz9v_ts0001', 'fingerbot').replace('_thermostat', '').replace('den_boiler_relay', 'RELAY');
const days = [...new Set(events.map((e) => e.day))].filter((d) => d >= firstDay).sort();
console.log('day          fails (no reply + timed out)   valve/boiler moves logged   by device');
for (const day of days) {
  const today = events.filter((e) => e.day === day);
  const counts = new Map();
  for (const e of today) counts.set(e.entity, (counts.get(e.entity) ?? 0) + 1);
  const list = [...counts].sort((a, b) => b[1] - a[1]).map(([k, v]) => `${short(k)} ${v}`).join(', ');
  console.log(`${day}  ${String(today.length).padStart(4)} (${today.filter((e) => e.failed === 'noResponse').length}+${today.filter((e) => e.failed === 'timedOut').length})   moves ${String(commandsPerDay.get(day) ?? 0).padStart(4)}   ${list}`);
}
