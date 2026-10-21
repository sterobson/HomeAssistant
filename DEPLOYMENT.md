# Deployment

Deployment is handled by a **separate, shared tool** that lives outside this repo:

```
C:\Users\stero\source\repos\Deployment\
```

It discovers this repo by finding `.deployment/deploy-config.json`, and deploys all
three parts of the system. It is menu-driven — there are no command-line flags for
picking an environment or task.

```powershell
pwsh C:\Users\stero\source\repos\Deployment\deploy.ps1
```

From the menu: pick the project, pick **Deploy**, pick the environment, pick tasks.
The same tool also launches the local dev stack and manages secrets and environment
variables. Its own `README.md` is the reference for the tool itself; this file covers
what is specific to HomeAssistant.

> **The legacy script.** `deploy.ps1` in this repo's root is the superseded 3,500-line
> red/green script. It still runs, but it reads secrets from `~/.homeassistant-deploy/`
> and state from `.deployment/state.json`, which the current tool does not use. Prefer
> the Deployment repo. The root script is kept only until the slot-swap behaviour is
> reimplemented there.

## Environments and tasks

Defined in `.deployment/deploy-config.json`.

### `production` — resource group `HomeAssistant-Production-RG`

| Task | Type | Target |
|---|---|---|
| `backend` | `azure-functions` | Slot `homeassistant-prod-functions` |
| `frontend` | `azure-static-web-app` | `delightful-plant-0656dfa03.4.azurestaticapps.net`, served at `https://homeassistant.everymilesoftware.com` |
| `hardware` | `dotnet-publish` | Copied to `\\192.168.1.188\config\netdaemon6` (the Samba share add-on, **by IP**), and uploaded to `/api/app-release/upload` for OTA |

The frontend build receives `VITE_API_URL` (templated from the backend task's URL) and
`VITE_FUNCTION_KEY` (from the stored secret). The hardware task runs `nd-codegen` as a
pre-build step and runs `Backend/Tests` before publishing.

### `local`

Runs the whole stack on this machine: Functions on **7159**, frontend dev server on
**5173**, the NetDaemon app via `dotnet run`, and Azurite for storage
(blob **10030**, queue **10031**, table **10032**, data in `.azurite`).

Note the frontend port here differs from `Frontend/vite.config.js`, which defaults Vite
to **3000** when you run `npm run dev` yourself.

### `testing`

`.deployment/state.json` still carries a `testing` environment pointing at the
`sterobson-homeassistant-testing` slot, but there are no `testing` tasks in
`deploy-config.json`. Treat testing as retired unless you re-add it.

## Config and secrets are not in git

`.gitignore` excludes the whole `/.deployment` directory, so **`deploy-config.json` and
`state.json` exist only on the machine that last deployed.** There is no copy in the
repository and no backup. If you set up a new machine you will have to recreate the
config by hand.

Secrets are DPAPI-encrypted per user, keyed by the project GUID from `deploy-config.json`:

```
~\.deploy-secrets\b7e3a1d9-4c2f-48a6-9e1b-5d8f0c3a7e42\secrets.json
```

Being DPAPI, they are tied to this Windows user on this machine and cannot be copied to
another. Use the tool's **Manage secret keys** menu rather than editing the file.

## Verifying what is actually deployed

Git history will not tell you — pushed is not deployed. Ask the running app:

```bash
curl -s https://homeassistant-prod-functions.azurewebsites.net/api/version
```

The response is the `DeploymentVersion` baked into the assembly at build time via
`/p:DeploymentVersion=<timestamp>-<sha>`, so the trailing SHA identifies the deployed
commit exactly. `GET /api/health` is a cheap liveness check. Both are anonymous.

The NetDaemon app versions itself differently: the `GenerateVersionFile` MSBuild target
writes `version.txt` (`yyyyMMddHHmmss` UTC) into the publish directory on every publish,
and the OTA endpoints under `/api/app-release/` compare running against desired.

## GitHub Actions

`.github/workflows/` holds two workflows that build the frontend and push to GitHub
Pages — `deploy-github-pages.yml` (on `development` or `testing`) and
`deploy-release.yml` (on `release`).

**Both are effectively dead.** The `development` and `release` branches do not exist, and
production has moved to Azure Static Web Apps. `Frontend/vite.config.js` still contains
the GitHub Pages base-path logic (`GITHUB_PAGES`, `DEPLOY_PATH`) these workflows rely on.
Delete all three together, or leave them all alone — don't half-remove it.

## Deploying by hand, and what the tool does not do

The tool is a menu-driven script with many `Read-Host` branches (environment-variable sync, git
commit and push, token entry, a Stripe step). Piping answers into it is unsafe: a line meant for a menu
can be swallowed as an app setting value and written to production. The 2026-10-17 deploy instead ran
each step as an explicit command, using the tool's own modules only to resolve the DPAPI secrets
in-process (`Resolve-TemplateValue` from `modules\ConfigManagement.ps1`, never printed). The steps,
in the order that works:

1. **Preflight and backup.** Clean `main`, in step with `origin/main`. Save production's `schedules`,
   `device-settings`, `battery-rules`, `house-details` and `heating-config` blobs (anonymous GETs for
   house `1234567`) before changing anything.
2. **Functions.** `func azure functionapp publish homeassistant-prod-functions --dotnet-isolated
   --dotnet-cli-params "/p:DeploymentVersion=<yyyy.MM.dd.HH.mm>-<sha>"` from `Backend\HomeAssistant.Functions`.
   Backward compatible with the running app, so it goes first. Then read `/api/version` back. The tool
   also writes a `DEPLOYMENT_VERSION` app setting; nothing reads it (the version is baked into the
   assembly), so skipping it avoids a production settings write.
3. **NetDaemon app.** `dotnet test`, then `dotnet publish Backend/App -c Release --self-contained false
   -p:DebugType=none -p:DebugSymbols=false -o publish\hardware-app`. Copy everything **except
   `appsettings.*.json`** to the share (the real `appsettings.secrets.all.json` and
   `appsettings.production.json` live only on the host and must never be overwritten), verify
   `HomeAssistant.dll` byte-for-byte, upload the OTA zip, then **restart the NetDaemon add-on yourself**:
   the tool does not, and the running app keeps the old code in memory until it restarts. Confirm via
   `GET /api/app-release/running?houseId=1234567` reporting the new `version.txt`.
4. **Data migration, if the release changes room names.** The app matches room names in code, so the new
   app on old names (or the reverse) leaves those rooms unbound until the blob is migrated. Do it straight
   after the app restarts, with `LocalEnv/migrate-rooms.mjs` (or `LocalEnv/add-room.mjs` to add one new room; dry run first; writing to production needs
   `--apply --i-mean-production`). The live app picks up the change over SignalR within seconds.
5. **Frontend.** Build with `VITE_API_URL` and `VITE_FUNCTION_KEY`, then `swa deploy` with the token from
   `az staticwebapp secrets list`. Compare the live `index-*.js` name to the local build to prove it.

`nd-codegen` (the tool's pre-build step) was skipped: it regenerates `HomeAssistantGenerated.cs` from the
live Home Assistant, which makes the deployed build differ from the commit.

The 2026-10-19 deploy (`d3ff40a`) followed the same steps and added these, all checked by hand:

- **Restarting the add-on:** `node tools/ha/addon.cjs c6a2317c_netdaemon6 restart`. The app reported its new
  `version.txt` to `/api/app-release/running` within about 30 seconds.
- **Uploading the OTA zip does not trigger an update.** `app-release/upload` only stores the release; the app
  updates itself only when a desired version is set (`app-release/desired`, from the settings menu).
- **The publish output has an `apps/` subfolder** (`YamlConfigApp.yaml`). The tool's copy step copies top-level
  items without `-Recurse`, so it never updates that folder. Copy with `-Recurse` and still skip
  `appsettings.*.json`. The publish output contains no `appsettings.*.json` at all as of this release, but keep
  the exclusion.
- **`frontend.staticWebAppName` in the config holds the default hostname, not the resource name.** The resource is
  `HomeAssistant-Prod-WebApp` in `HomeAssistant-Production-RG`, which is what `az staticwebapp secrets list` needs.
- **`Frontend/.env.local` is read by production builds too.** It holds the local stack's `VITE_LOCALHOST_KEY` and
  API URL. Environment variables beat `.env` files in Vite, so the build sets `VITE_API_URL` and
  `VITE_FUNCTION_KEY` from the config and blanks `VITE_LOCALHOST_KEY`. Then it checks the bundle contains the
  production URL and no `localhost:7159` / `localhost:7071` before running `swa deploy`.
- **Straight after a restart the room states show every target as null** with `activeScheduleTrackId` 0. That is
  the app's first upload, sent before its first evaluation. Until the fix in the commit after `a2ffab2`, the
  evaluated targets then stayed off the API until some room's temperature or heating changed: a new target or
  track never triggered an upload. That took 2 to 7½ minutes after the four restarts on 2026-10-19, and looked
  like a slow start, but the add-on log showed the startup evaluation running within 5 s. With the fix the
  targets arrive with that first evaluation. `EvaluateAllSchedules` also now logs any failure ("Heating
  evaluation failed") instead of losing it, so the add-on log (Settings → Add-ons → NetDaemon → Log, not
  readable through the API) is where to look when targets do not appear.

The 2026-10-20 deploys (`659216c`-era fix, then `d60474c`) added these:

- **Run `func azure functionapp publish ... --dotnet-cli-params "/p:DeploymentVersion=..."` from PowerShell, not Git Bash.** Git Bash
  rewrites `/p:...` as a path, and the publish fails with `MSB1009: Project file does not exist`.
- **`Resolve-TemplateValue` needs three modules dot-sourced, not one:** `Output.ps1`, `SecretsManagement.ps1` and `ConfigManagement.ps1`
  (it calls `Get-StoredSecrets`). With only `ConfigManagement.ps1` it fails with "The term 'Get-StoredSecrets' is not recognized".
- **The OTA zip is built from the publish folder and uploaded to `app-release/upload?version=<version.txt>` with the `x-functions-key` header.**
  The publish folder can be anywhere (a scratch folder worked); only `version.txt` and the zip contents matter.
- **Adding one room needs `LocalEnv/add-room.mjs`** after the app that knows its name is live (see CLAUDE.md → hallway).

The 2026-10-21 deploy (`b3143ba`: heater warm-up, boiler history, occupancy on tracks) followed the same steps,
each as an explicit command, and added these:

- **`local.settings.json` is not production's storage.** Its `ScheduleStorageConnectionString` names
  `personalprojectsa4ee`; the production Functions app's own setting names `homeassistantprodstorage`. A data fix
  written with the local string lands where production never looks (the first boiler-history backfill did, and 38
  stray rows were left in `personalprojectsa4ee/boilerhistory`). Read the connection string from the app instead:
  `az functionapp config appsettings list --name homeassistant-prod-functions --resource-group HomeAssistant-Production-RG`.
- **The publish output now includes `appsettings.json`** (the base config, identical to the copy already on the share
  by hash). It is not one of the host's own `appsettings.*.json` files, so the usual exclusion leaves it in.
- The OTA upload and the frontend build were run as scripts that dot-source the three modules, resolve
  `${secret:function-key}` and `${backend.url}` in-process, and never print the key; the frontend script refuses to
  deploy a bundle without the production API URL or with a localhost one.

### The Samba share, and restarting add-ons

- Use the IP, `\\192.168.1.188\config`. `\\homeassistant.local` did not resolve for SMB from this
  machine, and Windows holds saved credentials for the IP.
- **The Samba add-on can report `started` while nothing listens on 445/139** (seen 2026-10-17: ports
  refused, SSH and the file editor fine). Check with a raw TCP connect, not `Test-NetConnection`, whose
  output is easy to misread. Restarting just that add-on fixed it in under 5 seconds.
- Add-ons can be read and restarted through the HA websocket (`supervisor/api` with endpoints such as
  `/addons/core_samba/restart`), using the same long-lived token. The REST `/api/hassio` proxy returns
  401 for it, and the add-on log endpoints failed (`unknown_error`), so a crashed add-on's log is only
  readable from the HA UI. The NetDaemon add-on's slug is `c6a2317c_netdaemon6`.

## OTA update of the NetDaemon app

The `hardware` task uploads each published build to the `app-releases` blob container via
`POST /api/app-release/upload` (one of only two function-key-protected endpoints). The
app polls for a desired version, downloads it, swaps its directory, and restarts through
`IGracefulShutdownService`, which cancels work and waits up to 10 seconds before exiting.
The frontend's settings menu drives this.
