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

`Backend/HomeAssistant.Functions/local.settings.json` points `ScheduleStorageConnectionString` at a **real
Azure storage account** and `AzureSignalRConnectionString` at the **live SignalR service**. Running the
Functions project from its own folder reads and writes real Azure data, separated only by the house ID. That
account is **not** the one production uses, though: it is `personalprojectsa4ee`, while the production Functions app's
own setting names `homeassistantprodstorage` (read from the app's settings with `az`, 2026-10-21, after a backfill
written with the local connection string never appeared in production). For anything that must reach production's
data, take the connection string from the Function app's settings, as `tools/ha/boiler-history-backfill.cjs` does. The NetDaemon app has two matching traps: it loads its `appsettings.*development*.json` files *after*
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
NetDaemon app as "Supervisor", or the device itself), add-on state, logs and restarts (`addon.cjs`), running the
monthly valve exercise now (`valve-exercise.cjs`),
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
| Dining room | `dining desk` ("Ruth's desk") | dining heater plug | dining room thermometer | yes — desk plug on and ≥ 30 W for 5 s (since 2026-10-19) |
| Dining room | `dining room` ("Dining room") | dining TRV | dining room thermometer | yes — Ruth's desk plug switched on (since 2026-10-19; not part of "house") |
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

- **Presence comes from fixed detectors, and which ones count for each zone is configurable** (owner,
  2026-10-19). The detectors, each a way of telling from plugs that something is in use with its own wattage
  and hold, are defined in code in `PresenceService`. Each zone's choice of them is saved in device settings
  as `heating.zoneOccupancy` (zone canonical name → detector ids), edited from the zone's **Devices** list →
  **Occupancy** ([ZoneOccupancyModal](Frontend/src/components/ZoneOccupancyModal.vue)). Since 2026-10-21 they are
  also shown where they matter (owner): the schedule editor lists them, each with whether it is on now, under its
  room-occupancy buttons, and every track with a "room in use" or "not in use" condition lists them too
  (`zoneDetectors` in `utils/zoneOccupancy.js`; the track uses the app's reported choice, the editor the saved one).
  Each room state carries `waitingForOccupancyTrackIds`: tracks due now (started today, for the house's Home/Away
  mode, no earlier than the active track) that only their room condition holds back
  (`FindTracksWaitingForOccupancyAsync`), and the track says "Due now, but not running" with why. A track that cannot run
  now is greyed out but still opens the editor (owner, 2026-10-21): one not set for today (`utils/trackDays.js`, with
  `useToday` keeping "today" current past midnight), or one whose room condition is not met right now
  (`roomConditionMet`). Never the active track, which just after midnight can be yesterday's last one carried over. A track's
  "house occupied/unoccupied" flags (1 and 2) are **not** presence: they are the Home/Away (plane) switch in the
  page header (`HouseOccupancyState` in the schedules blob). Each phone detector is listed in a zone's Devices list as
  the phone itself (role `phone`, entity ID `phone.ste` / `phone.ruth` / `phone.ellie`, the app's own, state
  "home"/"away"; `PresenceService.DescribeOwnDevice`), not as the router's whole-house online list it is read from,
  which apps before 2026-10-21 showed as "Router". A zone is in use while
  any of its detectors is. `null` means the built-in defaults (`DefaultZoneDetectors`), which reproduce
  exactly what follows. Once saved the map is authoritative, so the UI's first save writes every zone's
  current detectors plus the edit, and a zone it leaves out has no presence. "House" is any detector that
  shows someone is there (`MeansSomeoneHome`), whichever zone it belongs to. Each room state carries its
  `occupancy` (its detector ids, whether default, and every detector with whether it is active now). Every
  detector is checked once per pass (`GetSnapshotAsync`), and both schedule conditions and device rules read
  that snapshot. Adding a *new kind* of detector is still a code change. **Phones** (owner, 2026-10-20): "Ste's
  phone on the home Wi-Fi", "Ruth's" and "Ellie's" are active while the router's online list
  (`sensor.mesh_online_devices` → `devices`, from the Linksys Velop integration) holds that phone's router id.
  They do not count towards "house" and no zone uses them by default. They read the list, not the integration's
  device trackers, which are blind to a phone on a secondary Velop unit. Ruth's phone is in flight mode
  overnight, so hers reads "away" while she sleeps. A phone that gets a new random Wi-Fi address shows up as a
  new router device, and its detector silently reads "away" until the id in `PresenceService` is updated (see
  HOME-ASSISTANT.md → router note for finding it). The built-in defaults:
  Ste's desk (`games desk`) uses its desk
  plug (on, and ≥ 40 W within the last 5 minutes). **Ruth's desk plug gives two detectors** (owner,
  2026-10-19): `dining desk` is in use while the plug is on **and** has drawn ≥ 30 W for the whole of the last
  5 s, with no hold (the desk is really being used, which her heater's default rule needs); `dining room` is
  in use while the plug is merely **switched on**, so the radiator can have a track for "the desk is set up".
  The second is left out of `house` (`IPresenceService.HouseZones`), because a plug left on says nobody is
  home. The `den` uses its TV plug's **power
  alone**, since 2026-10-19, through a **latch with two thresholds** (`PowerLatch`; owner, 2026-10-22): the TV is
  on once it draws ≥ 100 W and stays on until it is down to ≤ 30 W, whatever it does in between. The HA
  motion-lights automations use the same pair. Neither threshold works alone: at night the plug draws 35–80 W on
  its own (about 40 W for a minute or two most hours, 55–80 W for 35 minutes around 3 am), which with the old
  single 30 W made the den "in use" and started the boiler; and watching YouTube draws anywhere from 73 to 160 W
  as the picture changes, which with a single 100 W flipped the motion lights every few seconds (both seen on
  2026-10-22). At startup the latch replays the plug's last 10 minutes from the recorder, so it starts as it really
  was: without that, the 06:48 restart on 2026-10-22 came up during a dark scene (62–77 W), read the den as empty,
  and turned the boiler off with someone watching. "Den TV on recently" is the latch on, or off for less than 5 minutes: the hold, for the heating,
  and it does **not** count towards "house" (owner: a TV that was on a
  few minutes ago does not mean anyone is home). "Den TV on now" (the latch on for 5 s, no
  hold) does count towards "house", and matches the motion lights' rule; they follow it through their own HA
  automations, not the app. The
  `living room` (also since 2026-10-19) is in use when its stick lights plug is switched on,
  **or** its TV has drawn ≥ 30 W for the whole of the last 5 seconds — no
  hold, as specified by the owner. The TV plugs' on/off switches are deliberately not read (owner:
  only the draw matters, and a switched-off plug reads 0 W), so each TV has one presence device, its
  power sensor; the desks still check their switch. The stick lights plug follows the WiZ lamp through two HA
  automations, so living room presence inherits that lamp's detection lag (HOME-ASSISTANT.md
  gotcha 15). Heating re-evaluates 6 s after the den TV's power crosses 100 W or 30 W, or the living room TV's or Ruth's desk's crosses 30 W. Every "for 5
  seconds" detector (Ruth's desk, den TV on now, living room TV) is a `SustainedCondition` fed by live
  readings, not a history query: the history API lags a few seconds, which made Ruth's heater flap on a
  pulsing load on 2026-10-20 (HOME-ASSISTANT.md gotcha 21). Ruth's also requires the plug to be on in the
  same condition, since an off plug keeps its last power reading. After an app restart a steady draw counts
  as "on now" only 5 s later.
  `IsRoomInUse` returns `false` for any name it doesn't recognise, so a `RoomInUse`
  track on a presence-blind zone silently never fires, and a `RoomNotInUse` track always
  fires. That asymmetry is why the dining radiator took only the presence-free baseline
  tracks in the split. Neither the den's, the living room's nor the dining room's live tracks used a
  room condition when they gained presence, so nothing they did changed. All the rules live in
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
  boost, schedules and share link. The upstairs bathroom used to have a `DummySwitchEntity` so it could be
  scheduled, which offered controls that did nothing; that was removed on 2026-10-19. Never give a zone a dummy
  device to make it look controllable. **Since 2026-10-20 it has a real one instead: its towel rail** (`TowelRail`),
  which sits on the boiler's circuit with no valve or switch, so it is hot whenever the boiler runs (owner). It is
  listed with the zone's switches, so the bathroom can be scheduled and boosted; nothing is ever sent to it, and it
  counts in the boiler decision as a radiator that is always open (below). It is not in HA, so its `devices` entry
  (role `towelRail`) has the app's own ID, and its state is the boiler belief in fingerbot mode, null otherwise.

**The hallway** (since 2026-10-20) is a zone of its own that is only a thermometer: `hallway` reads `sensor.hallway_climate_temperature`
(`HallwayClimateTemperature`) and has no switches, so it reports a temperature and nothing else (no boost, schedules or share link). Like every zone it exists only once a room named "Hallway" is in the schedules blob and the layout; the heating page cannot add rooms, so that is done with `LocalEnv/add-room.mjs` (dry run first; production needs `--apply --i-mean-production`), after the app that knows the name is deployed. It was added to production on 2026-10-20 as room 11 in the Downstairs group (`g3`), with no icon.

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
  fingerbot is unavailable. A **corrective press**, either way, is checked and pressed again if it did not work, up to
  5 in a row, the later ones further apart in case the trouble is the network (owner, 2026-10-23: the app must be
  certain the boiler did what it was told; until then a second disagreement was only logged). `CorrectionDelays`: the
  first at once, then 5, 15, 30 and 60 minutes after each failed check (each check itself takes up to 60–90 minutes).
  After the fifth the app stops, logs an error and raises an HA persistent notification ("Boiler may still be running"
  or "Boiler may not be running"): presses that keep failing mean something the fingerbot cannot fix, such as the
  switch at the boiler being on the boiler thermostat. While a corrective press waits, or after giving up, the boiler
  is evidently not where the app believes it (`BeliefStillContradicted`), so the app never presses to change the belief,
  which would do the opposite: a room wanting heat with the boiler believed off but evidently running takes it as
  already on, and no room calling with it believed on but evidently not running takes it as already off. Either is
  checked like a press, and the waiting press dropped; the valve exercise is skipped. A fingerbot movement or a change
  to the boiler thermostat drops it too. Before that fix the turn-on verification could never arm in the running app, because
  valves were reported as heating before the press, which wiped the "shut since" time it needs.
  A press by hand on the boiler itself is still invisible. **A press counts only once HA has confirmed it** (since
  2026-10-21): `PressBoilerFingerbotAsync` waits for HA's answer, and on an error (on 2026-10-21 "device did not
  respond", HOME-ASSISTANT.md gotcha 24) it leaves the belief alone and decides the boiler again 1 then 2 minutes
  later. Until then a press was fire-and-forget, so the belief went to ON with the fingerbot unmoved and the next
  "off" pressed it on. (The 2026-10-21 version of this did not actually wait, see Conventions; fixed 2026-10-22.)
  **A press HA reports as failed can still land** (2026-10-23, HOME-ASSISTANT.md gotcha 27: "The operation has
  timed out", and the fingerbot moved 11 s later). Since then a movement the way that press asked within 50 s
  (`LatePressWindow`, under the first 1-minute retry) is taken as the app's own press, logged "landed after all";
  a later one is still a press from outside the app. Either way the belief follows the fingerbot and is checked
  against the radiators. Until then it was taken as an outside press, which also wiped the verification's valve
  history (`Reset`; it now calls `DiscardPendingVerification`), so a movement to "off" could never arm a turn-off check.
  Even a confirmed press is only the fingerbot's word: the radiator check below is what decides. The UI shows the belief in fingerbot mode ("Boiler
  on since …" at the top of the heating page, which opens the **boiler history** chart since 2026-10-21; the Functions
  app records each change of belief from the room-state uploads in the `boilerhistory` table, and the week before that
  was backfilled, marked inferred, by `tools/ha/boiler-history-backfill.cjs` from the valves warming and cooling; owner, 2026-10-19): the app sends it as `boiler` on every
  room-state upload (`RoomStatesResponse.Boiler`, uploaded whenever the belief or mode changes, and when a
  valve exercise starts). Until 2026-10-19 `NotifyValveStopped` also
  overwrote a shut valve's "shut since" time on every 5-minute cycle, so a valve could never count as shut for
  the 30 minutes the turn-on verification wants. It now keeps the first time. [HeatingBoilerControlTests](Backend/Tests/HeatingBoilerControlTests.cs)
  drives all of this through `HeatingControlService` with the real verification service.
- **A failed verification presses the boiler, so a wrong one inverts the belief** (HOME-ASSISTANT.md gotcha 23:
  on 2026-10-20 two false turn-on failures each pressed a boiler that was already right, and a false turn-off pass
  let it run all night while the UI said off). Since 2026-10-21 a failure needs strong evidence, and anything
  weaker clears the verification without a press. All of it reads the TRVs' own sensors, which lag:
  - **Only valves open after the pass are watched.** The verification is armed in `RecordValvesAndVerifyAsync`,
    after the rooms have moved their valves (`_pressToVerify`), with each valve's state taken from the command
    sent this pass where there was one (`IsValveOpenAfterPass`), since HA reports a new setpoint late. Until then it
    was armed in `DecideBoiler` with the valves open *before* the pass, and watched a dining valve that pass shut.
  - **Turn-on** passes as soon as any watched TRV has risen 1 °C at any check, and fails only if none has within
    60 minutes (`TurnOnDeadlineMinutes`; it was one check at 15, and real TRVs showed +0.5 at 16 minutes and a
    first rise at 28). A watched valve the app shuts stops counting. Since 2026-10-22 it never gives up: it is
    armed even with no cold radiator open, a cold radiator whose valve opens later is watched from then (its hour
    runs from when it opened), and it fails once any watched valve has gone its hour without any rising. Until
    then it gave up when its valves were shut, and the 02:42 press that never reached the boiler went unnoticed
    for three hours while Bedroom 3's open valve cooled. A fingerbot movement the app did not make is checked the
    same way: it still flips the belief, but no longer cancels the check, since the switch's word is not proof.
    **Warm radiators are watched too** (owner, 2026-10-22, after a 06:23 press with only warm den radiators open
    could not be judged at all): every open valve counts, and one whose radiator had hot water in the last half
    hour is "warm". Any watched radiator rising 1 °C passes the press (den radiator 2, warm at 23.2 °C, read 24.4
    twelve minutes after a real turn-on; a radiator whose hot water has stopped only coasts up about 0.5 °C). A warm
    one is not held to the hour; it fails the press only by **cooling** 1 °C below its highest reading, after 30
    minutes, while that highest reading stayed at least 1 °C under its own setpoint, since a TRV at its setpoint
    throttles and cools between cycles even on a running boiler (`WarmRadiator*` in `BoilerVerificationService`).
  - **Turn-off** fails only when a watched open valve's TRV **heats up again** after the 30-minute settling: 1 °C
    above its lowest reading and back to at least its reading at the turn-off. That is a TRV regulating on hot
    water, so the boiler is still running. Since late 2026-10-21 it also fails when a watched TRV **never stopped
    climbing**: once settling is over it is still at its highest since the turn-off and 1 °C or more above it (den
    radiator 2 went 20.0 → 23.5 °C through settling that night and had no low to come back up from). Water left in a
    radiator peaks and falls within the settling, so it does not count. **Every open valve is watched** (since
    2026-10-23): until then only those whose radiator had had hot water for 30 minutes were, so a turn-off within half
    an hour of a turn-on went unchecked, and on 2026-10-23 the boiler ran on for over an hour after a turn-off 14 minutes
    after a turn-on (HOME-ASSISTANT.md gotcha 27). A radiator with less than 30 minutes of hot water (or no history,
    after a restart) read close to its room at the turn-off, so it fails the press only by climbing to a new high a
    degree above that reading; heating up again from its low is not enough, since a heater plug or the sun can lift a
    near-room-temperature TRV past its turn-off reading. A radiator merely falling is not taken as proof of anything, because an
    open TRV on a running boiler falls between cycles (Bedroom 3, 21.5–23 °C all night), and one that stays warm is
    not a failure any more either. Open valves are the only useful ones to watch: a shut valve's radiator cools
    whether the boiler runs or not, so ignoring or shutting the open ones would leave nothing that could ever
    show the boiler running.
  - **Every press has a valve open to check against** (owner, 2026-10-23), turn-on and turn-off alike. If no valve is
    open after the pass (say only the towel rail called for heat, which has no sensor), the app opens one (setpoint
    reading + 5), preferring one its device rules allow. For a **turn-off**, the radiator that reads warmest: it had hot
    water most recently, so with the boiler off it can only cool, and nothing else warming the room can lift it to a new
    high. For a **turn-on**, the coldest, which shows the clearest rise. One open valve, chosen the same way, is then
    **held** for the whole check (`_valveHeldForBoilerCheck`, `IsHeldForBoilerCheck`): its room does not move it, so it
    cannot be shut and leave nothing to watch (after a failed turn-off check, when valves follow their rooms and warm
    rooms' valves shut; or when its room warms before a turn-on has shown). Its setpoint is kept at least 2 °C above its
    reading (`HeldValveHeadroomCelsius`), so its TRV cannot throttle at its setpoint and stop climbing, which used to be
    the turn-off check's one blind spot. The hold ends when the check does, or when the belief changes. A held room may
    overshoot its target a little while the check runs; a turn-on usually shows within half an hour. "The turn-OFF
    cannot be checked" (Warning) now means no valve has a reading at all.
  Pinned by the 2026-10-20 and 2026-10-23 cases in `BoilerVerificationServiceTests` and `HeatingBoilerControlTests`.
  The 2026-10-23 changes were not deployed when written; check the NetDaemon app's version before assuming them live.
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
  - **The upstairs bathroom counts too (since 2026-10-20)**, through its towel rail: a radiator with no valve,
    so always open. It calls for heat, adds to the deficit and starts the boiler on a boost exactly like a valve
    room, with a radiator's default 30-minute pre-heat. It has no rules, no verification (no TRV to read) and
    nothing to wait for the boiler. Because it cannot be shut it is also hot whenever the boiler runs for another
    room, and reports `heatingActive` whenever the app believes the boiler on.
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
    costs TRV battery. They are brought into line in the pass that turns the boiler on. There are two exceptions.
    Evidence: a turn-off verification that sees an open radiator heat up again sets `_boilerMayBeRunning`, and valves
    then follow their rooms (so warm rooms' valves shut) until the app next turns the boiler on or off itself. And the
    check of a press (since 2026-10-23): the valve it holds open is kept open with headroom, and one is opened if none is.
    While waiting, a valve room reports `heatingActive: false` even if its valve was left open.
  - **Order inside `EvaluateAllSchedules`:**
    1. `UpdateBoilerControlPathAsync`: the mode, the saved belief, and presses made outside the app.
    2. `ResolveTargetAsync` for every room: what each room wants, before anything moves.
    3. `DecideBoiler`: verification corrections, then on/off presses (each press only noted in `_pressToVerify`).
       For a turn-off to check, the warmest open valve is then chosen to be held open through the pass (for a
       turn-on, the valve to hold is chosen after the rooms have moved theirs).
    4. The rooms' devices, each decided on its own in `EvaluateSchedule` (see "Each device decides for itself" below).
    5. `RecordValvesAndVerifyAsync`: arms the verification of this pass's press with the valves open after the
       pass (opening one first if none is), then records valves (one only counts as heating while
       open *and* the boiler is believed on) and checks.
  - In boiler-thermostat mode none of this applies, and valves follow their rooms as before, since the boiler
    thermostat may be running the boiler.
  - **Monthly valve exercise** (owner, 2026-10-19, after finding a TRV seized from months without moving):
    08:00 on the 1st (`ValveExerciseCron`), or on demand with `tools/ha/valve-exercise.cjs` (an HA event). If the
    boiler is believed on, the heating is evidently working and that month is skipped. Otherwise the boiler is
    pressed on and every TRV flipped (open to shut, shut to open); after 2½ minutes each is put back to its exact
    setpoint, and at 5 minutes the boiler goes off unless the rooms would have turned it on themselves
    (`_boilerOnForExercise` in `DecideBoiler`). Normal evaluations are skipped while it runs, and the turn-on
    verification is reset, since a 5-minute burn would fail it and provoke a corrective press. In
    boiler-thermostat mode only the valves are exercised. [HeatingControlService.ValveExercise.cs](Backend/App/Services/Climate/HeatingControlService.ValveExercise.cs)
  - **One command per device per pass.** HA reports a device's new state only a moment after the command, so
    anything that reads a device again in the same pass sees the old state and sends the command again (seen
    2026-10-19 on the kitchen plug, when a separate rules pass followed the room's switch). Each device is now
    decided once per pass; `SwitchingAZoneOn_SendsEachDeviceOneCommand` pins it with a switch that reports late.
  - Deployed 2026-10-19, in stages; everything above is live except per-device pre-heat (below) until it ships.
    The stricter boiler verification (2026-10-21, above) was not deployed when written; check the NetDaemon
    app's version before assuming it is live.
- **Every heating device can carry rules, and a device runs only while its zone wants heat AND its
  rules pass (since 2026-10-19).** Rules are per device — heater plug or radiator valve — keyed by entity ID
  in the **device-settings** blob, `heating.deviceRules`: time windows (days + `HH:mm` start/end; end ≤
  start runs past midnight and belongs to the start day; none = any time) AND a presence rule (sources
  combined Any/All; none = no presence needed). A source is a presence zone's canonical name or `house` =
  any of the zones that mean someone is home in use (games desk, dining desk, den, living room; not the
  dining room's "desk switched on"; never motion sensors, the cat sets them off). Each device also has a
  **pre-heat** (`PreHeatMinutes`, 0–60; owner, 2026-10-19): how long before a warmer track starts it begins
  heating for it. Unset means **Auto** for a radiator valve (owner, 2026-10-20) and none for a heater plug; a valve can
  still be given a fixed value. Since 2026-10-21 `PreHeatAuto` (bool?) says Auto or fixed outright, so a **heater plug
  can be Auto** too (owner); null keeps the default for the kind. An Auto heater with nothing measured has no pre-heat.
  Whatever the pre-heat, a device still only runs while its rules pass (`DeviceTarget` brings the target forward,
  `IsDeviceAllowed` still gates it; `AnAutoHeaterPlug_StillWaitsForItsRules`). It replaced the per-track `RampUpMinutes`, which is still stored but no longer read,
  because a radiator warms a room far more slowly than a fan heater.
  **Auto works the time out from how fast that valve has actually warmed its room.** [WarmUpRates](Backend/App/Services/Climate/WarmUpRates.cs)
  times each run of a valve being open with the boiler believed on, from the moment it began until the room's
  thermometer first reached the valve's target. A run counts only if that took 30 minutes or more and did reach the
  target (a run cut short by the boiler going off, or by the target moving, is dropped), and gives one sample; the
  rate is the mean °C/h of the last 5 per valve, kept in `warm-up-rates.json` in the local cache folder. Fingerbot
  mode only, since in boiler-thermostat mode nothing says when the boiler runs. **Heater plugs are measured too**
  (since 2026-10-21): a run is the plug meant to be on, no boiler needed, and counts from **5 minutes**
  (`HeaterMinimumMinutes`, owner) rather than a radiator's 30. They publish the same fields, and their seed estimates
  (Ste's and Ruth's desk heaters; the kitchen and Bedroom 1 heaters had no usable runs) come from
  `warmup-history.cjs --only heaters --seed`, which merges into the file. Auto then starts the valve when
  (target − room now) ÷ rate, rounded up and at most 120 minutes, reaches back to the track; once started it carries
  on until the track begins (`_autoPreHeatTrackIds`), or a rate a little pessimistic would shut it again part-way as
  the room warmed. With no samples it uses 30 minutes. Since 2026-10-20 each valve starts with estimates from the recorder's history, shipped in the app as `Backend/App/warm-up-seed.json` (made by `tools/ha/warmup-history.cjs --seed`, rough: see that script's header) and merged with its own measurements by start time, so the valve's own warm-ups push them out as they come in. Regenerating the file and redeploying the app is how to refresh them. The rate is the room thermometer's, so it includes the radiator
  warming up, which is why a small deficit comes out a little under. Passes have to see the reading that reached the
  target: when that room was the last calling, the boiler is turned off in the same pass, so the observation counts the
  boiler as on if it was at the start of the pass (`_boilerWasOnAtStartOfPass`). Each valve and heater plug publishes `preHeatAuto`,
  `heatingRateCelsiusPerHour`, `heatingRateSamples` and `warmUpMinutes` (time to the next warmer track, from the
  room's temperature now) on its room-state device, and the UI shows the longest per zone. Evaluated by [DeviceRulesEvaluator](Backend/App/Services/Climate/DeviceRulesEvaluator.cs),
  once-per-pass presence snapshot in `HeatingControlService.PrepareDeviceRulesAsync`.
  **Each device decides for itself** in `EvaluateSchedule`: its own target (the room's, brought forward by its
  own pre-heat, so in one room a radiator can be pre-heating while the heater waits), its own 0.2 °C hysteresis
  (`_devicesWantingHeat`, not read from the device, since rules can hold a device off while it wants heat), then
  its rules. A device whose rules stop passing is **switched off** even if it still wants heat (owner's choice),
  and back on when they pass again. The room reports the highest target any of its devices is aiming at, and
  the boiler judges a room by its valves' targets, so a radiator's pre-heat calls for the boiler early. A boost does not
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

- **Every command the NetDaemon app sends is retried if HA answers with an error** (owner, 2026-10-21).
  `AddServiceCallRetries` in `program.cs` wraps NetDaemon's `IHaContext` in `RetryingHaContext`, so `TurnOn()`,
  `CallService` and generated entities all go through `ServiceCallRetrier`
  ([ServiceCallRetries.cs](Backend/App/Services/ServiceCallRetries.cs)): 30 s, 1, 2, 5, 10, 15 min, then it gives
  up with an error log. A newer command to the same entity cancels a pending retry. Anything where a repeat is
  not harmless (a toggle, like the boiler fingerbot) must use `IConfirmedServiceCaller` / `TurnOnConfirmedAsync`
  instead and handle the failure itself. It must be registered after `UseNetDaemonRuntime`, and throws at startup
  if it finds no `IHaContext` to wrap (`ServiceCallRetrierTests` builds the real host to check). **NetDaemon's own
  `CallServiceAsync` extension does not wait for HA's answer** (it only logs a failure), which made the first
  version of this, deployed 2026-10-21, retry nothing: commands go as our own `CallServiceMessage` through
  `SendCommandAndReturnResponseRawAsync`, which waits up to 20 s and throws on an error *or no answer*.
  `ServiceCallAgainstRealClientTests` drives NetDaemon's real websocket client against a stand-in HA to pin that;
  never test this layer only against a fake that throws.
- **No `var` in C#** — explicit types everywhere.
- **Sentence case** for all UI text: labels, buttons, modal titles, accordions.
  "Add new room", not "Add New Room".
- Modals: fixed overlay + centred panel, base `z-index` 1000. A modal opened from another
  modal goes 100 higher, passed in as a prop.
- Frontend composables are singletons holding reactive state, auto-persisted — cookies for
  small settings, `localStorage` for structured JSON.
- Keep the docs current as you learn — see **Write back what you learn** at the top of
  this file for which doc takes what.
