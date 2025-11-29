# HomeAssistant Deployment Guide

## Quick Start

From the **root** directory:

```powershell
.\deploy.ps1
```

The script will interactively ask you:
1. Which environment? (Testing or Live)
2. What to deploy? (Enter one or more numbers: 1=Frontend, 2=Backend)
   - You can select multiple by separating with spaces or commas (e.g., "1 2" or "1,2")

## Command-Line Options

You can also specify options directly to skip prompts:

```powershell
# Deploy everything to testing
.\deploy.ps1 -Environment testing -Frontend -Backend

# Deploy only frontend to testing
.\deploy.ps1 -Environment testing -Frontend

# Deploy only backend to live
.\deploy.ps1 -Environment live -Backend
```

## What Gets Deployed

### Frontend
- **Testing**: Builds and deploys to GitHub Pages at `/testing/` subdirectory
- **Live**: Builds and deploys to GitHub Pages at root

The frontend is built with the appropriate API URLs for each environment.

### Backend (Azure Functions)
- **Testing**: Deploys to `sterobson-homeassistant-testing` Function App
- **Live**: Deploys to `sterobson-homeassistant` Function App

## Prerequisites

### For Frontend Deployment
- Node.js and npm installed
- Git configured with GitHub authentication
- Write access to the repository's gh-pages branch

### For Backend Deployment
- Azure Functions Core Tools: `npm install -g azure-functions-core-tools@4`
- Azure CLI: `winget install Microsoft.AzureCLI`
- Logged in to Azure: `az login`
- .NET 10 SDK

## Environment Configuration

The script uses these configurations:

### Testing
- Frontend: `https://YOUR_USERNAME.github.io/YOUR_REPO/testing/`
- Backend API: `https://sterobson-homeassistant-testing-e9ahagcjb0dyede6.uksouth-01.azurewebsites.net`

### Live
- Frontend: `https://YOUR_USERNAME.github.io/YOUR_REPO/`
- Backend API: `https://sterobson-homeassistant-cmcvcfe6gdb0h5f4.uksouth-01.azurewebsites.net`

## Adding New Deployment Targets

The script is designed to be extensible. To add a new deployment target:

1. **Add a new parameter** at the top of `deploy.ps1`:
   ```powershell
   [Parameter(Mandatory=$false)]
   [switch]$NewTarget
   ```

2. **Update the configuration object** with settings for your new target:
   ```powershell
   testing = @{
       NewTarget = @{
           Url = "..."
           # other config
       }
   }
   ```

3. **Add to the interactive menu**:
   ```powershell
   Write-Host "  3. New Target" -ForegroundColor White
   ```

   And in the switch statement:
   ```powershell
   "3" { $NewTarget = $true }
   ```

4. **Add the deployment section** after the Backend deployment:

Example structure:
```powershell
# ============================================================================
# Deploy [NEW TARGET]
# ============================================================================
if ($NewTarget) {
    Write-Host ""
    Write-Host "========================================" -ForegroundColor Cyan
    Write-Info "  Deploying [NEW TARGET]"
    Write-Host "========================================" -ForegroundColor Cyan
    Write-Host ""

    # Your deployment logic here
}
```

## Troubleshooting

### Frontend Deployment Issues

**"Not in a git repository"**
- Make sure you're running the script from the repository root
- Ensure the directory is a valid git repository

**"Failed to push to GitHub"**
- Check your git authentication: `git config --list`
- Ensure you have write access to the repository
- Try: `git push origin gh-pages` manually to see the error

### Backend Deployment Issues

**"Azure Functions Core Tools not found"**
- Install: `npm install -g azure-functions-core-tools@4`
- Or: `winget install Microsoft.Azure.FunctionsCoreTools`

**"Not authenticated with Azure"**
- Run: `az login`
- Ensure you're logged in to the correct subscription

**"Can't find app with name..."**
- Verify the Function App name in Azure Portal
- Check you're logged in to the correct Azure subscription
- List your apps: `az functionapp list --query "[].name"`

## Manual Deployment (Advanced)

If you prefer to deploy manually or the script fails:

### Frontend
```bash
cd Frontend
npm ci
$env:GITHUB_PAGES="true"
$env:DEPLOY_PATH="/testing/"  # or "/" for live
$env:VITE_API_URL="https://..."
npm run build
# Then manually copy dist/ contents to gh-pages branch
```

### Backend
```bash
cd Backend/HomeAssistant.Functions
dotnet build -c Release
func azure functionapp publish sterobson-homeassistant-testing --dotnet-isolated
```

## Safety Features

- **Live deployment confirmation**: Always prompts "yes" confirmation before deploying to live
- **Pre-flight checks**: Verifies all required tools are installed before starting
- **Detailed error messages**: Provides helpful guidance when something goes wrong
- **No automatic GitHub Actions**: You have full control over when deployments happen
