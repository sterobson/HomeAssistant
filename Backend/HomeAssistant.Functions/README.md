# API (Azure Functions)

The HTTP API and persistence layer for the house. Azure Functions on the **isolated
worker** model (`net10.0`). Serves the Vue frontend and acts as the sync point between
the frontend and the NetDaemon hardware app in `../App`.

Deployed to slot `homeassistant-prod-functions` — see [../../DEPLOYMENT.md](../../DEPLOYMENT.md).

## Running locally

```bash
func start                          # http://localhost:7071/api
```

Needs Azure Functions Core Tools and a `local.settings.json` (git-ignored). Storage can
be Azurite — the deploy tool's `local` environment starts one on blob 10030, queue 10031,
table 10032.

App settings required in production:

- `AzureWebJobsStorage`
- `ScheduleStorageConnectionString` — **every** storage service reads this one connection
  string, for both Blob and Table
- `AzureSignalRConnectionString`

There is a Bruno collection in `Bruno/` for manual request testing, including
`set-schedules.bru`.

**Room names.** `RoomDto` has two: `name` is the canonical one and is the join key the
NetDaemon app matches on to find a room's hardware, so changing it silently unbinds the
room; `displayName` is optional, UI-only, and safe to change. The heating UI edits
`displayName` and leaves `name` alone. `POST /schedules` replaces the whole blob with
whatever it is sent, so a client that omits `displayName` will wipe it for every room.

`layout` is a third, separate thing: an ordered tree of rooms and groups (groups hold more nodes, to any
depth) saying how the UI arranges the rooms, plus `RoomDto.icon` for an emoji beside a room. Nothing server-side
reads either. A room missing from the tree shows at the end of the top level, and a node naming a room that no
longer exists is skipped. Both are subject to the same whole-blob replacement rule as `displayName`.

## Conventions

- **Scoped by `houseId`.** Nearly every endpoint takes a `?houseId=` query parameter and
  uses it as the blob name or partition key. A missing or blank value is a 400.
- **camelCase JSON, flexible enums.** `JsonConfiguration` configures both the worker and
  ASP.NET Core serializer options so enums deserialize from either strings or numbers,
  and properties serialize as camelCase for the frontend.
- **No tests.** `Backend/Tests` covers the NetDaemon app only. Shared DTO serialization is
  covered indirectly by `JsonSerializationTests` there.

## Authentication — none, in practice

Every HTTP trigger is `AuthorizationLevel.Anonymous` except two:

| Endpoint | Level |
|---|---|
| `POST /api/app-release/desired` | `Function` |
| `POST /api/app-release/upload` | `Function` |

There is no worker middleware and no custom key validation in this project. The
`x-functions-key` header the frontend sends is ignored by every other endpoint, as is
`X-Localhost-Key`. Anyone who knows the host name and a `houseId` can read and write
schedules, heating config, battery rules, device settings and history.

If you close this, the data endpoints are the ones that matter, and rotating the key
achieves nothing until they require one. See
[../../Frontend/ENVIRONMENT_SETUP.md](../../Frontend/ENVIRONMENT_SETUP.md).

## Endpoints

### Heating

| Method | Route | Function |
|---|---|---|
| GET/POST | `schedules` | Room schedules — the legacy model that actually runs the house |
| GET/POST | `room-states` | Live room temperature, heating state, active track, `devices` (the HA entities each zone is bound to, with their state) and `occupancy` (the zone's presence detectors), plus a house-wide `boiler`: `{ appControlsBoiler, believedOn, since }`, what the app believes the boiler is doing (since 2026-10-19), and `batteries`: every replaceable battery `{ entityId, name, percent, area, deviceEntityIds }` for the UI's low-battery icons (since 2026-10-20). Since 2026-10-21 also each room's `waitingForOccupancyTrackIds`, and heater plugs carry the warm-up fields valves do. The shape is the shared `RoomStateDto`, so a field the deployed Functions build doesn't know is dropped on save: deploy Functions before or with the NetDaemon app. Each upload's `boiler` is also recorded in `boilerhistory` when it differs from the newest row there |
| GET | `boiler-history` | The boiler's on/off history (since 2026-10-21): `hours` (1–744, default 48) → `{ from, to, before, events: [{ at, on, appControlsBoiler, inferred }] }`, oldest first, with `before` the newest change before the window. Written by `SetRoomStates`, not by any endpoint; `inferred` rows were backfilled from the radiator valves by `tools/ha/boiler-history-backfill.cjs` |
| GET | `room-history` | Temperature history for one room over a date range |
| GET/POST | `heating-config` | Newer device/rule config model — **stored but not yet consumed** by the NetDaemon app |
| GET/POST | `house-details` | House name and occupancy |
| GET/POST | `room-shares`, POST `room-shares/revoke` | Owner side of the QR links (since 2026-10-19): list, get-or-create, and revoke-and-reissue the share token of a zone (`roomId=`) or a layout group (`groupId=`). House-ID keyed like everything else |
| GET | `guest/room?token=` | Guest side: `{ kind: 'zone'|'group', name, icon, lockedBy, zones: [{ name, icon, currentTemperature, targetTemperature, heatingActive }], override, limits }` — a group lists every zone in it at any depth. `lockedBy` names the group around the target whose boost is running (null otherwise); while it is set, `guest/override` and `guest/override/cancel` return **409** (`GuestOverride.LockedBy`). A group's `override` is only its own boost (tagged `boost.groupId`). **Token only — never takes or returns the house ID** |
| POST | `guest/override?token=`, `guest/override/cancel?token=` | Set or change (`{ temperature, durationMinutes }`, 16–24 °C, ≤ 360 min, enforced here) or cancel the boost on the token's zone or on every zone in its group; a new override replaces a running one. Saves the schedules blob and sends `schedules-changed`, like an owner save. Cannot touch schedules, other zones or anything else. Logic in the linked `App/Shared/Climate/GuestOverride.cs`, tested in `Backend/Tests/GuestOverrideTests.cs` |

**Security model.** Every endpoint is `AuthorizationLevel.Anonymous`; for owner endpoints the house ID is the
only credential, so anyone who has it can do everything. The QR links exist so a guest never needs it: a
share token (128 random bits) resolves server-side to one house and zone, and the `guest/*` endpoints can only
change that zone's boost. A guest who was never given the house ID cannot reach anything else.

`GetSchedules` has one behaviour worth knowing: rooms that exist in `room-states` but not
in the schedules blob are synthesized into the response with the default name
`Room {id}`.

### Energy and battery

| Method | Route | Function |
|---|---|---|
| GET | `battery-history` | Battery state history |
| POST | `battery-state` | Push a battery state sample |
| POST | `battery-history-backfill` | Request a backfill |
| POST | `battery-history-replace` | Replace a range |
| GET/POST | `battery-pricing` | Octopus price history |
| GET/POST | `battery-rules` | Battery zone rules |
| GET | `energy-history` | One day of hourly energy data |
| GET | `energy-history-range` | A date range |
| POST | `energy-history` | Push one hour |
| POST | `energy-history-replace` | Replace one day |
| POST | `energy-history-bulk-replace` | Replace many days |
| POST | `energy-history-backfill` | Request a backfill |
| POST | `power-monitor/start` | Begin a live power-monitoring session |
| POST | `power-monitor/snapshot` | Push a snapshot, fanned out over SignalR |

### Device setup

| Method | Route | Function |
|---|---|---|
| GET/POST | `entities` | The Home Assistant entity list, pushed by the hardware app so the UI can offer pickers |
| GET/POST | `device-settings` | Entity-ID mappings configured in the UI, plus `heating.boilerControlPath` — which of the boiler's two control paths the house is physically wired to (`0` boiler thermostat, the default; `1` fingerbot). Nothing can read the switch at the boiler, so it is set by hand and the app trusts it. Also `heating.deviceRules` (since 2026-10-19): per-device heating rules keyed by entity ID, including each device's `preHeatMinutes`, `null` until first saved; stored as-is, evaluated only by the NetDaemon app. And `heating.zoneOccupancy` (since 2026-10-19): presence detector ids per zone canonical name, `null` until first saved. And `heating.areaPlacement` (since 2026-10-20): Home Assistant area name → `"room:{id}"` or `"group:{id}"`, where the UI shows that area's battery devices; presentation only, the app never reads it. Room states carry each zone's `occupancy` (its detectors and every one available) through the linked `RoomStateDto`. The rule types are the linked `App/Shared/Climate/DeviceRulesDto.cs`, but `Models/DeviceSettingsDto.cs` is a separate copy of the app's — a field missing from it is dropped on save, so deploy this app first |
| GET/POST | `device-batteries` | Daily battery level per **replaceable cell** — the coin cells and AAs in thermometers, TRVs, buttons and the boiler fingerbot. `GET` takes an optional `days` (default 30, max 365) and returns snapshots newest day first |

`device-batteries` is not the SolaX home battery; that is `battery-history` above, pushed
every ten minutes. This one is about which cells need replacing. Two things it guarantees:

- **One row per device per day.** The row key is derived from the UTC date alone, so the
  app pushing more than once a day — it re-checks every four hours and on every restart —
  replaces that day's row instead of duplicating it.
- **`unavailable` is recorded as `isAvailable: false` with a null percent, never 0%.** A
  device that has fallen off the mesh has not reported a flat battery, and a run of
  unavailable days is the signal worth seeing. The boiler fingerbot is exactly this case.

### OTA updates

| Method | Route | Auth |
|---|---|---|
| GET | `app-release/latest` | Anonymous |
| GET | `app-release/desired` | Anonymous |
| POST | `app-release/desired` | **Function** |
| GET | `app-release/download` | Anonymous |
| POST | `app-release/upload` | **Function** |
| GET/POST | `app-release/running` | Anonymous |
| POST | `app-release/acknowledge` | Anonymous |

### Health and real-time

| Method | Route | Function |
|---|---|---|
| GET/HEAD | `health` | Liveness |
| GET | `version` | The `DeploymentVersion` baked in at build time — use this to tell what is deployed |
| GET/POST | `signalr/negotiate` | SignalR handshake |
| POST | `signalr/add-to-group` | Join a house group |
| POST | `signalr/send-test-message` | Diagnostics |

### Timer triggers

| Function | Schedule | Does |
|---|---|---|
| `DailyHistorySimplify` | `0 30 1 * * *` | Thins stored history daily at 01:30 |
| `CleanupOldHistory` | `0 0 3 * * *` | Prunes old room history at 03:00 |

## Storage

One connection string, many containers and tables.

**Blob containers** — `schedules`, `room-states`, `house-details`, `entities`,
`device-settings`, `heating-config`, `battery-rules`, `app-releases`.

Most are a single JSON blob per house, named `{houseId}.json`.

**Tables** — `batteryhistory`, `energypricehistory`, `energyhistory`, `roomhistory`,
`devicebatteryhistory`, `AppReleaseState`, `RoomShares` (PartitionKey house ID, RowKey share token, `RoomId`
for a zone or `GroupId` for a layout group, `CreatedAt`; one live token per zone or group; looked up by RowKey
for guests).

`devicebatteryhistory` is partitioned by `{houseId}_{entityId}` with an inverted-date row
key, so one device's history is a single partition scan and the newest day sorts first.

`boilerhistory` (since 2026-10-21) is partitioned by house ID with inverted ticks of the change's time as row key:
`At`, `On`, `AppControlsBoiler`, `Inferred`. One row per change of the app's belief or control path (a corrective
press, which keeps the belief, is not a change). Inferred rows are a backfill worked out from the radiator valves
(`tools/ha/boiler-history-backfill.cjs`, which never writes at or after the app's first own row and replaces its own
earlier rows on each `--apply`).

## SignalR

Clients join a group named `house-{houseId}` via `signalr/negotiate` and
`signalr/add-to-group`. Writes then broadcast a change event to that group, so the
frontend and the hardware app refresh without polling.

Events emitted by this project, in full:

| Event | Emitted by |
|---|---|
| `schedules-changed` | `SetSchedules` |
| `room-states-changed` | `SetRoomStates` |
| `heating-config-changed` | `SetHeatingConfig` |
| `device-settings-changed` | `SetDeviceSettings` |
| `battery-state-changed` | `SetBatteryState` |
| `battery-rules-changed` | `SetBatteryRules` |
| `battery-history-replaced` | `ReplaceBatteryHistory` |
| `backfill-battery-history` | `RequestBatteryHistoryBackfill` |
| `energy-history-changed` | `PostEnergyHour` |
| `energy-history-replaced` | `ReplaceEnergyHistory`, `BulkReplaceEnergyHistory` |
| `backfill-energy-history` | `RequestEnergyHistoryBackfill` |
| `start-power-monitor` | `StartPowerMonitor` |
| `power-snapshot` | `PostPowerSnapshot` |
| `update-available` | `SetDesiredAppVersion` |
| `version-report` | `SetRunningAppVersion` |

The `backfill-*` and `start-power-monitor` events are commands aimed at the **hardware
app**, not the browser: the API has no Home Assistant access, so it asks the NetDaemon
app to do the work and push results back.
