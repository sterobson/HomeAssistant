#!/usr/bin/env pwsh
# Unified Deployment Script for HomeAssistant
# Deploys Frontend and/or Backend to Testing or Live environments

param(
    [Parameter(Mandatory=$false)]
    [ValidateSet("testing", "live")]
    [string]$Environment,

    [Parameter(Mandatory=$false)]
    [switch]$Frontend,

    [Parameter(Mandatory=$false)]
    [switch]$Backend
)

# Color functions for better output
function Write-Info($message) { Write-Host $message -ForegroundColor Cyan }
function Write-Success($message) { Write-Host $message -ForegroundColor Green }
function Write-Warning($message) { Write-Host $message -ForegroundColor Yellow }
function Write-Error($message) { Write-Host $message -ForegroundColor Red }
function Write-Gray($message) { Write-Host $message -ForegroundColor Gray }

# Configuration
$config = @{
    testing = @{
        Frontend = @{
            DeployPath = "/testing/"
            DestinationDir = "testing"
            ApiUrl = "https://sterobson-homeassistant-testing-e9ahagcjb0dyede6.uksouth-01.azurewebsites.net"
            UseMockApi = $true
        }
        Backend = @{
            AppName = "sterobson-homeassistant-testing"
            Url = "https://sterobson-homeassistant-testing-e9ahagcjb0dyede6.uksouth-01.azurewebsites.net"
        }
    }
    live = @{
        Frontend = @{
            DeployPath = "/"
            DestinationDir = $null  # Root of gh-pages
            ApiUrl = "https://sterobson-homeassistant-cmcvcfe6gdb0h5f4.uksouth-01.azurewebsites.net"
            UseMockApi = $false
        }
        Backend = @{
            AppName = "sterobson-homeassistant"
            Url = "https://sterobson-homeassistant-cmcvcfe6gdb0h5f4.uksouth-01.azurewebsites.net"
        }
    }
}

$houseId = "00000000-0000-0000-0000-000000000000"

# Banner
Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  HomeAssistant Deployment Tool" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# Ask for environment if not provided
if (-not $Environment) {
    Write-Warning "Select deployment environment:"
    Write-Host "  1. Testing" -ForegroundColor White
    Write-Host "  2. Live (Production)" -ForegroundColor White
    Write-Host ""

    $choice = Read-Host "Enter choice (1 or 2)"

    switch ($choice) {
        "1" { $Environment = "testing" }
        "2" { $Environment = "live" }
        default {
            Write-Error "Invalid choice. Please select 1 or 2."
            exit 1
        }
    }
}

# Ask what to deploy if not specified
if (-not $Frontend -and -not $Backend) {
    Write-Host ""
    Write-Warning "What would you like to deploy?"
    Write-Host "  1. Frontend" -ForegroundColor White
    Write-Host "  2. Backend (Azure Functions)" -ForegroundColor White
    Write-Host ""
    Write-Gray "You can select multiple options (e.g., '1 2' or '1,2')"
    Write-Host ""

    $input = Read-Host "Enter choice(s)"

    # Parse input - split by spaces, commas, or combination
    $choices = $input -replace ',', ' ' -split '\s+' | Where-Object { $_ -match '^\d+$' }

    if ($choices.Count -eq 0) {
        Write-Error "No valid choices entered."
        exit 1
    }

    # Process each choice
    foreach ($choice in $choices) {
        switch ($choice) {
            "1" { $Frontend = $true }
            "2" { $Backend = $true }
            default {
                Write-Warning "Ignoring invalid choice: $choice"
            }
        }
    }

    # Validate at least one option was selected
    if (-not $Frontend -and -not $Backend) {
        Write-Error "No valid deployment options selected."
        exit 1
    }
}

# Confirmation for live environment
if ($Environment -eq "live") {
    Write-Host ""
    Write-Warning "WARNING: You are about to deploy to PRODUCTION!"
    Write-Gray "Environment: Live"
    if ($Frontend) { Write-Gray "  - Frontend → GitHub Pages (root)" }
    if ($Backend) { Write-Gray "  - Backend → $($config.live.Backend.Url)" }
    Write-Host ""

    $confirmation = Read-Host "Are you sure you want to deploy to LIVE? (yes/no)"
    if ($confirmation -ne "yes") {
        Write-Warning "Deployment cancelled."
        exit 0
    }
}

Write-Host ""
Write-Info "Deploying to: $Environment"
if ($Frontend) { Write-Gray "  ✓ Frontend" }
if ($Backend) { Write-Gray "  ✓ Backend (Azure Functions)" }
Write-Host ""

$selectedConfig = $config[$Environment]
$deploymentSuccess = $true

# ============================================================================
# Deploy Frontend
# ============================================================================
if ($Frontend) {
    Write-Host ""
    Write-Host "========================================" -ForegroundColor Cyan
    Write-Info "  Deploying Frontend"
    Write-Host "========================================" -ForegroundColor Cyan
    Write-Host ""

    $frontendPath = Join-Path $PSScriptRoot "Frontend"

    if (-not (Test-Path $frontendPath)) {
        Write-Error "Frontend directory not found at: $frontendPath"
        $deploymentSuccess = $false
    } else {
        Push-Location $frontendPath

        try {
            # Install dependencies
            Write-Info "Installing dependencies..."
            npm ci --silent

            if ($LASTEXITCODE -ne 0) {
                Write-Error "Failed to install dependencies"
                $deploymentSuccess = $false
            } else {
                Write-Success "Dependencies installed"

                # Build
                Write-Host ""
                Write-Info "Building frontend..."

                $env:GITHUB_PAGES = "true"
                $env:DEPLOY_PATH = $selectedConfig.Frontend.DeployPath
                $env:VITE_API_URL = $selectedConfig.Frontend.ApiUrl
                $env:VITE_USE_MOCK_API = if ($selectedConfig.Frontend.UseMockApi) { "true" } else { "false" }
                $env:VITE_HOUSE_ID = $houseId

                npm run build

                if ($LASTEXITCODE -ne 0) {
                    Write-Error "Frontend build failed"
                    $deploymentSuccess = $false
                } else {
                    Write-Success "Frontend build completed"

                    # Deploy to GitHub Pages
                    Write-Host ""
                    Write-Info "Deploying to GitHub Pages..."

                    $distPath = Join-Path $frontendPath "dist"

                    if (-not (Test-Path $distPath)) {
                        Write-Error "Build output not found at: $distPath"
                        $deploymentSuccess = $false
                    } else {
                        # Use git worktree to deploy to gh-pages branch (SAFE - doesn't touch main working tree)
                        Write-Gray "Setting up deployment worktree..."

                        # Create temp directory for gh-pages worktree
                        $worktreePath = Join-Path $env:TEMP "gh-pages-deploy-$(Get-Date -Format 'yyyyMMddHHmmss')"

                        try {
                            # Fetch latest gh-pages
                            git fetch origin gh-pages:gh-pages 2>&1 | Out-Null

                            # Create worktree for gh-pages branch
                            git worktree add $worktreePath gh-pages 2>&1 | Out-Null

                            if ($LASTEXITCODE -ne 0) {
                                # gh-pages branch doesn't exist, create orphan branch
                                Write-Warning "gh-pages branch doesn't exist, creating it..."
                                git worktree add --detach $worktreePath 2>&1 | Out-Null
                                Push-Location $worktreePath
                                git checkout --orphan gh-pages 2>&1 | Out-Null
                                git rm -rf . 2>&1 | Out-Null
                                Pop-Location
                            }

                            Push-Location $worktreePath

                            try {
                                # Clean the worktree (remove old files)
                                Write-Gray "Cleaning deployment directory..."
                                Get-ChildItem -Path $worktreePath -Exclude ".git" | Remove-Item -Recurse -Force -ErrorAction SilentlyContinue

                                # Determine target directory within worktree
                                if ($selectedConfig.Frontend.DestinationDir) {
                                    $targetDir = Join-Path $worktreePath $selectedConfig.Frontend.DestinationDir
                                    New-Item -ItemType Directory -Path $targetDir -Force | Out-Null
                                } else {
                                    $targetDir = $worktreePath
                                }

                                # Copy built files to worktree
                                Write-Gray "Copying built files to deployment directory..."
                                Copy-Item -Path "$distPath\*" -Destination $targetDir -Recurse -Force

                                # Add .nojekyll file
                                $nojekyll = Join-Path $worktreePath ".nojekyll"
                                if (-not (Test-Path $nojekyll)) {
                                    New-Item -ItemType File -Path $nojekyll -Force | Out-Null
                                }

                                # Commit and push
                                git add -A 2>&1 | Out-Null

                                $commitMessage = "Deploy $Environment frontend - $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')"
                                git commit -m $commitMessage 2>&1 | Out-Null

                                if ($LASTEXITCODE -eq 0) {
                                    Write-Gray "Pushing to GitHub..."
                                    git push origin gh-pages 2>&1 | Out-Null

                                    if ($LASTEXITCODE -eq 0) {
                                        Write-Success "Frontend deployed to GitHub Pages!"

                                        $pageUrl = if ($selectedConfig.Frontend.DestinationDir) {
                                            "https://sterobson.github.io/HomeAssistant/$($selectedConfig.Frontend.DestinationDir)/"
                                        } else {
                                            "https://sterobson.github.io/HomeAssistant/"
                                        }
                                        Write-Gray "URL: $pageUrl"
                                    } else {
                                        Write-Error "Failed to push to GitHub"
                                        $deploymentSuccess = $false
                                    }
                                } else {
                                    Write-Warning "No changes to commit"
                                }
                            } finally {
                                Pop-Location
                            }
                        } finally {
                            # Remove the worktree
                            if (Test-Path $worktreePath) {
                                git worktree remove $worktreePath --force 2>&1 | Out-Null
                            }
                        }
                    }
                }
            }
        } finally {
            Pop-Location

            # Clean up environment variables
            Remove-Item Env:\GITHUB_PAGES -ErrorAction SilentlyContinue
            Remove-Item Env:\DEPLOY_PATH -ErrorAction SilentlyContinue
            Remove-Item Env:\VITE_API_URL -ErrorAction SilentlyContinue
            Remove-Item Env:\VITE_USE_MOCK_API -ErrorAction SilentlyContinue
            Remove-Item Env:\VITE_HOUSE_ID -ErrorAction SilentlyContinue
        }
    }
}

# ============================================================================
# Deploy Backend (Azure Functions)
# ============================================================================
if ($Backend) {
    Write-Host ""
    Write-Host "========================================" -ForegroundColor Cyan
    Write-Info "  Deploying Backend (Azure Functions)"
    Write-Host "========================================" -ForegroundColor Cyan
    Write-Host ""

    $backendPath = Join-Path $PSScriptRoot "Backend\HomeAssistant.Functions"

    if (-not (Test-Path $backendPath)) {
        Write-Error "Backend directory not found at: $backendPath"
        $deploymentSuccess = $false
    } else {
        # Check if func tool is installed
        Write-Info "Checking Azure Functions Core Tools..."

        $funcVersion = func --version 2>&1
        if ($LASTEXITCODE -ne 0) {
            Write-Error "Azure Functions Core Tools not found!"
            Write-Host ""
            Write-Warning "Install Azure Functions Core Tools:"
            Write-Gray "  npm install -g azure-functions-core-tools@4"
            Write-Gray "  or: winget install Microsoft.Azure.FunctionsCoreTools"
            Write-Host ""
            $deploymentSuccess = $false
        } else {
            Write-Success "Azure Functions Core Tools found"

            # Check Azure authentication
            Write-Info "Checking Azure authentication..."

            $azResult = az account show 2>&1
            if ($LASTEXITCODE -ne 0) {
                Write-Error "Not authenticated with Azure!"
                Write-Host ""
                Write-Warning "Please login to Azure:"
                Write-Gray "  az login"
                Write-Host ""
                $deploymentSuccess = $false
            } else {
                Write-Success "Authenticated with Azure"

                Push-Location $backendPath

                try {
                    # Build
                    Write-Host ""
                    Write-Info "Building backend..."
                    dotnet build -c Release --nologo

                    if ($LASTEXITCODE -ne 0) {
                        Write-Error "Backend build failed"
                        $deploymentSuccess = $false
                    } else {
                        Write-Success "Backend build completed"

                        # Deploy
                        Write-Host ""
                        Write-Info "Deploying to Azure..."
                        Write-Gray "This may take a few minutes..."
                        Write-Host ""

                        $ErrorActionPreference = "Continue"
                        func azure functionapp publish $selectedConfig.Backend.AppName --dotnet-isolated 2>&1 | ForEach-Object {
                            if ($_ -is [System.Management.Automation.ErrorRecord]) {
                                if ($_.Exception.Message -and $_.Exception.Message.Trim()) {
                                    Write-Host $_.Exception.Message
                                }
                            } else {
                                Write-Host $_
                            }
                        }
                        $ErrorActionPreference = "Stop"

                        if ($LASTEXITCODE -eq 0) {
                            Write-Host ""
                            Write-Success "Backend deployed to Azure!"
                            Write-Gray "URL: $($selectedConfig.Backend.Url)/api/"
                        } else {
                            Write-Error "Backend deployment failed"
                            $deploymentSuccess = $false
                        }
                    }
                } finally {
                    Pop-Location
                }
            }
        }
    }
}

# ============================================================================
# Summary
# ============================================================================
Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
if ($deploymentSuccess) {
    Write-Success "  Deployment Complete!"
} else {
    Write-Error "  Deployment Failed!"
}
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

if ($deploymentSuccess) {
    Write-Host "Deployed to: $Environment" -ForegroundColor White
    if ($Frontend) {
        Write-Host "  [OK] Frontend -> GitHub Pages" -ForegroundColor Green
    }
    if ($Backend) {
        Write-Host "  [OK] Backend -> Azure Functions" -ForegroundColor Green
        Write-Gray "    Endpoints:"
        $apiUrl = $selectedConfig.Backend.Url
        Write-Gray "      ${apiUrl}/api/schedules?houseId={guid}"
        Write-Gray "      ${apiUrl}/api/room-states?houseId={guid}"
    }
    Write-Host ""
    exit 0
} else {
    Write-Host "Some deployments failed. Please check the errors above." -ForegroundColor Red
    Write-Host ""
    exit 1
}
