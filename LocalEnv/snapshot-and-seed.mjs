// One-off, READ-ONLY snapshot of production data into files, then a load into the LOCAL emulator.
//
//   production  --GET only-->  .work/snapshot/*.json  --POST-->  http://localhost:7159  (Azurite)
//
// Production is only ever read, and only by these anonymous GETs (the same ones the web app makes).
// The local side refuses to run against anything that is not localhost.
//
//   node snapshot-and-seed.mjs [--from 1234567] [--to LOCAL-COPY]
//
// The target house ID deliberately does not exist in production, so a misconfiguration that
// pointed a local component at production would find nothing instead of the real house.
import { mkdirSync, writeFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const here = dirname(fileURLToPath(import.meta.url));
const arg = (name, fallback) => {
  const i = process.argv.indexOf(`--${name}`);
  return i > -1 ? process.argv[i + 1] : fallback;
};

const PROD = 'https://homeassistant-prod-functions.azurewebsites.net/api';
const LOCAL = arg('local', 'http://localhost:7159/api');
const from = arg('from', '1234567');
const to = arg('to', 'LOCAL-COPY');

if (!/^http:\/\/(localhost|127\.0\.0\.1)[:/]/.test(LOCAL)) {
  console.error(`REFUSING: the load target must be localhost, got ${LOCAL}`);
  process.exit(1);
}
if (to === from) {
  console.error('REFUSING: the target house ID must differ from the production house ID.');
  process.exit(1);
}

const dir = join(here, '.work', 'snapshot');
mkdirSync(dir, { recursive: true });

const canonical = (v) => JSON.stringify(v, (_, x) => (x && typeof x === 'object' && !Array.isArray(x) ? Object.fromEntries(Object.entries(x).sort(([a], [b]) => a.localeCompare(b))) : x));

console.log(`production house ${from}  ->  local house ${to}\n`);
for (const endpoint of ['schedules', 'device-settings', 'battery-rules', 'house-details', 'heating-config']) {
  const got = await fetch(`${PROD}/${endpoint}?houseId=${from}`); // GET only
  if (!got.ok) {
    console.log(`${endpoint.padEnd(16)} skipped (production GET ${got.status})`);
    continue;
  }
  const text = await got.text();
  writeFileSync(join(dir, `${endpoint}.json`), text);

  const posted = await fetch(`${LOCAL}/${endpoint}?houseId=${to}`, {
    method: 'POST', headers: { 'Content-Type': 'application/json' }, body: text,
  });
  if (!posted.ok) {
    console.log(`${endpoint.padEnd(16)} copied to file, LOCAL POST FAILED ${posted.status}: ${(await posted.text()).slice(0, 160)}`);
    continue;
  }

  const back = await (await fetch(`${LOCAL}/${endpoint}?houseId=${to}`)).text();
  let same = false;
  try { same = canonical(JSON.parse(back)) === canonical(JSON.parse(text)); } catch { /* leave false */ }
  console.log(`${endpoint.padEnd(16)} ${String(text.length).padStart(6)} bytes  loaded locally, read back ${same ? 'IDENTICAL' : 'DIFFERS (see below)'}`);
  if (!same) console.log(`     local: ${back.slice(0, 200)}`);
}
console.log(`\nSnapshot files: ${dir}`);
