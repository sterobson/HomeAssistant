#!/usr/bin/env pwsh
# Unified Deployment Script for HomeAssistant
# Deploys Frontend and/or Backend to Testing or Live environments

param(
    [Parameter(Mandatory=$false)]
    [ValidateSet("local", "testing", "live")]
    [string]$Environment,

    [Parameter(Mandatory=$false)]
    [switch]$Frontend,

    [Parameter(Mandatory=$false)]
    [switch]$Backend,

    [Parameter(Mandatory=$false)]
    [switch]$HardwareApp
)

# Color functions for better output
function Write-Info($message) { Write-Host $message -ForegroundColor Cyan }
function Write-Success($message) { Write-Host $message -ForegroundColor Green }
function Write-Warning($message) { Write-Host $message -ForegroundColor Yellow }
function Write-Error($message) { Write-Host $message -ForegroundColor Red }
function Write-Gray($message) { Write-Host $message -ForegroundColor Gray }

# Configuration
$config = @{
    local = @{
        Frontend = @{
            DeployPath = "/"
            DestinationDir = $null
            ApiUrl = "http://localhost:7159"
            UseMockApi = $false
        }
        Backend = @{
            AppName = $null  # Not deployed to Azure
            Url = "http://localhost:7159"
        }
    }
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


# Banner
Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  HomeAssistant Deployment Tool" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# Ask for environment if not provided
if (-not $Environment) {
    Write-Warning "Select deployment environment:"
    Write-Host "  1. Local (Build for local development)" -ForegroundColor White
    Write-Host "  2. Testing" -ForegroundColor White
    Write-Host "  3. Live (Production)" -ForegroundColor White
    Write-Host ""

    $choice = Read-Host "Enter choice (1, 2, or 3)"

    switch ($choice) {
        "1" { $Environment = "local" }
        "2" { $Environment = "testing" }
        "3" { $Environment = "live" }
        default {
            Write-Error "Invalid choice. Please select 1, 2, or 3."
            exit 1
        }
    }
}

# Ask what to deploy if not specified
if (-not $Frontend -and -not $Backend -and -not $HardwareApp) {
    Write-Host ""
    Write-Warning "What would you like to deploy?"
    Write-Host "  1. Frontend" -ForegroundColor White
    Write-Host "  2. Backend (Azure Functions)" -ForegroundColor White
    Write-Host "  3. Hardware App (NetDaemon)" -ForegroundColor White
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
            "3" { $HardwareApp = $true }
            default {
                Write-Warning "Ignoring invalid choice: $choice"
            }
        }
    }

    # Validate at least one option was selected
    if (-not $Frontend -and -not $Backend -and -not $HardwareApp) {
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
    if ($HardwareApp) { Write-Gray "  - Hardware App → NetDaemon deployment" }
    Write-Host ""

    $confirmation = Read-Host "Are you sure you want to deploy to LIVE? (yes/no)"
    if ($confirmation -ne "yes") {
        Write-Warning "Deployment cancelled."
        exit 0
    }
}

# Skip backend deployment for local environment (it doesn't make sense)
if ($Environment -eq "local" -and $Backend) {
    Write-Host ""
    Write-Warning "Backend deployment is not supported for local environment."
    Write-Gray "For local backend, run: func start --csharp in Backend\HomeAssistant.Functions"
    $Backend = $false
}

Write-Host ""
if ($HardwareApp) {
    Write-Info "Building Hardware App"
    Write-Gray "  - Hardware App (framework-dependent)"
} elseif ($Environment -eq "local") {
    Write-Info "Building for: Local Development"
} else {
    Write-Info "Deploying to: $Environment"
}
if ($Frontend) { Write-Gray "  - Frontend" }
if ($Backend) { Write-Gray "  - Backend (Azure Functions)" }
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
            npm install --silent

            if ($LASTEXITCODE -ne 0) {
                Write-Error "Failed to install dependencies"
                $deploymentSuccess = $false
            } else {
                Write-Success "Dependencies installed"

                # Build
                Write-Host ""
                Write-Info "Building frontend..."

                # Load function key from local secrets file if it exists
                $secretsFile = Join-Path $PSScriptRoot "deploy.secrets.ps1"
                $functionKey = $null
                $localhostKey = $null

                if (Test-Path $secretsFile) {
                    Write-Gray "Loading function keys from deploy.secrets.ps1..."
                    . $secretsFile

                    if ($Environment -eq "local" -and $env:LOCALHOST_FUNCTION_KEY) {
                        $localhostKey = $env:LOCALHOST_FUNCTION_KEY
                    } elseif ($Environment -eq "testing" -and $env:TESTING_FUNCTION_KEY) {
                        $functionKey = $env:TESTING_FUNCTION_KEY
                    } elseif ($Environment -eq "live" -and $env:PRODUCTION_FUNCTION_KEY) {
                        $functionKey = $env:PRODUCTION_FUNCTION_KEY
                    }
                }

                if ($Environment -ne "local" -and -not $functionKey) {
                    Write-Warning "No function key found for $Environment environment"
                    Write-Gray "Create deploy.secrets.ps1 with your Azure Function keys (this file is gitignored)"
                }

                if ($Environment -eq "local" -and -not $localhostKey) {
                    # Try to load from .env.local
                    $envLocalFile = Join-Path $frontendPath ".env.local"
                    if (Test-Path $envLocalFile) {
                        Write-Gray "Loading localhost key from .env.local..."
                        $envContent = Get-Content $envLocalFile
                        foreach ($line in $envContent) {
                            if ($line -match "VITE_LOCALHOST_KEY=(.+)") {
                                $localhostKey = $matches[1]
                                break
                            }
                        }
                    }
                }

                $env:VITE_API_URL = $selectedConfig.Frontend.ApiUrl
                $env:VITE_USE_MOCK_API = if ($selectedConfig.Frontend.UseMockApi) { "true" } else { "false" }

                if ($Environment -ne "local") {
                    $env:GITHUB_PAGES = "true"
                    $env:DEPLOY_PATH = $selectedConfig.Frontend.DeployPath
                    if ($functionKey) { $env:VITE_FUNCTION_KEY = $functionKey }
                } else {
                    # Local development build
                    if ($localhostKey) { $env:VITE_LOCALHOST_KEY = $localhostKey }
                }

                # Use appropriate build mode
                if ($Environment -eq "local") {
                    # For local, don't specify mode - use development defaults
                    $buildMode = "development"
                } elseif ($Environment -eq "testing") {
                    $buildMode = "testing"
                } else {
                    $buildMode = "production"
                }

                npm run build -- --mode $buildMode

                if ($LASTEXITCODE -ne 0) {
                    Write-Error "Frontend build failed"
                    $deploymentSuccess = $false
                } else {
                    Write-Success "Frontend build completed"

                    $distPath = Join-Path $frontendPath "dist"

                    if (-not (Test-Path $distPath)) {
                        Write-Error "Build output not found at: $distPath"
                        $deploymentSuccess = $false
                    } elseif ($Environment -eq "local") {
                        # Local build - skip GitHub Pages deployment
                        Write-Host ""
                        Write-Success "Local build ready!"
                        Write-Gray "Build output: $distPath"
                    } else {
                        # Deploy to GitHub Pages
                        Write-Host ""
                        Write-Info "Deploying to GitHub Pages..."
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
            Remove-Item Env:\VITE_FUNCTION_KEY -ErrorAction SilentlyContinue
            Remove-Item Env:\VITE_LOCALHOST_KEY -ErrorAction SilentlyContinue
            Remove-Item Env:\TESTING_FUNCTION_KEY -ErrorAction SilentlyContinue
            Remove-Item Env:\PRODUCTION_FUNCTION_KEY -ErrorAction SilentlyContinue
            Remove-Item Env:\LOCALHOST_FUNCTION_KEY -ErrorAction SilentlyContinue
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

            $azCommand = Get-Command az -ErrorAction SilentlyContinue
            if (-not $azCommand) {
                Write-Warning "Azure CLI (az) not found - skipping authentication check"
                Write-Gray "  Install from: https://aka.ms/installazurecliwindows"
                Write-Success "Continuing with deployment (authentication will be checked during func azure functionapp publish)"
            } else {
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
                }
            }

            if ($deploymentSuccess) {

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
# Deploy Hardware App (Self-contained, trimmed executable)
# ============================================================================
if ($HardwareApp) {
    Write-Host ""
    Write-Host "========================================" -ForegroundColor Cyan
    Write-Info "  Building Hardware App"
    Write-Host "========================================" -ForegroundColor Cyan
    Write-Host ""

    $appProjectPath = Join-Path $PSScriptRoot "Backend\App\HomeAssistant.csproj"
    $testProjectPath = Join-Path $PSScriptRoot "Backend\Tests\HomeAssistant.Tests.csproj"

    if (-not (Test-Path $appProjectPath)) {
        Write-Error "Backend App project not found at: $appProjectPath"
        $deploymentSuccess = $false
    } else {
        $appPath = Join-Path $PSScriptRoot "Backend\App"
        Push-Location $appPath

        try {
            # Step 1: Run nd-codegen
            Write-Host ""
            Write-Info "Running nd-codegen..."
            nd-codegen

            if ($LASTEXITCODE -ne 0) {
                Write-Error "nd-codegen failed"
                $deploymentSuccess = $false
            } else {
                Write-Success "nd-codegen completed successfully"

                # Step 2: Build to verify code compiles
                Write-Host ""
                Write-Info "Building project to verify compilation..."
                dotnet build $appProjectPath -c Release --nologo

                if ($LASTEXITCODE -ne 0) {
                    Write-Error "Build failed after nd-codegen"
                    $deploymentSuccess = $false
                } else {
                    Write-Success "Build completed successfully"

                    # Step 3: Run unit tests
                    Write-Host ""
                    Write-Info "Running unit tests..."
                    dotnet test $testProjectPath -c Release --nologo

                    if ($LASTEXITCODE -ne 0) {
                        Write-Error "Unit tests failed"
                        $deploymentSuccess = $false
                    } else {
                        Write-Success "All unit tests passed"

                        # Step 4: Publish the application
                        Write-Host ""
                        Write-Info "Publishing Hardware App..."
                        Write-Gray "This may take a few minutes..."
                        Write-Host ""

                        $outputPath = Join-Path $PSScriptRoot "publish\hardware-app"

                        dotnet publish $appProjectPath `
                            -c Release `
                            --self-contained false `
                            -p:DebugType=none `
                            -p:DebugSymbols=false `
                            -o $outputPath

                        if ($LASTEXITCODE -eq 0) {
                            Write-Host ""
                            Write-Success "Hardware App published successfully!"
                            Write-Gray "Output location: $outputPath"
                            Write-Host ""

                            # List the published files
                            Write-Info "Published files:"
                            Get-ChildItem -Path $outputPath | Select-Object -First 10 | ForEach-Object {
                                $sizeInMB = [math]::Round($_.Length / 1MB, 2)
                                Write-Gray "  $($_.Name) ($sizeInMB MB)"
                            }
                            $totalFiles = (Get-ChildItem -Path $outputPath).Count
                            if ($totalFiles -gt 10) {
                                Write-Gray "  ... and $($totalFiles - 10) more files"
                            }
                            Write-Host ""

                            # Ask if user wants to copy to NetDaemon directory
                            Write-Info "Copy to NetDaemon?"
                            Write-Host "  1. Yes (copy to \\homeassistant.local\config\netdaemon6)" -ForegroundColor White
                            Write-Host "  2. Copy somewhere else" -ForegroundColor White
                            Write-Host "  3. No" -ForegroundColor White
                            Write-Host ""

                            $copyChoice = Read-Host "Enter choice (1, 2, or 3)"

                            $copyDestination = $null
                            switch ($copyChoice) {
                                "1" {
                                    $copyDestination = "\\homeassistant.local\config\netdaemon6"
                                }
                                "2" {
                                    $copyDestination = Read-Host "Enter destination path"
                                }
                                "3" {
                                    Write-Gray "Skipping copy operation"
                                }
                                default {
                                    Write-Warning "Invalid choice. Skipping copy operation."
                                }
                            }

                            if ($copyDestination) {
                                Write-Host ""
                                Write-Info "Copying files to $copyDestination..."

                                if (-not (Test-Path $copyDestination)) {
                                    Write-Error "Destination path does not exist: $copyDestination"
                                    $deploymentSuccess = $false
                                } else {
                                    try {
                                        # Copy all files except appsettings.*.json
                                        Get-ChildItem -Path $outputPath | Where-Object {
                                            -not ($_.Name -like "appsettings.*.json")
                                        } | ForEach-Object {
                                            Copy-Item -Path $_.FullName -Destination $copyDestination -Force
                                        }

                                        Write-Success "Files copied successfully!"
                                        Write-Host ""
                                        Write-Warning "Note: appsettings.*.json files were NOT copied."
                                        Write-Gray "You will need to update these configuration files manually if needed."
                                        Write-Host ""
                                        Write-Info "Next steps:"
                                        Write-Gray "  1. Update appsettings.*.json files if needed"
                                        Write-Gray "  2. Restart the NetDaemon add-on in Home Assistant"
                                    } catch {
                                        Write-Error "Failed to copy files: $($_.Exception.Message)"
                                        $deploymentSuccess = $false
                                    }
                                }
                            }
                        } else {
                            Write-Error "Hardware App publish failed"
                            $deploymentSuccess = $false
                        }
                    }
                }
            }
        } finally {
            Pop-Location
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
    if ($HardwareApp) {
        Write-Host "Built Hardware App" -ForegroundColor White
        Write-Host "  [OK] Hardware App -> publish\hardware-app" -ForegroundColor Green
    } elseif ($Environment -eq "local") {
        Write-Host "Built for: Local Development" -ForegroundColor White
        if ($Frontend) {
            Write-Host "  [OK] Frontend -> Built" -ForegroundColor Green
        }
    } else {
        Write-Host "Deployed to: $Environment" -ForegroundColor White
        if ($Frontend) {
            Write-Host "  [OK] Frontend -> GitHub Pages" -ForegroundColor Green
        }
    }
    if ($Backend) {
        Write-Host "  [OK] Backend -> Azure Functions" -ForegroundColor Green
        Write-Gray "    Endpoints:"
        $apiUrl = $selectedConfig.Backend.Url
        Write-Gray "      ${apiUrl}/api/schedules?houseId={guid}"
        Write-Gray "      ${apiUrl}/api/room-states?houseId={guid}"
    }
    Write-Host ""

    # Offer to run dev server for local frontend builds
    if ($Environment -eq "local" -and $Frontend) {
        Write-Host "========================================" -ForegroundColor Cyan
        Write-Info "  Run Development Server?"
        Write-Host "========================================" -ForegroundColor Cyan
        Write-Host ""
        Write-Host "Would you like to start the Vite development server now?" -ForegroundColor White
        Write-Host "  1. Yes - Start dev server (npm run dev)" -ForegroundColor White
        Write-Host "  2. No - Exit" -ForegroundColor White
        Write-Host ""

        $runChoice = Read-Host "Enter choice (1 or 2)"

        if ($runChoice -eq "1") {
            Write-Host ""
            Write-Info "Starting Vite development server..."
            Write-Gray "Press Ctrl+C to stop the server"
            Write-Host ""

            $frontendPath = Join-Path $PSScriptRoot "Frontend"
            Push-Location $frontendPath
            try {
                npm run dev
            } finally {
                Pop-Location
            }
        } else {
            Write-Host ""
            Write-Gray "To start the dev server later, run:"
            Write-Gray "  cd Frontend"
            Write-Gray "  npm run dev"
            Write-Host ""
        }
    }

    exit 0
} else {
    Write-Host "Some deployments failed. Please check the errors above." -ForegroundColor Red
    Write-Host ""
    exit 1
}
