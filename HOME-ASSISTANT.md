# Querying the live Home Assistant instance

Reference for inspecting and debugging the real house from a dev machine. Written for an
agent: facts and recipes first, gotchas tabulated, device inventory last.

Everything here was verified against the live instance on **2026-10-17**.

## Connection

| Item | Value |
|---|---|
| Base URL from a dev machine | `http://homeassistant.local:8123` |
| Base URL from the HA host itself | `http://localhost:8123` |
| Token | `Backend/App/appsettings.secrets.development.json` → `HomeAssistant.Token` |
| Fallback token locations | `appsettings.secrets.all.json`, `appsettings.development.json` → `HomeAssistant.Token` or `HomeAssistantApiEndpoints.Token` |
| Auth header | `Authorization: Bearer <token>` |
| Hardware | Home Assistant Green; Zigbee via ZHA on a Sonoff Zigbee 3.0 USB Dongle Plus |
| Scale | ~791 entities, 31 config entries, 20 battery sensors |

`appsettings.json` is committed and holds **placeholders** — a token containing
`insert_token_later_in_appsettings` or `<secret>` is not real. The secrets files are
git-ignored. **Never print, log, echo or commit the token value.**

Note the two hosts: `HomeAssistant.Host` is `localhost` because the NetDaemon app runs on
the HA box itself. From a dev machine use `homeassistant.local`.

```bash
# Smoke test, no credentials needed
curl -s -o /dev/null -w "%{http_code}\n" http://homeassistant.local:8123/
```

## What works on this instance

| Endpoint | Method | Status | Use |
|---|---|---|---|
| `/api/states` | GET | works | All entities with attributes. Start here |
| `/api/states/{entity_id}` | GET | works | One entity |
| `/api/history/period/{start}` | GET | works | Recorder history — **see gotcha 1** |
| `/api/config/config_entries/entry` | GET | works | Every integration + load state. High value |
| `/api/services/{domain}/{service}` | POST | works | Write. Physical effects — see Safety |
| `/api/error_log` | GET | **404** | Unavailable. Do not plan around logs |
| `/api/config/config_entries/flow` | GET | **405** | Cannot enumerate the discovery queue over REST |

## Gotchas

These each produced a wrong conclusion during the October 2026 boiler investigation. Read
before trusting any timestamp or any HTTP 200.

**Add to this table.** If an API, timestamp or status code misleads you, append a numbered
row before you finish — that is how this list earns its keep.

| # | Symptom | Cause | Do instead |
|---|---|---|---|
| 1 | History returns 0 rows, or *fewer* rows for a wider window | `/api/history/period/{start}` defaults to **start + 1 day**. A 45-day start fetches day 45 only | Always pass `end_time=<now ISO>` |
| 2 | History empty beyond ~10 days | Recorder retention is **~10 days**; older rows are purged | Probe the edge with a chatty entity before concluding "never reported" |
| 3 | `last_changed` says the device broke today when it broke days ago | `last_changed` / `last_reported` / `last_updated` are **rewritten on HA restart and on config-entry reload** | Get the real date from recorder history, never the state object |
| 4 | Hundreds of entities share one `last_updated` minute | HA restarted | Bucket `last_updated[:16]` and count. A 500-entity spike is a restart, not an incident |
| 5 | Service call returns HTTP 200 but nothing happened | HA accepts calls for unreachable devices. `switch.toggle` on an unavailable entity returns **200 with 0 changed entities** | Always read the entity state back afterwards |
| 6 | Device looks "off" but is dead | `unavailable` is not `off` — and `CustomSwitchEntity.IsOn()` returns false for both | Test `state == "unavailable"` explicitly |
| 7 | A device group looks broken but its sensors are fine | Config/diagnostic sub-entities (`_identify`, `_comfort_*`, `_firmware`, `_offset`) sit `unknown` by nature and inflate "down" counts | Check the specific entity, never a device-prefix group count |
| 8 | A Zigbee device vanished and isn't in the discovery queue | ZHA devices join via the coordinator's pairing mode, not HA discovery | Look for new ZHA entities; check **Settings → Devices → ZHA → _device_ → Last seen** |
| 9 | Unfamiliar entity IDs like `tz3210_j4pdtz9v_ts0001` | ZHA names entities from `manufacturer_model` when the device has no name | Identify devices by `friendly_name` |
| 10 | Entity ID names the wrong room | Entity IDs are **not** renamed when a device is renamed. `sensor.bedroom_3_temperature_and_humidity_battery` is physically the **bathroom** sensor | `friendly_name` is authoritative; entity-ID prefixes are not |
| 11 | Duplicate-looking entities with `_2` / `_3` suffixes | Genuine additional devices of the same model. Only the *unrenamed* entities keep the model prefix, so `sonoff_snzb_02d_battery_2` and `_3` belong to devices whose temperature entities are named `sensor.livingroom_climate_temperature` and `sensor.diningroom_temperature`. Counting model-prefixed entity IDs **undercounts** the fleet — there are 7 SNZB-02Ds, not 3 | Disambiguate by `friendly_name`; get the real model from the device registry (recipe below) |
| 12 | A device the C# app uses has no entity ID in the code | Buttons are bound by **IEEE address**, not entity ID (`new SonoffButton(ha, "d4:48:67:...")`) | Grep for `SonoffButton` as well as entity-ID literals when auditing coverage |
| 13 | The boiler is clearly running (or clearly isn't) and it contradicts everything the app did | The boiler has **two exclusive control paths** — the fingerbot, or a boiler thermostat (portable, normally in the kitchen) — selected by a **physical switch HA cannot read**, and in boiler-thermostat mode the boiler's button is **dead**, so fingerbot presses are no-ops. **As of 2026-10-17 the switch is on the boiler thermostat.** Since 2026-10-17 the app is *told* which path it is on, by the `heating.boilerControlPath` device setting (**Settings → Boiler control** on the heating page), and it defaults to the boiler thermostat, so out of the box it does not touch the boiler at all | Never infer the control path from HA — no entity exposes it, and the setting is a statement of belief, not a reading. Check the setting *and* ask which way the switch is set before diagnosing any heating behaviour, and treat radiator warmth as evidence the boiler fired, not that the app caused it |
| 14 | A configured entity ID reads nothing, with no error anywhere | `new NumericSensorEntity(ha, id)` succeeds for an entity that does not exist. It never throws, never logs, and its state-change subscription simply never fires. The "not configured" warning in `OctopusElectricityMeter` only covers an *empty* setting, not a dead one | Check device-settings entity IDs against `/api/states` rather than assuming a configured ID is a live one. `dotnet nd-codegen` is a good canary: entities vanishing from the generated file means they vanished from HA |
| 15 | Something "follows" the living room lamp, but minutes late | The lamp (`light.living_room_lamp`, a WiZ Wi-Fi bulb) is switched by cutting its power at the wall, which tells HA nothing. HA only sees it go `unavailable` after the bulb stops answering its polls, and after an HA restart with the lamp off its WiZ config entry sits in `setup_retry`, whose retry interval keeps growing — so "lamp came back on" can wait for the next retry. Measured 2026-10-19 (owner switched on, then off a few seconds later): HA saw **on** within a few seconds (bulb boot + Wi-Fi join, with the WiZ entry `loaded`) but **off ~60 s late**; the light tree automations then followed each within ~0.1 s, so the whole delay is detection | Measure lamp lag against a known flip time before blaming an automation. Ways to speed up "off", not done (owner happy to live with it): a Zigbee button driving the bulb instead of cutting its power (instant both ways); a Zigbee plug under the lamp, only if the switch is on the lamp rather than the socket; or pinging the bulb every few seconds (~5–10 s, needs a fixed IP) |
| 16 | Renaming an entity might silently orphan whatever tracks it | Some integrations store a target *entity ID*. The Octopus cost tracker does (`target_entity_id`) — but on 2026-10-19 it **followed** a registry rename live (`loving_room_charging_dock_summation_delivered` → `living_room_plug_tv_summation_delivered`). Whether that survives an HA restart was not checked | Before renaming, search states' attributes and automation configs for the old ID (`tools/ha/rename-device.cjs` leaves other integrations' entities alone, but cannot see what *refers* to them). After the next HA restart, check the tracker's `target_entity_id` |
| 17 | The NetDaemon app forgets what it saved after an add-on restart | The Supervisor recreates an add-on's container on restart, so anything the app writes outside `/data` (or the mapped `/config`) is gone. Seen 2026-10-19: the boiler belief saved in the user profile was lost, the app assumed the boiler was off, and it never pressed it off. Inferred from the app's behaviour (it skipped a press it would otherwise have made); the add-on log could not be read through the API to confirm | The app's caches go in `/data/HomeAssistant` when it runs as an add-on (`LocalStorageFolder`). Anything else that must survive a restart belongs there too |
| 18 | The boiler fingerbot changed state with nobody pressing it | At 13:17:08 BST on 2026-10-19 `switch.tz3210_j4pdtz9v_ts0001` went `on` with **no context** in the logbook (no user, automation or service call: the device reported it). Either someone touched the fingerbot's own button or it sent a spurious report. The app counts any change it did not command as a press, flipped its belief and pressed 21 s later. If the report was spurious, that press really toggled the boiler | Run `tools/ha/who-changed.cjs switch.tz3210_j4pdtz9v_ts0001` before trusting any fingerbot change: "Supervisor" is the app, a named user is a person or a script on their token, and no context is the device. If uncommanded reports recur, the outside-press rule needs rethinking |
| 19 | The Dreame Vacuum login form says only "Could not login, check the credentials." | The form hides the cloud's answer. The real one is in HA's system log (websocket `system_log/list`, logger `custom_components.dreame_vacuum.dreame.protocol`). On 2026-10-19 it was `user password not match` with `maxAttempts 5` and a `remains` count, so the account and region were right, only the password was wrong, and **each failed submit spends one of five attempts** before the account is locked. The account had been created with Google sign-in, which the integration cannot use, so a password had to be set in the Dreamehome app first | Read the system log after the first failure instead of retrying the form. Test the password in the Dreamehome app itself before submitting it again |

## Recipes

**Scripts first.** [tools/ha/](tools/ha/README.md) has tested Node scripts for the common jobs, which
already apply the gotchas above: `entity-history.cjs` (real transitions, with `end_time` always set),
`find-entities.cjs` (matches friendly names too), `list-devices.cjs` (the registry: model, area,
entity names, live state), `pair-zigbee.cjs` (ZHA join plus house-style renames) and
`put-automation.cjs` (HA automations from `tools/ha/automations/*.json`). Only the last two write. Reach for them before writing ad-hoc queries, and add a script there if you find yourself
writing the same query twice.

### Are the entities this repo depends on actually alive?

The single most useful check. Extract every entity ID bound in
`Backend/App/apps/NamedEntities.cs`, compare against `/api/states`, and remember gotcha 12
— buttons won't appear in an entity-ID scan.

Baseline as of 2026-10-17: **40 entity IDs bound, 31 healthy.** The expected-offline set is
the MG car (cloud, `setup_error`), seasonal plugs (`switch.christmas_tree`,
`switch.lego_village`), two WiZ lamps, and `switch.kitchen_plug_heater_switch`
(deliberately unplugged). Anything else being down is new.

### What hardware is this entity actually on?

`/api/states` does not expose manufacturer or model, and entity IDs lie after a rename
(gotchas 10 and 11). The device registry is authoritative:
`node tools/ha/list-devices.cjs --model SNZB-02` reads it over the WebSocket API. Without the
script, `POST /api/template`
reaches it — this works on the instance and is the only reliable way to group devices by
hardware type:

```jinja
{{ device_attr('sensor.bathroom_temperature', 'manufacturer') }}
{{ device_attr('sensor.bathroom_temperature', 'model') }}
```

Iterate over a filtered set to fingerprint the whole fleet at once:

```jinja
{%- for e in states.sensor
      | selectattr('attributes.device_class', 'defined')
      | selectattr('attributes.device_class', 'eq', 'temperature')
      | map(attribute='entity_id') | list -%}
{{ e }} | {{ device_attr(e, 'model') }} | {{ state_attr(e, 'friendly_name') }}
{% endfor -%}
```

Do this before buying batteries or assuming two devices are the same type.

### When did a device really stop reporting?

`node tools/ha/entity-history.cjs <entity_id> --hours 240` does all of this. By hand:

```
GET /api/history/period/{iso_start}?filter_entity_id={entity_id}&end_time={iso_now}
```

Collapse consecutive identical states to get transitions. The recorder writes only on
state *change*, so a device that stays `unavailable` across an HA restart produces no new
row — which is exactly why gotcha 3 bites.

### Which integrations are broken?

```
GET /api/config/config_entries/entry
```

Each entry carries `domain`, `title`, `entry_id`, `state`. Anything not `loaded` is a
problem: `setup_retry` keeps retrying (often just a powered-off device), `setup_error` has
given up and needs a reload or re-auth. Reload one with:

```
POST /api/services/homeassistant/reload_config_entry   {"entry_id": "<entry_id>"}
```

`{"entity_id": "<any entity on it>"}` works when you don't have the entry ID. A reload
re-creates that integration's entities, resetting their timestamps (gotcha 3) and briefly
showing them unavailable.

**Do not reload the `zha` entry** to chase one Zigbee device — it drops every Zigbee device
in the house, heating sensors included, and will not revive a flat battery.

Cloud integrations (`octopus_energy`, `mg_saic`, `hypervolt_charger`, `solax_modbus`) go
`setup_error` after an internet outage and **do not self-heal**. Octopus feeds
`ElectricityRatePushService` and battery pricing, so stale rates are a real consequence.

### What is actually deployed?

Never infer from git — pushed is not deployed.

```bash
curl -s https://homeassistant-prod-functions.azurewebsites.net/api/version
```

Returns `<timestamp>-<sha>`; the SHA is the deployed commit. See [DEPLOYMENT.md](DEPLOYMENT.md).

## Device inventory

Physical notes that aren't recoverable from code or the API. Battery levels are a
**2026-10-17 snapshot** — re-query rather than trusting these numbers.

### Battery replacement by device type

Neither battery type nor tooling is exposed over the API, and they are what you most want
to know before walking upstairs. Models below come from the device registry (authoritative
— see the recipe above); battery types are marked by how they were established.

**Fill this in as you go.** Whenever a device is opened, record its battery and the tools
needed, and upgrade its † to ✔︎. A row marked `unrecorded` means nobody has looked yet —
that is a gap to close, not a dead end.

| Device type | Battery | Tools | Count | Devices |
|---|---|---|---|---|
| Tuya **TS0001** fingerbot (`_TZ3210_j4pdtz9v`) | **CR2** ✔︎ | **None — toolless** ✔︎ | 1 | Boiler-Fingerbot |
| SONOFF **SNZB-02D** temp/humidity | **CR2450** ✔︎ | **Small Phillips** ✔︎ | 8 | Every room thermometer — bedrooms 1/2/3, dining, living, kitchen, games room desk, den (added 2026-10-19) |
| eWeLink **SNZB-01P** button | **CR2477** † | unrecorded | 4 | Dining Room Desk, Games Room Desk, Dining Bookshelf, Christmas Tree |
| eWeLink **SNZB-02P** temp/humidity | **CR2477** † | unrecorded | 1 | Bathroom — **not** an SNZB-02D, do not fit a CR2450 |
| SONOFF **TRVZB** radiator valve | **3 × AA** (LR6 alkaline) † | unrecorded | 7 | Bedrooms 1/2/3, dining, living, den (radiators 1 and 2, added 2026-10-18, fresh batteries) |
| eWeLink **SNZB-03P** motion | unrecorded | unrecorded | 2 | Porch, Motion-Sensor-02 |
| SONOFF **SNZB-05P** flood | unrecorded | unrecorded | 1 | Kitchen flood detector |

✔︎ verified by hand on the actual device.
† from the manufacturer's published spec, not yet confirmed on these units — check the cell
before buying in quantity.

**CR2477 covers five devices** (four buttons plus the bathroom sensor) and **CR2450 covers
eight** (every other room thermometer), so two coin-cell types plus AAs and a single CR2
stock the whole house.

### Batteries, lowest first

These levels are now also **recorded daily**, so the trend is no longer something you have
to catch by hand: `DeviceBatteryApp` pushes every replaceable cell's level to
`POST /api/device-batteries`, and `GET /api/device-batteries?houseId=&days=N` reads it back
newest day first. That exists because the fingerbot's cell died on 2026-10-08 and nothing
noticed for nine days.

Two things to know when reading that data. A sensor that is `unavailable` is stored with a
null percent and `isAvailable: false`, **never 0%** — a device off the mesh has not reported
a flat battery (gotcha 6 again), and a run of unavailable days is itself the finding. And
the selection is `sensor` domain, `device_class=battery`, entity ID ending `_battery` or
`_battery_N`: that trailing number is load-bearing, because the living room and dining room
thermometers are `_battery_2` and `_battery_3` (gotcha 11), and it is what keeps phones, the
car and the SolaX home battery — all of which also carry `device_class=battery` — out of a
list meant for cells someone has to replace. As of 2026-10-19 it selects 24 devices (21 on
2026-10-17, plus the two den radiators and the den thermometer), which matches the inventory
above exactly — counted by applying `DeviceBatteryPushService.IsReplaceableBatterySensor`'s
rules to a live `GET /api/states`.

| Level | Device | Battery entity | Used by the C# app as |
|---|---|---|---|
| **1.0** (unverified) | Boiler-Fingerbot | `sensor.tz3210_j4pdtz9v_ts0001_battery` | `BoilerFingerbot` |
| **22%** | Dining Room – Desk – Button | `sensor.ewelink_snzb_01p_battery` | `DiningRoomDeskButton` |
| 94% | Bedroom 3 – Radiator (TRV) | `sensor.bedroom_3_radiator_battery` | `Bedroom3RadiatorThermostat` |
| 95% | Bedroom 1 / 2, Dining, Living – Radiator (TRVs) | `sensor.*_radiator_battery` | `*RadiatorThermostat` |
| 100% | Bedroom 1 / 2 / 3 – Climate | `sensor.bedroom_*_climate_battery` | `Bedroom*Temperature` |
| 100% | Games Room – Desk – Climate | `sensor.sonoff_snzb_02d_battery` | `GamesRoomDeskTemperature` |
| 100% | Living Room – Climate | `sensor.sonoff_snzb_02d_battery_2` | `LivingRoomClimateTemperature` |
| 100% | Dining Room – Climate | `sensor.sonoff_snzb_02d_battery_3` | `DiningRoomClimateTemperature` |
| 100% | Den – Climate (new 2026-10-19) | `sensor.den_climate_battery` | `DenClimateTemperature` |
| 100% | Den – Radiator 1 / 2 (TRVs, new 2026-10-18) | `sensor.den_radiator_*_battery` | `DenRadiator*Thermostat` |
| 100% | Kitchen temperature and humidity | `sensor.kitchen_temperature_and_humidity_battery` | `KitchenTemperature` |
| 100% | Bathroom – Climate | `sensor.bedroom_3_temperature_and_humidity_battery` | `UpstairsBathroomTemperature` (gotcha 10) |
| 100% | Games Room Desk / Dining Bookshelf / Christmas Tree – Buttons | `sensor.*_button_battery` | `*Button` (bound by IEEE) |
| 100% | Porch motion, Motion-Sensor-02 | `sensor.ewelink_snzb_03p_battery`, `sensor.motion_sensor_02_battery` | `PorchMotionSensor` |
| 100% | Kitchen flood detector | `sensor.sonoff_snzb_05p_battery` | not used |

### Per-device notes

**Boiler fingerbot** — `switch.tz3210_j4pdtz9v_ts0001`, friendly name `Boiler-Fingerbot`.
Tuya TS0001 (`_TZ3210_j4pdtz9v`) on ZHA. Takes a **CR2 battery**, and replacing it is
**toolless** — the case opens by hand. It is a *momentary
button pusher*, so one switch toggle equals one physical press of the boiler's button, and
it reports **no feedback at all** — `_boilerOn` in `HeatingControlService` is a belief, not
a reading. Exposes `_up_movement`, `_down_movement`, `_sustain_time`, `_mode`, `_reverse`
and `_touch_control` for arm calibration. Died 2026-10-08 17:25 UTC and went unnoticed for
nine days because of gotchas 5 and 6.

**Back online 2026-10-19** after the owner fitted a new CR2 (told to us; the cell itself is not
observable). Read from the HA recorder: `switch.tz3210_j4pdtz9v_ts0001` went `unavailable` →
`off` at 08:03:22 local on 2026-10-19, after ten days unavailable, with the same entity ID, so
nothing in `NamedEntities.cs` needs changing. Its battery sensor reads **`1.0`**, which is
unverified — it may be 1% (nonsense for a fresh cell) or a 0–1 fraction/raw value; check it
against the daily `device-batteries` snapshot before trusting or alerting on it. A press has
**not** been tested, and the app still does not drive the boiler (control path is a setting, see
below), so "active" here means *reporting to HA*, not *controlling the boiler*.

**The boiler has two control paths, and the fingerbot is only one of them** (established by
the owner, 2026-10-17). The boiler can be driven either by the fingerbot pressing its
button, or by the **boiler thermostat**: the boiler's own temperature- and time-based
thermostat, a **portable unit that normally lives in the kitchen** (owner, 2026-10-19; older notes
called it the "external" or "kitchen scheduler"). A **physical switch at the boiler** chooses
between them. That switch is not a
Zigbee device, not an entity, and not readable from HA in any form, and neither is the
boiler thermostat: an `/api/states` sweep on 2026-10-17 found the **only** `climate.*`
entities to be the five radiator TRVs and the car — there is no wall-thermostat entity, so
HA cannot see the mode, the programme, or the setpoint.

**In boiler-thermostat mode the boiler's physical button is dead**, so a fingerbot press does
*nothing at all* — it is not an override, it is a no-op. The two paths are exclusive, not
additive.

**As of 2026-10-17 the switch is set to the boiler thermostat**, and has been for the
period covering the fingerbot's death. So:

> **Since 2026-10-17 the app knows which path it is on, because someone tells it.** The
> `heating.boilerControlPath` field in the **device-settings** blob (a number: 0 = boiler
> thermostat, the default, or 1 = fingerbot) gates every boiler action in `HeatingControlService`: in
> boiler-thermostat mode it makes no fingerbot call, holds no `_boilerOn` belief, runs no
> verification and issues no corrective press, while valves and heater plugs carry on
> unchanged. Change it in the web UI under **Settings → Boiler control** on the heating page;
> the change reaches the app over the existing `device-settings-changed` SignalR event. It is
> still only a claim about the house — **nothing can verify it against the physical switch**,
> so if someone flips the switch and not the setting, the app is wrong again and silent about
> it. Set it to `fingerbot` only after the switch at the boiler has actually been moved.

**2026-10-19, confirmed by the owner by hand:** a single `switch.turn_on` on the fingerbot
(`switch.tz3210_j4pdtz9v_ts0001`) physically pressed the boiler button and the boiler responded. So
the boiler's switch was on the **fingerbot** path at that moment, no longer the boiler thermostat as
on 2026-10-17. Nobody checked what `heating.boilerControlPath` was set to; if it is still
0 (boiler thermostat) the app is not driving a boiler that can now be driven. HA kept the switch at
`on` afterwards and never reverted it (read back from the recorder later that morning), so the
fingerbot's HA state **latches**: each on/off change is one press, and the state says nothing about
the boiler. The app relies on that to spot presses made outside it. Whether a `turn_off` also
physically presses has not been watched. The owner chose to leave it as it was.

- **`Backend/App` is not currently heating this house.** Every boiler decision
  `HeatingControlService` made in this period was discarded by a dead button — and since
  2026-10-17 it no longer makes them, because the control-path setting defaults to the
  boiler thermostat. The boiler thermostat plus the TRVs' own regulation is what has
  actually kept the house warm. The app's boiler control only becomes real again when
  someone physically flips the switch at the boiler **and** sets the control path to
  `fingerbot`.
- The dead fingerbot therefore cost nothing in comfort, and the CR2 is **not** urgent — it
  is a prerequisite for *returning* control to the app, not a repair of a house that is
  currently broken.
- `BoilerVerificationService` is actively misleading in this mode, both ways. It verifies a
  turn-on by looking for a ≥ threshold rise in TRV `CurrentTemperature`, and a turn-off by
  looking for a fall after a settling period. A coincidental boiler-thermostat burn passes a
  turn-on that never happened; such a burn during a turn-off window fails a turn-off
  that did. On failure `HeatingControlService` **presses the fingerbot again** to "correct"
  the mismatch ([HeatingControlService.cs](Backend/App/Services/Climate/HeatingControlService.cs)) —
  a press that can never work, against a button that cannot respond, costing mechanical
  wear and CR2 life for nothing. **Gated since 2026-10-17**: in boiler-thermostat mode
  verification is not run at all and the corrective press cannot be issued, and the
  pending verification is discarded on entering that mode so it cannot resurface later.
- Radiator warmth is evidence that *the boiler fired*, never evidence that *the app made it
  fire*.

Still unrecorded: how the switch is labelled and which way is which, the make/model of the
boiler thermostat, and its programme. Worth capturing next time someone is at the boiler —
without them, nobody can tell from this repo whether the app is live.

**Games room desk climate** — `sensor.sonoff_snzb_02d_temperature`, a Sonoff SNZB-02D.
Battery replaced 2026-10-17 after dropping to 5%. Worth watching regardless of level,
because it is the `games desk` zone's **only** temperature source: when it dies
`GetCurrentTemperatureForRoom("games desk")` returns null, `EvaluateSchedule` hits its
`currentTemperature == null` early return, and Ste's desk heater silently stops being
controlled — no error, no alert. (The `den` zone read it too until 2026-10-19; it now has
its own thermometer, below.) The same holds for every room's thermometer. It is one of **eight** identical SNZB-02Ds (gotcha 11), so
a CR2450 and a small Phillips screwdriver cover every room thermometer in the house except
the bathroom.

**Den climate** — `sensor.den_climate_temperature` / `_humidity` / `_battery`, device "Den - Climate", a
Sonoff SNZB-02D in the **Games Room** area (there is no Den area; the den radiators are there too). Paired
2026-10-19: joined ~90 s after the ZHA join window opened, as soon as it was powered on (a new unit needed no
button press), with `tools/ha/pair-zigbee.cjs` (the example in its README is this exact run). First readings: 20.8 °C / 55.2 %, against 21.4 °C at Ste's desk at the same moment. It
measures the whole den, unlike the desk thermometer, and since 2026-10-19 it is what the `den` zone reads
(`DenClimateTemperature` in `NamedEntities.cs`), so it drives both den radiator valves; the `games desk` zone
keeps the desk thermometer. Deployed 2026-10-19 (app `20261019112544`) and verified from production: the den
zone's room state read 19.2 °C, matching `sensor.den_climate_temperature`, while Ste's desk read 21.6 °C. If it dies the den is simply not controlled; the desk
thermometer is **not** used as a fallback. Named the way Bedrooms 2 and 3 are: every entity renamed to
`den_climate_*` and the temperature/humidity entities named after the room. RSSI/LQI are disabled by the
integration, as on every other unit.

**Radiator TRVs** — `climate.{room}_radiator_thermostat` in bedrooms 1–3, dining and living
rooms, plus `climate.den_radiator_1_thermostat` and `climate.den_radiator_2_thermostat` (paired 2026-10-18, named "Den - Radiator 1/2", in the Games
Room area; wired into `Backend/App` as the `den` zone). Battery powered, 94–95%
(the den ones 100%, new). Expose `hvac_action`, `child_lock`, `open_window`,
`local_temperature_offset` and `external_temperature_sensor_value`. **Nothing in this repo
writes the external-temperature registers**, so a TRV's own reading is independent of the
room thermometer — bedroom 1 read 17.8 °C at the room sensor and 16.4 °C at the TRV
simultaneously on 2026-10-17. Schedules and the UI use the room sensor; boiler verification
uses the TRV.

**Pairing a new TRVZB** (procedure confirmed by hand on 2026-10-18, two units): the display shows "F5" (it can
look like "FS") until the TRV is on a radiator valve — that only means it is not installed on a valve yet, it
is not a fault. Then: (1) install it on the valve; (2) press the button 3 times to activate it; (3) turn the
dial all the way to "OF"; (4) long-press the button (~3 s) until the Zigbee icon flashes — pairing mode, which
times out after about 3 minutes; (5) have ZHA's join window open at that moment (`zha.permit`, `duration` up to
254 s; its 200 response proves nothing, so watch for the device to appear). "In" on the display is the
installation/adaptation state. Afterwards rename the device and its entities together (device `{Room} -
Radiator`, entities `{room}_radiator_*`), or the entities stay `sonoff_trvzb_*`.
[`tools/ha/pair-zigbee.cjs`](tools/ha/README.md) opens the join window and does the renaming
(`--model TRVZB --name "{Room} - Radiator" --prefix {room}_radiator`); it was first used on the den
thermometer, not yet on a TRV, so dry-run it with `--plan` against an existing radiator first.

**Living room plugs** — both Sonoff S60ZBTPGs, both now the living room's **presence sensors** in
`Backend/App` (see CLAUDE.md → presence):

| Device | Switch | Feeds | Presence rule |
|---|---|---|---|
| Living Room - Plug - TV | `switch.living_room_plug_tv_switch` | the living room TV | `sensor.living_room_plug_tv_power` ≥ 30 W for the whole last 5 s (the switch is not read) |
| LivingRoomLightTreePlug (the owner calls them the **stick lights**) | `switch.livingroomlighttreeplug` | the stick lights | switched on |

The TV plug was the **charging dock** plug under the TV until 2026-10-19, when it was repurposed and renamed
with `tools/ha/rename-device.cjs` (it was "Living room plug - charging dock", entities misspelt
`loving_room_charging_dock_*`). Its "Living room charging dock completion" automation, which sent a
"charging complete" notification to Ste's phone, was deleted the same day. The Octopus cost tracker
"octopus_home_mini" is attached to this device and now costs the TV (gotcha 16). Its start-up behaviour is
**On**. The stick lights plug is not renamed and follows the WiZ lamp via the automations "Living room light
tree on" (lamp power > 1 W, or back from `unavailable`) and "… off" (lamp turned off, `unavailable`, or 23:00) —
hence the lag in gotcha 15.

**Den plugs** — two Sonoff S60ZBTPGs in the **Games Room** area, both paired 2026-10-19 with
`tools/ha/pair-zigbee.cjs`, each joining within 30 s of being plugged in:

| Device | Switch | Feeds |
|---|---|---|
| Den - Plug - TV | `switch.den_plug_tv_switch` | the den TV |
| Den - Plug - Motion lights | `switch.den_plug_motion_lights_switch` | the den's motion-sensitive lights, which have their **own built-in motion sensor** (owner, 2026-10-19). HA cannot see that sensor; it can only cut the lights' power |

Mains powered, with the usual `_power`, `_current`, `_voltage` and `_summation_delivered` sensors.
Both shipped with start-up behaviour **Off**, so after a power cut whatever they feed stays unpowered
until the plug is switched on. The TV plug is also the den's **presence sensor** in `Backend/App`
(`DenTvPlugPower` only — the switch is not read; in use at ≥ 30 W within 5 minutes, see `PresenceService`), so
den schedules can be set for occupied or unoccupied. The motion lights plug is not referenced by
`Backend/App`. ZHA reused the same temporary
entity IDs (`sonoff_s60zbtpg_3`, `_11`) for the second plug once the first had been renamed, so a
default entity ID does not identify a device; the device ID does.

**The motion lights follow the TV**, through two HA automations (not `Backend/App`), created
2026-10-19 from [tools/ha/automations/](tools/ha/automations/):

- `automation.den_motion_lights_off_when_tv_is_on`: TV plug power **≥ 30 W for 5 s** → lights plug off.
- `automation.den_motion_lights_on_when_tv_is_off`: TV plug power **under 30 W for more than 5 s** →
  lights plug on. An `unavailable` power reading counts as neither.

The 30 W triggers fire on a *crossing*, not a level, so on their own they would leave the lights in
the wrong state after anything that skips a crossing (a power cut — both plugs start up Off — or an
HA restart mid-change). So both automations also **re-check on HA start, on automation reload, and
when the lights plug comes back from `unavailable`**, and both carry a condition repeating their
threshold, so whichever trigger fires they act only if the TV's power is really on that side of 30 W.

Observed 2026-10-19, read from automation traces:

- The ≥ 30 W path works on the real TV: the TV plug was switched on at 07:37:27 UTC, the power read
  35 W two seconds later, and the off automation ran at 07:37:34.
- The reload re-check works: the on automation ran on `automation_reloaded` and stopped at its
  condition, because the TV was at 45 W.
- A `switch.turn_off` to the lights plug once failed with **"Failed to send request: device did not
  respond"** while both plugs showed `available` and good LQI moments later. HA automations do not
  retry, so a single lost Zigbee command leaves the lights in the wrong state until the next trigger.
- **The TV drew a steady 35–45 W for about two minutes after its plug came on, then dropped to
  4 W** (07:37:29–07:39:38 UTC, then 4 W from 07:39:41). The owner was surprised by the 40 W, so
  it was probably the TV powering up after its plug was switched on rather than being watched; that
  is unconfirmed. The 4 W it settled at is presumably standby, comfortably under 30 W.
- The < 30 W path works on the real TV: the on automation ran at 07:39:46, 5 s after the drop, and
  the lights plug turned on. So the full off → on cycle has been seen working.
- Consequence: **switching the TV plug on by itself cuts the motion lights' power for about two
  minutes**, since that start-up draw crosses 30 W.

**Heater plugs** — `switch.{room}_plug_heater_switch` in bedroom 1, dining room, games room
and kitchen. **Mains powered**, each with `_power`, `_current`, `_voltage` and
`_summation_delivered` sensors. The power sensors are not yet bound in `NamedEntities.cs`
despite being useful for detecting electric-heat contribution. The kitchen one is
deliberately unplugged, so its `unavailable` is expected, not a fault.

**Octopus electricity — no import tariff is visible to anything (open fault, found and
investigated 2026-10-17).** `device-settings → battery.electricityRateSensorId` points at
`sensor.octopus_energy_electricity_24j0494691_1591015382045_current_rate`, which no longer
exists. Established from the entity/device registries over the WebSocket API, the system log,
and recorder history:

- **The import meter is absent from HA entirely**, not merely unavailable. The device
  registry lists "Electricity Meter Export" and "Gas Meter" but no import meter, and the
  entity registry has no `octopus_energy` entries for MPAN `1591015382045`. The one stray
  entity (`..._current_accumulative_consumption_cost`) belongs to HA's *Energy dashboard*
  (`platform=energy`, a `grid_cost` helper), not the integration.
- **The import data stopped updating on 9 Oct 2026.** Export and gas rate events tick every
  15 minutes (96/day) right through to now; the import ones stop after 9 Oct. The rate sat
  frozen at 0.03327 for 8 days, the standing charge went `unknown` at 23:00:01 UTC on 9 Oct
  (**00:00 BST on 10 Oct**, which looks like an agreement boundary), and at 06:10 on 17 Oct
  everything went `unavailable`, then the entities were removed at 14:15:45.
- **The installed integration can no longer read the account.** It is v17.1.1 (v19.2.1
  exists) and `custom_components.octopus_energy.api_client` logs HTTP 400 from Octopus's
  GraphQL API on every refresh: `Cannot query field 'octoHeatPumpControllerEuids' on type
  'Query'`, plus `savingSessions` and `greenerNightsForecast`. Octopus has changed its schema
  under it. At 14:15 it started "using cached account information", and the cache has no
  active import agreement.
- Separately, the 06:10 restart failed integration setup with a DNS error reaching
  `api.octopus.energy` (`DNS server returned general failure`). That explains the 8-hour gap
  in export/gas data but is not why the import meter is gone.

**Root cause (read from the integration's cached account, HA diagnostics, 2026-10-17):**
the import meter's agreement was `E-1R-INTELLI-FIX-12M-25-08-29-F` (Intelligent Octopus Go,
12-month fixed), valid **until 2026-10-09T23:00:00Z — midnight BST on 10 Oct**, exactly where
the rate froze and the standing charge went `unknown`. The cache holds no later agreement,
because the integration could not refresh the account (the GraphQL 400 above). So: the fixed
term ended, Octopus will have started a new agreement, and v17.1.1 never learned of it.
Read this yourself with
`GET /api/diagnostics/config_entry/<octopus account entry id>` → `data.account` — it shows
agreements and tariff codes without needing the Octopus token, and `data.using_cached_account_data`
says whether it is stale.

**Fix applied 2026-10-17:** the integration was updated v17.1.1 → **v19.2.1** by calling
`update.install` on `update.octopus_energy_update` (HACS), read back to confirm. **Takes effect
only after a Home Assistant restart.** v18/v19 remove the target-rate sensors, the saving-
session and free-electricity binary sensors and the greener-nights sensors; nothing in this
repo or in the 16 UI-defined automations/scripts/scenes uses them. Requires HA ≥ 2025.11.0
(this instance: 2026.2.2).

**Restarted and verified 2026-10-17.** HA came back in 77 s and the import meter returned with
its full entity set, live: `current_rate` 0.356164 (35.6p peak), `next_rate` 0.076 (7.6p off-peak),
standing charge 0.607. The account refresh now succeeds and shows the new agreement
**`E-1R-IOG-SMB-FIX-12M-26-10-01-F`** (valid 2026-10-09 → 2027-10-09; the account had also moved
from `INTELLI-FIX` to `IOG-SMB-FIX-12M-25-08-29` on 2026-08-26). The entity ID is **unchanged**
(`..._1591015382045_current_rate`), so `battery.electricityRateSensorId` needed no repointing.

**REST pricing now prices the four-rate tariffs; HA is the safety net (code written 2026-10-17, not
yet deployed, unverified against the live account — see below).** `OctopusReader` used to call only the
public `…/products/{product}/electricity-tariffs/{tariff}/standard-unit-rates/` endpoint, which returns
**18 rates for the old `INTELLI-FIX` tariff but 0 for both `IOG-SMB-FIX` tariffs** (and the new product
publishes no tariffs in the public product listing at all). An empty result used to fall through to a
default `EnergyRate` of 0, which is why `battery-pricing` stores `importPrice` 0 for 17–19 Oct and why
`BatteryControlService` had no usable import rates.

**What supplies what, import.** In order, per call:

1. **`OctopusReader` → `standard-unit-rates`.** Unchanged; still the whole story for INTELLI-FIX and any
   ordinary tariff (the time-bounded records are passed through as-is).
2. **`OctopusReader` → `day-unit-rates` + `night-unit-rates`, when (1) is empty or errors.** The
   open-ended day and night records are synthesised into time-bounded ones over the cache window
   (`FourRateTariff.Synthesise`), with the night rate applied **23:30–05:30 local time**, evaluated in
   `TimeZoneInfo.Local` so it follows BST/GMT (the 24/25 Oct night is seven hours long, 22:30Z–05:30Z).
   The window is the single constant `FourRateTariff.NightWindowStart/End`, taken from the product
   description — the API does not state it, so if Octopus changes the product this is where to look.
   Records also split wherever the day or night rate itself changes. **Import only**: export passes
   `allowFourRate: false` and is exactly as before. Tests: `FourRateTariffTests`, built from hand-written
   JSON modelled on the real responses (the real account cannot be queried locally — placeholders in
   `appsettings.json`). It has not been run against the live account; the first deployed refresh should
   show the `Refreshed rates: N import …` log line with a non-zero count and the stored pricing leaving 0.
3. **`HomeAssistantImportRates`, when REST (1)+(2) return nothing.** See next paragraph.

**Why REST stays ahead of HA now that it works (decided 2026-10-17; the order was not changed).**
REST's day/night rates are dispatch-blind; HA's events may reflect dispatches, so HA looks like the better
source. It isn't the one to prefer, because dispatches reach battery control through a different path:
`BatteryControlService.ReactToRateChangeAsync` fires on every change of the HA import-rate sensor
(`OnCurrentRatePerKwhChanged`), overlays the live sensor price on the cached rates until the next
published boundary (aligned with a REST boundary, e.g. the 23:30 switch) or, for an unaligned change such
as a dispatch, for 60 minutes, and re-applies it across rate refreshes. That is the same mechanism
that handled dispatches on INTELLI-FIX, whose REST rates were equally dispatch-blind, so this is no
regression. What it does **not** give is *forward* visibility: a planned dispatch is unknown until the
sensor flips, and a dispatch longer than 60 minutes reverts to the REST rate until the sensor next
changes. HA's events are not a clear fix (integration bugs #1884, #1892, #1898, #1901 on IOG dispatch
sensors). If that gap matters, merge HA's cheap slots into REST's in `FallbackElectricityRatesReader`
rather than swapping the order. (This also means the override depends on the sensor being healthy —
it is, since the 2026-10-17 restart.)

What supplies import rates when REST has nothing: `IElectricityRatesReader` is registered as
`FallbackElectricityRatesReader`, which asks `OctopusReader` first and, **only for import and only
when REST returns nothing**, reads `HomeAssistantImportRates` — the integration's
`event.octopus_energy_electricity_<serial>_<mpan>_{previous,current,next}_day_rates` entities
(import meter found by entity-ID pattern; the `_export_` ones are excluded). Each event's `rates`
attribute is a list of `{start, end, value_inc_vat, is_capped}` — read from the live
`current_day_rates` event 2026-10-17: 48 half-hourly slots per event, ISO timestamps with a `+01:00`
offset, `value_inc_vat` in **GBP/kWh** (0.076 / 0.356164), so the app multiplies by 100 to get
p/kWh. The events carry no ex-VAT figure; `RateExcVat` is derived as inc ÷ 1.05 (nothing reads it).
Coverage is yesterday–tomorrow only. Export is untouched (still REST, 12p via `OUTGOING-VAR-24-10-26`).
If REST *and* HA both come back empty the reader logs one warning ("No electricity import rates
available …") and re-arms it once rates return, instead of silently pricing at 0. That warning is the
signal that the integration is down or renamed its events. Tests: `ImportRatesFallbackTests`.

**Deployed 2026-10-17 (Functions `2026.10.17.20.24-338b011`, app `20261017192754`).** Verified from
production afterwards: `battery-pricing` for today shows 7.6p / 35.6164p slots (00:00, 05:30, 23:30) instead of
0, and the first daily battery snapshot arrived (21 devices, the dead fingerbot unavailable rather than 0%).
**Observed, then self-corrected:** at 20:34 BST tomorrow and the day after were each a single flat
35.6164p slot rather than three. By 05:07 the next morning both had their three slots (a watcher polling only
while the dev PC was awake saw it flat at 20:43 and fixed at 05:07, so the exact correcting push is not known). The reader and slot builder are proven correct for all three days
(`EachOfTodayTomorrowAndTheDayAfter_GetsTheCheapNightSlots`, `FourRateMultiDayTests`). The likely writer is
`ElectricityRatePushService.HandleRateChangeAsync` (the live-sensor override), which rewrites a whole future
day as one point at the sensor's rate when it thinks an override runs past midnight; it fires at app start,
before REST has loaded. That is a hypothesis: if the hourly push has not corrected it, look there. Battery
*planning* is unaffected either way, since `BatteryControlService` reads rates from the reader, not from the
stored pricing.

**Why REST returned zero (researched 2026-10-17, all verified against Octopus's public API; since
addressed by `FourRateTariff` above).** The
new Intelligent Octopus Go products (`IOG-SMB-*`) are **four-rate EV tariffs**: in the product record
the tariffs sit under `four_rate_ev_electricity_tariffs`, not `single_register_electricity_tariffs`,
and the `standard-unit-rates` link is dropped. The rates are published instead as **single open-ended
records** from `day-unit-rates/` (35.6164p), `night-unit-rates/` (7.6p) and `standing-charges/`
(60.7016p), plus EV-device peak/off-peak variants (same values), all identical to what the HA sensors
show. The old `INTELLI-FIX` tariff encoded the cheap window by *slicing* `standard-unit-rates` into
time-bounded records (22:30Z–04:30Z, i.e. 23:30–05:30 BST), which is all `OctopusReader` knows how to read.
**The API does not say when the night rate applies** — only the product description ("23:30 - 05:30
every night") does — so any REST implementation has to hard-code the window; the HA integration does
the same. Smart-charge dispatch windows are not in the REST data at all.
Corroborated by the integration's own tracker: issue #1708 ("Intelligent Octopus Go new 6-hour cap tariff
structure is not handled, causing rates to stop updating"), fixed in integration v18.3.3 (EV-device rates
deferred). Open integration bugs on IOG off-peak/dispatch sensors as of this date, worth reading before
trusting them for dispatch-aware decisions: #1884, #1892, #1898, #1901.
Both the 26 Aug–9 Oct product (`IOG-SMB-FIX-12M-25-08-29`: 25.1214p / 3.327p from 5 Jul) and the current
one are four-rate, so the app's REST path has been blind to the *account's* real product since 26 Aug;
it kept looking right through 9 Oct only because the older `INTELLI-FIX` product happens to publish
identical numbers. The step from 26.37747/3.49335 to 25.1214/3.327 is in INTELLI-FIX's own published
history (both pairs appear in its records), not a VAT artefact.

**The committed `Backend/App/appsettings.json` holds 8-character placeholders for the Octopus
`AccountId` and `Token`** — the real ones live with the deployed app on the HA host, which is
not reachable from the dev machine. So `OctopusReader` cannot be exercised locally: a 404 from
`/v1/accounts/<placeholder>/` is expected and means nothing.

**The battery logic reads this in two places, so it is not just a dashboard problem.**
`BatteryControlService` plans from the REST `OctopusReader` (account REST endpoint, not
GraphQL) *and* uses the HA sensor as a live override (`ReactToRateChangeAsync`) to catch
smart-charge windows. (When this was written the sensor half was dead, so the override never fired;
it has been live since the 2026-10-17 restart.) The stored
pricing suggests the REST half is also returning nothing for import: `battery-pricing` holds
one flat slot per day, `importPrice` 3.327 for 10–16 Oct, then **0 for 17, 18 and 19 Oct**,
and 18/19 are future days that could only have come from the REST reader. 3.327 is the
frozen sensor value, not a real rate; the genuine peak rate was 25.1214p.

(An earlier note here guessed the stored rates went VAT-exclusive on 1 Oct. That was wrong: Octopus's
REST `value_inc_vat` for the old tariff is exactly 25.1214 / 3.327, so those were inc-VAT; the 5% step
is simply two different tariffs.)

**Buttons** — four Sonoff/eWeLink buttons bound by IEEE address, not entity ID (gotcha 12):
living room Christmas tree, dining room desk, dining room bookshelf, games room desk.
Driven through `zha_event` with `toggle` / `on` / `off` commands mapped to single, double
and long press.

**Bathroom** — `sensor.bathroom_temperature` and `sensor.bathroom_humidity`. An eWeLink
**SNZB-02P**, the only room thermometer that is *not* an SNZB-02D, so its battery and
tooling are unrecorded — don't assume CR2450. The device's other entities still carry a
stale `bedroom_3_temperature_and_humidity_*` prefix from a rename (gotcha 10). There is
**no TRV in the bathroom**, so it cannot act as a witness for boiler verification. Its
humidity sensor is the obvious shower detector if confounder filtering is ever implemented.

**Bedroom 1 clock — retired, not broken.** A Tuya "Temperature and Humidity Sensor with
Clock Function" is still physically in bedroom 1, but **is no longer used as a
thermometer**. Both its entities, `sensor.clock_temperature_and_humidity_temperature` and
`_humidity`, read `unavailable` and that is the **expected state — do not investigate it
as a fault**. It is not bound in `NamedEntities.cs` and nothing reads it.

Two traps here. Its friendly name is also **"Bedroom 1"**, so it collides with the real
sensor in any list sorted by name — the one the app actually reads is
`sensor.bedroom_1_climate_temperature` (SNZB-02D). And its `last_reported` shows this
morning's HA restart rather than a genuine report, which is gotcha 3 in miniature: a
recent timestamp on an `unavailable` entity means nothing.

**Robot vacuum: Dreame X50 Ultra Complete.** Owned (owner, 2026-10-19); until that day nothing in HA
knew about it. HA has no core Dreame integration; the community HACS one,
[Tasshack/dreame-vacuum](https://github.com/Tasshack/dreame-vacuum), lists the X50 Ultra Complete as
supported (`dreame.vacuum.r2532*` and `r2538*`, per its supported-devices page). **v2.0.2 was
installed through HACS on 2026-10-19** (`hacs.cjs`, then `restart-core.cjs`; HA 2026.2.2), domain
`dreame_vacuum`. Its config flow first asks for the account type (`dreame`, `mi`, `mova`, `trouver`,
`local`); the owner logs in themselves.
Its per-room entities (cleaning mode, suction, wetness, repeats) need a **cloud** login, not the
local-only mode, and its `vacuum_clean_segment` service ignores the suction and repeats it is given
while the robot's "customized cleaning" is switched on.

## Safety

Reads (`/api/states`, `/api/history/...`, `/api/config/config_entries/entry`) are free —
use them freely.

Writes via `/api/services/...` move real hardware in someone's home. Boiler, heaters and
valves affect comfort, gas use and safety.

- Ask before any write, and state the physical effect first.
- One fingerbot toggle equals **one press of the boiler's physical button**.
- A fingerbot press is not the only thing driving the boiler, and right now it is not
  driving it at all. A physical switch at the boiler chooses between the fingerbot and the
  boiler thermostat (gotcha 13); HA cannot read it, and **as of 2026-10-17 it is set to
  the boiler thermostat, which leaves the boiler's button dead**. (`Backend/App` is now gated on
  the `heating.boilerControlPath` device setting and leaves the boiler alone by default,
  but that gate does not apply to a service call you make by hand.) So a press you make may do
  literally nothing, and heat you observe afterwards is probably not yours. Say that
  rather than reporting a press as "boiler now on".
- Read the state back afterwards and report what actually changed, not what the HTTP
  status implied.
- Never sign out, change credentials, or touch anything the task does not need.
