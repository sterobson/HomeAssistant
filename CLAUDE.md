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
| You wrote a Home Assistant query or setup step you'd run again | A script in [tools/ha/](tools/ha/README.md), listed in its README |
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
no tests for the Functions app, and the frontend's are Vitest files for the pure helpers in
`Frontend/src/utils/` only (no component tests).

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
environment variables (so env vars cannot redirect it), and it caches schedules, device settings, battery
rules and its boiler belief in `%LOCALAPPDATA%\HomeAssistant`, shared by every copy and not keyed by house.
(On the host, as an add-on, the same caches live in `/data/HomeAssistant`, the only folder that survives an
add-on restart; see `LocalStorageFolder`.)

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

**Use the scripts in [tools/ha/](tools/ha/README.md) before writing your own queries.** They cover
state history (`entity-history.cjs`), what caused each change (`who-changed.cjs`: a user, the
NetDaemon app as "Supervisor", or the device itself), add-on state and restarts (`addon.cjs`),
finding entities by friendly name (`find-entities.cjs`),
the device/area registry (`list-devices.cjs`), pairing a Zigbee device with house-style names
(`pair-zigbee.cjs`) and creating HA automations from version-controlled JSON
(`put-automation.cjs`, with the files in `tools/ha/automations/`). Only those last two write. They read the token without printing it and already
dodge the history and timestamp traps. When you write a new query you'd want again, add it there
and list it in that README.

Writes move real hardware in someone's home. Ask first, and say what will physically
happen.

## Heating: the one thing that will catch you out

**Room names are the join key between configuration and hardware.** Room names are not
defined in code — they live in the `schedules/{houseId}.json` blob. Three separate
`switch` statements match on `room.Name.Trim().ToLower()` to decide what a room is
wired to:

- `GetSwitchesForRoom` — which plugs and valves to drive
- `GetCurrentTemperatureForRoom` — which sensor to read
- `PresenceService.CanDetectIfRoomInUse`

all in [HeatingControlService.cs](Backend/App/Services/Climate/HeatingControlService.cs)
and [PresenceService.cs](Backend/App/Services/PresenceService.cs). Renaming a room in the
blob silently unbinds it from its hardware — every switch falls through to `[]` or `null`
with no error. Adding a room means adding arms to all three.

**So rooms are renamed through `displayName`, never `name`.** `RoomDto`/`RoomSchedule`
carry an optional `DisplayName` that is UI-only — nothing binds to it, and the heating
code never reads it. The frontend renders every room through `roomLabel(room)`
([Frontend/src/utils/rooms.js](Frontend/src/utils/rooms.js)), which falls back to `name`
when no display name is set. In Edit layout the pencil button on a room or group opens one dialog that
changes its name and emoji together (a room leaves the name empty to fall back). `name`
stays as the canonical hardware key. Two traps if you touch this:

- `cleanRoomsForApi` in [HeatingView.vue](Frontend/src/views/HeatingView.vue) whitelists
  the fields it POSTs, and that payload replaces the whole blob. A field missing from the
  whitelist is wiped on the next save of anything.
- Render room names via `roomLabel`, not `room.name`, or renamed rooms will show their
  old name in whichever corner you missed.

**A "room" is really a heating zone, and the two dining zones share a thermometer.** The
dining room and the den each hold two zones that are scheduled and controlled independently:

| Group | Zone (`name`) | Drives | Reads | Presence |
|---|---|---|---|---|
| Dining room | `dining desk` ("Ruth's desk") | dining heater plug | dining room thermometer | yes — desk plug power |
| Dining room | `dining room` ("Dining room") | dining TRV | dining room thermometer | no |
| Den | `games desk` ("Ste's desk") | games heater plug | desk thermometer (`sensor.sonoff_snzb_02d_temperature`) | yes — desk plug power |
| Den | `den` ("Den") | the two den radiator TRVs (`climate.den_radiator_1_thermostat`, `_2_`; paired 2026-10-18) | den thermometer (`sensor.den_climate_temperature`, since 2026-10-19) | yes — TV plug power (since 2026-10-19) |

Which zones sit together, and in what order, is the **layout**: an ordered tree saved as `layout` next to the
room list, whose nodes are rooms (`{ roomId }`) or groups (`{ id, name, icon, children }`), nested to any
depth ("Downstairs" can hold the Dining room group, a Kitchen, and so on). It is **purely presentation**: the
heating code never reads it, and every room is scheduled, controlled and reported on its own wherever it
sits. The room list stays flat, and a room the layout does not mention appears at the end of the top level, so
the two cannot get stuck out of step. The UI is [RoomGroupCard](Frontend/src/components/RoomGroupCard.vue) and
the recursive [LayoutNode](Frontend/src/components/LayoutNode.vue); all the tree rules live in
[roomLayout.js](Frontend/src/utils/roomLayout.js) with tests. Rooms also carry an optional `icon` (an emoji).

Four consequences worth knowing before changing any of this:

- **Presence comes from plugs, and only some zones have it.** The desks use their desk
  plug (on, and ≥ 40 W within the last 5 minutes); the `den` uses its TV plug's **power
  alone** (≥ 30 W within 5 minutes — the threshold the HA motion-lights automations use), since
  2026-10-19. The 5-minute look-back doubles as a hold after the draw stops. The
  `living room` (also since 2026-10-19) is in use when its stick lights plug is switched on,
  **or** its TV has drawn ≥ 30 W for the whole of the last 5 seconds — no
  hold, as specified by the owner. The TV plugs' on/off switches are deliberately not read (owner:
  only the draw matters, and a switched-off plug reads 0 W), so each TV has one presence device, its
  power sensor; the desks still check their switch. The stick lights plug follows the WiZ lamp through two HA
  automations, so living room presence inherits that lamp's detection lag (HOME-ASSISTANT.md
  gotcha 15). Heating re-evaluates 6 s after either TV's power crosses 30 W. The dining room
  radiator zone stays presence-blind, because a person at Ruth's desk is not the whole room
  in use. `IsRoomInUse` returns `false` for any name it doesn't recognise, so a `RoomInUse`
  track on a presence-blind zone silently never fires, and a `RoomNotInUse` track always
  fires. That asymmetry is why the dining radiator took only the presence-free baseline
  tracks in the split. Neither the den's nor the living room's live tracks used a room
  condition when they gained presence, so nothing they did changed. All the rules live in
  [PresenceService.cs](Backend/App/Services/PresenceService.cs), pinned by
  [PresenceServiceTests](Backend/Tests/PresenceServiceTests.cs); the heating tests all use a
  mock and never exercise them.
- `"games room"` no longer exists — it was only ever the desk's plug and thermometer, so
  it became `games desk`. `den` drives both den radiators and, since 2026-10-19, reads its
  own room thermometer; until then it borrowed the games desk's, which read ~0.6 °C warmer
  than the room at pairing. Before the radiators were fitted its arm was deliberately empty so
  it read as "no controls".
- [RoomZoneBindingTests.cs](Backend/Tests/RoomZoneBindingTests.cs) pins each zone to the
  hardware it owns, including that an unknown name binds to nothing. If you rename a zone
  and those fail, that is the silent unbinding being caught for once.
- **The UI's device list comes from the same switches.** `GetDevicesForRoom` in `HeatingControlService`
  reads `GetThermometerForRoom`, `GetSwitchesForRoom` and `IPresenceService.GetPresenceDevices` and
  publishes the result as `devices` on each room state, which the web UI shows behind a "Devices" link.
  So adding a zone's hardware to those switches is enough for it to appear; never keep a second copy of
  the wiring in the frontend. A zone that has lost its hardware shows an empty list there.
  A zone's switches also decide its capabilities: with none it cannot set a temperature, and the UI hides its
  boost, schedules and share link. The upstairs bathroom is such a zone (thermometer only). It used to have a
  `DummySwitchEntity` so it could be scheduled, which offered controls that did nothing; that was removed on
  2026-10-19. Never give a zone a dummy device to make it look controllable.

Four more heating notes:

- **Two temperature sources per room.** Schedules and the UI read the standalone room
  thermometer (`sensor.*`); boiler verification reads the TRV's own onboard sensor
  (`climate.*` → `Attributes.CurrentTemperature`). They are different numbers from
  different devices. Don't assume one when you mean the other.
- **The boiler has no feedback.** It is switched by a Tuya fingerbot pressing a physical
  button (`switch.tz3210_j4pdtz9v_ts0001`), so the app's `_boilerOn` is a belief, not a
  reading. `BoilerVerificationService` infers success from radiator temperature changes.
  Since 2026-10-19 the belief is meant to survive restarts (`boiler-state.json` in the local cache folder,
  reloaded only at startup, never after coming back from the boiler thermostat). **On the host it did not
  at first:** the cache was in the user profile inside the add-on's container, which the Supervisor recreates
  on every add-on restart, so the app forgot it had turned the boiler on and never pressed it off. Since
  then `LocalStorageFolder` uses the add-on's persistent `/data/HomeAssistant` when it runs as an add-on,
  and logs the path at startup ("Boiler belief is kept in ..." in the NetDaemon add-on log). A fingerbot
  movement the app did not command (HA UI, a script) flips it; a press is refused while the
  fingerbot is unavailable; and after one corrective press a second disagreement is logged, not
  pressed. Before that fix the turn-on verification could never arm in the running app, because
  valves were reported as heating before the press, which wiped the "shut since" time it needs.
  A press by hand on the boiler itself is still invisible. Until 2026-10-19 `NotifyValveStopped` also
  overwrote a shut valve's "shut since" time on every 5-minute cycle, so a valve could never count as shut for
  the 30 minutes the turn-on verification wants. It now keeps the first time. [HeatingBoilerControlTests](Backend/Tests/HeatingBoilerControlTests.cs)
  drives all of this through `HeatingControlService` with the real verification service.
- **The app isn't the only thing that can run the boiler, and it has to be told which.** A
  physical switch at the boiler chooses between the fingerbot and the **boiler thermostat**
  (the boiler's own thermostat: portable, normally in the kitchen; called "external scheduler" or
  "kitchen scheduler" in older notes). The two are exclusive: in boiler-thermostat mode the
  boiler's button is **dead**, so fingerbot presses are no-ops. Neither the switch nor the
  boiler thermostat exists in HA (verified 2026-10-17 — the only `climate.*` entities are the five
  TRVs), so nothing can *read* which mode the house is in. Since 2026-10-17 it is
  configuration instead: `heating.boilerControlPath` in the **device-settings** blob, stored as a
  number (0 = `BoilerThermostat`, the default; 1 = `Fingerbot`), set from the web UI under
  **Settings → Boiler control** on the heating page and delivered by the existing
  `device-settings-changed` SignalR event. In boiler-thermostat mode `HeatingControlService`
  makes no fingerbot call, holds no `_boilerOn` belief, runs no verification and issues no
  corrective press; rooms are still controlled normally. **On 2026-10-17 the switch was on the
  boiler thermostat and the setting matched it. By 2026-10-19 production's setting read `fingerbot`
  (`boilerControlPath: 1`, read from the API), so the app now drives the boiler.** Whether the
  physical switch was flipped to match was not confirmed. The setting is a claim, not a reading — if someone flips the switch
  without changing it the app is wrong again and cannot tell. Never conclude from radiator
  temperatures that a press worked. See [HOME-ASSISTANT.md](HOME-ASSISTANT.md) → gotcha 13
  and the boiler fingerbot device note.
- **In fingerbot mode the boiler follows the rooms with radiator valves (since 2026-10-18, reworked
  2026-10-19).** Only valves whose device rules allow them count; heater-plug rooms (kitchen, desks) have no
  valve and never involve the boiler. Temperatures are the room thermometer's, not the TRV's own sensor.
  - **Calling for heat.** A valve room starts calling once it is `HysteresisOffset` (0.2 °C) below its
    target, the point at which its valve would open, and stops when it reaches its target
    (`_roomsCallingForHeat`).
  - **On** needs at least one room calling, and one of: a total deficit ≥ 3 °C (`BoilerDeficitThresholdCelsius`,
    inclusive); a calling room with a running boost; or a calling room that is **in use**, meaning its active
    track has the "room in use" condition, which is only selected while its presence says someone is there
    (owner, 2026-10-19). The in-use rule came from the den: its TV was on and its 21 °C in-use track active, but
    1 °C short on its own it never started the boiler. A room short of an ordinary track still waits for the 3 °C. The deficit is the sum of target − current over rooms
    below target; a room that is too hot adds nothing and does not offset a cold one. Boosts include owner
    zone and group boosts and guest QR overrides. A boost sets the target for every device in its zone, and
    heater plugs follow it whenever their rules allow (`ABoostRunsAHeaterPlug_OnlyIfItsRulesAllow`).
  - **Off** once no room is calling. Until 2026-10-19 it was "total deficit is 0", so Bedroom 3 at 17.9 °C
    against 18 °C, too little to open its valve, held the boiler on with every valve shut. That was found live
    after a boost was cancelled.
  - **Valves move only when there may be hot water behind them.** While the boiler is believed off, valves
    are neither opened nor shut (`_valvesWaitForBoiler`): their position makes no difference, and each move
    costs TRV battery. They are brought into line in the pass that turns the boiler on. The one exception is
    evidence: a turn-off verification that finds radiators still hot sets `_boilerMayBeRunning`, and valves
    then follow their rooms (so warm rooms' valves shut) until the app next turns the boiler on or off itself.
    While waiting, a valve room reports `heatingActive: false` even if its valve was left open.
  - **Order inside `EvaluateAllSchedules`:**
    1. `UpdateBoilerControlPathAsync`: the mode, the saved belief, and presses made outside the app.
    2. `ResolveTargetAsync` for every room: what each room wants, before anything moves.
    3. `DecideBoiler`: verification corrections, then on/off presses.
    4. The rooms' devices.
    5. `RecordValvesAndVerifyAsync`: a valve only counts as heating while open *and* the boiler is believed on.
  - In boiler-thermostat mode none of this applies, and valves follow their rooms as before, since the boiler
    thermostat may be running the boiler.
  - **A room switched in a pass is not enforced again in that pass.** HA reports a device's new state only a
    moment after the command, so `EnforceDeviceRulesAsync` straight after a switch read the old states and sent
    every command twice (seen 2026-10-19 on the kitchen plug: "Turning on", then "turned on by its device rules").
    For a TRV each duplicate is battery. `_roomsSwitchedThisPass` prevents it; `SwitchingAZoneOn_SendsEachDeviceOneCommand`
    pins it with a switch that reports late.
  - Deployed 2026-10-19: the threshold in `20261019112544`; the boost rule and the first version of valve
    waiting in `20261019114902`. The calling/off rule and leaving valves alone in both directions are in code,
    not yet deployed.
- **Every heating device can carry rules, and a device runs only while its zone wants heat AND its
  rules pass (since 2026-10-19).** Rules are per device — heater plug or radiator valve — keyed by entity ID
  in the **device-settings** blob, `heating.deviceRules`: time windows (days + `HH:mm` start/end; end ≤
  start runs past midnight and belongs to the start day; none = any time) AND a presence rule (sources
  combined Any/All; none = no presence needed). A source is a presence zone's canonical name or `house` =
  any presence zone in use (games desk, dining desk, den, living room — never motion sensors, the cat sets
  them off). Evaluated by [DeviceRulesEvaluator](Backend/App/Services/Climate/DeviceRulesEvaluator.cs),
  once-per-pass presence snapshot in `HeatingControlService.PrepareDeviceRulesAsync`, enforced both ways by
  `EnforceDeviceRulesAsync`: a device whose rules stop passing is **switched off** even if the room is still
  cold (owner's choice), and back on if they pass again while the zone still wants heat. A boost does not
  bypass rules. Valves held off by their rules add no boiler demand. `deviceRules: null` (never saved) means
  the built-in [DefaultDeviceRules](Backend/App/Services/Climate/DefaultDeviceRules.cs): kitchen
  05:30–22:00 + House; Bedroom 1 08:00–08:30 and 21:00–21:15 + House; Ste's desk heater needs the games
  desk; Ruth's needs the dining desk; valves none. Once anything is saved the blob is authoritative and a
  missing entry means no rules — so the UI's first save writes every device's defaults plus the edit.
  Unreadable settings fall back to the defaults. Edited from each zone's **Devices** list → **Rules**
  ([DeviceRulesModal](Frontend/src/components/DeviceRulesModal.vue)). This replaced the hard-coded
  `HeaterConditions`/`CustomSwitchWithConditions` gate (which only blocked turning on) and the desk-only
  `HousePresenceSensor`. **Deploy the Functions app first**: its `DeviceSettingsDto` copy must know
  `deviceRules` or a save drops it. Tests: `DeviceRulesEvaluatorTests`, `DeviceRulesHeatingTests`; the
  schedule-selection tests in `HeatingControlServiceTests` run with empty saved rules on purpose.
- **Boost expiry compares instants, never clock faces (fixed 2026-10-17).** A boost's end is a
  `DateTimeOffset` (the UI sends UTC) while the evaluation's `now` is the *local* wall clock, so comparing
  `EndTime.DateTime` to `now` was wrong by the UTC offset: in British Summer Time every boost ended an hour
  early and any boost shorter than an hour never ran at all. It now compares against
  `_timeProvider.GetUtcNow()`. [BoostExpiryTests](Backend/Tests/BoostExpiryTests.cs) uses a London clock for
  this; the shared `FakeTimeProvider` is hard-wired to GMT and can never show a summer-time bug, so do not
  use it for anything that depends on local time.

## In-flight work

- `/heating2` and `Frontend/src/components/heating2/` are a **superseded UI**, slated for
  removal. The final heating UI is the original room-card view (`HeatingView.vue` +
  `RoomCard.vue`).
- The newer heating config engine (`HeatingConfigDto`, `heating-config` container,
  `HeatingConfigFunctions`) is **shipped dark**: the Functions app stores and serves it,
  but nothing in `Backend/App` references it yet. The legacy schedule stack is still what
  actually runs the house.

## Access: the house ID is the only credential

Every Functions endpoint is `AuthorizationLevel.Anonymous` and keyed by `houseId`, so **anyone with the house
ID can read and change everything** — schedules, device rules, device settings. The web app keeps it in a
cookie. Two consequences:

- **Guests use QR links instead (since 2026-10-19).** Each zone, and each layout group, can have one share
  token (Share on the zone's or group's card). The `/r/<token>` page and the `guest/*` endpoints let the holder
  set, change or cancel an override on that zone — or on every zone in that group, at any depth — within
  16–24 °C and ≤ 6 h, stored as those zones' boosts, and never see the house ID. Revoking issues a new token and
  kills the old QR; a link to a deleted group stops working. Never put the house ID in a guest-facing page, link
  or response, and never let a `guest/*` endpoint save anything but those zones' boosts — `GuestOverrideTests`
  pins that.
- **Every boost sets the exact temperature, up or down** (owner, 2026-10-19, after briefly trying "raise-only"):
  an owner's zone boost, an owner's group boost (the ⚡ on a group card — the same boost on every zone inside),
  and a guest's override all replace the schedule until they end, and boosting again changes a running one.
  Per-device rules still decide whether each heater or valve may actually run.
- **A group's boost locks everything inside it** (owner, 2026-10-19). A group boost is stored as the same boost on
  every zone inside, each tagged `boost.groupId`. While it runs, those zones and any smaller groups inside cannot
  be boosted, changed or cancelled on their own, by the owner or a guest: only that group can, or a group around
  it, whose boost supersedes it by overwriting every zone inside. Cancelling a group clears only boosts tagged
  with it. The rules are `utils/boosts.js` in the UI and `GuestOverride.LockedBy` on the server, which refuses a
  locked guest change with 409. The owner endpoints still trust the house ID, so the owner's lock is UI-only.
  Before the tag existed a group boost was indistinguishable from identical zone boosts, and cancelling one zone
  inside a boosted group left the group and the zone disagreeing.
- **The production house ID is short and guessable** (seven digits). That makes the owner endpoints
  effectively public to anyone who tries. Not fixed by the QR work; replacing it with a long random ID (or
  real auth) is an open follow-up.

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
