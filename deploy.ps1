#!/usr/bin/env pwsh
# Red/Green Deployment Script for HomeAssistant
# Supports multiple backend slots for zero-downtime deployments

param(
    [Parameter(Mandatory=$false)]
    [string]$Environment,

    [Parameter(Mandatory=$false)]
    [switch]$Frontend,

    [Parameter(Mandatory=$false)]
    [switch]$Backend,

    [Parameter(Mandatory=$false)]
    [switch]$HardwareApp
)

# ============================================================================
# Configuration
# ============================================================================
$script:ScriptRoot = $PSScriptRoot
$script:StateFilePath = Join-Path $PSScriptRoot ".deployment\state.json"
$script:SecretsDir = Join-Path $env:USERPROFILE ".homeassistant-deploy"
$script:SecretsFilePath = Join-Path $script:SecretsDir "secrets.json"
$script:SpectreAvailable = $false
$script:SC = '[/]'  # Spectre Console closing tag

# Window titles for local dev processes
$script:WindowTitleBackend = "HomeAssistant - Backend"
$script:WindowTitleFrontendDev = "HomeAssistant - Frontend (Dev)"
$script:WindowTitleFrontendPreview = "HomeAssistant - Frontend (Preview)"

# PID file directory for tracking spawned windows
$script:PidDir = Join-Path $PSScriptRoot ".deployment"

# Required environment variables for backend
$script:RequiredEnvVars = @(
    "AzureWebJobsStorage",
    "ScheduleStorageConnectionString",
    "AzureSignalRConnectionString"
)

# ============================================================================
# Output Functions
# ============================================================================
function Write-Info($message) { Write-Host $message -ForegroundColor Cyan }
function Write-Success($message) { Write-Host $message -ForegroundColor Green }
function Write-Warning($message) { Write-Host $message -ForegroundColor Yellow }
function Write-ErrorMessage($message) { Write-Host $message -ForegroundColor Red }
function Write-Gray($message) { Write-Host $message -ForegroundColor Gray }

# ============================================================================
# Process Management
# ============================================================================
function Save-WindowPid($name, $processId) {
    if (-not (Test-Path $script:PidDir)) { New-Item -ItemType Directory -Path $script:PidDir -Force | Out-Null }
    $pidFile = Join-Path $script:PidDir "$name.pid"
    $processId | Out-File -FilePath $pidFile -Force
}

function Stop-TrackedWindow($name) {
    $pidFile = Join-Path $script:PidDir "$name.pid"
    if (Test-Path $pidFile) {
        $savedPid = [int](Get-Content $pidFile -ErrorAction SilentlyContinue)
        if ($savedPid) {
            $proc = Get-Process -Id $savedPid -ErrorAction SilentlyContinue
            if ($proc) {
                Write-Gray "  Closing previous $name window (PID $savedPid)"
                Stop-Process -Id $savedPid -Force -ErrorAction SilentlyContinue
            }
        }
        Remove-Item $pidFile -Force -ErrorAction SilentlyContinue
    }
}

# ============================================================================
# Dependency Management
# ============================================================================
function Test-WingetAvailable {
    $winget = Get-Command winget -ErrorAction SilentlyContinue
    return $null -ne $winget
}

function Install-WithWinget($packageId, $packageName) {
    if (-not (Test-WingetAvailable)) {
        Write-ErrorMessage "winget is not available. Please install $packageName manually."
        return $false
    }

    Write-Info "Installing $packageName via winget..."
    winget install --id $packageId --accept-source-agreements --accept-package-agreements

    if ($LASTEXITCODE -ne 0) {
        Write-ErrorMessage "Failed to install $packageName"
        return $false
    }

    Write-Success "$packageName installed successfully"
    Write-Warning "You may need to restart your terminal for the changes to take effect."
    return $true
}

function Ensure-Dependency($command, $packageName, $wingetId, $manualInstallUrl) {
    $cmd = Get-Command $command -ErrorAction SilentlyContinue
    if ($cmd) {
        return $true
    }

    Write-Warning "$packageName is not installed."
    Write-Host ""
    Write-Host "Would you like to install it now?" -ForegroundColor Cyan

    if (Test-WingetAvailable) {
        Write-Host "  1. Yes, install via winget (Recommended)" -ForegroundColor White
        Write-Host "  2. No, I'll install it manually" -ForegroundColor White
        Write-Host ""

        $choice = Read-Host "Enter choice (1 or 2)"

        if ($choice -eq "1") {
            $result = Install-WithWinget $wingetId $packageName

            if ($result) {
                # Refresh PATH
                $env:Path = [System.Environment]::GetEnvironmentVariable("Path", "Machine") + ";" + [System.Environment]::GetEnvironmentVariable("Path", "User")

                # Check again
                $cmd = Get-Command $command -ErrorAction SilentlyContinue
                if ($cmd) {
                    return $true
                } else {
                    Write-Warning "$packageName was installed but the command is not yet available."
                    Write-Warning "Please restart your terminal and run the script again."
                    exit 0
                }
            }
            return $false
        }
    } else {
        Write-Host "  winget is not available for automatic installation." -ForegroundColor Gray
    }

    Write-Host ""
    Write-Info "Please install $packageName manually from:"
    Write-Gray "  $manualInstallUrl"
    Write-Host ""
    Write-Host "After installation, restart your terminal and run this script again."
    exit 0
}

function Ensure-SpectreConsole {
    # Already loaded - nothing to do
    if ($script:SpectreAvailable) {
        return $true
    }

    # PwshSpectreConsole requires PowerShell 7+
    if ($PSVersionTable.PSVersion.Major -lt 7) {
        Write-Gray "Note: For enhanced table display, run this script in PowerShell 7+"
        Write-Gray "  Install: winget install Microsoft.PowerShell"
        Write-Gray "  Then run: pwsh .\deploy.ps1"
        Write-Host ""
        $script:SpectreAvailable = $false
        return $false
    }

    # Check if already loaded in this session
    if (Get-Module -Name PwshSpectreConsole) {
        $script:SpectreAvailable = $true
        return $true
    }

    # Enable UTF-8 for Spectre Console
    $OutputEncoding = [console]::InputEncoding = [console]::OutputEncoding = [System.Text.UTF8Encoding]::new()

    # Check if installed but not loaded
    $installed = Get-Module -ListAvailable -Name PwshSpectreConsole
    if (-not $installed) {
        Write-Info "Installing PwshSpectreConsole module for better table display..."
        try {
            Install-Module -Name PwshSpectreConsole -Scope CurrentUser -Force -AllowClobber -ErrorAction Stop
            Write-Success "PwshSpectreConsole installed"
        } catch {
            Write-Warning "Could not install PwshSpectreConsole: $($_.Exception.Message)"
            Write-Gray "Continuing with basic table display..."
            $script:SpectreAvailable = $false
            return $false
        }
    }

    try {
        Import-Module PwshSpectreConsole -ErrorAction Stop
        $script:SpectreAvailable = $true
        return $true
    } catch {
        Write-Warning "Could not load PwshSpectreConsole module: $($_.Exception.Message)"
        $script:SpectreAvailable = $false
        return $false
    }
}

function Ensure-ProductionDependencies {
    Write-Info "Checking dependencies..."
    Write-Host ""

    # Azure CLI
    $azInstalled = Ensure-Dependency "az" "Azure CLI" "Microsoft.AzureCLI" "https://aka.ms/installazurecliwindows"
    if (-not $azInstalled) { exit 1 }

    # Azure Functions Core Tools
    $funcInstalled = Ensure-Dependency "func" "Azure Functions Core Tools" "Microsoft.Azure.FunctionsCoreTools" "https://docs.microsoft.com/azure/azure-functions/functions-run-local"
    if (-not $funcInstalled) { exit 1 }

    # .NET SDK (needed for building backend)
    $dotnetInstalled = Ensure-Dependency "dotnet" ".NET SDK" "Microsoft.DotNet.SDK.10" "https://dotnet.microsoft.com/download"
    if (-not $dotnetInstalled) { exit 1 }

    # Node.js (needed for frontend)
    $npmInstalled = Ensure-Dependency "npm" "Node.js" "OpenJS.NodeJS.LTS" "https://nodejs.org/"
    if (-not $npmInstalled) { exit 1 }

    Write-Success "All dependencies are installed"
    Write-Host ""
}

function Ensure-LocalDependencies {
    Write-Info "Checking dependencies..."
    Write-Host ""

    # .NET SDK (needed for building backend)
    $dotnetInstalled = Ensure-Dependency "dotnet" ".NET SDK" "Microsoft.DotNet.SDK.10" "https://dotnet.microsoft.com/download"
    if (-not $dotnetInstalled) { exit 1 }

    # Node.js (needed for frontend)
    $npmInstalled = Ensure-Dependency "npm" "Node.js" "OpenJS.NodeJS.LTS" "https://nodejs.org/"
    if (-not $npmInstalled) { exit 1 }

    # Azure Functions Core Tools (for local backend)
    $funcInstalled = Ensure-Dependency "func" "Azure Functions Core Tools" "Microsoft.Azure.FunctionsCoreTools" "https://docs.microsoft.com/azure/azure-functions/functions-run-local"
    if (-not $funcInstalled) { exit 1 }

    Write-Success "All dependencies are installed"
    Write-Host ""
}

function Ensure-HardwareAppDependencies {
    Write-Info "Checking dependencies..."
    Write-Host ""

    $dotnetInstalled = Ensure-Dependency "dotnet" ".NET SDK" "Microsoft.DotNet.SDK.10" "https://dotnet.microsoft.com/download"
    if (-not $dotnetInstalled) { exit 1 }

    Write-Success "All dependencies are installed"
    Write-Host ""
}

# ============================================================================
# State File Management
# ============================================================================
function Get-DeploymentState {
    if (-not (Test-Path $script:StateFilePath)) {
        Write-ErrorMessage "Deployment state file not found at: $script:StateFilePath"
        Write-ErrorMessage "Please ensure .deployment/state.json exists."
        exit 1
    }
    return Get-Content $script:StateFilePath -Raw | ConvertFrom-Json
}

function Save-DeploymentState($state) {
    $state | ConvertTo-Json -Depth 10 | Set-Content $script:StateFilePath -Encoding UTF8
}

function Get-EnvironmentConfig($envName) {
    $state = Get-DeploymentState
    $envConfig = $state.environments.$envName
    if (-not $envConfig) {
        Write-ErrorMessage "Environment '$envName' not found in deployment state."
        exit 1
    }
    return $envConfig
}

# ============================================================================
# Secrets Management (DPAPI)
# ============================================================================
function Ensure-SecretsDir {
    if (-not (Test-Path $script:SecretsDir)) {
        New-Item -ItemType Directory -Path $script:SecretsDir -Force | Out-Null
    }
}

function Get-StoredSecrets {
    Ensure-SecretsDir
    if (-not (Test-Path $script:SecretsFilePath)) {
        return @{}
    }

    try {
        $encrypted = Get-Content $script:SecretsFilePath -Raw | ConvertFrom-Json
        $decrypted = @{}

        foreach ($prop in $encrypted.PSObject.Properties) {
            $key = $prop.Name
            $encValue = $prop.Value

            if ($encValue) {
                try {
                    $secureString = ConvertTo-SecureString $encValue -ErrorAction Stop
                    $credential = New-Object System.Management.Automation.PSCredential("dummy", $secureString)
                    $decrypted[$key] = $credential.GetNetworkCredential().Password
                } catch {
                    Write-Warning "Failed to decrypt secret '$key'. It may have been created by a different user."
                    $decrypted[$key] = $null
                }
            }
        }

        return $decrypted
    } catch {
        Write-Warning "Failed to read secrets file: $_"
        return @{}
    }
}

function Save-StoredSecrets($secrets) {
    Ensure-SecretsDir

    $encrypted = @{}
    foreach ($key in $secrets.Keys) {
        $value = $secrets[$key]
        if ($value) {
            $secureString = ConvertTo-SecureString $value -AsPlainText -Force
            $encrypted[$key] = ConvertFrom-SecureString $secureString
        }
    }

    $encrypted | ConvertTo-Json -Depth 10 | Set-Content $script:SecretsFilePath -Encoding UTF8
}

function Get-SecretKey($envName, $varName) {
    return "$envName`:$varName"
}

function Get-EnvironmentSecret($envName, $varName) {
    $secrets = Get-StoredSecrets
    $key = Get-SecretKey $envName $varName
    return $secrets[$key]
}

function Set-EnvironmentSecret($envName, $varName, $value) {
    $secrets = Get-StoredSecrets
    $key = Get-SecretKey $envName $varName
    $secrets[$key] = $value
    Save-StoredSecrets $secrets
}

function Get-FunctionKey($envName) {
    $secrets = Get-StoredSecrets
    $key = "function-key-$envName"
    return $secrets[$key]
}

function Set-FunctionKey($envName, $token) {
    $secrets = Get-StoredSecrets
    $key = "function-key-$envName"
    $secrets[$key] = $token
    Save-StoredSecrets $secrets
}

function Get-SwaDeploymentToken($envName) {
    $secrets = Get-StoredSecrets
    $key = "swa-deployment-token-$envName"
    return $secrets[$key]
}

function Set-SwaDeploymentToken($envName, $token) {
    $secrets = Get-StoredSecrets
    $key = "swa-deployment-token-$envName"
    $secrets[$key] = $token
    Save-StoredSecrets $secrets
}

function Import-OldSecrets {
    $oldSecretsPath = Join-Path $script:ScriptRoot "deploy.secrets.ps1"
    if (-not (Test-Path $oldSecretsPath)) {
        return $false
    }

    # Check if any function keys already exist
    $secrets = Get-StoredSecrets
    $hasExisting = ($secrets.Keys | Where-Object { $_ -like "function-key-*" }).Count -gt 0
    if ($hasExisting) {
        return $false
    }

    Write-Info "Found old deploy.secrets.ps1 - migrating to encrypted storage..."

    $content = Get-Content $oldSecretsPath -Raw
    $placeholders = @("your-localhost-key-here", "your-testing-function-key-here", "your-production-function-key-here")

    $migrated = $false
    $mapping = @{
        "LOCALHOST_FUNCTION_KEY" = "function-key-local"
        "TESTING_FUNCTION_KEY" = "function-key-testing"
        "PRODUCTION_FUNCTION_KEY" = "function-key-production"
    }

    foreach ($oldKey in $mapping.Keys) {
        if ($content -match "\`$env:${oldKey}\s*=\s*`"(.+?)`"") {
            $value = $matches[1]
            if ($placeholders -notcontains $value) {
                $secrets[$mapping[$oldKey]] = $value
                $migrated = $true
            }
        }
    }

    if ($migrated) {
        Save-StoredSecrets $secrets
        Write-Success "Secrets migrated to encrypted storage at: $script:SecretsDir"
        Write-Gray "You can now delete deploy.secrets.ps1 from the repo"
        Write-Host ""
        return $true
    }

    return $false
}

# ============================================================================
# Azure CLI Helpers
# ============================================================================
function Test-AzureCliInstalled {
    $azCommand = Get-Command az -ErrorAction SilentlyContinue
    return $null -ne $azCommand
}

function Test-AzureCliLoggedIn {
    if (-not (Test-AzureCliInstalled)) {
        return $false
    }

    $ErrorActionPreference = "Continue"
    $null = az account show 2>&1
    $result = $LASTEXITCODE -eq 0
    $ErrorActionPreference = "Stop"

    return $result
}

function Ensure-AzureCliLoggedIn {
    if (-not (Test-AzureCliInstalled)) {
        Write-ErrorMessage "Azure CLI not found. Please install it from https://aka.ms/installazurecliwindows"
        exit 1
    }

    if (-not (Test-AzureCliLoggedIn)) {
        Write-Warning "Not logged in to Azure CLI."
        Write-Host ""
        Write-Host "How would you like to authenticate?" -ForegroundColor Cyan
        Write-Host "  1. Device code (Recommended - works with MFA)" -ForegroundColor White
        Write-Host "  2. Browser login" -ForegroundColor White
        Write-Host ""

        $loginChoice = Read-Host "Enter choice (1 or 2) or press Enter for default"

        if ([string]::IsNullOrWhiteSpace($loginChoice) -or $loginChoice -eq "1") {
            Write-Info "Starting device code authentication..."
            Write-Gray "A code will be displayed. Open a browser, go to https://microsoft.com/devicelogin"
            Write-Gray "and enter the code to authenticate."
            Write-Host ""

            az login --use-device-code
        } else {
            Write-Info "Opening browser for authentication..."
            az login
        }

        if ($LASTEXITCODE -ne 0) {
            Write-ErrorMessage "Azure login failed."
            Write-Host ""
            Write-Host "If you have MFA enabled, try these steps:" -ForegroundColor Yellow
            Write-Host "  1. Run: az login --use-device-code" -ForegroundColor Gray
            Write-Host "  2. Or run: az login --tenant YOUR_TENANT_ID" -ForegroundColor Gray
            Write-Host ""
            exit 1
        }

        Write-Success "Successfully logged in to Azure"
    }
}

function Get-AzureFunctionAppSettings($resourceGroup, $appName) {
    $ErrorActionPreference = "Continue"
    $result = az functionapp config appsettings list --resource-group $resourceGroup --name $appName 2>&1
    $exitCode = $LASTEXITCODE
    $ErrorActionPreference = "Stop"

    if ($exitCode -ne 0) {
        Write-ErrorMessage "Failed to get app settings for $appName"
        Write-Gray $result
        return $null
    }

    return $result | ConvertFrom-Json
}

function Set-AzureFunctionAppSetting($resourceGroup, $appName, $name, $value) {
    Write-Gray "  Setting $name on $appName..."
    $ErrorActionPreference = "Continue"
    $result = az functionapp config appsettings set --resource-group $resourceGroup --name $appName --settings "$name=$value" 2>&1
    $exitCode = $LASTEXITCODE
    $ErrorActionPreference = "Stop"

    if ($exitCode -ne 0) {
        Write-ErrorMessage "  Failed to set $name"
        Write-Gray "  $result"
        return $false
    }

    return $true
}

function Restart-AzureFunctionApp($resourceGroup, $appName) {
    Write-Gray "  Restarting $appName..."
    $ErrorActionPreference = "Continue"
    $result = az functionapp restart --resource-group $resourceGroup --name $appName 2>&1
    $exitCode = $LASTEXITCODE
    $ErrorActionPreference = "Stop"

    if ($exitCode -ne 0) {
        Write-Warning "  Failed to restart $appName (this may be okay)"
        return $false
    }

    return $true
}

function Set-AzureFunctionAppCors($resourceGroup, $appName, $origins) {
    Write-Gray "Setting CORS for $appName..."

    # Clear existing CORS
    $ErrorActionPreference = "Continue"
    $null = az functionapp cors remove --resource-group $resourceGroup --name $appName --allowed-origins "*" 2>&1
    $ErrorActionPreference = "Stop"

    # Add each origin
    foreach ($origin in $origins) {
        $ErrorActionPreference = "Continue"
        $null = az functionapp cors add --resource-group $resourceGroup --name $appName --allowed-origins $origin 2>&1
        $ErrorActionPreference = "Stop"
    }

    Write-Success "CORS configured for $appName"
}

function Get-SlotHostname($resourceGroup, $slotName) {
    $ErrorActionPreference = "Continue"
    $hostname = az functionapp show --resource-group $resourceGroup --name $slotName --query "defaultHostName" -o tsv 2>&1
    $exitCode = $LASTEXITCODE
    $ErrorActionPreference = "Stop"

    if ($exitCode -eq 0 -and $hostname -and $hostname -notlike "*ERROR*") {
        return $hostname.Trim()
    }

    # Fallback to standard format
    return "$slotName.azurewebsites.net"
}

# ============================================================================
# Azure Resource Verification
# ============================================================================
function Test-AzureFunctionAppExists($resourceGroup, $appName) {
    $ErrorActionPreference = "Continue"
    $result = az functionapp show --resource-group $resourceGroup --name $appName 2>&1
    $exitCode = $LASTEXITCODE
    $ErrorActionPreference = "Stop"
    return $exitCode -eq 0
}

function Get-AzureResourceGroups {
    $ErrorActionPreference = "Continue"
    $result = az group list --query "[].name" -o json 2>&1
    $exitCode = $LASTEXITCODE
    $ErrorActionPreference = "Stop"

    if ($exitCode -ne 0) {
        return @()
    }

    try {
        $names = $result | ConvertFrom-Json
        if ($names -is [array]) {
            return $names | Sort-Object
        } elseif ($names) {
            return @($names)
        }
        return @()
    } catch {
        return @()
    }
}

function Get-AzureFunctionAppsInResourceGroup($resourceGroup) {
    $ErrorActionPreference = "Continue"
    $result = az functionapp list --resource-group $resourceGroup --query "[].name" -o json 2>&1
    $exitCode = $LASTEXITCODE
    $ErrorActionPreference = "Stop"

    if ($exitCode -ne 0) {
        return @()
    }

    try {
        $names = $result | ConvertFrom-Json
        if ($names -is [array]) {
            return $names | Sort-Object
        } elseif ($names) {
            return @($names)
        }
        return @()
    } catch {
        return @()
    }
}

function Verify-AzureResources($envName, $envConfig) {
    $resourceGroup = $envConfig.resourceGroup
    $slots = @($envConfig.slots)

    Write-Info "Verifying Azure resources..."
    Write-Gray "  Resource group: $resourceGroup"
    Write-Gray "  Function apps: $($slots -join ', ')"

    # Test if we can access ALL slots
    $inaccessibleSlots = @()
    foreach ($slot in $slots) {
        if (-not (Test-AzureFunctionAppExists $resourceGroup $slot)) {
            $inaccessibleSlots += $slot
        }
    }

    if ($inaccessibleSlots.Count -eq 0) {
        Write-Success "Azure resources verified"
        return $envConfig
    }

    Write-Host ""
    Write-Warning "Cannot access Function App(s) in resource group '$resourceGroup':"
    foreach ($slot in $inaccessibleSlots) {
        Write-Gray "  - $slot"
    }
    Write-Host ""
    Write-Host "This could mean:" -ForegroundColor Cyan
    Write-Host "  - The resource group name has changed" -ForegroundColor Gray
    Write-Host "  - The Function App name has changed" -ForegroundColor Gray
    Write-Host "  - You don't have access to these resources" -ForegroundColor Gray
    Write-Host ""

    Write-Host "Would you like to update the Azure resource configuration?" -ForegroundColor Cyan
    Write-Host "  1. Yes, let me update the settings" -ForegroundColor White
    Write-Host "  2. No, abort deployment" -ForegroundColor White
    Write-Host ""

    $choice = Read-Host "Enter choice (1 or 2)"

    if ($choice -ne "1") {
        Write-Info "Deployment cancelled."
        exit 0
    }

    # Get available resource groups
    Write-Host ""
    Write-Info "Fetching available resource groups..."
    $resourceGroups = @(Get-AzureResourceGroups)

    if ($resourceGroups.Count -eq 0) {
        Write-ErrorMessage "No resource groups found. Check your Azure CLI login and subscription."
        exit 1
    }

    Write-Host ""
    Write-Host "Available resource groups:" -ForegroundColor Cyan
    for ($i = 0; $i -lt $resourceGroups.Count; $i++) {
        $marker = if ($resourceGroups[$i] -eq $resourceGroup) { " (configured)" } else { "" }
        Write-Host "  $($i + 1). $($resourceGroups[$i])$marker" -ForegroundColor White
    }
    Write-Host ""

    $rgChoice = Read-Host "Select resource group (1-$($resourceGroups.Count)) or enter a new name"

    $newResourceGroup = $null
    if ($rgChoice -match '^\d+$') {
        $index = [int]$rgChoice - 1
        if ($index -ge 0 -and $index -lt $resourceGroups.Count) {
            $newResourceGroup = $resourceGroups[$index]
        }
    }

    if (-not $newResourceGroup) {
        $newResourceGroup = $rgChoice
    }

    # Get function apps in the selected resource group
    Write-Host ""
    Write-Info "Fetching Function Apps in '$newResourceGroup'..."
    $functionApps = @(Get-AzureFunctionAppsInResourceGroup $newResourceGroup)

    if ($functionApps.Count -eq 0) {
        Write-Warning "No Function Apps found in '$newResourceGroup'"
        Write-Host ""
        $manualName = Read-Host "Enter the Function App name manually (or press Enter to abort)"

        if ([string]::IsNullOrWhiteSpace($manualName)) {
            Write-Info "Deployment cancelled."
            exit 0
        }

        $functionApps = @($manualName)
    }

    # Let user select/update only the INACCESSIBLE slots
    $newSlots = @()

    Write-Host ""
    Write-Host "Available Function Apps in '$newResourceGroup':" -ForegroundColor Cyan
    for ($i = 0; $i -lt $functionApps.Count; $i++) {
        Write-Host "  $($i + 1). $($functionApps[$i])" -ForegroundColor White
    }
    Write-Host ""

    if ($inaccessibleSlots.Count -lt $slots.Count) {
        Write-Host "Only updating inaccessible slot(s). Accessible slots will be kept." -ForegroundColor Gray
        Write-Host ""
    }

    foreach ($currentSlot in $slots) {
        if ($inaccessibleSlots -contains $currentSlot) {
            # This slot is inaccessible - prompt for replacement
            $slotChoice = Read-Host "Replacement for '$currentSlot' (1-$($functionApps.Count) or enter name)"

            if ([string]::IsNullOrWhiteSpace($slotChoice)) {
                $newSlots += $currentSlot
            } elseif ($slotChoice -match '^\d+$') {
                $index = [int]$slotChoice - 1
                if ($index -ge 0 -and $index -lt $functionApps.Count) {
                    $newSlots += $functionApps[$index]
                } else {
                    $newSlots += $currentSlot
                }
            } else {
                $newSlots += $slotChoice
            }
        } else {
            # This slot is accessible - keep it
            Write-Gray "  Keeping '$currentSlot' (accessible)"
            $newSlots += $currentSlot
        }
    }

    # Verify the new configuration - check ALL slots
    Write-Host ""
    Write-Info "Verifying new configuration..."
    Write-Gray "  Resource group: $newResourceGroup"
    Write-Gray "  Slots: $($newSlots -join ', ')"

    $stillInaccessible = @()
    foreach ($slot in $newSlots) {
        if (-not (Test-AzureFunctionAppExists $newResourceGroup $slot)) {
            $stillInaccessible += $slot
        }
    }

    if ($stillInaccessible.Count -gt 0) {
        Write-ErrorMessage "Still cannot access Function App(s) in '$newResourceGroup':"
        foreach ($slot in $stillInaccessible) {
            Write-Gray "  - $slot"
        }
        Write-Host ""
        Write-Host "Please verify:" -ForegroundColor Yellow
        Write-Host "  - The resource group and Function App names are correct" -ForegroundColor Gray
        Write-Host "  - You have access to these resources" -ForegroundColor Gray
        Write-Host "  - Run 'az account show' to check your subscription" -ForegroundColor Gray
        exit 1
    }

    Write-Success "New configuration verified!"

    # Update the state file
    Write-Host ""
    Write-Info "Updating deployment state..."

    $state = Get-DeploymentState
    $state.environments.$envName.resourceGroup = $newResourceGroup
    $state.environments.$envName.slots = $newSlots
    Save-DeploymentState $state

    Write-Success "Deployment state updated"

    # Return updated config
    return Get-EnvironmentConfig $envName
}

# ============================================================================
# Environment Variable Sync
# ============================================================================
function Sync-EnvironmentVariables($resourceGroup, $slotName, $envName) {
    Write-Info "Checking environment variables for $slotName..."

    $azureSettings = Get-AzureFunctionAppSettings $resourceGroup $slotName
    if (-not $azureSettings) {
        Write-ErrorMessage "Could not fetch Azure settings. Aborting."
        exit 1
    }

    # Convert Azure settings to hashtable
    $azureVars = @{}
    foreach ($setting in $azureSettings) {
        $azureVars[$setting.name] = $setting.value
    }

    $needsUpdate = $false
    $updates = @{}

    foreach ($varName in $script:RequiredEnvVars) {
        # Secrets are stored per environment (e.g., "production"), not per slot
        $localValue = Get-EnvironmentSecret $envName $varName
        $azureValue = $azureVars[$varName]

        $hasLocal = -not [string]::IsNullOrEmpty($localValue)
        $hasAzure = -not [string]::IsNullOrEmpty($azureValue)

        if (-not $hasLocal -and -not $hasAzure) {
            # Neither has it - need to get from user
            Write-Warning "  $varName - NOT SET"
            Write-Host ""
            $newValue = Read-Host "  Enter value for $varName"

            if ([string]::IsNullOrWhiteSpace($newValue)) {
                Write-ErrorMessage "  $varName cannot be empty."
                exit 1
            }

            Set-EnvironmentSecret $envName $varName $newValue
            $updates[$varName] = $newValue
            $needsUpdate = $true
            Write-Success "  $varName - saved locally and will be set in Azure"

        } elseif (-not $hasLocal -and $hasAzure) {
            # Azure has it, store locally
            Set-EnvironmentSecret $envName $varName $azureValue
            Write-Success "  $varName - fetched from Azure and stored locally"

        } elseif ($hasLocal -and -not $hasAzure) {
            # Local has it, push to Azure
            $updates[$varName] = $localValue
            $needsUpdate = $true
            Write-Warning "  $varName - missing in Azure, will be set from local"

        } elseif ($localValue -ne $azureValue) {
            # Both have it but different
            Write-Warning "  $varName - VALUES DIFFER"
            Write-Gray "    Local:  $(if ($localValue.Length -gt 20) { $localValue.Substring(0, 20) + '...' } else { $localValue })"
            Write-Gray "    Azure:  $(if ($azureValue.Length -gt 20) { $azureValue.Substring(0, 20) + '...' } else { $azureValue })"
            Write-Host ""
            Write-Host "  Which value should be used?" -ForegroundColor Cyan
            Write-Host "    1. Keep LOCAL value (update Azure)" -ForegroundColor White
            Write-Host "    2. Keep AZURE value (update local)" -ForegroundColor White
            Write-Host "    3. Enter NEW value" -ForegroundColor White
            Write-Host ""

            $choice = Read-Host "  Enter choice (1/2/3)"

            switch ($choice) {
                "1" {
                    $updates[$varName] = $localValue
                    $needsUpdate = $true
                    Write-Success "  Using local value for $varName"
                }
                "2" {
                    Set-EnvironmentSecret $envName $varName $azureValue
                    Write-Success "  Using Azure value for $varName"
                }
                "3" {
                    $newValue = Read-Host "  Enter new value for $varName"
                    if ([string]::IsNullOrWhiteSpace($newValue)) {
                        Write-ErrorMessage "  $varName cannot be empty."
                        exit 1
                    }
                    Set-EnvironmentSecret $envName $varName $newValue
                    $updates[$varName] = $newValue
                    $needsUpdate = $true
                    Write-Success "  $varName updated"
                }
                default {
                    Write-ErrorMessage "Invalid choice."
                    exit 1
                }
            }
        } else {
            # Both have same value
            Write-Success "  $varName - OK"
        }
    }

    Write-Host ""

    # Ask if user wants to review/change any variables
    Write-Host "Would you like to review or change any environment variables?" -ForegroundColor Cyan
    Write-Host "  1. No, continue with deployment" -ForegroundColor White
    Write-Host "  2. Yes, let me review/change variables" -ForegroundColor White
    Write-Host ""

    $reviewChoice = Read-Host "Enter choice (1 or 2)"

    if ($reviewChoice -eq "2") {
        Write-Host ""
        Write-Info "Current values (stored locally for $envName):"

        foreach ($varName in $script:RequiredEnvVars) {
            $value = Get-EnvironmentSecret $envName $varName
            $displayValue = if ($value.Length -gt 30) { $value.Substring(0, 30) + '...' } else { $value }
            Write-Host "  $varName = $displayValue" -ForegroundColor Gray
        }

        Write-Host ""
        Write-Host "Enter variable name to change (or press Enter to continue):" -ForegroundColor Cyan

        while ($true) {
            $varToChange = Read-Host "Variable name"

            if ([string]::IsNullOrWhiteSpace($varToChange)) {
                break
            }

            if ($script:RequiredEnvVars -notcontains $varToChange) {
                Write-Warning "Unknown variable: $varToChange"
                Write-Gray "Valid variables: $($script:RequiredEnvVars -join ', ')"
                continue
            }

            $newValue = Read-Host "New value for $varToChange"

            if (-not [string]::IsNullOrWhiteSpace($newValue)) {
                Set-EnvironmentSecret $envName $varToChange $newValue
                $updates[$varToChange] = $newValue
                $needsUpdate = $true
                Write-Success "$varToChange updated"
            }

            Write-Host ""
            Write-Host "Enter another variable name to change (or press Enter to continue):" -ForegroundColor Cyan
        }
    }

    # Return updates to be applied later (in parallel with deployment)
    if ($needsUpdate -and $updates.Count -gt 0) {
        Write-Host ""
        Write-Info "$($updates.Count) setting(s) will be applied to Azure during deployment"
    }

    Write-Host ""
    return $updates
}

function Start-EnvVarUpdateJob($resourceGroup, $slotName, $updates) {
    if (-not $updates -or $updates.Count -eq 0) {
        return $null
    }

    # Convert hashtable to array for passing to job
    $updatesList = @()
    foreach ($key in $updates.Keys) {
        $updatesList += @{ Name = $key; Value = $updates[$key] }
    }

    $job = Start-Job -ScriptBlock {
        param($resourceGroup, $slotName, $updatesList)

        foreach ($update in $updatesList) {
            az functionapp config appsettings set --resource-group $resourceGroup --name $slotName --settings "$($update.Name)=$($update.Value)" 2>&1 | Out-Null
            if ($LASTEXITCODE -ne 0) {
                throw "Failed to set $($update.Name)"
            }
        }
        return $true
    } -ArgumentList $resourceGroup, $slotName, $updatesList

    return $job
}

# ============================================================================
# Environment Management
# ============================================================================
function Get-SlotVersion($slotName) {
    $versionUrl = "https://$slotName.azurewebsites.net/api/version"
    try {
        $response = Invoke-RestMethod -Uri $versionUrl -Method Get -TimeoutSec 10 -ErrorAction Stop
        return @{
            Version = $response.version
            Status = "Healthy"
        }
    } catch {
        return @{
            Version = "(unavailable)"
            Status = "Unhealthy"
        }
    }
}

function Show-SlotOverview($envName, $envConfig) {
    $slots = @($envConfig.slots)
    $lastDeployed = $envConfig.lastDeployedSlot
    $frontendPointsTo = $envConfig.frontendPointsTo

    Write-Host ""

    # Build table data
    $tableData = @()
    foreach ($slot in $slots) {
        $versionInfo = Get-SlotVersion $slot

        $notes = @()
        if ($slot -eq $lastDeployed) { $notes += "Last deployed" }
        if ($slot -eq $frontendPointsTo) { $notes += "Frontend points here" }
        $notesStr = $notes -join ", "

        $tableData += [PSCustomObject]@{
            Slot = $slot
            Version = $versionInfo.Version
            Status = $versionInfo.Status
            Notes = $notesStr
        }
    }

    if ($script:SpectreAvailable) {
        $tableData | Format-SpectreTable -Border Rounded -Title "Slot Overview"
    } else {
        # Fallback to basic display
        Write-Host "Slot Overview" -ForegroundColor Cyan
        Write-Host ("=" * 90) -ForegroundColor Gray
        $header = "Slot".PadRight(35) + "Version".PadRight(30) + "Status".PadRight(12) + "Notes"
        Write-Host $header -ForegroundColor Cyan
        Write-Host ("-" * 90) -ForegroundColor Gray

        foreach ($row in $tableData) {
            $line = $row.Slot.PadRight(35) + $row.Version.PadRight(30)
            $status = $row.Status -replace '\[.*?\]', ''
            if ($status -eq "Healthy") {
                Write-Host $line -NoNewline
                Write-Host $status.PadRight(12) -ForegroundColor Green -NoNewline
            } else {
                Write-Host $line -NoNewline
                Write-Host $status.PadRight(12) -ForegroundColor Red -NoNewline
            }
            Write-Host $row.Notes -ForegroundColor Gray
        }
        Write-Host ("=" * 90) -ForegroundColor Gray
    }

    # Frontend info
    if ($envConfig.frontend) {
        Write-Host ""
        Write-Host "Frontend: $($envConfig.frontend.url)" -ForegroundColor Gray
        if ($frontendPointsTo) {
            Write-Host "  Points to: $frontendPointsTo" -ForegroundColor Gray
        }
    }

    Write-Host ""
}

function Show-EnvironmentVariables($envName, $envConfig) {
    $resourceGroup = $envConfig.resourceGroup
    $slots = @($envConfig.slots)

    Write-Host ""

    # Get local values
    $localValues = @{}
    foreach ($varName in $script:RequiredEnvVars) {
        $localValues[$varName] = Get-EnvironmentSecret $envName $varName
    }

    # Get Azure values for each slot
    $azureValues = @{}
    foreach ($slot in $slots) {
        Write-Gray "Fetching settings from $slot..."
        $settings = Get-AzureFunctionAppSettings $resourceGroup $slot
        $azureValues[$slot] = @{}

        if ($settings) {
            foreach ($setting in $settings) {
                if ($script:RequiredEnvVars -contains $setting.name) {
                    $azureValues[$slot][$setting.name] = $setting.value
                }
            }
        } else {
            Write-Warning "  Could not fetch settings from $slot"
        }
    }

    # Build table data
    $tableData = @()
    $varIndex = 1
    foreach ($varName in $script:RequiredEnvVars) {
        $localVal = $localValues[$varName]

        # Check if all values match
        $allMatch = $true
        $firstAzureVal = $null
        foreach ($slot in $slots) {
            $azureVal = $azureValues[$slot][$varName]
            if ($null -eq $firstAzureVal -and -not [string]::IsNullOrEmpty($azureVal)) {
                $firstAzureVal = $azureVal
            }
            if (-not [string]::IsNullOrEmpty($azureVal) -and $azureVal -ne $localVal) {
                $allMatch = $false
            }
            if (-not [string]::IsNullOrEmpty($azureVal) -and -not [string]::IsNullOrEmpty($firstAzureVal) -and $azureVal -ne $firstAzureVal) {
                $allMatch = $false
            }
        }

        # Determine row color based on status
        $rowColor = if ([string]::IsNullOrEmpty($localVal)) { "red" } elseif ($allMatch) { "green" } else { "yellow" }

        # Build row object
        $rowObj = [ordered]@{
            "#" = $varIndex
            Setting = $varName
        }

        # Local value
        $localDisplay = if ([string]::IsNullOrEmpty($localVal)) {
            "X"
        } elseif ($localVal.Length -gt 22) {
            $localVal.Substring(0, 19) + "..."
        } else {
            $localVal
        }
        $rowObj["Local"] = $localDisplay

        # Azure values for each slot
        foreach ($slot in $slots) {
            $shortSlot = $slot -replace "sterobson-", ""
            $azureVal = $azureValues[$slot][$varName]
            $azureDisplay = if ([string]::IsNullOrEmpty($azureVal)) {
                "X"
            } elseif ($azureVal -eq $localVal) {
                "OK"
            } elseif ($azureVal.Length -gt 17) {
                "! " + $azureVal.Substring(0, 14) + "..."
            } else {
                "! $azureVal"
            }
            $rowObj[$shortSlot] = $azureDisplay
        }

        # Store row color for fallback display
        $rowObj["_Color"] = $rowColor
        $tableData += [PSCustomObject]$rowObj
        $varIndex++
    }

    Write-Host ""

    if ($script:SpectreAvailable) {
        $displayData = $tableData | Select-Object -Property * -ExcludeProperty _Color
        $displayData | Format-SpectreTable -Border Rounded -Title "Environment Variables" | Out-Host
    } else {
        Write-Host "Environment Variables" -ForegroundColor Cyan
        Write-Host ("=" * 105) -ForegroundColor Gray

        $header = "#".PadRight(5) + "Setting".PadRight(35) + "Local".PadRight(25)
        foreach ($slot in $slots) {
            $shortSlot = $slot -replace "sterobson-", ""
            $header += $shortSlot.PadRight(20)
        }
        Write-Host $header -ForegroundColor Cyan
        Write-Host ("-" * 105) -ForegroundColor Gray

        foreach ($row in $tableData) {
            $line = "$($row.'#')".PadRight(5) + $row.Setting.PadRight(35) + $row.Local.PadRight(25)
            foreach ($slot in $slots) {
                $shortSlot = $slot -replace "sterobson-", ""
                $line += $row.$shortSlot.PadRight(20)
            }

            switch ($row._Color) {
                "red" { Write-Host $line -ForegroundColor Red }
                "green" { Write-Host $line -ForegroundColor Green }
                "yellow" { Write-Host $line -ForegroundColor Yellow }
                default { Write-Host $line }
            }
        }

        Write-Host ("=" * 105) -ForegroundColor Gray
    }

    Write-Host ""
    Write-Host "Legend: " -NoNewline
    Write-Host "OK = Matches  " -ForegroundColor Green -NoNewline
    Write-Host "! = Different  " -ForegroundColor Yellow -NoNewline
    Write-Host "X = Not set" -ForegroundColor Red
    Write-Host ""

    return @{
        LocalValues = $localValues
        AzureValues = $azureValues
        Slots = $slots
        ResourceGroup = $resourceGroup
    }
}

function Resolve-VarName($userInput) {
    if ([string]::IsNullOrWhiteSpace($userInput)) { return $null }

    $num = 0
    if ([int]::TryParse($userInput, [ref]$num)) {
        if ($num -ge 1 -and $num -le $script:RequiredEnvVars.Count) {
            return $script:RequiredEnvVars[$num - 1]
        }
        return $null
    }

    foreach ($v in $script:RequiredEnvVars) {
        if ($v -eq $userInput) { return $v }
    }
    return $null
}

function Edit-EnvironmentSetting($envName, $context, $resolvedVarName) {
    $varName = $resolvedVarName

    $currentLocal = $context.LocalValues[$varName]
    Write-Host ""
    Write-Host "Current local value: " -NoNewline
    if ([string]::IsNullOrEmpty($currentLocal)) {
        Write-Host "(not set)" -ForegroundColor Red
    } else {
        Write-Host $currentLocal -ForegroundColor Cyan
    }

    foreach ($slot in $context.Slots) {
        $azureVal = $context.AzureValues[$slot][$varName]
        Write-Host "Azure ($slot): " -NoNewline
        if ([string]::IsNullOrEmpty($azureVal)) {
            Write-Host "(not set)" -ForegroundColor Red
        } elseif ($azureVal -eq $currentLocal) {
            Write-Host "OK matches local" -ForegroundColor Green
        } else {
            Write-Host $azureVal -ForegroundColor Yellow
        }
    }

    Write-Host ""
    Write-Host "Options:" -ForegroundColor Cyan
    Write-Host "  1. Set new local value" -ForegroundColor White
    Write-Host "  2. Push local value to all Azure slots" -ForegroundColor White
    Write-Host "  3. Pull value from Azure to local" -ForegroundColor White
    Write-Host "  4. Cancel" -ForegroundColor White
    Write-Host ""

    $choice = Read-Host "Enter choice (1-4)"

    switch ($choice) {
        "1" {
            $newValue = Read-Host "Enter new value"
            if (-not [string]::IsNullOrWhiteSpace($newValue)) {
                Set-EnvironmentSecret $envName $varName $newValue
                Write-Success "Local value updated for $varName"
            }
        }
        "2" {
            if ([string]::IsNullOrEmpty($currentLocal)) {
                Write-ErrorMessage "No local value to push"
                return
            }

            Write-Info "Pushing $varName to all slots..."
            foreach ($slot in $context.Slots) {
                $result = Set-AzureFunctionAppSetting $context.ResourceGroup $slot $varName $currentLocal
                if ($result) {
                    Write-Success "  Updated $slot"
                }
            }
        }
        "3" {
            Write-Host ""
            Write-Host "Pull from which slot?" -ForegroundColor Cyan
            for ($i = 0; $i -lt $context.Slots.Count; $i++) {
                $slot = $context.Slots[$i]
                $val = $context.AzureValues[$slot][$varName]
                $display = if ([string]::IsNullOrEmpty($val)) { "(not set)" } else { $val }
                Write-Host "  $($i + 1). $slot - $display" -ForegroundColor White
            }
            Write-Host ""

            $slotChoice = Read-Host "Enter choice (1-$($context.Slots.Count))"
            $slotIndex = [int]$slotChoice - 1

            if ($slotIndex -ge 0 -and $slotIndex -lt $context.Slots.Count) {
                $selectedSlot = $context.Slots[$slotIndex]
                $azureVal = $context.AzureValues[$selectedSlot][$varName]

                if ([string]::IsNullOrEmpty($azureVal)) {
                    Write-Warning "No value in $selectedSlot to pull"
                } else {
                    Set-EnvironmentSecret $envName $varName $azureVal
                    Write-Success "Local value updated from $selectedSlot"
                }
            }
        }
        default {
            Write-Info "Cancelled"
        }
    }
}

function Copy-EnvironmentVariables($targetEnvName) {
    $state = Get-DeploymentState
    $envNames = @($state.environments.PSObject.Properties.Name | Where-Object { $_ -ne $targetEnvName })

    if ($envNames.Count -eq 0) {
        Write-Warning "No other environments to copy from."
        return
    }

    Write-Host ""
    Write-Host "Copy from which environment?" -ForegroundColor Cyan
    for ($i = 0; $i -lt $envNames.Count; $i++) {
        $displayName = (Get-Culture).TextInfo.ToTitleCase($envNames[$i])
        Write-Host "  $($i + 1). $displayName" -ForegroundColor White
    }
    Write-Host ""

    $srcChoice = Read-Host "Enter choice (or press Enter to cancel)"
    if ([string]::IsNullOrWhiteSpace($srcChoice)) { return }

    $srcIndex = [int]$srcChoice - 1
    if ($srcIndex -lt 0 -or $srcIndex -ge $envNames.Count) {
        Write-Warning "Invalid choice"
        return
    }
    $sourceEnvName = $envNames[$srcIndex]

    Write-Host ""
    Write-Host "What to copy?" -ForegroundColor Cyan
    Write-Host "  1. All variables (overwrite existing)" -ForegroundColor White
    Write-Host "  2. Only variables not set locally" -ForegroundColor White
    Write-Host ""

    $modeChoice = Read-Host "Enter choice (1-2, or press Enter to cancel)"
    if ([string]::IsNullOrWhiteSpace($modeChoice)) { return }

    $onlyEmpty = $modeChoice -eq "2"

    $sourceValues = @{}
    $targetValues = @{}
    foreach ($varName in $script:RequiredEnvVars) {
        $sourceValues[$varName] = Get-EnvironmentSecret $sourceEnvName $varName
        $targetValues[$varName] = Get-EnvironmentSecret $targetEnvName $varName
    }

    $proposals = @()
    foreach ($varName in $script:RequiredEnvVars) {
        $srcVal = $sourceValues[$varName]
        $curVal = $targetValues[$varName]
        $newVal = $curVal

        if (-not [string]::IsNullOrEmpty($srcVal)) {
            if ($onlyEmpty) {
                if ([string]::IsNullOrEmpty($curVal)) {
                    $newVal = $srcVal
                }
            } else {
                $newVal = $srcVal
            }
        }

        $proposals += @{
            Name = $varName
            Current = $curVal
            Proposed = $newVal
            Changed = ($newVal -ne $curVal)
        }
    }

    $changeCount = ($proposals | Where-Object { $_.Changed }).Count
    if ($changeCount -eq 0) {
        Write-Info "No changes to apply."
        return
    }

    $sourceDisplayName = (Get-Culture).TextInfo.ToTitleCase($sourceEnvName)
    $targetDisplayName = (Get-Culture).TextInfo.ToTitleCase($targetEnvName)

    Write-Host ""
    Write-Host "Proposed changes (copying from $sourceDisplayName to $targetDisplayName):" -ForegroundColor Cyan
    Write-Host ("=" * 105) -ForegroundColor Gray

    $header = "Setting".PadRight(38) + "Current".PadRight(28) + "Proposed".PadRight(28) + "Status"
    Write-Host $header -ForegroundColor Cyan
    Write-Host ("-" * 105) -ForegroundColor Gray

    foreach ($p in $proposals) {
        $curDisplay = if ([string]::IsNullOrEmpty($p.Current)) { "(not set)" } elseif ($p.Current.Length -gt 25) { $p.Current.Substring(0, 22) + "..." } else { $p.Current }
        $newDisplay = if ([string]::IsNullOrEmpty($p.Proposed)) { "(not set)" } elseif ($p.Proposed.Length -gt 25) { $p.Proposed.Substring(0, 22) + "..." } else { $p.Proposed }

        $line = $p.Name.PadRight(38) + $curDisplay.PadRight(28) + $newDisplay.PadRight(28)

        if ($p.Changed) {
            $line += "CHANGED"
            Write-Host $line -ForegroundColor Yellow
        } else {
            $line += "-"
            Write-Host $line -ForegroundColor Gray
        }
    }

    Write-Host ("=" * 105) -ForegroundColor Gray
    Write-Host ""
    Write-Host "$changeCount variable(s) will be updated." -ForegroundColor Yellow
    Write-Host ""

    $confirm = Read-Host "Apply these changes? (yes/no)"
    if ($confirm -ne "yes") {
        Write-Info "Cancelled."
        return
    }

    foreach ($p in $proposals) {
        if ($p.Changed) {
            Set-EnvironmentSecret $targetEnvName $p.Name $p.Proposed
            Write-Success "  Updated $($p.Name)"
        }
    }
    Write-Host ""
    Write-Success "Done - $changeCount variable(s) updated locally."
}

function Manage-Slots($envName, $envConfig) {
    while ($true) {
        Write-Host ""
        Write-Host "Slot Management" -ForegroundColor Cyan
        Write-Host ""

        $slots = @($envConfig.slots)
        Write-Host "Current slots:" -ForegroundColor Gray
        for ($i = 0; $i -lt $slots.Count; $i++) {
            Write-Host "  $($i + 1). $($slots[$i])" -ForegroundColor White
        }

        Write-Host ""
        Write-Host "Options:" -ForegroundColor Cyan
        Write-Host "  1. Add a new slot" -ForegroundColor White
        Write-Host "  2. Remove a slot" -ForegroundColor White
        Write-Host "  3. Back" -ForegroundColor White
        Write-Host ""

        $choice = Read-Host "Enter choice (1-3)"

        switch ($choice) {
            "1" {
                Write-Host ""
                Write-Host "Fetching available Function Apps..." -ForegroundColor Gray
                $functionApps = @(Get-AzureFunctionAppsInResourceGroup $envConfig.resourceGroup)

                $available = $functionApps | Where-Object { $slots -notcontains $_ }

                if ($available.Count -eq 0) {
                    Write-Warning "No additional Function Apps found in resource group"
                    Write-Host ""
                    $manualName = Read-Host "Enter Function App name manually (or press Enter to cancel)"
                    if (-not [string]::IsNullOrWhiteSpace($manualName)) {
                        if (Test-AzureFunctionAppExists $envConfig.resourceGroup $manualName) {
                            $state = Get-DeploymentState
                            $state.environments.$envName.slots += $manualName
                            Save-DeploymentState $state
                            $envConfig = Get-EnvironmentConfig $envName
                            Write-Success "Added $manualName to slots"
                        } else {
                            Write-ErrorMessage "Function App '$manualName' not found"
                        }
                    }
                } else {
                    Write-Host ""
                    Write-Host "Available Function Apps:" -ForegroundColor Cyan
                    for ($i = 0; $i -lt $available.Count; $i++) {
                        Write-Host "  $($i + 1). $($available[$i])" -ForegroundColor White
                    }
                    Write-Host ""

                    $addChoice = Read-Host "Select to add (1-$($available.Count)) or press Enter to cancel"
                    if ($addChoice -match '^\d+$') {
                        $index = [int]$addChoice - 1
                        if ($index -ge 0 -and $index -lt $available.Count) {
                            $newSlot = $available[$index]
                            $state = Get-DeploymentState
                            $state.environments.$envName.slots += $newSlot
                            Save-DeploymentState $state
                            $envConfig = Get-EnvironmentConfig $envName
                            Write-Success "Added $newSlot to slots"
                        }
                    }
                }
            }
            "2" {
                if ($slots.Count -le 1) {
                    Write-Warning "Cannot remove the last slot"
                } else {
                    Write-Host ""
                    Write-Host "Select slot to remove:" -ForegroundColor Cyan
                    for ($i = 0; $i -lt $slots.Count; $i++) {
                        Write-Host "  $($i + 1). $($slots[$i])" -ForegroundColor White
                    }
                    Write-Host ""

                    $removeChoice = Read-Host "Select to remove (1-$($slots.Count)) or press Enter to cancel"
                    if ($removeChoice -match '^\d+$') {
                        $index = [int]$removeChoice - 1
                        if ($index -ge 0 -and $index -lt $slots.Count) {
                            $slotToRemove = $slots[$index]
                            $confirm = Read-Host "Remove '$slotToRemove' from configuration? (yes/no)"
                            if ($confirm -eq "yes") {
                                $state = Get-DeploymentState
                                $state.environments.$envName.slots = @($slots | Where-Object { $_ -ne $slotToRemove })
                                Save-DeploymentState $state
                                $envConfig = Get-EnvironmentConfig $envName
                                Write-Success "Removed $slotToRemove from slots"
                            }
                        }
                    }
                }
            }
            "3" {
                return $envConfig
            }
            default {
                Write-Warning "Invalid choice"
            }
        }
    }
}

function Configure-FrontendSettings($envName, $envConfig) {
    $displayEnvName = (Get-Culture).TextInfo.ToTitleCase($envName)

    Write-Host ""
    Write-Host "========================================" -ForegroundColor Cyan
    Write-Host "  Frontend settings for $displayEnvName" -ForegroundColor Cyan
    Write-Host "========================================" -ForegroundColor Cyan
    Write-Host ""

    if (-not $envConfig.frontend) {
        $envConfig | Add-Member -NotePropertyName 'frontend' -NotePropertyValue @{} -Force
    }

    $currentUrl = $envConfig.frontend.url
    $currentSwaName = $envConfig.frontend.staticWebAppName

    Write-Host "Current settings:" -ForegroundColor Cyan
    Write-Host "  1. Static Web App name: $(if ($currentSwaName) { $currentSwaName } else { '(not set)' })" -ForegroundColor White
    Write-Host "  2. Frontend URL: $(if ($currentUrl) { $currentUrl } else { '(not set)' })" -ForegroundColor White
    Write-Host "  3. Back" -ForegroundColor White
    Write-Host ""

    $choice = Read-Host "Enter setting number to edit (1-3)"

    switch ($choice) {
        "1" {
            Write-Host ""
            Write-Host "Enter the Static Web App name from Azure Portal" -ForegroundColor Cyan
            Write-Host "(e.g., 'HomeAssistantUi' or 'HomeAssistantUi-Testing')" -ForegroundColor Gray
            if ($currentSwaName) {
                Write-Host "Current value: $currentSwaName" -ForegroundColor Gray
            }
            Write-Host ""
            $newValue = Read-Host "Static Web App name"

            if (-not [string]::IsNullOrWhiteSpace($newValue)) {
                $state = Get-DeploymentState
                if (-not $state.environments.$envName.frontend) {
                    $state.environments.$envName | Add-Member -NotePropertyName 'frontend' -NotePropertyValue @{} -Force
                }
                if ($state.environments.$envName.frontend -is [PSCustomObject]) {
                    if (-not $state.environments.$envName.frontend.PSObject.Properties['staticWebAppName']) {
                        $state.environments.$envName.frontend | Add-Member -NotePropertyName 'staticWebAppName' -NotePropertyValue $null
                    }
                    $state.environments.$envName.frontend.staticWebAppName = $newValue
                } else {
                    $state.environments.$envName.frontend = @{
                        staticWebAppName = $newValue
                        url = $currentUrl
                    }
                }
                Save-DeploymentState $state
                $envConfig = Get-EnvironmentConfig $envName
                Write-Success "Static Web App name updated to: $newValue"
            }
        }
        "2" {
            Write-Host ""
            Write-Host "Enter the frontend URL" -ForegroundColor Cyan
            Write-Host "(e.g., 'https://ambitious-stone-03cd89503.4.azurestaticapps.net')" -ForegroundColor Gray
            if ($currentUrl) {
                Write-Host "Current value: $currentUrl" -ForegroundColor Gray
            }
            Write-Host ""
            $newValue = Read-Host "Frontend URL"

            if (-not [string]::IsNullOrWhiteSpace($newValue)) {
                $state = Get-DeploymentState
                if (-not $state.environments.$envName.frontend) {
                    $state.environments.$envName | Add-Member -NotePropertyName 'frontend' -NotePropertyValue @{} -Force
                }
                if ($state.environments.$envName.frontend -is [PSCustomObject]) {
                    if (-not $state.environments.$envName.frontend.PSObject.Properties['url']) {
                        $state.environments.$envName.frontend | Add-Member -NotePropertyName 'url' -NotePropertyValue $null
                    }
                    $state.environments.$envName.frontend.url = $newValue
                } else {
                    $state.environments.$envName.frontend = @{
                        staticWebAppName = $currentSwaName
                        url = $newValue
                    }
                }
                Save-DeploymentState $state
                $envConfig = Get-EnvironmentConfig $envName
                Write-Success "Frontend URL updated to: $newValue"
            }
        }
        "3" {
            # Back - do nothing
        }
        default {
            Write-Warning "Invalid choice"
        }
    }

    return $envConfig
}

function Set-SlotEnvVar($envConfig) {
    $slots = @($envConfig.slots)

    Write-Host ""
    Write-Host "Set environment variable on a slot" -ForegroundColor Cyan
    Write-Host ""

    Write-Host "Select slot:" -ForegroundColor Cyan
    for ($i = 0; $i -lt $slots.Count; $i++) {
        Write-Host "  $($i + 1). $($slots[$i])" -ForegroundColor White
    }
    Write-Host "  $($slots.Count + 1). All slots" -ForegroundColor White
    Write-Host ""

    $slotChoice = Read-Host "Enter choice (1-$($slots.Count + 1))"
    $slotIndex = [int]$slotChoice - 1

    $targetSlots = @()
    if ($slotIndex -eq $slots.Count) {
        $targetSlots = $slots
    } elseif ($slotIndex -ge 0 -and $slotIndex -lt $slots.Count) {
        $targetSlots = @($slots[$slotIndex])
    } else {
        Write-Warning "Invalid choice"
        return
    }

    Write-Host ""
    $varName = Read-Host "Enter variable name"
    if ([string]::IsNullOrWhiteSpace($varName)) {
        return
    }

    $varValue = Read-Host "Enter value"
    if ([string]::IsNullOrWhiteSpace($varValue)) {
        return
    }

    Write-Host ""
    foreach ($slot in $targetSlots) {
        $result = Set-AzureFunctionAppSetting $envConfig.resourceGroup $slot $varName $varValue
        if ($result) {
            Write-Success "  Set $varName on $slot"
        }
    }
}

function Add-NewEnvironment {
    Write-Host ""
    Write-Host "Add new environment" -ForegroundColor Cyan
    Write-Host "-------------------" -ForegroundColor Gray
    Write-Host ""

    $envName = Read-Host "Enter environment name (e.g. staging or development)"
    if ([string]::IsNullOrWhiteSpace($envName)) {
        Write-Warning "Environment name cannot be empty"
        return $null
    }
    $envName = $envName.ToLower().Trim()

    $state = Get-DeploymentState
    if ($state.environments.$envName) {
        Write-Warning "Environment '$envName' already exists"
        return $null
    }

    Write-Host ""
    Write-Host "Enter details for the new environment:" -ForegroundColor Cyan
    Write-Host ""

    $resourceGroup = Read-Host "Azure Resource Group name"
    if ([string]::IsNullOrWhiteSpace($resourceGroup)) {
        Write-Warning "Resource group cannot be empty"
        return $null
    }

    $firstSlot = Read-Host "Function App name (first slot)"
    if ([string]::IsNullOrWhiteSpace($firstSlot)) {
        Write-Warning "Function App name cannot be empty"
        return $null
    }

    $frontendUrl = Read-Host "Frontend URL (optional - press Enter to skip)"

    $envConfig = @{
        resourceGroup = $resourceGroup
        slots = @($firstSlot)
        cors = @("https://sterobson.github.io", "http://localhost:5173")
    }

    if (-not [string]::IsNullOrWhiteSpace($frontendUrl)) {
        $envConfig.frontend = @{
            url = $frontendUrl
        }
    }

    $state.environments | Add-Member -NotePropertyName $envName -NotePropertyValue $envConfig -Force
    Save-DeploymentState $state

    Write-Success "Environment '$envName' created successfully"
    Write-Host ""

    return $envName
}

function Start-ManagementMode {
    Write-Host ""
    Write-Host "========================================" -ForegroundColor Cyan
    Write-Host "  Environment Management" -ForegroundColor Cyan
    Write-Host "========================================" -ForegroundColor Cyan
    Write-Host ""

    $null = Ensure-SpectreConsole
    Ensure-AzureCliLoggedIn

    while ($true) {
        $state = Get-DeploymentState
        $envNames = @($state.environments.PSObject.Properties.Name)

        Write-Host ""
        Write-Host "Which environment would you like to manage?" -ForegroundColor Cyan
        $index = 1
        foreach ($env in $envNames) {
            $displayName = (Get-Culture).TextInfo.ToTitleCase($env)
            Write-Host "  $index. $displayName" -ForegroundColor White
            $index++
        }
        Write-Host "  $index. Add new environment" -ForegroundColor Green
        $backIndex = $index + 1
        Write-Host "  $backIndex. Back" -ForegroundColor White
        Write-Host ""

        $envChoice = Read-Host "Enter choice (default: 1)"
        if ([string]::IsNullOrWhiteSpace($envChoice)) { $envChoice = "1" }

        $choiceNum = [int]$envChoice
        if ($choiceNum -eq $backIndex) {
            Write-Host ""
            return
        } elseif ($choiceNum -eq $index) {
            $envName = Add-NewEnvironment
            if (-not $envName) { continue }
        } elseif ($choiceNum -ge 1 -and $choiceNum -lt $index) {
            $envName = $envNames[$choiceNum - 1]
        } else {
            $envName = $envNames[0]
        }

        $envConfig = Get-EnvironmentConfig $envName
        $envConfig = Verify-AzureResources $envName $envConfig

        $goBack = $false
        while (-not $goBack) {
            Show-SlotOverview $envName $envConfig

            $displayEnvName = (Get-Culture).TextInfo.ToTitleCase($envName)
            Write-Host "What would you like to do with $displayEnvName`?" -ForegroundColor Cyan
            Write-Host "  1. View/edit environment variables" -ForegroundColor White
            Write-Host "  2. Set environment variable on slot" -ForegroundColor White
            Write-Host "  3. Manage backend deployment slots" -ForegroundColor White
            Write-Host "  4. Configure frontend settings" -ForegroundColor White
            Write-Host "  5. Refresh" -ForegroundColor White
            Write-Host "  6. Back to environment selection" -ForegroundColor White
            Write-Host ""

            $menuChoice = Read-Host "Enter choice (1-6)"

            switch ($menuChoice) {
                "1" {
                    while ($true) {
                        $context = Show-EnvironmentVariables $envName $envConfig

                        Write-Host "Enter a variable number to edit it, or:" -ForegroundColor Gray
                        Write-Host "  97. Push all local values to all slots" -ForegroundColor White
                        Write-Host "  98. Copy variables from another environment" -ForegroundColor White
                        Write-Host "  99. Back" -ForegroundColor White
                        Write-Host ""

                        $varChoice = Read-Host "Choice"

                        if ([string]::IsNullOrWhiteSpace($varChoice) -or $varChoice -eq "99") {
                            break
                        }

                        if ($varChoice -eq "97") {
                            Write-Host ""
                            $slotList = $context.Slots -join ", "
                            Write-Warning "This will update ALL settings on the following slots with local values:"
                            Write-Warning "  $slotList"
                            $confirm = Read-Host "Are you sure? (yes/no)"

                            if ($confirm -eq "yes") {
                                foreach ($slot in $context.Slots) {
                                    Write-Info "Updating $slot..."
                                    foreach ($vName in $script:RequiredEnvVars) {
                                        $localVal = $context.LocalValues[$vName]
                                        if (-not [string]::IsNullOrEmpty($localVal)) {
                                            $null = Set-AzureFunctionAppSetting $context.ResourceGroup $slot $vName $localVal
                                        }
                                    }
                                    Write-Success "  $slot updated"
                                }
                            }
                            continue
                        }

                        if ($varChoice -eq "98") {
                            Copy-EnvironmentVariables $envName
                            continue
                        }

                        $resolved = Resolve-VarName $varChoice
                        if ($resolved) {
                            Edit-EnvironmentSetting $envName $context $resolved
                        } else {
                            Write-Warning "Unknown variable: $varChoice"
                        }
                    }
                }
                "2" {
                    Set-SlotEnvVar $envConfig
                }
                "3" {
                    $envConfig = Manage-Slots $envName $envConfig
                }
                "4" {
                    $envConfig = Configure-FrontendSettings $envName $envConfig
                }
                "5" {
                    Write-Info "Refreshing..."
                }
                "6" {
                    $goBack = $true
                }
                default {
                    Write-Warning "Invalid choice"
                }
            }
        }
    }
}

# ============================================================================
# Function Key Management
# ============================================================================
function Start-FunctionKeyManagement {
    Write-Host ""
    Write-Host "========================================" -ForegroundColor Cyan
    Write-Host "  Function key management" -ForegroundColor Cyan
    Write-Host "========================================" -ForegroundColor Cyan
    Write-Host ""
    Write-Gray "Function keys are DPAPI-encrypted and stored in:"
    Write-Gray "  $script:SecretsDir"
    Write-Host ""

    Import-OldSecrets | Out-Null

    while ($true) {
        $keyNames = @("function-key-local", "function-key-testing", "function-key-production")
        $keyLabels = @("Local", "Testing", "Production")

        Write-Host "Current function keys:" -ForegroundColor Cyan
        Write-Host ("=" * 70) -ForegroundColor Gray

        $secrets = Get-StoredSecrets
        $index = 1
        foreach ($keyName in $keyNames) {
            $value = $secrets[$keyName]
            $label = $keyLabels[$index - 1]
            $display = if ([string]::IsNullOrEmpty($value)) {
                "(not set)"
            } elseif ($value.Length -gt 20) {
                $value.Substring(0, 10) + "..." + $value.Substring($value.Length - 5)
            } else {
                $value
            }

            $color = if ($display -eq "(not set)") { "Red" } else { "Green" }
            Write-Host "  $index. " -NoNewline
            Write-Host "$label".PadRight(15) -NoNewline -ForegroundColor White
            Write-Host $display -ForegroundColor $color
            $index++
        }

        Write-Host ("=" * 70) -ForegroundColor Gray
        Write-Host ""
        Write-Host "Enter a number to edit, or press Enter to go back" -ForegroundColor Gray
        Write-Host ""

        $choice = Read-Host "Choice"

        if ([string]::IsNullOrWhiteSpace($choice)) {
            break
        }

        $choiceNum = 0
        if (-not [int]::TryParse($choice, [ref]$choiceNum) -or $choiceNum -lt 1 -or $choiceNum -gt 3) {
            Write-Warning "Invalid choice"
            Write-Host ""
            continue
        }

        $selectedKey = $keyNames[$choiceNum - 1]
        $selectedLabel = $keyLabels[$choiceNum - 1]

        Write-Host ""
        $newValue = Read-Host "Enter new value for $selectedLabel function key"

        if (-not [string]::IsNullOrWhiteSpace($newValue)) {
            $secrets[$selectedKey] = $newValue
            Save-StoredSecrets $secrets
            Write-Success "$selectedLabel function key updated"
        } else {
            Write-Gray "No change made"
        }

        Write-Host ""
    }
}

# ============================================================================
# Slot Selection
# ============================================================================
function Select-DeploymentSlot($envConfig) {
    $slots = @($envConfig.slots)
    $lastSlot = $envConfig.lastDeployedSlot

    if ($slots.Count -eq 1) {
        return $slots[0]
    }

    # Determine the recommended slot (the "other" slot from last deployment)
    $suggestedSlot = $null
    if ($lastSlot) {
        $lastIndex = [array]::IndexOf($slots, $lastSlot)
        if ($lastIndex -ge 0) {
            $nextIndex = ($lastIndex + 1) % $slots.Count
            $suggestedSlot = $slots[$nextIndex]
        }
    }

    if (-not $suggestedSlot) {
        $suggestedSlot = $slots[0]
    }

    # Reorder slots so recommended is first
    $orderedSlots = @($suggestedSlot) + ($slots | Where-Object { $_ -ne $suggestedSlot })

    Write-Host ""
    Write-Info "Select deployment slot:"

    for ($i = 0; $i -lt $orderedSlots.Count; $i++) {
        $slot = $orderedSlots[$i]
        $marker = ""
        if ($slot -eq $suggestedSlot) {
            $marker = " (Recommended)"
        }
        if ($slot -eq $lastSlot) {
            $marker += " [Last deployed]"
        }
        Write-Host "  $($i + 1). $slot$marker" -ForegroundColor White
    }

    Write-Host ""
    $choice = Read-Host "Enter choice (1-$($orderedSlots.Count)) or press Enter for default"

    if ([string]::IsNullOrWhiteSpace($choice)) {
        $choice = "1"
    }

    $index = [int]$choice - 1
    if ($index -lt 0 -or $index -ge $orderedSlots.Count) {
        Write-ErrorMessage "Invalid choice."
        exit 1
    }

    return $orderedSlots[$index]
}

# ============================================================================
# Version Verification
# ============================================================================
$script:VersionFilePath = Join-Path $PSScriptRoot ".deployment\version.txt"

function Get-ExpectedVersion {
    if (Test-Path $script:VersionFilePath) {
        $timestamp = Get-Content $script:VersionFilePath -Raw
    } else {
        $timestamp = Get-Date -Format "yyyy.MM.dd.HH.mm"
    }

    $gitHash = git rev-parse --short HEAD 2>$null
    if ($LASTEXITCODE -ne 0 -or -not $gitHash) {
        $gitHash = "unknown"
    }
    return "$timestamp-$gitHash"
}

function Test-BackendVersion($slotName, $expectedVersion, $maxRetries = 10) {
    $hostname = Get-SlotHostname $envConfig.resourceGroup $slotName
    $baseUrl = "https://$hostname/api/version"

    Write-Info "Verifying backend deployment..."
    Write-Gray "  Expected version: $expectedVersion"
    Write-Gray "  URL: $baseUrl"

    $headers = @{
        "Cache-Control" = "no-cache, no-store, must-revalidate"
        "Pragma" = "no-cache"
        "Origin" = "https://portal.azure.com"
    }

    $hadDnsError = $false

    for ($i = 1; $i -le $maxRetries; $i++) {
        Write-Gray "  Attempt $i of $maxRetries..."

        try {
            $cacheBuster = [System.Guid]::NewGuid().ToString("N")
            $timestamp = [DateTimeOffset]::UtcNow.ToUnixTimeMilliseconds()
            $url = "$baseUrl`?_=$cacheBuster&t=$timestamp"

            $response = Invoke-WebRequest -Uri $url -Method Get -Headers $headers -TimeoutSec 10 -UseBasicParsing -DisableKeepAlive
            $json = $response.Content | ConvertFrom-Json
            $actualVersion = $json.version

            Write-Gray "  Actual version: $actualVersion"

            if ($actualVersion -eq $expectedVersion) {
                Write-Success "Backend version verified!"
                return $true
            }

            Write-Warning "  Version mismatch, waiting for deployment to complete..."
        } catch {
            $errorMessage = $_.Exception.Message
            Write-Gray "  Request failed: $errorMessage"
            if ($errorMessage -match "No such host is known|could not be resolved") {
                $hadDnsError = $true
            }
        }

        if ($i -lt $maxRetries) {
            Start-Sleep -Seconds 10
        }
    }

    Write-ErrorMessage "Backend version verification failed after $maxRetries attempts."
    if ($hadDnsError) {
        Write-Host ""
        Write-Warning "Hint: The hostname could not be resolved."
        Write-Warning "Azure may have assigned a longer default domain with random characters."
        Write-Warning "Check the Azure Portal for the actual hostname and update your slot configuration."
    }
    return $false
}

# ============================================================================
# Git Status Check
# ============================================================================
function Test-GitClean {
    $status = git status --porcelain 2>$null
    return [string]::IsNullOrWhiteSpace($status)
}

function Get-GitCommitHash {
    return git rev-parse --short HEAD 2>$null
}

function Get-GitChangedFiles {
    $status = git status --porcelain 2>$null
    if ([string]::IsNullOrWhiteSpace($status)) {
        return @()
    }
    return $status -split "`n" | ForEach-Object { $_.Substring(3).Trim() } | Where-Object { $_ }
}

function Test-OnlyDeploymentFilesChanged {
    $changedFiles = Get-GitChangedFiles
    if ($changedFiles.Count -eq 0) {
        return $false
    }

    $deploymentFiles = @(
        ".deployment/version.txt",
        ".deployment/state.json",
        "deploy.ps1"
    )

    foreach ($file in $changedFiles) {
        if ($deploymentFiles -notcontains $file) {
            return $false
        }
    }

    return $true
}

function Write-VersionFile {
    $timestamp = Get-Date -Format "yyyy.MM.dd.HH.mm"
    $versionDir = Split-Path $script:VersionFilePath -Parent

    if (-not (Test-Path $versionDir)) {
        New-Item -ItemType Directory -Path $versionDir -Force | Out-Null
    }

    Set-Content -Path $script:VersionFilePath -Value $timestamp -NoNewline
    return $timestamp
}

function Commit-AndPush($message) {
    Write-Info "Committing changes..."
    git add -A
    if ($LASTEXITCODE -ne 0) {
        Write-ErrorMessage "Failed to stage changes."
        exit 1
    }

    git commit -m "$message"
    if ($LASTEXITCODE -ne 0) {
        Write-ErrorMessage "Failed to commit changes."
        exit 1
    }

    Write-Success "Changes committed."

    Write-Info "Pushing to origin..."
    git push
    if ($LASTEXITCODE -ne 0) {
        Write-ErrorMessage "Failed to push. You may need to pull first or resolve conflicts."
        exit 1
    }

    Write-Success "Changes pushed to origin."
}

function Ensure-GitClean {
    Write-Info "Writing version file..."
    $timestamp = Write-VersionFile
    Write-Success "Version file written: $timestamp"

    if (Test-OnlyDeploymentFilesChanged) {
        Write-Info "Only deployment files changed, auto-committing..."
        Commit-AndPush "Deployment $timestamp"
        return
    }

    if (-not (Test-GitClean)) {
        Write-Host ""
        Write-Host "!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!" -ForegroundColor Red
        Write-Host "!!                                                          !!" -ForegroundColor Red
        Write-Host "!!                        WARNING                           !!" -ForegroundColor Red
        Write-Host "!!                                                          !!" -ForegroundColor Red
        Write-Host "!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!" -ForegroundColor Red
        Write-Host ""
        Write-Warning "You have uncommitted changes. Production deployments require a clean git state"
        Write-Warning "so the version number includes the correct commit hash."
        Write-Host ""

        git status --short | ForEach-Object { Write-Host "  $_" -ForegroundColor Gray }

        Write-Host ""
        Write-Host "Would you like to commit and push your changes now?" -ForegroundColor Cyan
        Write-Host "  1. Yes, commit and push for me" -ForegroundColor White
        Write-Host "  2. Cancel deployment" -ForegroundColor White
        Write-Host ""

        $choice = Read-Host "Enter choice (1 or 2)"

        switch ($choice) {
            "1" {
                Write-Host ""
                $commitMessage = Read-Host "Enter commit message"

                if ([string]::IsNullOrWhiteSpace($commitMessage)) {
                    Write-ErrorMessage "Commit message cannot be empty."
                    exit 1
                }

                Commit-AndPush $commitMessage
            }
            default {
                Write-Info "Deployment cancelled."
                exit 0
            }
        }
    }
}

# ============================================================================
# Local Backend - Start Azure Functions
# ============================================================================
function Start-LocalBackend {
    Write-Host ""
    Write-Host "========================================" -ForegroundColor Cyan
    Write-Info "  Starting local backend (Azure Functions)"
    Write-Host "========================================" -ForegroundColor Cyan
    Write-Host ""

    $backendPath = Join-Path $script:ScriptRoot "Backend\HomeAssistant.Functions"

    if (-not (Test-Path $backendPath)) {
        Write-ErrorMessage "Backend directory not found at: $backendPath"
        return $false
    }

    # Check if something is already listening on port 7159
    $portInUse = Get-NetTCPConnection -LocalPort 7159 -State Listen -ErrorAction SilentlyContinue
    if ($portInUse) {
        Write-Warning "Port 7159 is already in use. Stopping existing process..."
        $existingPid = ($portInUse | Select-Object -First 1).OwningProcess
        Stop-Process -Id $existingPid -Force -ErrorAction SilentlyContinue
        Start-Sleep -Seconds 2
    }

    Stop-TrackedWindow "backend"

    # Build
    Write-Info "Building backend..."
    dotnet build $backendPath --nologo --verbosity quiet

    if ($LASTEXITCODE -ne 0) {
        Write-ErrorMessage "Backend build failed"
        return $false
    }
    Write-Success "Backend build completed"

    # Spawn func start in a new window
    Write-Host ""
    Write-Info "Starting Azure Functions on port 7159..."

    $funcProcess = Start-Process powershell -ArgumentList "-NoExit", "-Command", "Set-Location '$backendPath'; `$Host.UI.RawUI.WindowTitle = '$script:WindowTitleBackend'; func start --port 7159" -PassThru

    Save-WindowPid "backend" $funcProcess.Id

    # Wait and verify
    Write-Gray "Waiting for Azure Functions to start..."
    Start-Sleep -Seconds 5

    $listening = Get-NetTCPConnection -LocalPort 7159 -State Listen -ErrorAction SilentlyContinue
    if ($listening) {
        Write-Host ""
        Write-Success "Azure Functions started on http://localhost:7159"
    } else {
        Write-Host ""
        Write-Warning "Port 7159 is not yet listening. The Functions host may still be starting up."
        Write-Gray "Check the '$script:WindowTitleBackend' window for details."
    }

    return $true
}

# ============================================================================
# Local Frontend - Start Dev Server or Production Preview
# ============================================================================
function Start-LocalFrontend {
    param(
        [switch]$ProductionBuild
    )

    $mode = if ($ProductionBuild) { "Production Preview" } else { "Dev Server" }
    $port = if ($ProductionBuild) { 5175 } else { 5173 }
    $windowTitle = if ($ProductionBuild) { $script:WindowTitleFrontendPreview } else { $script:WindowTitleFrontendDev }
    $pidName = if ($ProductionBuild) { "frontend-preview" } else { "frontend-dev" }

    Write-Host ""
    Write-Host "========================================" -ForegroundColor Cyan
    Write-Info "  Starting Frontend $mode"
    Write-Host "========================================" -ForegroundColor Cyan
    Write-Host ""

    $connection = Get-NetTCPConnection -LocalPort $port -State Listen -ErrorAction SilentlyContinue
    if ($connection) {
        Write-Warning "Frontend server is running on port $port. Shutting it down..."
        $processId = $connection.OwningProcess
        Stop-Process -Id $processId -Force -ErrorAction SilentlyContinue
        Start-Sleep -Seconds 2
        Write-Success "Frontend server stopped"
    }
    Stop-TrackedWindow $pidName

    $frontendPath = Join-Path $script:ScriptRoot "Frontend"

    $nodeModules = Join-Path $frontendPath "node_modules"
    if (-not (Test-Path $nodeModules)) {
        Write-Info "Installing frontend dependencies..."
        Push-Location $frontendPath
        try {
            npm install --silent
            if ($LASTEXITCODE -ne 0) {
                Write-ErrorMessage "Failed to install dependencies"
                return $false
            }
            Write-Success "Dependencies installed"
        } finally {
            Pop-Location
        }
    }

    if ($ProductionBuild) {
        Write-Info "Building production bundle..."
        Push-Location $frontendPath
        try {
            npm run build
            if ($LASTEXITCODE -ne 0) {
                Write-ErrorMessage "Failed to build frontend"
                return $false
            }
            Write-Success "Production build complete"
        } finally {
            Pop-Location
        }

        Write-Info "Starting production preview server..."
        $proc = Start-Process -FilePath "powershell.exe" -ArgumentList "-NoExit", "-Command", "`$host.UI.RawUI.WindowTitle = '$windowTitle'; cd '$frontendPath'; npm run preview -- --port $port" -WindowStyle Normal -PassThru
    } else {
        Write-Info "Starting frontend dev server..."
        $proc = Start-Process -FilePath "powershell.exe" -ArgumentList "-NoExit", "-Command", "`$host.UI.RawUI.WindowTitle = '$windowTitle'; cd '$frontendPath'; npm run dev -- --port $port" -WindowStyle Normal -PassThru
    }
    Save-WindowPid $pidName $proc.Id

    Start-Sleep -Seconds 3

    $viteRunning = Get-NetTCPConnection -LocalPort $port -State Listen -ErrorAction SilentlyContinue
    if ($viteRunning) {
        Write-Success "Frontend started on http://localhost:$port"
        if ($ProductionBuild) {
            Write-Gray "  (Production build - check Network tab to verify bundle sizes)"
        }
    } else {
        Write-Warning "Frontend may still be starting. Check the console window."
    }

    return $true
}

# ============================================================================
# Production Backend Build (Background Job)
# ============================================================================
function Start-BackendBuildJob {
    $backendPath = Join-Path $script:ScriptRoot "Backend\HomeAssistant.Functions"

    $job = Start-Job -ScriptBlock {
        param($backendPath)

        Set-Location $backendPath
        dotnet build --configuration Release --nologo --verbosity quiet 2>&1
        if ($LASTEXITCODE -ne 0) {
            throw "Backend build failed"
        }
        return $true
    } -ArgumentList $backendPath

    return $job
}

function Wait-ForBackendBuild($buildJob) {
    if (-not $buildJob) {
        return $true
    }

    Write-Info "Waiting for backend build to complete..."
    $null = Wait-Job $buildJob -Timeout 300
    $buildState = $buildJob.State

    if ($buildState -ne "Completed") {
        Write-ErrorMessage "Backend build job failed or timed out (state: $buildState)"
        $jobOutput = Receive-Job $buildJob
        Write-Gray $jobOutput
        $null = Remove-Job $buildJob -Force -ErrorAction SilentlyContinue
        return $false
    }

    try {
        $buildResult = Receive-Job $buildJob -ErrorAction Stop
        Write-Success "Backend build completed"
        $null = Remove-Job $buildJob -Force -ErrorAction SilentlyContinue
        return $true
    } catch {
        Write-ErrorMessage "Backend build failed: $($_.Exception.Message)"
        $null = Remove-Job $buildJob -Force -ErrorAction SilentlyContinue
        return $false
    }
}

# ============================================================================
# Production Backend Deployment
# ============================================================================
function Deploy-ProductionBackend($envConfig, $slotName, $backendBuildJob, $envName, $alsoDeployingFrontend = $false) {
    Write-Host ""
    Write-Host "========================================" -ForegroundColor Cyan
    Write-Info "  Deploying Backend to $slotName"
    Write-Host "========================================" -ForegroundColor Cyan
    Write-Host ""

    $resourceGroup = $envConfig.resourceGroup
    $backendPath = Join-Path $script:ScriptRoot "Backend\HomeAssistant.Functions"

    # Sync environment variables
    $envVarUpdates = Sync-EnvironmentVariables $resourceGroup $slotName $envName

    # Get expected version before deployment
    $expectedVersion = Get-ExpectedVersion

    # Add DEPLOYMENT_VERSION to the env var updates
    if (-not $envVarUpdates) {
        $envVarUpdates = @{}
    }
    $envVarUpdates["DEPLOYMENT_VERSION"] = $expectedVersion

    # Start parallel jobs: CORS and env var updates
    Write-Info "Starting parallel configuration (CORS + env vars)..."

    $corsJob = Start-Job -ScriptBlock {
        param($resourceGroup, $slotName, $origins)
        foreach ($origin in $origins) {
            az functionapp cors add --resource-group $resourceGroup --name $slotName --allowed-origins $origin 2>&1 | Out-Null
        }
    } -ArgumentList $resourceGroup, $slotName, $envConfig.cors

    $envVarJob = Start-EnvVarUpdateJob $resourceGroup $slotName $envVarUpdates

    # Wait for backend build to complete
    if (-not (Wait-ForBackendBuild $backendBuildJob)) {
        $null = Stop-Job $corsJob -ErrorAction SilentlyContinue
        if ($envVarJob) { $null = Stop-Job $envVarJob -ErrorAction SilentlyContinue }
        $null = Remove-Job $corsJob -Force -ErrorAction SilentlyContinue
        if ($envVarJob) { $null = Remove-Job $envVarJob -Force -ErrorAction SilentlyContinue }
        return $false
    }

    # Deploy to Azure
    Write-Host ""
    Write-Info "Deploying to Azure Functions..."
    Write-Gray "This may take a few minutes..."
    Write-Host ""

    Push-Location $backendPath
    try {
        $ErrorActionPreference = "Continue"
        func azure functionapp publish $slotName --dotnet-isolated 2>&1 | ForEach-Object {
            if ($_ -is [System.Management.Automation.ErrorRecord]) {
                if ($_.Exception.Message -and $_.Exception.Message.Trim()) {
                    Write-Host $_.Exception.Message
                }
            } else {
                Write-Host $_
            }
        }
        $funcResult = $LASTEXITCODE
        $ErrorActionPreference = "Stop"

        if ($funcResult -ne 0) {
            Write-ErrorMessage "Backend deployment failed"
            return $false
        }
    } finally {
        Pop-Location
    }

    # Wait for parallel jobs to complete
    Write-Info "Waiting for parallel configuration to complete..."
    $null = Wait-Job $corsJob -Timeout 60
    if ($envVarJob) { $null = Wait-Job $envVarJob -Timeout 120 }

    $envVarSuccess = if ($envVarJob) { Receive-Job $envVarJob -ErrorAction SilentlyContinue } else { $true }

    $null = Remove-Job $corsJob -Force -ErrorAction SilentlyContinue
    if ($envVarJob) { $null = Remove-Job $envVarJob -Force -ErrorAction SilentlyContinue }

    if ($envVarSuccess -eq $false) {
        Write-ErrorMessage "Failed to set environment variables"
        return $false
    }
    Write-Success "CORS configuration completed"
    if ($envVarJob) { Write-Success "Environment variables updated (DEPLOYMENT_VERSION: $expectedVersion)" }

    # Restart the function app to pick up the new settings
    Write-Info "Restarting function app to apply settings..."
    Restart-AzureFunctionApp $resourceGroup $slotName

    # Verify deployment
    Write-Host ""
    $verified = Test-BackendVersion $slotName $expectedVersion
    if (-not $verified) {
        Write-ErrorMessage "Backend verification failed. The deployment may have issues."
        Write-Host ""
        Write-Host "What would you like to do?" -ForegroundColor Cyan
        Write-Host "  1. Abort" -ForegroundColor White
        if ($alsoDeployingFrontend) {
            Write-Host "  2. Abort but still deploy frontend" -ForegroundColor White
            Write-Host ""
            $choice = Read-Host "Enter choice (1 or 2)"
            if ($choice -eq "2") {
                return "frontend-only"
            }
        } else {
            Write-Host ""
            $null = Read-Host "Press Enter to abort"
        }
        return $false
    }

    # Update state with last deployed slot
    $state = Get-DeploymentState
    if (-not $state.environments.$envName.PSObject.Properties['lastDeployedSlot']) {
        $state.environments.$envName | Add-Member -NotePropertyName 'lastDeployedSlot' -NotePropertyValue $null
    }
    $state.environments.$envName.lastDeployedSlot = $slotName
    Save-DeploymentState $state

    $hostname = Get-SlotHostname $resourceGroup $slotName
    Write-Host ""
    Write-Success "Backend deployed to $slotName!"
    Write-Gray "URL: https://$hostname/api"

    return $true
}

# ============================================================================
# Production Frontend Deployment (Azure Static Web Apps)
# ============================================================================
function Deploy-ProductionFrontend($envConfig, $backendSlot, $envName) {
    Write-Host ""
    Write-Host "========================================" -ForegroundColor Cyan
    Write-Info "  Deploying Frontend to Azure Static Web Apps"
    Write-Host "========================================" -ForegroundColor Cyan
    Write-Host ""

    $frontendPath = Join-Path $script:ScriptRoot "Frontend"
    $resourceGroup = $envConfig.resourceGroup
    $staticWebAppName = $envConfig.frontend.staticWebAppName
    $frontendUrl = $envConfig.frontend.url
    $hostname = Get-SlotHostname $resourceGroup $backendSlot
    $apiUrl = "https://$hostname/api"

    # Check SWA CLI
    Write-Info "Checking Azure Static Web Apps CLI..."
    $swaCommand = Get-Command swa -ErrorAction SilentlyContinue
    if (-not $swaCommand) {
        Write-Warning "Azure Static Web Apps CLI not found."
        Write-Host ""
        Write-Host "Would you like to install it now?" -ForegroundColor Cyan
        Write-Host "  1. Yes, install @azure/static-web-apps-cli globally" -ForegroundColor White
        Write-Host "  2. No, cancel deployment" -ForegroundColor White
        Write-Host ""

        $installChoice = Read-Host "Enter choice (1 or 2)"

        if ($installChoice -eq "1") {
            Write-Info "Installing Azure Static Web Apps CLI..."
            npm install -g @azure/static-web-apps-cli
            if ($LASTEXITCODE -ne 0) {
                Write-ErrorMessage "Failed to install SWA CLI"
                return $false
            }
            Write-Success "SWA CLI installed"
        } else {
            return $false
        }
    }

    # Get deployment token for this environment
    $displayEnvName = (Get-Culture).TextInfo.ToTitleCase($envName)
    $deploymentToken = Get-SwaDeploymentToken $envName

    if (-not $deploymentToken) {
        Write-Warning "Deployment token not found for $displayEnvName environment."
        Write-Host ""
        Write-Host "How would you like to provide the token?" -ForegroundColor Cyan
        Write-Host "  1. Fetch automatically from Azure (requires Azure CLI)" -ForegroundColor White
        Write-Host "  2. Enter manually" -ForegroundColor White
        Write-Host ""

        $tokenChoice = Read-Host "Enter choice (1 or 2)"

        switch ($tokenChoice) {
            "1" {
                Ensure-AzureCliLoggedIn

                if (-not $staticWebAppName) {
                    Write-ErrorMessage "Static Web App name not configured for $displayEnvName environment."
                    Write-Gray "Add 'staticWebAppName' to frontend config in .deployment/state.json"
                    return $false
                }

                Write-Info "Fetching deployment token from Azure for $displayEnvName..."
                Write-Gray "  Static Web App: $staticWebAppName"
                Write-Gray "  Resource Group: $resourceGroup"
                $ErrorActionPreference = "Continue"
                $deploymentToken = az staticwebapp secrets list `
                    --name $staticWebAppName `
                    --resource-group $resourceGroup `
                    --query "properties.apiKey" -o tsv 2>&1
                $tokenResult = $LASTEXITCODE
                $ErrorActionPreference = "Stop"

                if ($tokenResult -ne 0 -or -not $deploymentToken -or $deploymentToken -like "*ERROR*") {
                    Write-ErrorMessage "Failed to fetch deployment token"
                    if ($deploymentToken) {
                        Write-Gray "Azure CLI output: $deploymentToken"
                    }
                    return $false
                }

                Write-Success "Deployment token retrieved for $displayEnvName"
            }
            "2" {
                Write-Host ""
                Write-Gray "Find the token in Azure Portal:"
                Write-Gray "  Static Web Apps > $staticWebAppName > Manage deployment token"
                Write-Host ""
                $deploymentToken = Read-Host "Enter deployment token"

                if ([string]::IsNullOrWhiteSpace($deploymentToken)) {
                    Write-ErrorMessage "No token provided"
                    return $false
                }
            }
            default {
                return $false
            }
        }

        # Save token for this environment
        $null = Set-SwaDeploymentToken $envName $deploymentToken
        Write-Success "Deployment token saved for $displayEnvName"
    }

    # Get function key for this environment
    Import-OldSecrets | Out-Null
    $functionKey = Get-FunctionKey $envName

    if (-not $functionKey) {
        Write-Warning "Function key not found for $envName environment."
        Write-Host ""
        $functionKey = Read-Host "Enter the function key for $envName (or press Enter to skip)"

        if (-not [string]::IsNullOrWhiteSpace($functionKey)) {
            Set-FunctionKey $envName $functionKey
            Write-Success "Function key saved for $envName"
        } else {
            Write-Warning "Deploying without function key - API calls may fail"
        }
    }

    # Verify backend is healthy before deploying frontend
    Write-Info "Verifying backend is healthy..."
    $versionUrl = "https://$hostname/api/version"

    try {
        $response = Invoke-RestMethod -Uri $versionUrl -Method Get -TimeoutSec 10
        Write-Success "Backend is healthy (version: $($response.version))"
    } catch {
        Write-ErrorMessage "Backend is not responding. Cannot deploy frontend."
        Write-Gray "URL: $versionUrl"
        Write-Gray "Error: $($_.Exception.Message)"
        return $false
    }

    # Build frontend
    Write-Info "Building frontend..."
    Write-Gray "  API URL: $apiUrl"
    Write-Gray "  Frontend URL: $frontendUrl"

    Push-Location $frontendPath
    try {
        # Install dependencies
        npm install --silent
        if ($LASTEXITCODE -ne 0) {
            Write-ErrorMessage "Failed to install dependencies"
            return $false
        }

        # Build with environment variables
        $env:VITE_API_URL = $apiUrl
        if ($functionKey) { $env:VITE_FUNCTION_KEY = $functionKey }

        npm run build

        if ($LASTEXITCODE -ne 0) {
            Write-ErrorMessage "Frontend build failed"
            return $false
        }

        Write-Success "Frontend build completed"

        # Deploy
        $distPath = Join-Path $frontendPath "dist"

        Write-Host ""
        Write-Info "Deploying to Azure Static Web Apps..."

        $ErrorActionPreference = "Continue"
        swa deploy $distPath `
            --deployment-token $deploymentToken `
            --env production 2>&1 | ForEach-Object {
                if ($_ -is [System.Management.Automation.ErrorRecord]) {
                    if ($_.Exception.Message -and $_.Exception.Message.Trim()) {
                        Write-Host $_.Exception.Message
                    }
                } else {
                    Write-Host $_
                }
            }
        $swaResult = $LASTEXITCODE
        $ErrorActionPreference = "Stop"

        if ($swaResult -ne 0) {
            Write-ErrorMessage "Frontend deployment failed"
            return $false
        }

    } finally {
        Pop-Location

        # Clean up environment variables
        Remove-Item Env:\VITE_API_URL -ErrorAction SilentlyContinue
        Remove-Item Env:\VITE_FUNCTION_KEY -ErrorAction SilentlyContinue
    }

    Write-Host ""
    Write-Success "Frontend deployed to Azure Static Web Apps!"
    Write-Gray "URL: $frontendUrl"

    # Track which backend the frontend points to
    $state = Get-DeploymentState
    if (-not $state.environments.$envName.PSObject.Properties['frontendPointsTo']) {
        $state.environments.$envName | Add-Member -NotePropertyName 'frontendPointsTo' -NotePropertyValue $null
    }
    $state.environments.$envName.frontendPointsTo = $backendSlot
    Save-DeploymentState $state

    return $true
}

# ============================================================================
# Build Frontend in Background (for parallel "Both" deployment)
# ============================================================================
function Start-FrontendBuildJob($envConfig, $backendSlot, $envName) {
    $frontendPath = Join-Path $script:ScriptRoot "Frontend"
    $hostname = Get-SlotHostname $envConfig.resourceGroup $backendSlot
    $apiUrl = "https://$hostname/api"

    Import-OldSecrets | Out-Null
    $functionKey = Get-FunctionKey $envName

    $job = Start-Job -ScriptBlock {
        param($frontendPath, $apiUrl, $functionKey)

        Set-Location $frontendPath

        # Install dependencies
        npm install --silent 2>&1
        if ($LASTEXITCODE -ne 0) {
            throw "Failed to install dependencies"
        }

        # Build with environment variables
        $env:VITE_API_URL = $apiUrl
        if ($functionKey) { $env:VITE_FUNCTION_KEY = $functionKey }

        npm run build 2>&1
        if ($LASTEXITCODE -ne 0) {
            throw "Frontend build failed"
        }

        return $true
    } -ArgumentList $frontendPath, $apiUrl, $functionKey

    return $job
}

function Deploy-FrontendFromBuildJob($envConfig, $backendSlot, $buildJob, $envName) {
    Write-Host ""
    Write-Host "========================================" -ForegroundColor Cyan
    Write-Info "  Deploying Frontend to Azure Static Web Apps"
    Write-Host "========================================" -ForegroundColor Cyan
    Write-Host ""

    $frontendPath = Join-Path $script:ScriptRoot "Frontend"
    $resourceGroup = $envConfig.resourceGroup
    $staticWebAppName = $envConfig.frontend.staticWebAppName
    $frontendUrl = $envConfig.frontend.url

    # Wait for build job to complete
    Write-Info "Waiting for frontend build to complete..."
    $null = Wait-Job $buildJob -Timeout 300
    $buildState = $buildJob.State

    if ($buildState -ne "Completed") {
        Write-ErrorMessage "Frontend build job failed or timed out (state: $buildState)"
        $jobOutput = Receive-Job $buildJob
        Write-Gray $jobOutput
        $null = Remove-Job $buildJob -Force -ErrorAction SilentlyContinue
        return $false
    }

    try {
        $null = Receive-Job $buildJob -ErrorAction Stop
        Write-Success "Frontend build completed"
    } catch {
        Write-ErrorMessage "Frontend build failed: $($_.Exception.Message)"
        $null = Remove-Job $buildJob -Force -ErrorAction SilentlyContinue
        return $false
    }

    $null = Remove-Job $buildJob -Force -ErrorAction SilentlyContinue

    # Get deployment token for this environment
    $displayEnvName = (Get-Culture).TextInfo.ToTitleCase($envName)
    $deploymentToken = Get-SwaDeploymentToken $envName

    if (-not $deploymentToken) {
        Write-Warning "Deployment token not found for $displayEnvName environment."
        Write-Host ""
        Write-Host "How would you like to provide the token?" -ForegroundColor Cyan
        Write-Host "  1. Fetch automatically from Azure" -ForegroundColor White
        Write-Host "  2. Enter manually" -ForegroundColor White
        Write-Host ""

        $tokenChoice = Read-Host "Enter choice (1 or 2)"

        switch ($tokenChoice) {
            "1" {
                Ensure-AzureCliLoggedIn

                if (-not $staticWebAppName) {
                    Write-ErrorMessage "Static Web App name not configured for $displayEnvName environment."
                    Write-Gray "Add 'staticWebAppName' to frontend config in .deployment/state.json"
                    return $false
                }

                Write-Info "Fetching deployment token from Azure for $displayEnvName..."
                Write-Gray "  Static Web App: $staticWebAppName"
                Write-Gray "  Resource Group: $resourceGroup"
                $ErrorActionPreference = "Continue"
                $deploymentToken = az staticwebapp secrets list `
                    --name $staticWebAppName `
                    --resource-group $resourceGroup `
                    --query "properties.apiKey" -o tsv 2>&1
                $tokenResult = $LASTEXITCODE
                $ErrorActionPreference = "Stop"

                if ($tokenResult -ne 0 -or -not $deploymentToken -or $deploymentToken -like "*ERROR*") {
                    Write-ErrorMessage "Failed to fetch deployment token"
                    if ($deploymentToken) {
                        Write-Gray "Azure CLI output: $deploymentToken"
                    }
                    return $false
                }

                Write-Success "Deployment token retrieved for $displayEnvName"
            }
            "2" {
                Write-Host ""
                Write-Gray "Find the token in Azure Portal:"
                Write-Gray "  Static Web Apps > $staticWebAppName > Manage deployment token"
                Write-Host ""
                $deploymentToken = Read-Host "Enter deployment token"

                if ([string]::IsNullOrWhiteSpace($deploymentToken)) {
                    Write-ErrorMessage "No token provided"
                    return $false
                }
            }
            default {
                return $false
            }
        }

        # Save token for this environment
        $null = Set-SwaDeploymentToken $envName $deploymentToken
        Write-Success "Deployment token saved for $displayEnvName"
    }

    # Verify backend is healthy
    $hostname = Get-SlotHostname $resourceGroup $backendSlot
    Write-Info "Verifying backend is healthy..."
    $versionUrl = "https://$hostname/api/version"

    try {
        $response = Invoke-RestMethod -Uri $versionUrl -Method Get -TimeoutSec 10
        Write-Success "Backend is healthy (version: $($response.version))"
    } catch {
        Write-ErrorMessage "Backend is not responding. Cannot deploy frontend."
        return $false
    }

    # Deploy
    $distPath = Join-Path $frontendPath "dist"

    Write-Info "Deploying to Azure Static Web Apps..."

    $ErrorActionPreference = "Continue"
    $swaOutput = swa deploy $distPath `
        --deployment-token $deploymentToken `
        --env production 2>&1
    $swaResult = $LASTEXITCODE
    $ErrorActionPreference = "Stop"

    # Display output
    $swaOutput | ForEach-Object {
        if ($_ -is [System.Management.Automation.ErrorRecord]) {
            if ($_.Exception.Message -and $_.Exception.Message.Trim()) {
                Write-Host $_.Exception.Message
            }
        } else {
            Write-Host $_
        }
    }

    if ($swaResult -ne 0) {
        Write-ErrorMessage "Frontend deployment failed"
        return $false
    }

    Write-Host ""
    Write-Success "Frontend deployed to Azure Static Web Apps!"
    Write-Gray "URL: $frontendUrl"

    # Track which backend the frontend points to
    try {
        $state = Get-DeploymentState
        if (-not $state.environments.$envName.PSObject.Properties['frontendPointsTo']) {
            $state.environments.$envName | Add-Member -NotePropertyName 'frontendPointsTo' -NotePropertyValue $null
        }
        $state.environments.$envName.frontendPointsTo = $backendSlot
        Save-DeploymentState $state
    } catch {
        Write-Warning "Failed to update state file: $($_.Exception.Message)"
        # Don't fail deployment for state tracking issues
    }

    return $true
}

# ============================================================================
# Build Hardware App (NetDaemon)
# ============================================================================
function Build-HardwareApp {
    Write-Host ""
    Write-Host "========================================" -ForegroundColor Cyan
    Write-Info "  Building Hardware App"
    Write-Host "========================================" -ForegroundColor Cyan
    Write-Host ""

    $appProjectPath = Join-Path $script:ScriptRoot "Backend\App\HomeAssistant.csproj"
    $testProjectPath = Join-Path $script:ScriptRoot "Backend\Tests\HomeAssistant.Tests.csproj"

    if (-not (Test-Path $appProjectPath)) {
        Write-ErrorMessage "Backend App project not found at: $appProjectPath"
        return $false
    }

    $appPath = Join-Path $script:ScriptRoot "Backend\App"
    Push-Location $appPath

    try {
        # Step 1: Run nd-codegen
        Write-Host ""
        Write-Info "Running nd-codegen..."
        nd-codegen

        if ($LASTEXITCODE -ne 0) {
            Write-ErrorMessage "nd-codegen failed"
            return $false
        }
        Write-Success "nd-codegen completed successfully"

        # Step 2: Build to verify compilation
        Write-Host ""
        Write-Info "Building project to verify compilation..."
        dotnet build $appProjectPath -c Release --nologo

        if ($LASTEXITCODE -ne 0) {
            Write-ErrorMessage "Build failed after nd-codegen"
            return $false
        }
        Write-Success "Build completed successfully"

        # Step 3: Run unit tests
        Write-Host ""
        Write-Info "Running unit tests..."
        dotnet test $testProjectPath -c Release --nologo

        if ($LASTEXITCODE -ne 0) {
            Write-ErrorMessage "Unit tests failed"
            return $false
        }
        Write-Success "All unit tests passed"

        # Step 4: Publish the application
        Write-Host ""
        Write-Info "Publishing Hardware App..."
        Write-Gray "This may take a few minutes..."
        Write-Host ""

        $outputPath = Join-Path $script:ScriptRoot "publish\hardware-app"

        dotnet publish $appProjectPath `
            -c Release `
            --self-contained false `
            -p:DebugType=none `
            -p:DebugSymbols=false `
            -o $outputPath

        if ($LASTEXITCODE -ne 0) {
            Write-ErrorMessage "Hardware App publish failed"
            return $false
        }

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
                Write-ErrorMessage "Destination path does not exist: $copyDestination"
                return $false
            }

            try {
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
                Write-ErrorMessage "Failed to copy files: $($_.Exception.Message)"
                return $false
            }
        }

        return $true
    } finally {
        Pop-Location
    }
}

# ============================================================================
# Main Script
# ============================================================================

# Banner
Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  HomeAssistant Deployment" -ForegroundColor Cyan
Write-Host "  (Red/Green Deployment Strategy)" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# Determine action and environment
$Action = $null

# If environment was provided via parameter, action is implicitly "deploy"
if ($Environment -or $HardwareApp) {
    $Action = "deploy"
} else {
    # Main action loop - allows returning from management mode
    while ($true) {
        Write-Host "Choose action:" -ForegroundColor Cyan
        Write-Host "  1. Deploy" -ForegroundColor White
        Write-Host "  2. Manage environment settings" -ForegroundColor White
        Write-Host "  3. Manage function keys" -ForegroundColor White
        Write-Host "  4. Exit" -ForegroundColor White
        Write-Host ""

        $actionChoice = Read-Host "Enter choice (1-4)"

        switch ($actionChoice) {
            "1" { $Action = "deploy"; break }
            "2" {
                Start-ManagementMode
                Write-Host ""
                continue
            }
            "3" {
                Start-FunctionKeyManagement
                Write-Host ""
                continue
            }
            "4" {
                Write-Host ""
                Write-Info "Goodbye!"
                exit 0
            }
            default {
                Write-ErrorMessage "Invalid choice."
                Write-Host ""
                continue
            }
        }
        break
    }
}

# Ask for environment if not provided (deploy action)
while (-not $Environment) {
    Write-Host ""
    Write-Host "Choose environment:" -ForegroundColor Cyan
    Write-Host "  1. Local (Start local development servers)" -ForegroundColor White

    # Build list of Azure environments from state
    $state = Get-DeploymentState
    $azureEnvNames = @($state.environments.PSObject.Properties.Name)
    for ($i = 0; $i -lt $azureEnvNames.Count; $i++) {
        $displayName = (Get-Culture).TextInfo.ToTitleCase($azureEnvNames[$i])
        Write-Host "  $($i + 2). $displayName (Azure)" -ForegroundColor White
    }
    $backChoice = $azureEnvNames.Count + 2
    Write-Host "  $backChoice. Back" -ForegroundColor White
    Write-Host ""

    $maxChoice = $backChoice
    $envChoice = Read-Host "Enter choice (1-$maxChoice)"
    $envChoiceNum = [int]$envChoice

    if ($envChoiceNum -eq 1) {
        $Environment = "local"
    } elseif ($envChoiceNum -eq $backChoice) {
        # Go back to action selection
        Write-Host ""
        $Action = $null
        while ($true) {
            Write-Host "Choose action:" -ForegroundColor Cyan
            Write-Host "  1. Deploy" -ForegroundColor White
            Write-Host "  2. Manage environment settings" -ForegroundColor White
            Write-Host "  3. Manage function keys" -ForegroundColor White
            Write-Host "  4. Exit" -ForegroundColor White
            Write-Host ""

            $actionChoice = Read-Host "Enter choice (1-4)"

            switch ($actionChoice) {
                "1" { $Action = "deploy"; break }
                "2" {
                    Start-ManagementMode
                    Write-Host ""
                    continue
                }
                "3" {
                    Start-FunctionKeyManagement
                    Write-Host ""
                    continue
                }
                "4" {
                    Write-Host ""
                    Write-Info "Goodbye!"
                    exit 0
                }
                default {
                    Write-ErrorMessage "Invalid choice."
                    Write-Host ""
                    continue
                }
            }
            break
        }
        continue
    } elseif ($envChoiceNum -ge 2 -and $envChoiceNum -le $azureEnvNames.Count + 1) {
        $Environment = $azureEnvNames[$envChoiceNum - 2]
    } else {
        Write-ErrorMessage "Invalid choice."
        continue
    }
}

# Ask what to deploy if not specified
$ProductionBuild = $false
if (-not $Frontend -and -not $Backend -and -not $HardwareApp) {
    Write-Host ""
    Write-Warning "What would you like to deploy?"

    if ($Environment -eq "local") {
        Write-Host "  1. Frontend (dev)" -ForegroundColor White
        Write-Host "  2. Frontend (production build)" -ForegroundColor Gray
        Write-Host "  3. Backend" -ForegroundColor White
        Write-Host "  4. Hardware App (NetDaemon)" -ForegroundColor White
        Write-Host "  5. Both (frontend dev + backend)" -ForegroundColor White
        Write-Host "  6. Both (frontend production build + backend)" -ForegroundColor Gray
        Write-Host ""

        $choice = Read-Host "Enter choice (1-6)"

        switch ($choice) {
            "1" { $Frontend = $true }
            "2" {
                $Frontend = $true
                $ProductionBuild = $true
            }
            "3" { $Backend = $true }
            "4" { $HardwareApp = $true }
            "5" {
                $Frontend = $true
                $Backend = $true
            }
            "6" {
                $Frontend = $true
                $Backend = $true
                $ProductionBuild = $true
            }
            default {
                Write-ErrorMessage "Invalid choice."
                exit 1
            }
        }
    } else {
        Write-Host "  1. Frontend" -ForegroundColor White
        Write-Host "  2. Backend" -ForegroundColor White
        Write-Host "  3. Both" -ForegroundColor White
        Write-Host "  4. Hardware App (NetDaemon)" -ForegroundColor White
        Write-Host ""

        $choice = Read-Host "Enter choice (1-4)"

        switch ($choice) {
            "1" { $Frontend = $true }
            "2" { $Backend = $true }
            "3" {
                $Frontend = $true
                $Backend = $true
            }
            "4" { $HardwareApp = $true }
            default {
                Write-ErrorMessage "Invalid choice."
                exit 1
            }
        }
    }
}

Write-Host ""
Write-Host "========================================" -ForegroundColor Yellow
Write-Warning "  Confirm deployment"
Write-Host "========================================" -ForegroundColor Yellow
Write-Host ""
Write-Host "Environment: " -NoNewline -ForegroundColor White
Write-Host "$Environment" -ForegroundColor Cyan
Write-Host ""
Write-Host "Components:" -ForegroundColor White
if ($Environment -eq "local") {
    if ($Frontend) {
        $frontendMode = if ($ProductionBuild) { "Frontend (production build) -> http://localhost:5175" } else { "Frontend (dev) -> http://localhost:5173" }
        Write-Host "  - $frontendMode" -ForegroundColor Gray
    }
    if ($Backend) { Write-Host "  - Backend -> http://localhost:7159/api" -ForegroundColor Gray }
} else {
    if ($Frontend) { Write-Host "  - Frontend -> Azure Static Web Apps" -ForegroundColor Gray }
    if ($Backend) { Write-Host "  - Backend -> Azure Functions (slot TBD)" -ForegroundColor Gray }
}
if ($HardwareApp) { Write-Host "  - Hardware App -> NetDaemon build" -ForegroundColor Gray }
Write-Host ""

$confirm = Read-Host "Proceed? (Y/n)"
if ($confirm -and $confirm.ToLower() -ne "y" -and $confirm.ToLower() -ne "yes") {
    Write-Host ""
    Write-Warning "Deployment cancelled."
    exit 0
}
Write-Host ""

$deploymentSuccess = $true

# ============================================================================
# Dependency Check
# ============================================================================
if ($HardwareApp -and -not $Frontend -and -not $Backend) {
    Ensure-HardwareAppDependencies
} elseif ($Environment -eq "local") {
    Ensure-LocalDependencies
} else {
    Ensure-ProductionDependencies
}

# ============================================================================
# Local Deployment
# ============================================================================
if ($Environment -eq "local") {
    if ($Backend) {
        $result = Start-LocalBackend
        if (-not $result) { $deploymentSuccess = $false }
    }

    if ($Frontend -and $deploymentSuccess) {
        $result = Start-LocalFrontend -ProductionBuild:$ProductionBuild
        if (-not $result) { $deploymentSuccess = $false }
    }
}

# ============================================================================
# Azure Deployment
# ============================================================================
if ($Environment -ne "local" -and ($Frontend -or $Backend)) {
    # Require clean git state
    Ensure-GitClean

    # Start backend build early (runs in parallel with Azure login, slot selection, etc.)
    $backendBuildJob = $null
    if ($Backend) {
        Write-Info "Starting backend build in background..."
        $backendBuildJob = Start-BackendBuildJob
    }

    # Ensure Azure CLI is available and logged in
    Ensure-AzureCliLoggedIn

    # Load environment config
    $envConfig = Get-EnvironmentConfig $Environment

    # Verify Azure resources are accessible
    $envConfig = Verify-AzureResources $Environment $envConfig

    # Select deployment slot
    $selectedSlot = $null
    if ($Backend -or ($Frontend -and -not $Backend)) {
        if ($Backend) {
            $selectedSlot = Select-DeploymentSlot $envConfig
        } else {
            $selectedSlot = $envConfig.lastDeployedSlot
            if (-not $selectedSlot) {
                Write-Warning "No previous backend deployment found. Please select which backend the frontend should point to:"
                $selectedSlot = Select-DeploymentSlot $envConfig
            } else {
                Write-Info "Frontend will point to: $selectedSlot"
                Write-Host ""
                Write-Host "Is this correct?" -ForegroundColor Cyan
                Write-Host "  1. Yes" -ForegroundColor White
                Write-Host "  2. No, let me choose" -ForegroundColor White
                Write-Host ""

                $confirmChoice = Read-Host "Enter choice (1 or 2)"
                if ($confirmChoice -eq "2") {
                    $selectedSlot = Select-DeploymentSlot $envConfig
                }
            }
        }
    }

    Write-Host ""
    Write-Info "Deployment target: $selectedSlot"
    Write-Host ""

    if ($Backend -and $Frontend) {
        # Both - start frontend build in background too
        Write-Info "Starting frontend build in background..."
        $frontendBuildJob = Start-FrontendBuildJob $envConfig $selectedSlot $Environment

        # Deploy backend (build job passed in, already running)
        $backendResult = Deploy-ProductionBackend $envConfig $selectedSlot $backendBuildJob $Environment $true
        if ($backendResult -eq $false) {
            $deploymentSuccess = $false
            $null = Stop-Job $frontendBuildJob -ErrorAction SilentlyContinue
            $null = Remove-Job $frontendBuildJob -Force -ErrorAction SilentlyContinue
        } else {
            if ($backendResult -eq "frontend-only") {
                $deploymentSuccess = $false
            }
            $frontendResult = Deploy-FrontendFromBuildJob $envConfig $selectedSlot $frontendBuildJob $Environment
            if (-not $frontendResult) { $deploymentSuccess = $false }
        }

    } elseif ($Backend) {
        $result = Deploy-ProductionBackend $envConfig $selectedSlot $backendBuildJob $Environment
        if (-not $result) { $deploymentSuccess = $false }

    } elseif ($Frontend) {
        $result = Deploy-ProductionFrontend $envConfig $selectedSlot $Environment
        if (-not $result) { $deploymentSuccess = $false }
    }
}

# ============================================================================
# Hardware App (can be deployed alongside any environment)
# ============================================================================
if ($HardwareApp) {
    $result = Build-HardwareApp
    if (-not $result) { $deploymentSuccess = $false }
}

# ============================================================================
# Summary
# ============================================================================
$completionTime = Get-Date -Format "HH:mm:ss on dd MMM yyyy"

Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
if ($deploymentSuccess) {
    Write-Success "  Deployment Complete!"
    Write-Host "  $completionTime" -ForegroundColor Gray
} else {
    Write-ErrorMessage "  Deployment Failed!"
    Write-Host "  $completionTime" -ForegroundColor Gray
}
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

if ($deploymentSuccess) {
    if ($Environment -eq "local") {
        Write-Host "Local development servers:" -ForegroundColor White
        if ($Backend) {
            Write-Host "  [OK] Backend  -> http://localhost:7159/api" -ForegroundColor Green
        }
        if ($Frontend) {
            $frontendPort = if ($ProductionBuild) { 5175 } else { 5173 }
            $modeLabel = if ($ProductionBuild) { "(production build)" } else { "(dev)" }
            Write-Host "  [OK] Frontend -> http://localhost:$frontendPort $modeLabel" -ForegroundColor Green
        }
    } elseif ($Frontend -or $Backend) {
        $envConfig = Get-EnvironmentConfig $Environment
        $activeSlot = $envConfig.lastDeployedSlot
        $envDisplayName = (Get-Culture).TextInfo.ToTitleCase($Environment)

        Write-Host "$envDisplayName deployment:" -ForegroundColor White
        if ($Backend) {
            $hostname = Get-SlotHostname $envConfig.resourceGroup $activeSlot
            Write-Host "  [OK] Backend  -> https://$hostname/api" -ForegroundColor Green
        }
        if ($Frontend) {
            Write-Host "  [OK] Frontend -> $($envConfig.frontend.url)" -ForegroundColor Green
            Write-Gray "       (pointing to $activeSlot)"
        }
    }
    if ($HardwareApp) {
        Write-Host "  [OK] Hardware App -> publish\hardware-app" -ForegroundColor Green
    }
    Write-Host ""
    exit 0
} else {
    Write-Host "Some deployments failed. Please check the errors above." -ForegroundColor Red
    Write-Host ""
    exit 1
}
