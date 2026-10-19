# Running the whole stack locally, safely

A local copy of the app, the Functions API and the web UI, working on a **copy** of the real data and
reading your **real devices**, without reading or writing production and without being able to change a
device.

```
 browser :3000 ──> Functions :7159 ──> Azurite (local storage emulator)      <- data: a copy, house "LOCAL-COPY"
                       ^
 local NetDaemon app ──┘
        │
        └──> ha-guard-proxy :8124 ──> real Home Assistant :8123               <- reads pass, commands are blocked
```

## Why this exists, and why not `func start`

`Backend/HomeAssistant.Functions/local.settings.json` points `ScheduleStorageConnectionString` at the
**real Azure storage account** and `AzureSignalRConnectionString` at the **live SignalR service**. A normal
`func start` there reads and writes production, separated from the real house only by the house ID.
**Never run the Functions project from its own folder.** Everything here runs from a published copy in
`.work/` with its own settings, so production credentials never enter the process.

The app has the same trap in two other places:

- It loads `appsettings.secrets.development.json` / `appsettings.development.json` **after** environment
  variables, so env vars cannot redirect it. Hence its own published copy and its own settings file.
- It caches schedules, device settings and battery rules in `%LOCALAPPDATA%\HomeAssistant`, shared by every
  copy run as the same Windows user and not keyed by house. Set `HOMEASSISTANT_LOCAL_STORAGE` to give a copy its
  own folder (done below).

## Safety properties, and how each was checked

| Property | How it is enforced | How it was verified |
|---|---|---|
| No production reads or writes | Isolated `local.settings.json` (emulator only); house ID `LOCAL-COPY` does not exist in production | `GET /api/schedules?houseId=1234567` locally returns **empty** although that house exists in production; `Get-NetTCPConnection` showed no connection to production Azure from the app or the host |
| No device can be changed | `ha-guard-proxy.cjs` forwards only known read-only websocket types and REST GETs; everything else is logged as `WOULD CALL ...` and answered with a fake success; **fails closed** | `guard-selftest.cjs` sends a harmless `system_log.write` marker through the guard and proves it never reaches HA, with a control marker sent directly to prove the check can see markers |
| Only the guard talks to the real HA | App config points at `localhost:8124` | Network check: the app's connections were only `:7159` and `:8124` |

The one-off snapshot of production data uses anonymous **GET** requests (the same ones the web app makes) and
is saved to `.work/snapshot/` so you can see exactly what was copied.

## Start (each step was run and checked)

All commands from the repo root; `.work/` is git-ignored.

```bash
# 1. Publish both apps to isolated folders (re-run after code changes; stop them first)
dotnet publish Backend/HomeAssistant.Functions -c Release -o LocalEnv/.work/functions
dotnet publish Backend/App -c Release -o LocalEnv/.work/app

# 2. Storage emulator
mkdir -p LocalEnv/.work/azurite && azurite --silent --location LocalEnv/.work/azurite --skipApiVersionCheck &

# 3. Functions host from the published copy, with ITS OWN settings (no SignalR, no production)
cat > LocalEnv/.work/functions/local.settings.json <<'EOF'
{ "IsEncrypted": false, "Values": { "AzureWebJobsStorage": "UseDevelopmentStorage=true", "FUNCTIONS_WORKER_RUNTIME": "dotnet-isolated", "ScheduleStorageConnectionString": "UseDevelopmentStorage=true" }, "Host": { "CORS": "*", "LocalHttpPort": 7159 } }
EOF
(cd LocalEnv/.work/functions && func start --port 7159) &

# 4. PROVE isolation: this house exists in production and must come back EMPTY here
curl "http://localhost:7159/api/schedules?houseId=1234567"

# 5. Copy the data, then (optionally) apply the room migration to the COPY
node LocalEnv/snapshot-and-seed.mjs                 # production --GET--> .work/snapshot --POST--> local
node LocalEnv/migrate-rooms.mjs LOCAL-COPY --apply  # refuses to write anywhere but localhost

# 6. The guard, then PROVE it blocks before the app is allowed near it
node LocalEnv/ha-guard-proxy.cjs &
node LocalEnv/guard-selftest.cjs                    # must print "ALL PASS"

# 7. The app, through the guard, with its own cache folder
node LocalEnv/make-app-config.mjs LOCAL-COPY
(cd LocalEnv/.work/app && HOMEASSISTANT_LOCAL_STORAGE="$PWD/../storage" DOTNET_ENVIRONMENT=Development dotnet HomeAssistant.dll) &

# 8. The UI. Frontend/.env.local already targets http://localhost:7159/api.
(cd Frontend && npm run dev)          # then set the cookie  homeassistant_house_id=LOCAL-COPY
```

Then `node LocalEnv/show-rooms.mjs` prints what the local app computed for each room beside what real Home
Assistant reports, and `LocalEnv/.work/guard.log` lists every device command the app *would* have sent.

## Letting a device be driven for real (deliberate, one entity at a time)

Default is read-only. To let specific entities be commanded, restart the guard with an explicit list. A service
call is forwarded only if **every** entity it targets is on it:

```bash
GUARD_ALLOW_ENTITIES=switch.games_room_plug_heater_switch node LocalEnv/ha-guard-proxy.cjs
```

Mind that the production app on the HA host is also controlling these devices. Two controllers will fight.
Never allow the SolaX inverter entities, the boiler fingerbot or anything the production app drives.

## Things that will surprise you

- **Re-publishing the app can undo the guard.** `dotnet publish Backend/App -o LocalEnv/.work/app` copies the
  repo's `appsettings.development.json`, which points at the real HA on `homeassistant.local:8123`, and on
  Windows that is the *same file* as the guard config `appsettings.Development.json`. It can also bring the
  secrets files, which `program.cs` loads *after* it (`appsettings.secrets.development.json` sets house
  `1701`). So after every publish: delete any `appsettings.secrets.*` / `appsettings.production*` from
  `.work/app`, re-run `node LocalEnv/make-app-config.mjs LOCAL-COPY`, and check the file says port 8124
  before starting the app. Then confirm at the network level that the app holds connections to 8124 and
  none to 8123 (`Get-NetTCPConnection -OwningProcess <pid>`). Found 2026-10-19 while re-publishing.
- A stale local stack looks like missing features, not an error: the old Functions copy drops fields it
  doesn't know (the room `devices` list read as 0 everywhere until both copies were re-published).
- The app makes one outbound HTTPS connection to `api.octopus.energy` (CloudFront); with the placeholder
  credentials it reads nothing. That is expected, not a leak.

- The app evaluates schedules at startup from its **cache folder first**; it only re-fetches after 10 minutes.
  To pick up an API change straight away, delete `.work/storage/heating-schedules.json` and restart it.
- Without SignalR (none is configured) `signalr/negotiate` returns 500 and the UI/app log a connection error.
  That is expected; edits made in the UI reach the app on its next 10-minute refresh.
- On a clean start the app immediately tries to set the SolaX charge current, charger mode and manual mode.
  With the guard in place those show up as `WOULD CALL` lines. Without it they would hit the real inverter.
- Octopus and SolaX credentials in `appsettings.json` are placeholders, so the local REST reader cannot reach the
  account; import rates come from the Home Assistant integration (the fallback path), which is a useful test of it.
- `.work/app/appsettings.Development.json` contains your HA token. It is git-ignored; delete `.work/` when done.

## Stop

```powershell
LocalEnv/stop.ps1
```
