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
`roomLayout`, `rooms`, `roomDevices`). There are no component tests.

## Routes

`vue-router` 4 in HTML5 history mode. SPA fallback for Azure Static Web Apps is
`public/staticwebapp.config.json`.

| Route | View | Notes |
|---|---|---|
| `/` | `LandingView.vue` | Feature cards |
| `/heating` | `HeatingView.vue` | **The** heating UI — room cards, schedules, boost |
| `/battery` | `BatteryView.vue` | Battery zones, pricing, energy charts and tables |
| `/heating2` | `Heating2View.vue` | **Superseded.** Floor/device/rule editor for the newer heating config model. Slated for removal; the final heating UI is `/heating`. |
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

There is no `/settings` route. Settings is a menu component, `SettingsMenu.vue`, which is
also where OTA updates are triggered.

## Layout

```
src/
  views/           One per route
  components/      Shared components; components/heating2/ belongs to the superseded UI
  composables/     Singleton reactive state, auto-persisted
  services/        One API client per backend area
  utils/           Pure functions
```

**Composables** are singletons, not factories — importing one twice gives the same
reactive state. They persist themselves: cookies for small settings, `localStorage` for
structured JSON such as battery zone rules.

`useApiErrors`, `useAppUpdate`, `useBatteryRules`, `useDeviceSettings`, `useFormatting`,
`useHeatingConfig`, `useSettings`, `useSignalR`, `useTheme`.

`useDeviceSettings` also owns the boiler control path (`boilerControlPath`,
`saveBoilerControlPath`, `BOILER_CONTROL_PATHS`), surfaced on the heating page as
**Settings → Boiler control** in `SettingsMenu.vue`. It says which of the boiler's two
control paths the house is wired to; nothing can read the physical switch at the boiler, so
the setting has to be kept in step with it by hand.

**Services** wrap `fetch` and attach auth headers: `x-functions-key` for deployed
environments, `X-Localhost-Key` when the URL is localhost. Every call is scoped by a
`houseId` read from a cookie — if it is unset the service throws, and `HouseIdModal.vue`
prompts for it.

`appReleaseApi`, `batteryApi`, `deviceSettingsApi`, `heatingApi`, `heatingConfigApi`.

## Real-time updates

`useSignalR` connects to the Functions SignalR hub via `/api/signalr/negotiate` and joins
the group `house-{houseId}`. Handlers are registered with `on(event, handler)`.

The events **this app consumes** are:

`room-states-changed`, `schedules-changed`, `heating-config-changed`,
`battery-state-changed`, `battery-history-replaced`, `energy-history-changed`,
`energy-history-replaced`, `power-snapshot`.

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
- **Devices per room.** Each room state carries `devices` (thermometer, radiator valves, heater plugs and
  presence devices — desk plugs and their power, TV power, the living room's lights plug — each with its live
  HA state), built by the NetDaemon app from the same switches that
  bind the zone. An expanded room card has a quiet "Devices (n)" link under its schedules, and an expanded
  group a "Devices in …" link at the top listing every zone inside it; both open `RoomDevicesModal`, which
  shows each zone's devices under **Readers**, **Heaters** and **Presence** (`groupDevices` /
  `DEVICE_GROUPS` in `utils/roomDevices.js`) by friendly name. Entity IDs are deliberately not shown (owner,
  2026-10-19); they are only keys.
  Labels and state text are pure functions in `utils/roomDevices.js`. `devices` is display only and is
  never sent back: `cleanRoomsForApi` leaves it out, as it should. The readings are a snapshot taken when
  the NetDaemon app last uploaded room states, which it does only when some room's temperature, heating
  on/off or active track changes, so a device-only change (plug switched, TV power, valve target) waits for
  the next such upload. Left that way deliberately (2026-10-19).
- **Boiler indicator.** In fingerbot mode the heating page's toolbar shows "Boiler on/off since …" from the
  `boiler` that comes with room states (`boilerState` in `HeatingView.vue`). It is the app's belief from its
  own presses, not a reading; nothing is shown while the boiler thermostat drives the boiler.
- **Zone occupancy.** Each zone in a devices list shows which presence detectors make it count as in use
  ("In use when: …") and an **Occupancy** button opening `ZoneOccupancyModal`. It ticks from every detector the
  app offers, with whether each is on now and whether it counts towards House. The detectors arrive on room
  states as `occupancy`, and `mergeSchedulesWithStates` / `loadRoomStates` must copy it across. The choice is
  saved as `heating.zoneOccupancy` through `useDeviceSettings().saveZoneOccupancy`; the first save writes every
  zone's built-in detectors plus the edit (`occupancyFirstSaveMap`). Pure helpers and tests are in
  `utils/zoneOccupancy.js`.
- **Device rules.** Each heater plug and radiator valve in a devices list shows its rules summary and a
  **Rules** button opening `DeviceRulesModal` (schedule windows, presence sources with Any/All, and the device's
  **pre-heat**, 0–60 minutes in 5-minute steps; unset is 30 for a radiator valve and none for a heater plug, so
  the dialog takes the device's `role`, and the summary mentions pre-heat only when it differs from that default).
  `normaliseRules` must carry `preHeatMinutes`, or saving the dialog would wipe it. Tracks still carry a
  `rampUpMinutes` (the schedule editor always sends 30), but since 2026-10-19 the app ignores it: pre-heat is
  per device. Rules live
  in the device-settings blob as `heating.deviceRules` (see CLAUDE.md → heating). While that is `null` each
  drivable device carries the app's built-in `rules` (`rulesAreDefault: true`); the first save writes every
  device's defaults plus the edit (`firstSaveMap`), because once saved a missing entry means "no rules".
  Pure helpers and their tests are in `utils/deviceRules.js`; saving goes through
  `useDeviceSettings().saveDeviceRules`.

  `RoomGroupCard` that shows the shared temperature and a flame if any zone is heating,
  but has no controls of its own — the controls live on the zone cards inside it. The
  grouping is built in `roomEntries` in `HeatingView.vue`, and a group takes the position
  of its first zone, so the blob's room order still decides the layout. Rooms with no
  `group` render exactly as before. Group expansion is not persisted (the expanded-room
  cookie stores a room id, and groups have none).
