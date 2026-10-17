# Hardware app (NetDaemon)

The part of the system that actually touches hardware. A NetDaemon 5 app (`net10.0`,
`NetDaemon.AppModel`/`NetDaemon.Runtime` 25.46.0) that connects to Home Assistant,
reads sensors, drives switches and valves, and syncs state with the Azure Functions API.

Runs on the Home Assistant host at `\homeassistant.local\config\netdaemon6`, and
updates itself over the air — see [../../DEPLOYMENT.md](../../DEPLOYMENT.md).

## Build and run

```bash
dotnet build                       # or build Backend/HomeAssistant.sln
dotnet run                         # needs a reachable Home Assistant and a token
dotnet test ../Tests               # MSTest + NSubstitute + Shouldly
```

### Code generation

`HomeAssistantGenerated.cs` is generated from the **live** Home Assistant instance. After
adding or renaming entities in Home Assistant, regenerate it:

```bash
dotnet tool restore
dotnet nd-codegen
```

Output location and namespace come from the `CodeGeneration` section of
`appsettings.json`. The generated file and `NetDaemonCodegen/EntityMetaData.json` are
committed, so regenerating produces a reviewable diff. `nd-codegen` also runs as a
pre-build step during deployment.

### Configuration

`appsettings.json` is committed and holds structure with `<secret>` placeholders. The real
values live in files that are **git-ignored**: `appsettings.development.json`,
`appsettings.secrets.all.json`, `appsettings.secrets.development.json`. Secrets cover the
Home Assistant token, Octopus, Solax, WeatherApi, the York bin service, and
`WebSynchronisation` (`HouseId` and the API URL that ties this app to the Functions
backend).

## Layout

```
apps/           NetDaemonApp classes — the automations themselves
  NamedEntities.cs    Every Home Assistant entity this app uses, in one place
Services/       The logic the apps delegate to
Shared/         DTOs shared with the Functions app
```

### `apps/`

One class per automation, each marked `[NetDaemonApp]`:

| App | Does |
|---|---|
| `HassModel/Climate/Heating.cs` | Entry point for heating; delegates to `HeatingControlService` |
| `HassModel/Climate/DiningRoomDehumidifier.cs` | Humidity-driven dehumidifier |
| `HassModel/Energy/EnergyApp.cs` | Battery control, pricing, power monitoring, history push |
| `HassModel/Lights/` | `DiningRoomLight`, `GamesRoomDesk` |
| `HassModel/Reminders/MilkBottles.cs` | Recurring reminder |
| `HassModel/Waste/BinCollections.cs` | York council bin collection notifications |
| `HassModel/AppUpdateApp.cs` | OTA self-update, 10s delayed init |
| `HassModel/DeviceBatteryApp.cs` | Daily push of replaceable battery levels — coin cells and AAs, **not** the home battery |
| `HassModel/Halloween/Wooooooo.cs` | Exactly what it sounds like |
| `Extensions/Scheduling/Scheduling.cs` | Cron-style scheduling helpers |
| `AppModel/YamlConfigApp/` | YAML-configured app example |

### `NamedEntities.cs`

All entity IDs are bound in one place, behind small wrapper interfaces —
`ICustomSwitchEntity`, `ICustomNumericSensorEntity`, `ICustomClimateControlEntity`,
`ICustomBooleanSensorEntity`, and `ICustomButton`/`SonoffButton` for ZHA button events.
The wrappers exist so services can be unit-tested against `FakeNamedEntities` instead of
a live Home Assistant. `EntityExistenceValidator` logs at startup if a bound entity is
missing from Home Assistant.

**Add new entities here, not inline in a service.**

### `Services/`

- **`Climate/`** — `HeatingControlService` (schedule evaluation, device toggling, boiler
  scoring), `BoilerVerificationService` (infers whether the boiler actually responded),
  `SchedulePersistenceService` + `ScheduleApiClient` (fetch and cache schedules, SignalR
  refresh), `RoomStatePersistenceService` (push room state back to the API).
- **`Energy/`** — battery control and zone resolution, Octopus pricing analysis, power
  monitoring, and the history push/backfill services.
- **`WasteManagement/`** — York council collection lookup.
- **Cross-cutting** — `EntityPushService`/`EntityPushCoordinator` (publish the HA entity
  list to the API on startup and every 6h, so the web UI can offer entity pickers),
  `DeviceBatteryPushService` (one record per day of every replaceable cell's level; the
  entity list above carries current levels too, but it is overwritten each push, so it can
  never show a battery declining),
  `DeviceSettingsPersistenceService` (dynamic entity mappings with hard-coded fallbacks),
  `SignalRConnectionService`, `NotificationService`, `PresenceService`,
  `GracefulShutdownService`, `AppUpdateService`.

## Heating gotcha

Room names are the join key between configuration and hardware, and they are not defined
in this repo — they come from the schedules blob. Four `switch` statements match on
`room.Name.Trim().ToLower()`. Renaming a room unbinds it from its hardware silently.
See the heating section of [../../CLAUDE.md](../../CLAUDE.md) before changing anything
in `HeatingControlService`.

## Versioning

The `GenerateVersionFile` MSBuild target writes `version.txt` (`yyyyMMddHHmmss` UTC) into
the publish directory after every publish. That value is what the OTA endpoints under
`/api/app-release/` compare to decide whether an update is pending.

## NetDaemon reference

- [netdaemon.xyz/docs](https://netdaemon.xyz/docs)
- [Code generator](https://netdaemon.xyz/docs/hass_model/hass_model_codegen)
