# Frontend

Vue 3 single-page app for the house: heating schedules, battery and energy charts,
device setup, and over-the-air updates for the NetDaemon app. Talks to the Azure
Functions API in `../Backend/HomeAssistant.Functions`.

Deployed to Azure Static Web Apps at `https://homeassistant.everymilesoftware.com`.

## Running it

```bash
npm install
npm run dev          # Vite dev server on http://localhost:3000
```

Point it at an API by creating `.env.local` — see [ENVIRONMENT_SETUP.md](ENVIRONMENT_SETUP.md).
For a local backend, `VITE_API_URL=http://localhost:7071/api` plus `VITE_LOCALHOST_KEY`.

| Script | Does |
|---|---|
| `npm run dev` | Vite dev server, port 3000 |
| `npm run build` | Production build into `dist/` |
| `npm run preview` | Serve the built output |
| `npm run test` | Vitest, watch mode |
| `npm run test:run` | Vitest, single pass |
| `npm run generate-icons` | Regenerate PWA icons from SVG via `sharp` |

Tests are Vitest files next to the pure helpers in `src/utils/` (`energyDistribution`,
`roomLayout`, `rooms`, `roomDevices`, `houseModel` and others). There are no component tests.

## Routes

`vue-router` 4 in HTML5 history mode. SPA fallback for Azure Static Web Apps is
`public/staticwebapp.config.json`.

| Route | View | Notes |
|---|---|---|
| `/` | `LandingView.vue` | Feature cards |
| `/heating` | `HeatingView.vue` | **The** heating UI — room cards, schedules, boost |
| `/battery` | `BatteryView.vue` | Battery zones, pricing, energy charts and tables |
| `/3d` | `House3DView.vue` | **3D house** (since 2026-10-22, the landing page's "3D" card): every Home Assistant device in its room, see below |
| `/manual-import` | `ManualImportView.vue` | Hidden recovery page, not linked from anywhere. Restores missing daily energy data by distributing a total across hours using another day's shape. |
| `/r/:token` | `GuestRoomView.vue` | **Guest page** opened from a zone's or group's QR code (since 2026-10-19): the zone's temperatures (a group lists each zone) and an override form (16–24 °C, up to 6 h) that stays available while one runs, so it can be changed, plus cancel. `App.vue` renders it bare — no header, menus or house-ID prompt — and it never reads or writes the house-ID cookie; it talks only to the `guest/*` endpoints with the token (`services/guestApi.js`) and refreshes every 30 s. A revoked or unknown token shows "This link is no longer valid". |

**Sharing a zone or group.** An expanded zone card has a quiet **Share** link beside "Devices (n)", and an
expanded group card one beside "Devices in …"; both open `ShareRoomModal.vue` (`room` or `group` prop) with the
QR code (the `qrcode` package, rendered in the browser), Copy link, Print, and "Revoke and make a new link"
(`services/roomShareApi.js`, which takes `{ roomId }` or `{ groupId }`).

**Boosts.** Every boost sets the exact temperature, up or down. A zone card's ⚡ stays available while its own
boost runs, so boosting again changes it. A group card has its own ⚡ (`BoostModal` with `is-group`), which puts
the same boost on every zone in the group in one save, each tagged with the group's id (`handleGroupBoost` in
`HeatingView.vue`, `groupBoost` in `utils/boosts.js`). That supersedes any boost already inside. While a group's
boost runs, everything inside it is locked: the zones' and smaller groups' ⚡ are disabled, a zone's boost card
says "Boosted with …" instead of offering Cancel, and the group itself shows "Boost running: … until …" with
**Cancel boost**, which clears only its own boost. `LayoutNode` works out each card's `lockedBy` with
`roomLockedBy` / `groupLockedBy` (tested in `boosts.test.js`). The guest page shows the same lock. The
server-side rules and the security model are in the Functions README and CLAUDE.md → "Access".

**The 3D house** (`/3d`). [HouseScene.vue](src/components/HouseScene.vue) draws the plan in
[utils/houseModel.js](src/utils/houseModel.js) (tested) with three.js: rooms as floor slabs with see-through walls
(a wall two rooms share is drawn once), stairs in the hallway, both storeys (the first lifted clear of the ground
floor under "Both floors"), and one marker per device. Laminate floors, doors (standing open), windows and
open-plan gaps (`OPENINGS`, cut out of the walls by `cutOpenings`) and furniture (`FURNITURE`, built by
[houseFurniture.js](src/components/houseFurniture.js)) come from the owner's description; a fitting with `litBy`
glows while that device is on (the stick lights and the heaters). A device listed in `DEVICE_SPOTS` (by its HA
name) sits at that spot and height; any other goes in the room its Home Assistant **area** maps to
(`roomForArea`), on a grid across the room's `free` area in alphabetical order; one with no area, or an area the
plan does not know, goes on the "Not in a room" pad beside the house. A marker's shape is its kind (`deviceKind`) and its colour
its status (`deviceStatus`: amber on, grey off, red unavailable or wet, blue a reading). `OrbitControls`: drag or one
finger turns it, scroll or pinch zooms, right-drag or two fingers move it. Tapping a marker (a press that moved under
6 px) opens [HouseDeviceModal](src/components/HouseDeviceModal.vue): status, the entities that can be used, its
readings, and everything else folded away. The plan's geometry, room sizes and the area mapping all live in
`houseModel.js`; the layout was inferred from the Zigbee mesh and corrected by the owner (den on the left, front to
back), see HOME-ASSISTANT.md → Zigbee mesh. The data comes from `services/houseDevicesApi.js`: the snapshot the
NetDaemon app uploads (`GET house-devices`), a request for live states on opening and every 4 minutes
(`house-devices/watch`), and commands. Switches and lights send `turn_on` / `turn_off`, buttons `press`; the panel
shows "Sending…" until `house-device-command-result` arrives (or "No answer from the house" after 20 s), and the
switch itself only moves when the new state arrives. Only entities whose `actions` the snapshot lists can be used;
the rest show `controlledBy`.

Devices are drawn by [houseDeviceModels.js](src/components/houseDeviceModels.js): smart plugs as white UK sockets,
buttons as white push buttons, thermometers white with an LCD that shows their own reading, each with a small LED
in its state's colour; wall-mounted kinds turn to face away from the nearest wall unless their spot gives a
rotation. The household are `person` items in `FURNITURE`, drawn by [houseFigures.js](src/components/houseFigures.js)
from a short description (height, build, hair, glasses, beard), sat on their chair or standing; they can be moved
in arrange mode like any furniture. Ruth, Ste and Ellie move by [utils/household.js](src/utils/household.js)
(tested): drawn only while their phone is home (the house app's `*-phone-home` detectors, read from the room
states), Ellie at the dining table Monday to Friday 08:00–15:00, at their desk while its plug is switched on
(Ellie has none), otherwise on the sofa in front of a TV
drawing 30 W or more (den first), otherwise in the kitchen 07:00–21:00, otherwise their bedroom (Bedroom 1, and
Bedroom 3 for Ellie). Joseph stays put. The house redraws only when one of them moves. Arrange
mode shows everyone at their usual place. A heater whose plug is on shows a cone of heat out of its front.

**Signal** (the 3D page's **Signal** button, since 2026-10-22) draws the Zigbee mesh the house app sends with its
snapshot: every link, coloured by LQI band (150+ green, 100–149 amber, under 100 red), faint when weak. With a
device selected, only its links, labelled with their LQI, and its likely route to the hub, thicker. The device panel
lists the same under "Zigbee signal". ZHA reports neighbour tables, not the routes messages take, so the route is
worked out ([utils/zigbee.js](src/utils/zigbee.js), tested): a battery device goes through its parent, then the
path whose weakest link is strongest. A link's LQI is its weaker direction. In the 3D view the device panel is
docked at the side with no backdrop (`docked`), so the house and its signal lines stay visible and tappable.

**Arrange mode** (the 3D page's **Arrange** button, since 2026-10-22) is how the owner corrects the layout. Anything
can be picked and dragged: devices across their floor (dropping one in another room moves it there), furniture, and
doors, windows and open gaps, which snap to the nearest wall. [HouseArrangePanel](src/components/HouseArrangePanel.vue)
offers turning in 45° steps, height for devices, longer/shorter/deeper/shallower for furniture, wider/narrower plus
"Hinge on other side" and "Open other way" for doors, and duplicate/delete. The edits are pure helpers in
`houseModel.js` (tested) and are kept in `localStorage` (`house3d-arrangement-v3`, bumped each time an arrangement is pasted in) until the owner copies the JSON
the panel shows (`layoutJson`: `devices` in `DEVICE_SPOTS`' shape, `furniture` and `openings` as in `FURNITURE` and
`OPENINGS`) and it is pasted into `houseModel.js`. A device's key is its name in lower case, or `id:<device id>`
when two devices share a name.

There is no `/settings` route. Settings is a menu component, `SettingsMenu.vue`, which is
also where OTA updates are triggered.

## Layout

```
src/
  views/           One per route
  components/      Shared components
  composables/     Singleton reactive state, auto-persisted
  services/        One API client per backend area
  utils/           Pure functions
```

**Composables** are singletons, not factories — importing one twice gives the same
reactive state. They persist themselves: cookies for small settings, `localStorage` for
structured JSON such as battery zone rules.

`useApiErrors`, `useAppUpdate`, `useBatteryRules`, `useDeviceSettings`, `useFormatting`,
`useSettings`, `useSignalR`, `useTheme`, `useToday`. (`useHeatingConfig` went with the Heating 2 page,
removed 2026-10-22.)

`useDeviceSettings` also owns the boiler control path (`boilerControlPath`,
`saveBoilerControlPath`, `BOILER_CONTROL_PATHS`), surfaced on the heating page as
**Settings → Boiler control** in `SettingsMenu.vue`. It says which of the boiler's two
control paths the house is wired to; nothing can read the physical switch at the boiler, so
the setting has to be kept in step with it by hand.

`loadSettings()` fetches the device settings once per page load, and nothing pushes later changes to the page.
Every save writes the whole blob back, so a dialog that shows or saves part of it calls `reloadSettings()` when it
opens (`RoomDevicesModal`, `ScheduleEditor`, since 2026-10-21). Before that, a page left open kept showing
occupancy ticks from when it loaded, while its device lists, which are polled, were current.

**Services** wrap `fetch` and attach auth headers: `x-functions-key` for deployed
environments, `X-Localhost-Key` when the URL is localhost. Every call is scoped by a
`houseId` read from a cookie — if it is unset the service throws, and `HouseIdModal.vue`
prompts for it.

`appReleaseApi`, `batteryApi`, `deviceSettingsApi`, `heatingApi`, `houseDevicesApi`.

## Real-time updates

`useSignalR` connects to the Functions SignalR hub via `/api/signalr/negotiate` and joins
the group `house-{houseId}`. Handlers are registered with `on(event, handler)`.

The events **this app consumes** are:

`room-states-changed`, `schedules-changed`,
`battery-state-changed`, `battery-history-replaced`, `energy-history-changed`,
`energy-history-replaced`, `power-snapshot`, and for the 3D house `house-devices-changed`, `house-device-states` and
`house-device-command-result`.

The API emits several more — `battery-rules-changed`, `device-settings-changed`,
`update-available`, `version-report`, and the `backfill-*` / `start-power-monitor`
commands aimed at the hardware app. The full list is in
[the API README](../Backend/HomeAssistant.Functions/README.md).

Two older PascalCase names, `RoomStatesUpdated` and `SchedulesUpdated`, still appear in
commented-out code in `services/heatingApi.js`. The live names are the kebab-case ones.

## Charts

`vue-chartjs` over Chart.js 4, with `chartjs-adapter-date-fns` for time axes. Chart.js
components must be registered explicitly. Theme colours are CSS custom properties, so
charts read them via `getComputedStyle` in `onMounted` rather than hard-coding hex values.

`BoilerHistoryModal.vue` (since 2026-10-21, opened from the boiler status on the heating page) is not a Chart.js
chart: the boiler is one on/off state over time, so it is a timeline strip of stretches positioned by time, with a
headline total, a hover readout, a legend of the states shown and a table of the changes. The cutting into stretches
is `utils/boilerHistory.js` (tested); inferred (backfilled) stretches are drawn hatched.

`BatteryChart.vue` carries two custom plugins: `zoneRenderingPlugin` (`afterDatasetsDraw`,
paints the coloured action zones and grey gaps) and `zoneInteractionPlugin`
(drag-to-create, click-to-edit).

## Conventions

- Composition API throughout, `<script setup>`.
- No UI framework — plain CSS, mobile-first.
- 20 themes, defined as CSS custom properties on `[data-theme]` in `index.html` and
  switched by `useTheme`.
- Modals: fixed overlay plus centred panel, base `z-index` 1000, `fadeIn`/`slideUp`
  animations. A modal opened from another modal takes a `z-index` 100 higher, passed as
  a prop.
- Sentence case for all UI text — "Add new room", not "Add New Room".
- **Never render `room.name` directly.** Rooms have two names: `name` is the canonical one
  the NetDaemon app matches on to find the room's radiator, plugs and sensors, and
  `displayName` is the UI-only one the user can edit. Always go through
  `roomLabel(room)` in [src/utils/rooms.js](src/utils/rooms.js), which falls back to
  `name`. Renaming is display-only by design — see **Heating** in the root
  [CLAUDE.md](../CLAUDE.md) for why changing `name` breaks hardware.
- `cleanRoomsForApi` in `HeatingView.vue` whitelists the fields sent to `POST /schedules`,
  and that payload replaces the entire blob. Any room field not in that whitelist is
  silently wiped the next time anything is saved — add new fields there as well as to the
  C# DTO.
- **The layout is a tree of any depth.** `layout` (rooms and groups, groups holding more of either) is saved
  next to the flat `rooms` list; `LayoutNode` renders itself for each group's children, and a group shows
  whether anything inside is heating plus the temperature (one value if every room agrees, otherwise the
  range). Edit layout mode gives every card choose-icon / move-up / move-down / "Move to…" buttons, and
  the toolbar adds "Add group". All the rules (moving, nesting without cycles, deleting a group without
  losing its rooms, reconciling a layout with the room list) are pure functions in `utils/roomLayout.js`.
  Layout state reaches `LayoutNode` through provide/inject (`utils/layoutContext.js`) rather than props,
  since it recurses. `cleanRoomsForApi` must keep sending `layout`, or the next save resets everyone's arrangement.
- **Devices per room.** Each room state carries `devices` (thermometer, radiator valves, heater plugs, the
  upstairs bathroom's towel rail (role `towelRail`: not in HA, never shown as unavailable, its state the boiler's) and
  presence devices — desk plugs and their power, TV power, the living room's lights plug, phones (since
  2026-10-21 listed as the phone, "On the home Wi-Fi", not the router's online list) — each with its live HA
  state), built by the NetDaemon app from the same switches that bind the zone. An expanded room card has a quiet
  "Devices (n)" link under its schedules, an expanded group a "Devices in …" link at the top listing every zone
  inside it, and the page toolbar a **Devices** button for the whole house (`houseDeviceSections`, layout order,
  then batteries placed nowhere; since 2026-10-21); all open `RoomDevicesModal`, which has filter toggles (All,
  TRVs, Plugs, Heating = TRVs and heater plugs, Thermometers, Battery = the battery list; `DEVICE_FILTERS` /
  `filterSection` in `utils/roomDevices.js`) and
  shows each zone's devices under **Readers**, **Heaters** and **Presence** (`groupDevices` /
  `DEVICE_GROUPS` in `utils/roomDevices.js`) by friendly name. Entity IDs are deliberately not shown (owner,
  2026-10-19); they are only keys.
  Labels and state text are pure functions in `utils/roomDevices.js`. `devices` is display only and is
  never sent back: `cleanRoomsForApi` leaves it out, as it should. The readings are a snapshot taken when
  the NetDaemon app last uploaded room states, which it does only when some room's temperature, heating
  on/off or active track changes, so a device-only change (plug switched, TV power, valve target) waits for
  the next such upload. Left that way deliberately (2026-10-19).
- **Silent thermometer.** A room card shows "No reading" and a dash for the temperature while its room state's
  `thermometerSilentSince` is set (since 2026-10-31): the zone's thermometer has stopped reporting, and the app gives
  the zone no heat until it does. Carried through both room-state merges in `HeatingView.vue`.
- **Boiler indicator.** In relay mode the heating page's toolbar shows "Boiler on/off since …" from the
  `boiler` that comes with room states (`boilerState` in `HeatingView.vue`): the state the app keeps the boiler
  relay in (until 2026-10-31, a belief from fingerbot presses). Nothing is shown while the boiler thermostat runs it.
- **Low batteries** (owner, 2026-10-20). Room states carry `batteries`, every replaceable battery with its HA
  area and its device's entities. `utils/batteries.js` (with tests) places each: on every zone whose heating
  uses the device; else on the zone or group its HA area is mapped to (`heating.areaPlacement`, edited in the
  `BatteriesModal`, opened from **Edit layout → Batteries** or by tapping the toolbar's battery); else at the
  top of the page. `BatteryBadge` shows the lowest battery at 20% or below (`LOW_BATTERY_PERCENT`; red at 10%)
  before the temperature on its zone or group card, and in the toolbar for unplaced devices. A collapsed group
  also shows the lowest of everything inside it (`LayoutNode` works this out), so folding cannot hide one.
  Devices lists show a **Batteries** block per zone, and sections for batteries placed on a group. Batteries
  only change on the app's next room-state upload, which a changed battery now triggers by itself.
- **Zone occupancy.** Each zone in a devices list shows which presence detectors make it count as in use
  ("In use when: …") and an **Occupancy** button opening `ZoneOccupancyModal`. It ticks from every detector the
  app offers, with whether each is on now and whether it counts towards House. The detectors arrive on room
  states as `occupancy`, and `mergeSchedulesWithStates` / `loadRoomStates` must copy it across. The choice is
  saved as `heating.zoneOccupancy` through `useDeviceSettings().saveZoneOccupancy`; the first save writes every
  zone's built-in detectors plus the edit (`occupancyFirstSaveMap`). Pure helpers and tests are in
  `utils/zoneOccupancy.js`.
- **Device rules.** Each heater plug and radiator valve in a devices list shows its rules summary and a
  **Rules** button opening `DeviceRulesModal` (schedule windows, presence sources with Any/All, and the device's
  **pre-heat**, 0–60 minutes in 5-minute steps; unset is **Auto** for a radiator valve (the app works it out from how
  fast that valve has warmed its room; saved as no value, `PRE_HEAT_AUTO` while editing) and none for a heater plug, so
  the dialog takes the device's `role`, and the summary mentions pre-heat only when it differs from that default).
  The dialog also takes the `device` to say what Auto amounts to, from the `preHeatAuto`, `heatingRateCelsiusPerHour`,
  `heatingRateSamples` and `warmUpMinutes` the app publishes on a valve; `utils/warmUp.js` formats them, and the
  devices list shows each valve's rate and, per zone, the longest expected warm-up. A valve with no warm-up of its own
  that goes by another valve's in its zone carries `heatingRateFromEntityId` (since 2026-10-23); the list and the
  dialog name that valve (`RoomDevicesModal` passes the name as `rateFromName`), and an Auto valve on the 30-minute
  fallback says so on its own line.
- **Radiator valve health** (since 2026-10-23): each valve carries `valveHealth`, what the NetDaemon app has seen it
  do wrong (let hot water through while shut, or stay cold while open), each kind with whether it is `flagged` (seen
  and not cleared by a later boiler run). `utils/valveHealth.js` (with tests) turns it into sentences in the user's
  temperature unit and time format; the devices list shows a flagged fault in full with what to check, and a cleared
  one in a line. `ValveHealthBadge` puts a "Valve" warning on the zone's card, and on a collapsed group's card for
  any zone inside, and opens the devices list. Read-only: nothing in the UI tests or moves a valve.
  `normaliseRules` must carry `preHeatMinutes`, or saving the dialog would wipe it. Tracks still carry a
  `rampUpMinutes` (the schedule editor always sends 30), but since 2026-10-19 the app ignores it: pre-heat is
  per device. Rules live
  in the device-settings blob as `heating.deviceRules` (see CLAUDE.md → heating). While that is `null` each
  drivable device carries the app's built-in `rules` (`rulesAreDefault: true`); the first save writes every
  device's defaults plus the edit (`firstSaveMap`), because once saved a missing entry means "no rules".
  Pure helpers and their tests are in `utils/deviceRules.js`; saving goes through
  `useDeviceSettings().saveDeviceRules`.

- **What is open is remembered** (owner, 2026-10-20). Any number of groups can be open at once, and one zone
  card at a time. Both are kept in `localStorage` as `heating-expanded` (`{ roomId, groupIds }`), saved by a watch
  in `HeatingView.vue` and restored on mount. It replaced the `heating-app-expanded-room` cookie, which stored the
  room id as text and so never matched the numeric id again; the old cookie is deleted on load.
- **A group's header counts what sits directly inside it** as zones ("4 zones"), rooms and groups alike, not
  every room nested below.
