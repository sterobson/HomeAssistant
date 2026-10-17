// Writes the LOCAL NetDaemon app's settings into .work/app/appsettings.Development.json.
//
//  - Home Assistant is reached THROUGH THE GUARD PROXY (localhost:8124), never directly, so every
//    device command is intercepted. The real HA is only ever the proxy's upstream.
//  - The API is the LOCAL Functions host (localhost:7159) backed by the Azurite emulator.
//  - The house ID is one that does not exist in production.
//
// The HA token is copied file-to-file from the existing dev secrets and is never printed.
import { readFileSync, writeFileSync, existsSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const here = dirname(fileURLToPath(import.meta.url));
const secretsPath = join(here, '..', 'Backend', 'App', 'appsettings.secrets.development.json');
const out = join(here, '.work', 'app', 'appsettings.Development.json');
const houseId = process.argv[2] ?? 'LOCAL-COPY';
const guardPort = Number(process.env.GUARD_LISTEN_PORT ?? 8124);
const apiUrl = process.env.LOCAL_API_URL ?? 'http://localhost:7159/';

if (!/^http:\/\/(localhost|127\.0\.0\.1)[:/]/.test(apiUrl)) {
  console.error(`REFUSING: the API must be local, got ${apiUrl}`);
  process.exit(1);
}
if (!existsSync(join(here, '.work', 'app'))) {
  console.error('Publish the app first: dotnet publish Backend/App -c Release -o LocalEnv/.work/app');
  process.exit(1);
}

const raw = readFileSync(secretsPath, 'utf8').replace(/^﻿/, '').split('\n').filter((l) => !/^\s*\/\//.test(l)).join('\n');
const token = JSON.parse(raw).HomeAssistant.Token;

const toGuard = { Host: 'localhost', Port: guardPort, Ssl: false, Token: token };
const config = {
  HomeAssistant: toGuard,
  HomeAssistantApiEndpoints: toGuard,
  WebSynchronisation: { HouseId: houseId, ScheduleApiUrl: apiUrl },
};
writeFileSync(out, JSON.stringify(config, null, 2));
console.log(`wrote ${out}`);
console.log(`  Home Assistant  -> localhost:${guardPort}  (the guard proxy, NOT the real HA)`);
console.log(`  API             -> ${apiUrl}  (local emulator)`);
console.log(`  house           -> ${houseId}`);
