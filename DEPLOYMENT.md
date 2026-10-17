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
| `hardware` | `dotnet-publish` | Copied to `\homeassistant.local\config\netdaemon6`, and uploaded to `/api/app-release/upload` for OTA |

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

## OTA update of the NetDaemon app

The `hardware` task uploads each published build to the `app-releases` blob container via
`POST /api/app-release/upload` (one of only two function-key-protected endpoints). The
app polls for a desired version, downloads it, swaps its directory, and restarts through
`IGracefulShutdownService`, which cancels work and waits up to 10 seconds before exiting.
The frontend's settings menu drives this.
