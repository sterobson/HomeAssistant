# Claude instructions for this repo

Home automation for a single house: a NetDaemon app that talks to Home Assistant
hardware, an Azure Functions API that stores configuration and history, and a Vue
SPA that drives both.

## Write back what you learn

These docs are the project's memory. Much of what matters here — which battery a device
takes, that an API lies about timestamps, that renaming a room unbinds its hardware — is
not recoverable from code, so if it isn't written down it is lost and the next person
rediscovers it the hard way. Before ending a session in which any of the following
happened, update the doc named:

| You learned | Write it here |
|---|---|
| A physical fact about a device (battery type, tooling, where it is, that it's deliberately offline) | [HOME-ASSISTANT.md](HOME-ASSISTANT.md) → Device inventory |
| An API or tool behaved in a surprising way, or a timestamp/status code misled you | [HOME-ASSISTANT.md](HOME-ASSISTANT.md) → Gotchas, as a new numbered row |
| You confirmed a spec marked † by hand | Upgrade it to ✔︎ in the battery table |
| A deploy target, slot, URL or secret location changed | [DEPLOYMENT.md](DEPLOYMENT.md) |
| An endpoint, storage container or SignalR event was added or removed | [Functions README](Backend/HomeAssistant.Functions/README.md) |
| A route, composable, service or convention changed | [Frontend README](Frontend/README.md) |
| Structure, commands or a load-bearing gotcha changed | This file |

Prefer correcting a wrong line over appending a new one, and say how a fact was
established — verified by hand, read from the API, or taken from a vendor spec. A
confident wrong doc is worse than a gap. All of these docs were ~11 months stale as of
October 2026; that is what this section exists to prevent.

## The three deployables

| Path | What it is | Runs on |
|---|---|---|
| `Backend/App` | NetDaemon 5 app (`net10.0`). Owns all hardware control — heating, battery, lights, reminders, waste. | The Home Assistant host, at `\homeassistant.local\config\netdaemon6`. Updated over the air. |
| `Backend/HomeAssistant.Functions` | Azure Functions, isolated worker (`net10.0`). The HTTP API plus all Blob/Table persistence and SignalR fan-out. | Azure, slot `homeassistant-prod-functions`. |
| `Frontend` | Vue 3 + Vite SPA. | Azure Static Web Apps, at `https://homeassistant.everymilesoftware.com`. |

`Backend/Tests` (MSTest + NSubstitute + Shouldly) covers `Backend/App` only. There are
no tests for the Functions app or the frontend beyond one Vitest file
(`Frontend/src/utils/energyDistribution.test.js`).

Solution file: `Backend/HomeAssistant.sln`.

## Commands

```bash
# Backend
dotnet build Backend/HomeAssistant.sln
dotnet test Backend/Tests                    # MSTest

# NetDaemon codegen — regenerates HomeAssistantGenerated.cs from the live HA instance.
# Required after adding entities in Home Assistant. Needs HA reachable.
cd Backend/App && dotnet tool restore && dotnet nd-codegen

# Frontend
cd Frontend && npm install
npm run dev                                   # Vite dev server on port 3000
npm run build
npm run test:run                              # Vitest, single pass
```

## Deployment

Deployment lives in a **separate repo**: `C:\Users\stero\source\repos\Deployment\`.
Run `deploy.ps1` from there; it discovers this repo via `.deployment/deploy-config.json`.
See [DEPLOYMENT.md](DEPLOYMENT.md).

Two things to know before touching deployment:

- `.deployment/` is **git-ignored**, so `deploy-config.json` exists only on the machine
  that deployed last. It is not version controlled.
- The `deploy.ps1` in *this* repo's root is the superseded red/green script. It still
  works but writes to different secret and state locations. Prefer the Deployment repo.

To check what is actually live, don't infer it from git — ask the deployed app:

```bash
curl -s https://homeassistant-prod-functions.azurewebsites.net/api/version
```

That returns the `DeploymentVersion` baked into the assembly at build time
(`/p:DeploymentVersion=<stamp>-<sha>`), so the SHA tells you the deployed commit.
The NetDaemon app reports its own version separately via `version.txt`
(`yyyyMMddHHmmss` UTC), written by the `GenerateVersionFile` MSBuild target on publish.

## Running it locally — do NOT `func start` in the Functions folder

`Backend/HomeAssistant.Functions/local.settings.json` points `ScheduleStorageConnectionString` at the **real
Azure storage account** and `AzureSignalRConnectionString` at the **live SignalR service**. Running the
Functions project from its own folder reads and writes production, separated from the real house only by the
house ID. The NetDaemon app has two matching traps: it loads its `appsettings.*development*.json` files *after*
environment variables (so env vars cannot redirect it), and it caches schedules, device settings and battery
rules in `%LOCALAPPDATA%\HomeAssistant`, shared by every copy and not keyed by house.

**[LocalEnv/](LocalEnv/README.md) runs the whole stack safely**: a published copy of the Functions app on the
Azurite emulator with its own settings, a one-off snapshot of production data under the non-production house ID
`LOCAL-COPY`, the real NetDaemon app reading your real devices, and `ha-guard-proxy.cjs` between it and Home
Assistant, which blocks every command that could change a device and logs it as `WOULD CALL ...`. Without that
guard, a local app immediately tries to drive the real SolaX inverter. Its isolation was checked at the network
level, not just by configuration; see the README for how, and for the one-time start sequence.

Set `HOMEASSISTANT_LOCAL_STORAGE` to give any extra copy of the app its own cache folder.

## Querying the live house

You can read and control the real Home Assistant instance from a dev machine:
`http://homeassistant.local:8123`, bearer token in
`Backend/App/appsettings.secrets.development.json` → `HomeAssistant.Token` (git-ignored;
never print or commit the value).

**Read [HOME-ASSISTANT.md](HOME-ASSISTANT.md) before querying it.** Several non-obvious
traps there will otherwise produce confidently wrong conclusions — chiefly that
`/api/history/period/{start}` silently defaults to a one-day window, that a state object's
`last_changed` is rewritten on every HA restart and integration reload (so it does *not*
tell you when a device failed), and that service calls return **HTTP 200 even when the
device is unreachable**. Always read state back instead of trusting the status code.

That file also holds the device inventory — battery types and levels, which sensors are
mains versus battery, and which devices are deliberately offline.

Writes move real hardware in someone's home. Ask first, and say what will physically
happen.

## Heating: the one thing that will catch you out

**Room names are the join key between configuration and hardware.** Room names are not
defined in code — they live in the `schedules/{houseId}.json` blob. Four separate
`switch` statements match on `room.Name.Trim().ToLower()` to decide what a room is
wired to:

- `GetSwitchesForRoom` — which plugs and valves to drive
- `GetCurrentTemperatureForRoom` — which sensor to read
- `GetRoomBoilerPriority` — the room's boiler score
- `PresenceService.CanDetectIfRoomInUse`

all in [HeatingControlService.cs](Backend/App/Services/Climate/HeatingControlService.cs)
and [PresenceService.cs](Backend/App/Services/PresenceService.cs). Renaming a room in the
blob silently unbinds it from its hardware — every switch falls through to `[]` or `null`
with no error. Adding a room means adding arms to all four.

**So rooms are renamed through `displayName`, never `name`.** `RoomDto`/`RoomSchedule`
carry an optional `DisplayName` that is UI-only — nothing binds to it, and the heating
code never reads it. The frontend renders every room through `roomLabel(room)`
([Frontend/src/utils/rooms.js](Frontend/src/utils/rooms.js)), which falls back to `name`
when no display name is set, and the pencil button on each room card edits it. `name`
stays as the canonical hardware key. Two traps if you touch this:

- `cleanRoomsForApi` in [HeatingView.vue](Frontend/src/views/HeatingView.vue) whitelists
  the fields it POSTs, and that payload replaces the whole blob. A field missing from the
  whitelist is wiped on the next save of anything.
- Render room names via `roomLabel`, not `room.name`, or renamed rooms will show their
  old name in whichever corner you missed.

**A "room" is really a heating zone, and two of them share a thermometer.** The dining
room and the den each hold two zones that are scheduled and controlled independently:

| Group | Zone (`name`) | Drives | Presence |
|---|---|---|---|
| Dining room | `dining desk` ("Ruth's desk") | dining heater plug | yes — desk plug power |
| Dining room | `dining room` ("Dining room") | dining TRV | no |
| Den | `games desk` ("Ste's desk") | games heater plug | yes — desk plug power |
| Den | `den` ("Den") | **nothing yet** — TRVs not fitted | no |

Which zones sit together, and in what order, is the **layout**: an ordered tree saved as `layout` next to the
room list, whose nodes are rooms (`{ roomId }`) or groups (`{ id, name, icon, children }`), nested to any
depth ("Downstairs" can hold the Dining room group, a Kitchen, and so on). It is **purely presentation**: the
heating code never reads it, and every room is scheduled, controlled and reported on its own wherever it
sits. The room list stays flat, and a room the layout does not mention appears at the end of the top level, so
the two cannot get stuck out of step. The UI is [RoomGroupCard](Frontend/src/components/RoomGroupCard.vue) and
the recursive [LayoutNode](Frontend/src/components/LayoutNode.vue); all the tree rules live in
[roomLayout.js](Frontend/src/utils/roomLayout.js) with tests. Rooms also carry an optional `icon` (an emoji).

Three consequences worth knowing before changing any of this:

- **Presence belongs to the desks, not the rooms.** `IsRoomInUse` returns `false` for any
  name it doesn't recognise, so a `RoomInUse` track on a presence-blind zone silently
  never fires, and a `RoomNotInUse` track always fires. That asymmetry is why the dining
  radiator took only the presence-free baseline tracks in the split.
- `"games room"` no longer exists — it was only ever the desk's plug and thermometer, so
  it became `games desk`. `den` is bound to nothing on purpose; that arm exists so the
  zone reads as "no controls" rather than as an unrecognised room.
- [RoomZoneBindingTests.cs](Backend/Tests/RoomZoneBindingTests.cs) pins each zone to the
  hardware it owns, including that an unknown name binds to nothing. If you rename a zone
  and those fail, that is the silent unbinding being caught for once.

Four more heating notes:

- **Two temperature sources per room.** Schedules and the UI read the standalone room
  thermometer (`sensor.*`); boiler verification reads the TRV's own onboard sensor
  (`climate.*` → `Attributes.CurrentTemperature`). They are different numbers from
  different devices. Don't assume one when you mean the other.
- **The boiler has no feedback.** It is switched by a Tuya fingerbot pressing a physical
  button (`switch.tz3210_j4pdtz9v_ts0001`), so the app's `_boilerOn` is a belief, not a
  reading. `BoilerVerificationService` infers success from radiator temperature changes.
  It has no test coverage.
- **The app isn't the only thing that can run the boiler, and it has to be told which.** A
  physical switch at the boiler chooses between the fingerbot and a separate
  temperature/time scheduler in the kitchen. The two are exclusive: in scheduler mode the
  boiler's button is **dead**, so fingerbot presses are no-ops. Neither the switch nor the
  scheduler exists in HA (verified 2026-10-17 — the only `climate.*` entities are the five
  TRVs), so nothing can *read* which mode the house is in. Since 2026-10-17 it is
  configuration instead: `heating.boilerControlPath` in the **device-settings** blob
  (`externalScheduler`, the default, or `fingerbot`), set from the web UI under
  **Settings → Boiler control** on the heating page and delivered by the existing
  `device-settings-changed` SignalR event. In external-scheduler mode `HeatingControlService`
  makes no fingerbot call, holds no `_boilerOn` belief, runs no verification and issues no
  corrective press; rooms are still controlled normally. **As of 2026-10-17 the switch is on
  the kitchen scheduler and the setting matches it, so `Backend/App` is deliberately not
  driving the boiler.** The setting is a claim, not a reading — if someone flips the switch
  without changing it the app is wrong again and cannot tell. Never conclude from radiator
  temperatures that a press worked. See [HOME-ASSISTANT.md](HOME-ASSISTANT.md) → gotcha 13
  and the boiler fingerbot device note.
- **Boosts end an hour early in British Summer Time (known bug, found 2026-10-17, not yet fixed).**
  `HeatingControlService` decides whether a boost is active with
  `Boost.EndTime.GetValueOrDefault().DateTime > now`, where `now` is the *local* clock but `EndTime` is the UTC
  timestamp the UI sends, with its offset stripped by `.DateTime`. In BST a boost therefore ends an hour early,
  and any boost shorter than an hour is **never active at all**. Reproduced locally against real devices: a
  1-hour boost produced no device commands, a 3-hour boost did. The fix is to compare as `DateTimeOffset`
  against `_timeProvider.GetUtcNow()`.

## In-flight work

- `/heating2` and `Frontend/src/components/heating2/` are a **superseded UI**, slated for
  removal. The final heating UI is the original room-card view (`HeatingView.vue` +
  `RoomCard.vue`).
- The newer heating config engine (`HeatingConfigDto`, `heating-config` container,
  `HeatingConfigFunctions`) is **shipped dark**: the Functions app stores and serves it,
  but nothing in `Backend/App` references it yet. The legacy schedule stack is still what
  actually runs the house.

## Conventions

- **No `var` in C#** — explicit types everywhere.
- **Sentence case** for all UI text: labels, buttons, modal titles, accordions.
  "Add new room", not "Add New Room".
- Modals: fixed overlay + centred panel, base `z-index` 1000. A modal opened from another
  modal goes 100 higher, passed in as a prop.
- Frontend composables are singletons holding reactive state, auto-persisted — cookies for
  small settings, `localStorage` for structured JSON.
- Keep the docs current as you learn — see **Write back what you learn** at the top of
  this file for which doc takes what.
