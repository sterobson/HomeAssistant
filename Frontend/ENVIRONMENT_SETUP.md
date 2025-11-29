# Environment Variables & Security Setup

## Overview

This document explains how environment variables and "secrets" work in this frontend application.

## ⚠️ IMPORTANT: Understanding Frontend "Secrets"

**There are NO true secrets in frontend applications.** Any value in your JavaScript code:
- Is visible in browser DevTools
- Can be extracted from Network requests
- Can be copied from the bundled JavaScript files

**Azure Function Keys are designed for this.** They are:
- ✅ Rotatable (can be changed if compromised)
- ✅ Trackable (Azure logs which key was used)
- ✅ Rate-limitable (can throttle per key)
- ✅ Revocable (can be disabled individually)

## Environment Files Structure

### Local Development (gitignored)
- **`.env.local`** - Your personal development settings with localhost key
  - ❌ NOT committed to git (in .gitignore)
  - Used for `npm run dev`
  - Contains your `VITE_LOCALHOST_KEY` for local Azure Functions

### Testing Environment (committed)
- **`.env.testing`** - Testing deployment configuration
  - ✅ Committed to git
  - Used for GitHub Pages `/testing/` deployment
  - Function key injected during GitHub Actions build from secrets

### Production Environment (committed)
- **`.env.production`** - Production deployment configuration
  - ✅ Committed to git
  - Used for GitHub Pages root deployment
  - Function key injected during GitHub Actions build from secrets

### Example (template)
- **`.env.example`** - Template for new developers
  - ✅ Committed to git
  - No real secrets, just placeholders

## How to Use

### 1. Local Development

1. Make sure `.env.local` exists (it's gitignored)
2. Add your localhost key from `Backend/HomeAssistant.Functions/local.settings.json`:
   ```bash
   VITE_LOCALHOST_KEY=kjhklhlll987945rkljhdf88df83khndf080487njnkjt0cu08n
   ```
3. Run `npm run dev` - Vite will load `.env.local` automatically

### 2. Setting up Deployment Secrets

To deploy to GitHub Pages with function keys using the `deploy.ps1` script:

1. Copy `deploy.secrets.example.ps1` to `deploy.secrets.ps1` (in the root directory)
2. Edit `deploy.secrets.ps1` and add your Azure Function keys:

   ```powershell
   # Testing environment Azure Function key
   $env:TESTING_FUNCTION_KEY = "your-testing-key-here"

   # Production environment Azure Function key
   $env:PRODUCTION_FUNCTION_KEY = "your-production-key-here"
   ```

3. This file is gitignored and will never be committed
4. The `deploy.ps1` script will automatically load these keys during deployment

### 3. Deploying with deploy.ps1

To deploy frontend and/or backend:

```powershell
# Interactive mode (will prompt for choices)
./deploy.ps1

# Deploy testing frontend
./deploy.ps1 -Environment testing -Frontend

# Deploy live backend
./deploy.ps1 -Environment live -Backend

# Deploy both frontend and backend to testing
./deploy.ps1 -Environment testing -Frontend -Backend
```

### 4. Getting Azure Function Keys

1. Go to Azure Portal
2. Navigate to your Azure Function App
3. Click **App keys** (under Functions)
4. Copy the **default** host key (or create a new one for GitHub Pages)

## Using Environment Variables in Code

In your Vue/JavaScript code:

```javascript
// Access environment variables
const apiUrl = import.meta.env.VITE_API_URL
const functionKey = import.meta.env.VITE_FUNCTION_KEY
const localhostKey = import.meta.env.VITE_LOCALHOST_KEY

// Make API calls
const headers = {}

// If calling localhost, add the localhost key header
if (apiUrl.includes('localhost')) {
  headers['X-Localhost-Key'] = localhostKey
} else {
  // For deployed environments, use Azure Function key
  headers['x-functions-key'] = functionKey
}

fetch(`${apiUrl}/api/schedules?houseId=${houseId}`, {
  headers: headers
})
```

## Build Modes

Vite uses different `.env` files based on the build mode:

- `npm run dev` → Uses `.env.local` (local development)
- `npm run build -- --mode testing` → Uses `.env.testing`
- `npm run build -- --mode production` → Uses `.env.production`

The `deploy.ps1` script overrides specific variables (like VITE_FUNCTION_KEY) by loading them from `deploy.secrets.ps1` during build.

## Security Model

### What's Protected:
1. ✅ **Localhost** - Only you can call functions on `localhost:7071` with the correct key
2. ✅ **CORS** - Browsers block other websites from calling your Azure Functions
3. ✅ **Rate Limiting** - Azure can throttle excessive requests

### What's NOT Protected:
1. ❌ **Function Keys** - Visible in browser, can be extracted
2. ❌ **API Endpoints** - Anyone with the key can call them
3. ❌ **CORS Bypass** - Non-browser tools (curl, Postman) ignore CORS

### Mitigation:
- 🔄 **Rotate keys regularly**
- 📊 **Monitor Azure usage**
- 🚨 **Set cost alerts**
- 🔒 **Consider Azure AD** for sensitive operations

## Key Rotation

If a function key is compromised:

1. Go to Azure Portal → Function App → **App keys**
2. Click **Renew key value** on the compromised key
3. Update the key in your local `deploy.secrets.ps1` file
4. Run `./deploy.ps1` to deploy with the new key
5. Old deployments will stop working until users refresh and get the new bundled key

## FAQ

**Q: Can't someone steal my function key from the browser?**
A: Yes, but that's by design. Function keys are meant to be semi-public. Rotate them if needed.

**Q: How do I make this truly secure?**
A: Implement Azure AD authentication, or use a backend-for-frontend pattern with a real backend server.

**Q: Why not use OAuth/JWT?**
A: You can! But for personal projects with low-risk data, function keys are simpler.

**Q: What if someone runs up my Azure bill?**
A: Set cost alerts in Azure, implement rate limiting, and monitor usage regularly.
