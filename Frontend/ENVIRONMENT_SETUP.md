# Environment variables and security

How configuration reaches the frontend, and what is and isn't protected.

## There are no true secrets in a frontend app

Any value compiled into the bundle is visible in DevTools, extractable from network
requests, and readable in the shipped JavaScript. Azure Function keys are designed with
that in mind — they are rotatable, trackable, rate-limitable and individually revocable.
Treat them as semi-public identifiers, not secrets.

## Environment files

| File | Committed | Used for |
|---|---|---|
| `.env.local` | No (git-ignored) | `npm run dev`. Holds `VITE_LOCALHOST_KEY` for a local Functions host. |
| `.env.testing` | Yes | `--mode testing` builds. |
| `.env.production` | Yes | `--mode production` builds. |
| `.env.example` | Yes | Template, placeholders only. |

Three variables are read anywhere in the app:

- `VITE_API_URL` — base API URL, e.g. `http://localhost:7071/api`
- `VITE_FUNCTION_KEY` — Azure Function key, injected at build time by the deploy tool
- `VITE_LOCALHOST_KEY` — local development key

The committed `.env.testing` and `.env.production` carry placeholder key values; the real
key is substituted during the deployment build.

## Local development

Create `.env.local`:

```bash
VITE_API_URL=http://localhost:7071/api
VITE_LOCALHOST_KEY=<value from Backend/HomeAssistant.Functions/local.settings.json>
```

Vite loads `.env.local` automatically for `npm run dev`. Note that the dev server runs on
port 3000 (`vite.config.js`), while the API runs on 7071 under `func start`, or 7159 if
you launch the stack through the deploy tool's `local` environment.

## Deployment builds

Deployment is handled by the shared tool in `C:\Users\stero\source\repos\Deployment\`,
not by a script in this repo. It builds the frontend with `VITE_API_URL` templated from
the backend task's URL and `VITE_FUNCTION_KEY` taken from its DPAPI-encrypted secret
store. Manage the key through the tool's **Manage secret keys** menu.

See [../DEPLOYMENT.md](../DEPLOYMENT.md).

> The root `deploy.ps1` and `deploy.secrets.ps1` are part of the superseded deployment
> script. The `TESTING_FUNCTION_KEY` / `PRODUCTION_FUNCTION_KEY` variables they use are
> not read by the current tool.

### Getting a function key

Azure Portal → the Function App → **App keys** (under Functions) → copy the **default**
host key, or create a dedicated one.

## Build modes

```bash
npm run dev                          # .env.local
npm run build -- --mode testing      # .env.testing
npm run build -- --mode production   # .env.production
```

## Using the variables in code

Services already do this — see `src/services/heatingApi.js`. The pattern is to pick the
header based on whether the API is local:

```javascript
const apiUrl = import.meta.env.VITE_API_URL
const headers = { 'Content-Type': 'application/json' }

if (apiUrl.includes('localhost')) {
  headers['X-Localhost-Key'] = import.meta.env.VITE_LOCALHOST_KEY
} else {
  headers['x-functions-key'] = import.meta.env.VITE_FUNCTION_KEY
}

fetch(`${apiUrl}/schedules?houseId=${houseId}`, { headers })
```

## Security model — read this before trusting the key

**The function key is not currently enforced.** Every HTTP trigger in
`Backend/HomeAssistant.Functions` is declared `AuthorizationLevel.Anonymous`, except two:

- `POST /api/app-release/desired`
- `POST /api/app-release/upload`

which are `AuthorizationLevel.Function`. There is no authentication middleware and no
custom key validation anywhere in the Functions project. The `x-functions-key` header the
frontend sends is therefore ignored by about forty of the forty-two endpoints, and
`X-Localhost-Key` is not checked either.

In practice that means anyone who knows the host name and a `houseId` can read and write
heating schedules, heating config, battery rules, device settings and history. CORS does
not help — it is a browser restriction, and `curl` ignores it.

### What actually protects the API today

1. Obscurity of the host name and `houseId`.
2. Azure platform rate limiting and the hosting plan's own limits.
3. CORS, but only against other *websites* in a browser.

### What does not

1. The function key — not enforced on the endpoints that matter.
2. Anything in the frontend bundle.

### If you want this closed

Switch the data endpoints to `AuthorizationLevel.Function`, or add a worker middleware
that validates a shared header, or put Azure AD / Easy Auth in front of the Function App.
Rotating the key changes nothing until the endpoints actually require one.

Worth doing regardless: set an Azure cost alert, and monitor usage.

## Key rotation

Once keys are enforced, rotation is:

1. Azure Portal → Function App → **App keys** → **Renew key value**
2. Update the stored secret via the deploy tool's **Manage secret keys** menu
3. Redeploy the frontend so the new key is bundled
4. Already-loaded browser sessions break until refreshed

## FAQ

**Can someone steal the function key from the browser?**
Yes, and that is expected for this class of key. Right now it hardly matters, because the
endpoints do not check it.

**How would I make this genuinely secure?**
Easy Auth / Azure AD in front of the Function App, or a backend-for-frontend holding the
credentials server-side.

**Why not OAuth or JWT?**
Reasonable, but heavier than this project needs. Enforcing the function key would be the
cheap first step.

**What if someone runs up the Azure bill?**
Set cost alerts, monitor usage, and close the auth gap above.
