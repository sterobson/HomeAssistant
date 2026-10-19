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
  desk presence plugs, each with its live HA state), built by the NetDaemon app from the same switches that
  bind the zone. An expanded room card has a quiet "Devices (n)" link under its schedules, and an expanded
  group a "Devices in …" link at the top listing every zone inside it; both open `RoomDevicesModal`.
  Labels and state text are pure functions in `utils/roomDevices.js`. `devices` is display only and is
  never sent back: `cleanRoomsForApi` leaves it out, as it should.

  `RoomGroupCard` that shows the shared temperature and a flame if any zone is heating,
  but has no controls of its own — the controls live on the zone cards inside it. The
  grouping is built in `roomEntries` in `HeatingView.vue`, and a group takes the position
  of its first zone, so the blob's room order still decides the layout. Rooms with no
  `group` render exactly as before. Group expansion is not persisted (the expanded-room
  cookie stores a room id, and groups have none).
