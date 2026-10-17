// What the LOCAL app computed for each room, next to what real Home Assistant reports.
// Read-only. Usage: node show-rooms.mjs [houseId]
import { readFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const here = dirname(fileURLToPath(import.meta.url));
const house = process.argv[2] ?? 'LOCAL-COPY';
const LOCAL = 'http://localhost:7159/api';

const raw = readFileSync(join(here, '..', 'Backend', 'App', 'appsettings.secrets.development.json'), 'utf8').replace(/^﻿/, '').split('\n').filter((l) => !/^\s*\/\//.test(l)).join('\n');
const token = JSON.parse(raw).HomeAssistant.Token;
const ha = async (id) => (await (await fetch(`http://homeassistant.local:8123/api/states/${id}`, { headers: { Authorization: `Bearer ${token}` } })).json()).state;

const schedules = await (await fetch(`${LOCAL}/schedules?houseId=${house}`)).json();
const states = (await (await fetch(`${LOCAL}/room-states?houseId=${house}`)).json()).roomStates ?? [];
const byId = new Map(states.map((s) => [s.roomId, s]));

// The sensor each room's temperature comes from, per the switch in HeatingControlService.
const sensorFor = {
  'kitchen': 'sensor.kitchen_temperature_and_humidity_temperature',
  'games desk': 'sensor.sonoff_snzb_02d_temperature',
  'den': 'sensor.sonoff_snzb_02d_temperature',
  'dining desk': 'sensor.diningroom_temperature',
  'dining room': 'sensor.diningroom_temperature',
  'living room': 'sensor.livingroom_climate_temperature',
  'bedroom 1': 'sensor.bedroom_1_climate_temperature',
  'bedroom 2': 'sensor.bedroom_2_climate_temperature',
  'bedroom 3': 'sensor.bedroom_3_climate_temperature',
  'upstairs bathroom': 'sensor.bathroom_temperature',
};
const caps = (n) => [(n & 1) ? 'set-temp' : '', (n & 2) ? 'presence' : ''].filter(Boolean).join('+') || 'none';

console.log('zone (display)                 group           app temp  real HA   target  heating  capabilities');
for (const r of schedules.rooms) {
  const s = byId.get(r.id);
  const real = sensorFor[r.name.toLowerCase()] ? await ha(sensorFor[r.name.toLowerCase()]) : '-';
  const label = `${r.name}${r.displayName ? ` (${r.displayName})` : ''}`;
  console.log(
    `${label.padEnd(30)} ${(r.group ?? '').padEnd(15)} ${String(s?.currentTemperature ?? '-').padEnd(9)} ${String(real).padEnd(9)} ${String(s?.targetTemperature ?? '-').padEnd(7)} ${String(s?.heatingActive ?? '-').padEnd(8)} ${s ? caps(s.capabilities) : '(no state yet)'}`,
  );
}
