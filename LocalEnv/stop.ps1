# Stops the local environment, and ONLY the local environment.
#
# Processes are identified by the ports they hold, not by name, so a normal dev session of the app
# (for example under Visual Studio) is left alone:
#   7159            the local Functions host
#   8124            the guard proxy
#   10000-10002     the Azurite storage emulator
#   connected to 8124   the local NetDaemon app (only it talks to the guard)
# It never touches the production app, which runs on the Home Assistant host.
#
#   ./stop.ps1 -WhatIf     lists what would be stopped without stopping anything
param([switch]$WhatIf)

$listening = Get-NetTCPConnection -State Listen -ErrorAction SilentlyContinue |
    Where-Object { $_.LocalPort -in 7159, 8124, 10000, 10001, 10002 }
$clientsOfGuard = Get-NetTCPConnection -State Established -ErrorAction SilentlyContinue |
    Where-Object { $_.RemotePort -eq 8124 -and $_.RemoteAddress -in '127.0.0.1', '::1' }

$ids = @($listening.OwningProcess) + @($clientsOfGuard.OwningProcess) | Where-Object { $_ -gt 4 } | Sort-Object -Unique
if (-not $ids) { 'nothing local is running'; return }

foreach ($id in $ids) {
    $p = Get-Process -Id $id -ErrorAction SilentlyContinue
    if (-not $p) { continue }
    $ports = @($listening | Where-Object OwningProcess -eq $id | ForEach-Object LocalPort) -join ','
    $role = if ($ports) { "listening on $ports" } else { 'client of the guard' }
    if ($WhatIf) { "would stop $($p.ProcessName) (PID $id): $role" }
    else { taskkill /PID $id /T /F | Out-Null; "stopped $($p.ProcessName) (PID $id): $role" }
}
