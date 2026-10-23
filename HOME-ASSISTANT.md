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
| `/api/error_log` | GET | **404** | Unavailable. Use the two below, or `system_log/list` over the websocket for warnings and errors with tracebacks |
| `/api/hassio/addons/{slug}/logs` | GET | works | An add-on's log as plain text, last 100 lines (`tools/ha/addon.cjs <slug> log`). Verified 2026-10-19; the same proxy refuses `/info` with 401 |
| `/api/hassio/core/logs` | GET | works | HA Core's own log, plain text, including debug lines after `logger.set_level` |
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
| 17 | The NetDaemon app forgets what it saved after an add-on restart | The Supervisor recreates an add-on's container on restart, so anything the app writes outside `/data` (or the mapped `/config`) is gone. Seen 2026-10-19: the boiler belief saved in the user profile was lost, the app assumed the boiler was off, and it never pressed it off. Inferred from the app's behaviour (it skipped a press it would otherwise have made). The add-on log can be read after all (`tools/ha/addon.cjs c6a2317c_netdaemon6 log`), but only the last 100 lines of the current run | The app's caches go in `/data/HomeAssistant` when it runs as an add-on (`LocalStorageFolder`). Anything else that must survive a restart belongs there too |
| 18 | The boiler fingerbot changed state with nobody pressing it | At 13:17:08 BST on 2026-10-19 `switch.tz3210_j4pdtz9v_ts0001` went `on` with **no context** in the logbook (no user, automation or service call: the device reported it). Either someone touched the fingerbot's own button or it sent a spurious report. The app counts any change it did not command as a press, flipped its belief and pressed 21 s later. If the report was spurious, that press really toggled the boiler | Run `tools/ha/who-changed.cjs switch.tz3210_j4pdtz9v_ts0001` before trusting any fingerbot change: "Supervisor" is the app, a named user is a person or a script on their token, and no context is the device. If uncommanded reports recur, the outside-press rule needs rethinking |
| 19 | The Dreame Vacuum login form says only "Could not login, check the credentials." | The form hides the cloud's answer. The real one is in HA's system log (websocket `system_log/list`, logger `custom_components.dreame_vacuum.dreame.protocol`). On 2026-10-19 it was `user password not match` with `maxAttempts 5` and a `remains` count, so the account and region were right, only the password was wrong, and **each failed submit spends one of five attempts** before the account is locked. The account had been created with Google sign-in, which the integration cannot use, so a password had to be set in the Dreamehome app first | Read the system log after the first failure instead of retrying the form. Test the password in the Dreamehome app itself before submitting it again |
| 20 | The phones' trackers say "home" all day, even when they are miles away | Home Assistant is only reachable on the home network (no external URL, Home Assistant Cloud not logged in; read from `/api/config` and `cloud/status`, 2026-10-19). Away from the Wi-Fi a phone's Companion app cannot reach it, so it sends nothing and the tracker keeps its last state, "home". Ste's and Ruth's trackers said "home" for all 10 days of history while the owner confirmed both phones were out many times; every one of their ~1,300 position reports was within ~150 m of home. Ellie's and Kevin's phones have location off as well ("unknown") | Do not use `device_tracker.*` or `person.*` for presence until remote access exists (Home Assistant Cloud, a VPN such as Tailscale, or router-based presence). Check a tracker's history for any "not_home" before trusting it |
| 21 | A "power ≥ X W for the last 5 s" check flaps a heater on and off every few seconds | `/api/history/period` runs behind live state by the recorder's commit interval: measured 2026-10-20 with the app's own query shape (second-truncated window ending now), the newest row was 4–6 s old in about half of 12 probes while `/api/states` already held the newer value. So a 5-second window cannot see its own last few seconds. Separately, a plug that is switched off keeps its last power reading (Ruth's desk plug showed a stale 32 W, yesterday 43 W, until it came back on; the heater plug likewise 1,875 W), so a state change to "on" is judged against that old number. On 2026-10-20 at 12:00 BST Ruth's desk plug was switched on and its load pulsed (~450 W for ~4 s, ~5–12 W for ~5 s). `IsDrawingSteadily` was fooled by the lag after each rise and the heater plug went on/off five times in 2 minutes (11:00:03 to 11:02:22 UTC; every switch matched a `Ruth's desk turned on/off` re-evaluation 6 s after a 30 W crossing) | Never build a short-window "steady for N s" rule on the history API. Fixed 2026-10-20: the three "for 5 seconds" presence detectors (Ruth's desk, den TV on now, living room TV) use `SustainedCondition`, which notes from live readings when a condition last became true; use it for any new rule of that kind. A 5-minute look-back on history is fine, since a few seconds of lag does not matter there. To inspect a flap: `who-changed.cjs` for the plug's switch, `entity-history.cjs` for the power sensor, and line the 6 s re-evaluation delay up against the 30 W crossings. **Still happens once per switch-on with the fix, because the dips are real:** on 2026-10-22, read from the recorder, Ruth's desk plug drew ~930–960 W just after it was switched on, 57–105 W for the next ~10 s, then one reading of 26–28 W (and at 07:35 UTC it then went 25 W ↔ 400–570 W every 5 s for 30 s). Each time the heater went on 5 s into the draw, off 6 s after the first dip, and on again once the draw had held ≥ 30 W for 5 s (07:34:48 / 07:35:03 / 07:35:39 and 13:44:02 / 13:44:17 / 13:44:22 UTC). `SustainedCondition` reported the load correctly; the "no hold" detector simply ended on any reading under 30 W. Fixed the same day (owner): once in use, Ruth's desk stays in use until the draw has been under 30 W for 15 s (`LatchedCondition`), and heating re-checks 16 s after a drop. Not deployed when written, and still not on 2026-10-23: switched on at 14:23:31 BST, the plug read 25 W once at 14:23:52 and 24 W once at 14:24:12, and the heater went off at 14:23:58 and 14:24:18, 6 s after each (the old delay), on build `20261022055140`. Check the running build before assuming a fix in this repo is live |
| 22 | A newly paired battery thermometer shows up as a device with no entities, and removing it is not in the usual place | On 2026-10-20 a SNZB-02D (the hallway one) joined ZHA with a device in the registry but **no entities**; `zha/devices` showed it with endpoints and quirk but an empty entity list, so nothing created `sensor.*_temperature`. It had gone back to sleep before ZHA finished its interview. A first attempt, plus a ZHA reconfigure (`zha/devices/reconfigure`), did not help. The second pairing sat at 0 entities for 4 minutes of button presses and then produced all 14 on its own (read from the registry; no ZHA reload was needed). Removing the broken device: `config/device_registry/remove_config_entry` fails ("Config entry does not support device removal") and `zha/devices/remove` does not exist. The service **`zha.remove` with the device's `ieee`** worked (it left the registry entry gone) | `pair-zigbee.cjs` now waits up to 10 minutes for the first entities (it used to accept 0 and rename only the device, leaving generic entity names) and says to press the device's button. To discard a half-paired device: call `zha.remove` with its IEEE, reset the device (hold its button ~5 s) and pair again. Removing a device does not forget its name: the re-joined one came back as `hallway_hall_climate_*` from the registry and was renamed again |
| 23 | The heating page says "Boiler off since …" but the boiler has been running for hours | The boiler state shown is the app's **belief**, kept by counting presses, so one press too many or too few inverts it until someone notices. On 2026-10-20 the 15-minute turn-on check (then `TurnOnWaitMinutes`, ≥ 1.0 °C rise on a TRV's own sensor) failed when the boiler had in fact fired: at 14:47 BST the dining radiator was +0.5 °C and went on to rise 4.8 °C; at 18:20 Bedroom 3 was +0.1 °C, with its first rise at 18:33, 28 min after the press. It also counted the dining valve, which that same pass had just shut. Each failure caused a **corrective press**, which toggled a boiler that had been right all along. After the 19:02 "off" the boiler ran all night while the app showed it off. Nothing noticed, because the turn-off check watched only Bedroom 3, whose valve was left open at 22.5 °C ("valves wait for the boiler") and so regulated itself, falling 22.0 → 21.5 °C between cycles, which the check read as "PASSED". Read from the add-on log, the fingerbot's logbook and TRV history on 2026-10-21. The press counts from 18:05 do not fully add up either (three presses from a running boiler should leave it off), so at least one press HA acknowledged probably did not toggle the boiler; which one is unknown. **Fixed in code 2026-10-21 (not deployed when written):** the check now watches only valves that are open *after* the pressing pass; a turn-on passes on a 1 °C rise seen at any check and fails only if none has come within 60 min (`TurnOnDeadlineMinutes`); a turn-off fails only if an open TRV heats up again after settling (1 °C off its low and back to its turn-off reading, as Bedroom 3's cycling would have shown), and a falling or steady reading no longer decides anything. Anything weaker clears the check without a press. Tests replay all three cases; see CLAUDE.md → "A failed verification presses the boiler" | Before trusting "Boiler on/off", check that some open TRV's onboard reading rises and falls (`trv-status.cjs`, TRV history), and look for `verification FAILED` / `state mismatch` in `addon.cjs c6a2317c_netdaemon6 log … --from 40000`. To bring them back in line, press the **boiler's own button by hand**, which the app cannot see. Pressing the fingerbot from HA or a script while the app runs does not help: the app counts it as an outside press and flips its belief along with the boiler, so they stay inverted. **Remotely:** `tools/ha/realign-boiler.cjs --apply` stops the add-on, presses the fingerbot once and starts it again, so the app never sees the press (done 2026-10-21 22:40 BST: fingerbot on → off, the app came back believing off) |
| 24 | The heating page says "Boiler on since …" but the fingerbot never moved | On 2026-10-21 at 21:01:16 BST the app decided to turn the boiler on (den in use) and sent `switch.turn_on` to the fingerbot. Zigbee did not deliver it: HA answered the websocket call with an **error**, `home_assistant_error: Failed to send request: device did not respond`, 17 s later (so over the websocket a ZHA failure is *not* a silent 200, unlike REST). Both den TRV commands in the same burst failed the same way and got through on the next pass 57 s later. But NetDaemon's `TurnOn()` is fire-and-forget: the error only reaches the add-on log as `NetDaemon.Client.IHomeAssistantConnection: Failed command (call_service)`, and `PressBoilerFingerbot` had already returned true, so the belief and the UI went to ON and the press was never retried. The switch stayed `off` with no state write since the previous evening (read from the add-on log, `entity-history.cjs` and the state's `last_reported`). Worse, while the belief is wrong the app's next "off" reads the fingerbot as off, sends `turn_on`, and so **turns the boiler on** while believing it off. That is exactly what happened at 21:58:40: the fingerbot moved to `on` 2 s after the app's "Boiler turned OFF". The turn-on verification needs 60 min before it fails (`TurnOnDeadlineMinutes`), so it is no quick net for this. Why the commands were not delivered was not established; zigpy logs `ZIGBEE_DELIVERY_FAILED` for several other devices at the same period, and the fingerbot and den TRVs route through games room plugs | **Not fixed by the 2026-10-21 deploy, which waited for nothing:** NetDaemon's `CallServiceAsync` extension it used does not wait for HA's answer either, so overnight a failed and a timed-out command were again only logged, and "Boiler turned ON" was logged at 02:42:17, 20 s before HA's timeout for that press (`did not get response in timely fashion`), on 2026-10-22. Its tests used a fake that threw. **Fixed again 2026-10-22, tested against NetDaemon's real client:** every service call the app makes now waits for HA's answer (`ServiceCallRetrier`, wrapping `IHaContext` in `Services/ServiceCallRetries.cs`) and an error is retried after 30 s, 1, 2, 5, 10 and 15 min, unless a newer command to the same entity has been sent since. The fingerbot is the exception, since a retried press that had landed after all would be a second press: its presses are sent once and confirmed (`TurnOnConfirmedAsync`), a failed press leaves the belief alone, and the boiler is decided again 1 then 2 min later, then by the 5-minute recheck. Its log line is "The boiler fingerbot press failed". If the fingerbot moves later anyway, the outside-press watcher counts it and flips the belief to match. To check by hand: after a "Boiler turned ON/OFF" line, the fingerbot's state should have changed within seconds |
| 25 | A "TV on" threshold on a plug's power either flaps while someone watches, or fires when nobody is there | A TV's draw follows the picture, and its standby is not flat. Den TV, read from the plug's recorder history on 2026-10-22: about 4 W in standby, but 35–80 W on its own at night (about 40 W for a minute or two most hours, 55–80 W for 35 minutes around 3 am, cause unknown: nobody knows if anyone was watching); watching YouTube, anywhere from 73 to 160 W, changing every reading; a single reading of 185 W the evening before. The ranges overlap, so no single threshold separates them: 30 W made the den "in use" at night and started the boiler, and 100 W flipped the den motion lights six times in two minutes. The plug reports every ~5 s, so an automation's "for 5 s" is about one reading and smooths nothing | Use two thresholds (on at a high one, off only below a low one) rather than one, and read a draw over minutes before choosing them. Den TV: on at ≥ 100 W, off at ≤ 30 W, in both the app (`PowerLatch`) and the motion-lights automations |
| 26 | A device just added to HA is missing from the 3D house view, though the page's list was rebuilt a moment ago | The view's device list (`HouseDevicesService.ReadDevices`) comes from NetDaemon's registry (`IHaRegistry.Devices`), which in the running app did not include a device added after it started. Seen 2026-10-23 with the den tall lamp on the LocalEnv stack: added and named in HA at about 12:45 BST, still absent from a list rebuilt at 12:52, while the lamp's state read fine from HA. The guard proxy passes events through, so it is not the cause. Not established whether NetDaemon refreshes on some registry changes, or only at startup | Restart the NetDaemon app after adding a device you want in the 3D view (production: `addon.cjs c6a2317c_netdaemon6 restart`), or check `house-devices` for it before assuming it is there |
| 27 | The heating page says "Boiler off" but the radiators say it is running, and no verification ever catches it | Seen 2026-10-23: the app pressed the boiler on at 14:23 BST, and pressed it off at 14:36:58 when no room was calling. HA answered "The operation has timed out", so the app kept its belief (on); 11 s later the fingerbot reported it had moved, which the app took as a press **outside the app** and flipped its belief to off. The boiler kept running: Bedroom 3's open valve climbed 17.3 → 21 °C from 14:37 to 15:28 and den radiator 2 (shut) 20 → 27 °C. No turn-off verification was armed, for two reasons found in the code afterwards: it only watched valves that had been open with the boiler on for 30 minutes (`RadiatorSettledMinutes`), and at 14:37 Bedroom 3's had been for 14; and an outside press called the verification's `Reset`, which wiped that valve history anyway, so a movement to "off" could never have armed one. The skip was logged at Debug only. So any turn-off within half an hour of a turn-on, and any belief flipped to "off" by a fingerbot movement, went unchecked. Resolved at 15:53 with `realign-boiler.cjs --apply` (one press with the app stopped), and the owner confirmed at the boiler that it went off, which also confirms it had been running since 14:23 | Read den radiator 2's own sensor (a high-20s plateau means the boiler is on), then `realign-boiler.cjs`. **Fixed 2026-10-23, live since 17:30 BST that day (NetDaemon `20261023162858`):** every open valve is watched at a turn-off, one with under 30 minutes of hot water judged only by climbing to a new high; a fingerbot movement keeps the valve history; a movement within 50 s of a press HA called failed, the way that press asked, is taken as the app's own press ("landed after all" in the add-on log); and a turn-off that cannot be checked is logged. Later the same day (owner), for turn-ons and turn-offs alike: every press has a valve to check against (one is opened if none is open, and one is held open with its setpoint 2 °C clear of its reading until the check ends: the warmest radiator for a turn-off, the coldest for a turn-on), and a corrective press that did not work is pressed again, up to 5 in a row, each waiting longer after its failed check (0, 5, 15, 30, 60 min), then an HA persistent notification. See CLAUDE.md → Heating |

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
| 100% | Hallway – Climate (new 2026-10-20) | `sensor.hallway_climate_battery` | `HallwayClimateTemperature` |
| 100% | Den – Radiator 1 / 2 (TRVs, new 2026-10-18) | `sensor.den_radiator_*_battery` | `DenRadiator*Thermostat` |
| 100% | Kitchen temperature and humidity | `sensor.kitchen_temperature_and_humidity_battery` | `KitchenTemperature` |
| 100% | Bathroom – Climate | `sensor.bedroom_3_temperature_and_humidity_battery` | `UpstairsBathroomTemperature` (gotcha 10) |
| 100% | Games Room Desk / Dining Bookshelf / Christmas Tree – Buttons | `sensor.*_button_battery` | `*Button` (bound by IEEE) |
| 100% | Porch motion, Motion-Sensor-02 | `sensor.ewelink_snzb_03p_battery`, `sensor.motion_sensor_02_battery` | `PorchMotionSensor` |
| 100% | Kitchen flood detector | `sensor.sonoff_snzb_05p_battery` | not used |

### Per-device notes

**Router: Linksys MX5500 Velop AX** (mesh), the default gateway at `192.168.1.1`, firmware 1.1.7.216940
(2026-07-09). Read from the router on 2026-10-19: its JNAP API (`POST /JNAP/` with an `X-JNAP-Action` header)
answers `core/GetDeviceInfo` without a login, but the connected-devices list (`devicelist/GetDevices`) needs the
admin password. Home Assistant has discovered it over UPnP as "Linksys10796" (a pending, un-set-up `upnp` flow,
which gives traffic stats only, not devices). The phone trackers are useless while HA is only reachable at home
(gotcha 20), so this is the candidate for presence: the community **Linksys Velop** integration
(`uvjim/linksys_velop`, HACS custom repository, maintained) has device trackers for chosen Wi-Fi devices.

**Installed 2026-10-19; working since HA Core was upgraded on 2026-10-20** (integration 2026.10.1 on HA
2026.10.2). The trackers update every ~10 s with the phone's IP, and away is declared 180 s after a phone drops
off (`consider_home`). History, from HA's system log and states:
- **On HA 2026.2.2 it could not work.** Release 2026.10.1 does not load there (it uses
  `SensorDeviceClass.UPTIME`, which that HA lacks, though its `hacs.json` claims HA 2026.1 is enough), and
  under 2026.9.1 the entities were written only when the entry loaded, never on later polls, so trackers were
  stuck at whatever was true at load. A config entry created by a newer release fails with `migration_error`
  under an older one; downgrading means deleting and re-adding it (the owner types the router password).
- The router's node status sensors (`binary_sensor.router_status`, `child_node_1_status`, `child_node_2_status`)
  crashed in `pyvelop` (`super(type, obj): obj (instance of NodeAdapterInfo) is not an instance ...`) under
  2026.9.1 and starved the trackers, so they were **disabled** in the entity registry. Still disabled; not
  re-tested on 2026.10.1.
- **Phones appear under random names.** Android phones use a random Wi-Fi address per network and show up as
  `Android_XXXXXXXX`. **Ellie's phone (Samsung Galaxy A56, SM-A566B) is `Android_17YGONY5`**, found by its IP
  (192.168.1.253 on 2026-10-19), router id `0461c717-bf51-4b13-b17a-f58c2e0cada0`, tracked as
  `device_tracker.android_17ygony5` (read `home` 2026-10-20). The
  entries named `Ste-s-A54` and `Ruth-s-A55` (also tracked) were not online while both phones were at home, so
  they are probably stale entries from before the random addresses, and the phones' current entries are
  unknown. Find each by the IP the phone shows in its Wi-Fi settings, as for Ellie's.
- Everything the router sees online is in `sensor.mesh_online_devices` → `devices` (name, router id, IP, which
  Velop unit). **Use this, not the trackers, for presence:** on 2026-10-20 Ste's phone (router name "unknown",
  MAC `A2:10:03:4A:15:87`, id `ce8b9c19-dd29-4250-8b90-6fccf8cef4b9`, IP 192.168.1.129 per the phone) was in
  this list on Child node 1 with no IP, while its tracker stayed `unknown` for good: the trackers' per-device
  query returns no connection data for phones on a secondary unit. Ellie's tracker works only while she is on the
  main router. The NetDaemon app's phone detectors read this list. Trackers kept, renamed
  `device_tracker.ste_phone_wifi` and `device_tracker.ellie_phone_wifi`. **Ruth's phone (Samsung Galaxy A55,
  SM-A556E) is `Android_SBFUAFP4`**, router id `ec6744ee-6707-4f8d-8fa4-6de13650bdb7`, MAC `8E:60:52:EE:88:75`,
  192.168.1.22, tracked as `device_tracker.ruth_phone_wifi` since 2026-10-20 (read `home`). Found without
  touching her phone: it was the only device to come online after she woke, minutes after her HA app reported in
  for the first time since the night; not confirmed from the phone itself. The stale `Ruth-s-A55` entry (on the
  router's offline list) is no longer tracked. **Ruth's phone goes into flight mode overnight** (owner,
  2026-10-20), so it is off the Wi-Fi while she sleeps and her phone detector reads "away" at night.
  "Android_304051d4d2134f188932394429bf9c2e" (192.168.1.26) was online while her phone was in flight mode, so it
  is not hers; not identified.

**HA Core upgrade, 2026-10-20: 2026.2.2 → 2026.10.2.** A full backup was taken first (slug `9066c98e`, 281 MB,
on the host and copied to `C:\Users\stero\HomeAssistantBackups\9066c98e.tar`, hash-checked); restoring it puts
back 2026.2.2 with every add-on. Only Core and Linksys Velop were updated; the OS (17.1), the add-ons (NetDaemon
26.3.0 must match what the app is built against) and the other HACS integrations (SolaX, Hypervolt, MG) were
left on their old versions, which all loaded. Compared with a snapshot taken before: no entity disappeared, none
that worked became unavailable, no errors in the system log; ZHA gained ~50 entities (TRV boost/timer controls,
plug LED switches) that nothing uses. The NetDaemon app reconnected by itself within about a minute and resumed
from its saved boiler belief.

**Zigbee mesh** (read from ZHA's neighbour tables on 2026-10-20 with `tools/ha/zigbee-mesh.cjs`; LQI 0–255).
The coordinator is a SONOFF ZBDongle-E in the living room; the 15 routers are the smart plugs. Strong near the
hub and upstairs (bedroom 3 plugs relay most of upstairs). **The den is the weak spot:** its four plugs reach the
hub at only 41–73 and none hears it directly; den traffic leaves via the kitchen heater plug (60 to the hub). The
**boiler fingerbot's parent is the games room desk plug**, so boiler control depends on that weak path. The
washing machine plug is barely heard (10). The porch sensor's last parent was the bedroom 3 lamp plug; there is no
router near the porch. **"Bookcase Light Strip" is a dead router** (not seen since 2026-08-10) still in ZHA;
remove it or power it. Bedroom 2 and the bathroom have no router of their own. Most useful addition: a mains
Zigbee plug between the den and the living room (the hallway has no sockets; see the den re-read below); another
near the front door for the porch sensor.

**Den re-read 2026-10-21 ~23:05 BST** (same script, plus each den router's full neighbour table and HA's system
log). Unchanged in shape. *Inside* the den the links are good: the four plugs hear each other at 138–201, and every
battery device's link to its parent is fair to strong (Den - Radiator 1 221, Games Room desk climate 173, desk
button 172, Den - Radiator 2 156, Den - Climate 129; the weakest is the **fingerbot, 106, to the games room desk
plug**). Everything except the fingerbot (both den TRVs, both thermometers, the desk button) hangs off the
**games room heater plug**. The way *out* of the den is the problem: the hub hears the den plugs at 46–65 and none of them lists the hub;
the best exit is the kitchen heater plug (104–138 from the den), whose own link is 98 hub→plug but only **31
plug→hub**; the next is the dining bookshelf plug (61–85 from the den, 176/113 to the hub). Failures since
2026-10-20 (add-on log): `device did not respond` on the den TRVs (12:30 and 21:01, each in a burst with other
TRVs), the fingerbot (21:01, gotcha 24), the games room desk plug (09:28) and also the **dining** TRV twice, so not
only den devices fail; zigpy's `ZIGBEE_DELIVERY_FAILED` hit 33 times over the same period across five devices, two
of them in the den. The failures come in bursts of several commands to sleepy devices at once, which a weak
backhaul makes worse. The fix stands: a mains router between the den and the hub. **The hallway has no sockets**
(owner, 2026-10-21), so a plug there is out; the options are the living room's wall nearest the hallway, moving the
Zigbee dongle that way on a USB extension, or an always-powered in-wall switch module (needs a neutral) on a
hallway light.
`zha/devices`' own `lqi`/`rssi` per device are no use for this: all ten den devices read 180 / −55 dBm (or close),
which looks like the coordinator's reading of the last hop into it, not each device's own link (inferred from the
identical values; not confirmed in ZHA's source). Use the neighbour tables.

**House layout as the mesh shows it** (drawn from the same tables, 2026-10-21; the owner said it was right apart
from left and right being swapped, and on 2026-10-22 that the den runs all the way to the back wall, both corrected
here; room sizes, doors and the rooms without a plug are still guesses). Ground floor, seen from the front: the
den/games room down the whole left side, front to back; then the hallway (with the stairs and the cupboard under
them) and porch, with the kitchen behind the hallway; then the living room (hub) at the front right with the dining
room behind it. The den reaches the rest by radio mainly through the kitchen heater plug, which is why its links
to the hub are so weak. First floor: Bedroom 1 directly over the living room (its heater plug and the
hub hear each other at 182–190), Bedroom 3 at the front over the hallway and porch, Bedroom 2 over the dining
room, and the bathroom (least certain) over the kitchen. Nothing heard over the den. Interactive plan:
https://claude.ai/artifact/XbaeYg9sZ5XAZE8VBmQSQv (private to the owner). The web app's 3D view draws the same plan
from `Frontend/src/utils/houseModel.js`, which is the copy to correct when more is learnt about the house.

**WiZ lamps.** Five Wi-Fi bulbs (the desk lamp and porch light both report WiZ app `homeId` 12642957 over UDP
`getSystemConfig` on port 38899, read 2026-10-23; the others were off): Games room desk lamp, Lamperion
Bane of Shadows (Bedroom 2), Porch light, Living room lamp and, **since 2026-10-23, the Den tall lamp**
(`light.den_tall_lamp`, model SHRGB, MAC `98:77:d5:18:12:e0`, 192.168.1.223 by DHCP, area Games Room, i.e. the
den). The owner plans more than one den lamp, so "tall" is part of the name. A new bulb is invisible to HA until it
has been added in the WiZ phone app, which puts it on the Wi-Fi: before that HA's WiZ discovery finds nothing and the
bulb is not in the router's list. Once it is on, HA discovers it by itself within a minute or so, as a pending `wiz`
flow (`config_entries/flow/progress`). Confirm that flow, then name it with `rename-device.cjs --platform wiz`
(tools/ha). It is a tall standing lamp in the den's front left corner, between the TV and the sofa (owner), and is
placed there in the 3D view's `DEVICE_SPOTS`. The view only shows it once the NetDaemon app has been restarted since
the lamp was added (gotcha 26).

**Where things are** (owner, 2026-10-22; drawn in the 3D view, with every device below at a fixed spot in
`DEVICE_SPOTS`). Laminate floors throughout (planks running front to back), off-white walls, grey carpet on the
stairs, wooden four-panel internal doors (the hallway to living room one slides). The den is **9 m long and 3.6 m
across** at its widest, which sets the model's scale; its sofa is a DFS Euphoria corner sofa in duck egg (short end
along the front wall against radiator 2, long side running back towards the middle, facing the TV), its TV 65", the
living room's 49"; the living room sofas are a purply red. The kitchen worktops and breakfast bar are white marble.
The hallway's radiator is at the kitchen end of its right wall, and the kitchen door opens into the hallway. *Den:* Ste's corner desk in the back left corner (desk plug, button,
thermometer, WiZ lamp and heater plug there), window in the back wall with a back door to its right, the **boiler
in a cupboard in the back right corner** (so the fingerbot is there, though HA gives it no area), a toilet next to it
along the right wall, Den radiator 1 on the toilet's wall and radiator 2 on the front wall, TV on the left wall
towards the front, a door near the front of the front wall, and a door to the kitchen next to the stairs. *Kitchen:*
doors from the hallway and (near the front) to the dining room, window at the back, worktops in a U (sink under the
window, hob on the left wall, then the built-in oven and fridge), a breakfast bar from the middle of the right wall,
and the heater, a **small portable unit on the front wall**. *Dining room:* back door at the left of the back wall,
window the rest of it, table and six chairs in the middle, Ruth's corner desk in the front left corner, her desk
heater on the right wall pointing at the desk, the radiator on the left wall. *Living room:* open to the dining room
along the right half of its back wall, TV on the left of the back wall, the stick lights in the middle of it, the
**Zigbee hub in the front right corner**, window and radiator on the front wall with a sofa in front, another sofa
on the right wall. *Hallway:* a radiator on the right wall that nothing controls, and the front door to the porch.
*Porch:* open at the front; the porch light is left of the front door. Not described yet, so only placed by guess
or on a grid: the dehumidifier plug (put next to the desk heater's plug, since the two hear each other at 255), the
door from the hallway to the living room if there is one, and everything upstairs.

**Porch motion detector** — eWeLink SNZB-03P, `binary_sensor.ewelink_snzb_03p_occupancy` (drives the porch
light automation). **Drops off Zigbee repeatedly**, read from recorder history on 2026-10-20: unavailable spells
on 15, 16 (twice), 17 and 18 October, from seconds to two hours, each ending by itself, and again from 04:19 BST
on 20 October. Its battery read 100% each time just before, so a weak link from outside the house is the more
likely cause than the cell. While unavailable it sees no motion at all (gotcha 6). Fixes if it persists: a mains
Zigbee device nearer the porch to act as a repeater, or re-pairing. Last motion before the 20 October outage:
22:15 BST on the 19th.

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

**A change of the fingerbot's state does not prove the boiler was pressed** (owner, 2026-10-22). At 02:42 BST on
2026-10-22 the app's `turn_on` got no answer from HA within 20 s, the switch reported `on` by itself 32 s later (no
context in the logbook), and the boiler never fired: two open den valves and later an open Bedroom 3 valve all kept
cooling for three hours (read from TRV history). So the state is at best "the command reached the fingerbot", not
"the button was pressed", and certainly not what the boiler is doing. Only radiators warming or cooling behind open
valves say whether the boiler runs. Anything that reads a press from this switch (the app's outside-press watcher,
`tools/ha/realign-boiler.cjs`) is reading a claim, not a fact.

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

**Hallway climate** — `sensor.hallway_climate_temperature` / `_humidity` / `_battery`, device "Hallway - Climate", a Sonoff
SNZB-02D in the **Hallway** area, paired 2026-10-20 with `tools/ha/pair-zigbee.cjs`. Its first reading was 25.7 °C / 50.1 % with the
battery at 100%. It was awkward to pair: see gotcha 22. In `Backend/App` it is `HallwayClimateTemperature`, read by the
`hallway` zone, which is a thermometer only (no switches, so no boost, schedules or share link). That zone also needs a room
named "Hallway" in the schedules blob and a place in the layout (the heating page cannot add rooms): see CLAUDE.md → Heating.

**Radiator TRVs** — `climate.{room}_radiator_thermostat` in bedrooms 1–3, dining and living
rooms, plus `climate.den_radiator_1_thermostat` and `climate.den_radiator_2_thermostat` (paired 2026-10-18, named "Den - Radiator 1/2", in the Games
Room area; wired into `Backend/App` as the `den` zone). Battery powered, 94–95%
(the den ones 100%, new). Expose `hvac_action`, `child_lock`, `open_window`,
`local_temperature_offset` and `external_temperature_sensor_value`. **Nothing in this repo
writes the external-temperature registers**, so a TRV's own reading is independent of the
room thermometer — bedroom 1 read 17.8 °C at the room sensor and 16.4 °C at the TRV
simultaneously on 2026-10-17. Schedules and the UI use the room sensor; boiler verification
uses the TRV.

**Den radiator 2's own sensor tracks the boiler even with its valve shut** (read from the recorder, 2026-10-20/21).
With `hvac_action` idle and its setpoint at 17 °C it climbed from about 22 to 27 °C over two hours after the boiler came
on at 14:41 BST on the 20th, held at 27.2–27.7 °C all night while the boiler ran, and began falling (about 0.5 °C every
7–10 minutes) at 05:29 on the 21st, minutes after the owner switched the boiler off by hand. Either the valve passes
water when shut or the sensor sits by the flow pipe. That makes it the clearest single sign of whether the boiler is
running: a plateau in the high 20s means on, a steady fall means off. The other TRVs only show the boiler while their
valve is open. **It is the valve, not the sensor's position** (owner, 2026-10-23): the radiator itself was hot with its
valve shut (setpoint 17 °C, `idle` since 09:35) while radiator 1, also shut, was cold. Same in the recorder that
morning: radiator 2 climbed 22.1 → 25.6 °C while idle as the boiler ran 08:34–09:10, while radiator 1 only fell. Not
known whether the TRV's pin does not close the valve fully or the valve body leaks. Both den TRVs read
`valve_closing_degree` 100 and `valve_opening_degree` 100 (Sonoff cluster 0xFC11, read over ZHA's websocket on
2026-10-23; the HA entities are disabled), so it is not that setting. Their calibration readings differ: radiator 1
closing/opening limit voltage 2584/2195, radiator 2 2394/2512 (meaning not established). The owner suspects a bad
travel calibration. Pressing the button 3 times on a fitted, working TRV did **not** re-run it (owner, 2026-10-23).
Sonoff's TRVZB help page (help.sonoff.tech/docs/trvzb, read 2026-10-23) has the valve learnt only after power-on:
batteries out and back in, wait for the icon to stop flashing, fit it to the valve, short-press the middle button,
"Ad" flashes for about 5 s, then it shows the temperature. Do not hold the button while putting the batteries in
(factory reset, "FA", unpairs it). Its errors F2/F3 mean the travel came out too wide or too small. **"LC" on the
display is the child lock**, which holding the middle button about 3 s toggles, after which the buttons do nothing;
it went on while the owner was pressing on 2026-10-23 and was turned off from HA (`switch.den_radiator_2_child_lock`).
**Relearnt at about 16:20 BST on 2026-10-23** by that power-on sequence ("Ad" shown); it stayed paired. Its limit
voltages then read closing/opening 2542/2147, the same way round as radiator 1's (2584/2195) instead of reversed.
Whether it now stops the water is to be judged at the next boiler run with its valve shut: its sensor should stay near
the room's temperature rather than climb into the high 20s.

**A quick check for a badly calibrated TRV** (all seven read 2026-10-23, after radiator 2's relearn): every TRV that
shuts properly has `valve_closing_limit_voltage` well above `valve_opening_limit_voltage` (closing/opening, mV: living
room 2983/1721, bedroom 1 3056/1973, bedroom 2 3117/1816, bedroom 3 2783/2031, dining 2494/1660, den 1 2584/2195,
den 2 2542/2147). Den radiator 2 was the only one the other way round (2394/2512), and the only one whose own sensor
climbed 2 °C or more while its valve was shut in the 10 days of recorder history (four times; the others never). One
case, so a correlation rather than a rule. All seven have both degrees at 100.

**The NetDaemon app now watches for both faults** (once deployed; written 2026-10-23): a valve letting water through
while shut, and one staying cold while open, flagged on the heating page with a "Valve" badge and explained in the
zone's Devices list (CLAUDE.md → Heating, `ValveHealthMonitor`). `tools/ha/trv-shut-leaks.cjs` asks the recorder the
first question directly, for the last 10 days. Battery TRVs answer attribute reads slowly; several timed out at the first try.

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

**Upstairs bathroom towel rail** — a heated towel rail on the boiler's circuit with **no valve and no switch**
(told to us by the owner, 2026-10-20): it is hot whenever the boiler runs and cannot be turned on or off on its own,
and there is nothing for it in Home Assistant. Its only reading is the room thermometer
(`sensor.bathroom_temperature`, an SNZB-02P). Since 2026-10-20 the app treats it as a radiator that is always open
(`TowelRail` in `Backend/App/Services/Climate`), so the bathroom can be scheduled and boosted and calls for the
boiler like a valve room. The flip side: it is also hot whenever the boiler runs for any other room.

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
