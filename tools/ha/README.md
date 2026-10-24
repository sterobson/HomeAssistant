# Home Assistant scripts

Small Node scripts for inspecting and setting up the live Home Assistant instance from a dev
machine. Read [HOME-ASSISTANT.md](../../HOME-ASSISTANT.md) first: these scripts already apply its
gotchas, but the results still need interpreting with them in mind.

Run them from the repo root. They need Node 20+ and the `ws` package from `Frontend/node_modules`
(`cd Frontend && npm install` once). The token is read from the git-ignored
`Backend/App/appsettings.secrets*.json` files and is never printed. Set `HA_HOST` to override
`homeassistant.local:8123`.

| Script | Writes? | What it answers |
|---|---|---|
| `entity-history.cjs <entity_id> [...] [--hours 48]` | no | Current state plus real transitions from the recorder. Use it for "when did this really fail / come back", never `last_changed` |
| `find-entities.cjs <regex>` | no | Entities whose ID **or friendly name** matches, with state and `last_updated` |
| `trv-status.cjs` | no | Whether each radiator TRV is heating or idle, with its setpoint and its own onboard reading |
| `trv-shut-leaks.cjs` | no | Each TRV's spells of its own sensor climbing 2 °C or more while its valve was shut (idle, not just after heating), from the recorder: hot water getting past a shut valve. Found den radiator 2, 2026-10-23 |
| `trv-calibration.cjs` | no | Each TRVZB's valve opening/closing degree and calibration limit voltages over ZHA. Slow (battery TRVs answer when they wake; a few minutes for all seven). A TRV that shuts properly reads closing > opening voltage |
| `list-devices.cjs [--model] [--name] [--area]` | no | Devices from the registry: manufacturer, model, area, and every entity with its live state, entity name and disabled flag |
| `list-devices.cjs --areas` | no | Area IDs, for `pair-zigbee.cjs --area` |
| `pair-zigbee.cjs --plan <device_id> --prefix ...` | no | Dry run: the renames a device would get under a naming scheme, and any clashes |
| `pair-zigbee.cjs --model ... --name ... --prefix ...` | **yes** | Opens the ZHA join window, waits for a new device of that model, then names the device and all its entities |
| `rename-device.cjs <device_id> --name ... --prefix ... [--platform wiz]` | no | Dry run: the renames an existing device would get, which entities of *other* integrations it leaves alone, and any clashes. Renames ZHA entities unless `--platform` names the device's own integration (`wiz` for a WiZ bulb; how the den tall lamp was named, 2026-10-23) |
| `rename-device.cjs <device_id> ... --apply` | **yes** | Renames an existing device and its own entities the house way (how the living room TV plug was renamed, 2026-10-19). Check what refers to the old IDs first — HOME-ASSISTANT.md gotcha 16 |
| `plug-startup.cjs <select entity_id>` | no | A smart plug's start-up behaviour (what it does when power returns after a cut) and the options it offers |
| `plug-startup.cjs <select entity_id> --set On` | **yes** | Changes it and reads it back. Moves nothing now; only the next power cut. The entity is `select.*_start_up_behaviour` or `*_power_on_behaviour`, depending on how ZHA named it. How the den sideboard lamp plug was set to On, 2026-10-23 |
| `put-automation.cjs --delete <id> --backup <file.json>` | **yes** | Saves an automation's config to a file, then deletes it from HA |
| `put-automation.cjs --check <file.json> [...]` | no | Whether each automation in `automations/` matches what HA has (key order ignored) |
| `put-automation.cjs <file.json> [...]` | **yes** | Creates or replaces those automations through HA's config API (the UI editor's), then reads them back |
| `who-changed.cjs <entity_id> [--hours 6]` | no | Each state change from HA's logbook with its cause: a user ("Supervisor" is the NetDaemon app; your account also covers these scripts, which use your token), an automation or script, or no context (the device reported it by itself) |
| `addon.cjs <slug>` | no | An add-on's state and version, through the Supervisor over the websocket (REST `/api/hassio` refuses the token for this) |
| `zigbee-mesh.cjs [--json]` | no | The Zigbee mesh from ZHA's neighbour tables: each router plug's link quality to the coordinator (both ways) and its best router neighbours, and each battery device's parent router with that link's LQI. Offline devices show when they were last seen |
| `addon.cjs <slug> log [regex] [--lines 80] [--from 100]` | no | The add-on's log, optionally only lines matching the regex. The Supervisor returns 100 journal lines unless asked for more; `--from 40000` reached back about two days across restarts (2026-10-21). REST `/api/hassio/addons/<slug>/logs` serves this one, while the websocket fails on it. `c6a2317c_netdaemon6 log "Boiler turned"` shows the app's boiler decisions and why |
| `boiler-history-backfill.cjs --house <id> [--days 7] [--runs] [--why]` | no | Dry run: when the boiler evidently ran over the last days, worked out from the radiator valves' own sensors climbing fast (hot water) and an open valve cooling (boiler off); `--why` shows the valves behind each run |
| `boiler-history-backfill.cjs --house <id> --apply` | **yes (Azure)** | Writes those runs, marked inferred, into the Functions app's `boilerhistory` table in the production storage account (connection string read from the production Function app with `az`, never printed), replacing any earlier inferred rows and stopping at the app's first own row. Done for 14–21 Oct 2026 |
| `warmup-history.cjs [--hours 240] [--min 30] [--heater-min 5] [--only valves\|heaters] [--runs]` | no | An **estimate** of how fast each radiator valve has warmed its room (°C an hour, averaged over its last 5 warm-ups), from the recorder. The history never recorded when the boiler was on, so it reads each episode from the valve's own temperature sensor (it climbs when hot water arrives, peaks, falls when the boiler goes off). Rough: good for order of magnitude and comparing radiators; the app's live measurement (`WarmUpRates`) replaces it. `--runs` lists each episode; `--seed Backend/App/warm-up-seed.json` writes them as the estimates the app ships and merges with its own measurements, merged into the file (entries it did not measure are kept). Heater plugs too since 2026-10-21: a run is the plug on until it switches off, 5+ minutes |
| `hacs.cjs <regex>` | no | HACS repositories matching a name: id, category, installed and latest version |
| `hacs.cjs --download <owner/repo>` | **yes** | Downloads a HACS repository's latest release into `/config` (how the Dreame vacuum integration was installed, 2026-10-19). An integration then needs `restart-core.cjs`, and adding it is a config flow in the HA UI |
| `restart-core.cjs` | **yes** | Checks the config, refuses if it is invalid, restarts HA Core and waits until it reports `RUNNING` (about 1–2 minutes). Devices keep their state; rewrites every `last_changed` (gotcha 3) |
| `realign-boiler.cjs [--apply]` | **yes** with `--apply` | Brings the boiler back in line with the app's belief without touching the boiler: stops the NetDaemon add-on, presses the fingerbot once (checking it moved), starts the add-on again. With the add-on stopped the press is not counted as an outside press, so the belief stays put while the boiler flips. Only when you know they disagree (HOME-ASSISTANT.md gotchas 23, 24). Dry run shows the add-on's and fingerbot's state |
| `valve-exercise.cjs` | **yes** | Runs the heating's monthly valve exercise now (normally 08:00 on the 1st) by firing the HA event the NetDaemon app listens for: boiler on, every TRV flipped, back after 2½ min, boiler back to normal at 5 min; skipped if the boiler is already on. HA answers 200 even if the app is not listening, so read the TRVs back with `trv-status.cjs` |
| `addon.cjs <slug> restart` | **yes** | Restarts the add-on and waits for it to report `started`. `c6a2317c_netdaemon6` stops and restarts the heating app (how the 2026-10-19 deploy restarted it); `core_samba` is the config share |

## Automations kept here

[automations/](automations/) holds automations created from this repo, one JSON file each, in
exactly the shape HA stores them, with a readable `id`. The repo copy is meant to be the source:
`put-automation.cjs` overwrites HA's version. If one is edited in the HA UI, copy it back (the
`--check` run shows which have drifted). Older automations made in the UI (porch light, light
tree, washing machine and so on) are not here.

| File | Does |
|---|---|
| `den_motion_lights_off_when_tv_on.json` | Den TV plug ≥ 30 W for 5 s → motion lights plug off |
| `den_motion_lights_on_when_tv_off.json` | Den TV plug < 30 W for 5 s → motion lights plug on |

| `living_room_motion_lights_off_when_tv_on.json` | Living room TV plug ≥ 30 W for 5 s → living room motion lights plug off |
| `living_room_motion_lights_on_when_tv_off.json` | Living room TV plug under 5 W for 5 s → living room motion lights plug on |
| `den_tall_lamp_on_with_sideboard_lamp.json` | Sideboard lamp plug goes from under 1 W to 1 W or more → den tall lamp (WiZ) on |
| `den_tall_lamp_off_with_sideboard_lamp.json` | Sideboard lamp plug goes from 1 W or more to 0 W → den tall lamp off |

Both motion-lights pairs also re-check on HA start, on `automation_reloaded` and when the lights plug comes back from
`unavailable`, guarded by a condition that repeats the threshold. The tall-lamp pair deliberately does not: it fires only on a
change between two real readings (never to or from `unavailable`), so the tall lamp can still be switched by hand.

Prefer entity-based triggers (`numeric_state`, `state`, `template`) over the UI's device triggers,
which reference opaque registry IDs and can't be read without a lookup. Threshold triggers fire on a
*crossing*, never on creation and never while a value stays on one side, so an automation that
should hold a state needs start/reload re-check triggers plus a condition, as the den pair has.
Saving through `put-automation.cjs` reloads automations, which fires those re-checks, so a save can
switch things.

## Examples

```bash
# Did the fingerbot really come back after its battery change?
node tools/ha/entity-history.cjs switch.tz3210_j4pdtz9v_ts0001 sensor.tz3210_j4pdtz9v_ts0001_battery --hours 72

# Every room thermometer, with how each is named
node tools/ha/list-devices.cjs --model SNZB-02

# Pair a new room thermometer (how the den one was added on 2026-10-19)
node tools/ha/pair-zigbee.cjs --model SNZB-02D --name "Den - Climate" --area games_room --prefix den_climate --label Den

# Check it afterwards
node tools/ha/list-devices.cjs --name "Den - Climate"
```

## Pairing and naming

`pair-zigbee.cjs` copies the naming used on the bedroom thermometers:

| Device | Device name | Entity IDs | `--label` |
|---|---|---|---|
| Room thermometer (SNZB-02D) | `{Room} - Climate` | `{room}_climate_*` | `{Room}`, set on the temperature and humidity sensors |
| Radiator valve (TRVZB) | `{Room} - Radiator` (add ` 1` / ` 2` when a room has two) | `{room}_radiator_*` | none |
| Smart plug (S60ZBTPG) | `{Room} - Plug - {Purpose}` | `{room}_plug_{purpose}_*`, main switch `switch.{room}_plug_{purpose}_switch` (`--main-suffix switch`) | none |

Every entity becomes `{domain}.{prefix}_{snake_case(original name)}`, e.g.
`sensor.den_climate_temperature`. A device's main entity (a plug's on/off switch) has no name of its
own, so it becomes `{domain}.{prefix}`, plus `_{--main-suffix}` if given. The plug pattern is taken
from the heater and bookshelf plugs. Older plugs (`Bedroom3LampPlug`, `switch.sonoff_s60zbtpg_2`,
the desk plugs) predate it and are not worth copying. Before pairing, dry-run the scheme against an existing device of
the same model (`list-devices.cjs` gives you its `device_id`); that shows the exact entity list and
any target IDs already taken.

When pairing for real: start the script **first**, then put the device into pairing mode. A battery device can join and then
sleep through its interview, leaving a device with no entities (HOME-ASSISTANT.md gotcha 22): the script waits up to 10 minutes
for the first ones and says to press the device's button. To start over, `zha.remove` (service, with the device's `ieee`) drops the
half-paired device. A
brand-new SNZB-02D joins as soon as it is powered on; a TRVZB needs the button sequence in
HOME-ASSISTANT.md → Radiator TRVs. The window is 254 s, ZHA's maximum. The script ignores any other
model that joins in that time, waits for the entity count to stop changing before renaming, and stops
without renaming anything if a target ID is taken.

For a smart plug, **ask the owner what its start-up behaviour should be** (what it does when power comes back after a cut)
before pairing it or repurposing it (owner, 2026-10-23). S60ZBTPGs ship with Off, which is wrong for most lights. Set the
answer with `plug-startup.cjs` and read it back.

What it does not do: bind the new entity into `Backend/App`. For that, run `dotnet nd-codegen`, add it
to `NamedEntities.cs`, and if it feeds heating, update the room arms in `HeatingControlService` and
`RoomZoneBindingTests`. Then record the device in HOME-ASSISTANT.md → Device inventory.

## Reading the output

- `entity-history.cjs` collapses repeated states, so each line is a change. The first line for an
  entity can be dated at the window's start rather than at a real event: the recorder reports the
  state *as of* the start time. Widen `--hours` if the first line matters. Retention is ~10 days.
- `last_updated` from `find-entities.cjs` is reset by HA restarts and integration reloads. Many
  entities sharing one timestamp means a restart, not an incident (gotchas 3 and 4).
- Config entities (`_identify`, `_comfort_*`, `_firmware`, `_offset`) sit at `unknown` by nature
  (gotcha 7).

## Adding a script

Put shared connection code in `lib/ha.cjs` (`rest`, `connect`, `snapshot`, `parseArgs`, `run`) and
naming rules in `lib/naming.cjs` (renames only the device's own `zha` entities, never ones another
integration such as the Octopus cost tracker attached to it). Start
the file with a comment saying whether it writes, add a row to the table above, and mark anything that
changes the house **yes** there. Anything that moves hardware asks first; see HOME-ASSISTANT.md →
Safety.
