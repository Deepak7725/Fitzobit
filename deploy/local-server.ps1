<#
.SYNOPSIS
  Runs Fitzobit as a production server on this PC (for Tailscale Funnel). See deploy\TAILSCALE.md.

.DESCRIPTION
  The server runs from a published copy in artifacts\local-server, so Visual Studio builds never lock it.
  It listens on http://localhost:<Port> only; the internet reaches it solely through Tailscale Funnel (HTTPS).
  Data stays in data\fitzobit.db; output is appended to data\logs\server.log.

.PARAMETER Publish
  Build the UI and server and (re)publish first. Use it the first time and after every code change.
  A running copy is stopped first; if the autostart task is installed it is restarted, otherwise the server runs here.

.PARAMETER Port
  Local port (default 5180). Tailscale Funnel must point at the same port.
#>
param([switch]$Publish, [int]$Port = 5180)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$app = Join-Path $root 'artifacts\local-server'
$exe = Join-Path $app 'Fitzobit.Web.exe'
$logDir = Join-Path $root 'data\logs'
$taskName = 'Fitzobit Server'

function Stop-LocalServer {
    Get-Process Fitzobit.Web -ErrorAction SilentlyContinue |
        Where-Object { $_.Path -and $_.Path.StartsWith($app, [StringComparison]::OrdinalIgnoreCase) } |
        ForEach-Object { Write-Host "Stopping running server (PID $($_.Id))..."; Stop-Process -Id $_.Id -Force }
}

if ($Publish -or -not (Test-Path $exe)) {
    Stop-LocalServer
    Push-Location (Join-Path $root 'src\Fitzobit.UI')
    try {
        if (-not (Test-Path 'node_modules')) { npm ci; if ($LASTEXITCODE) { throw 'npm ci failed' } }
        npm run build; if ($LASTEXITCODE) { throw 'UI build failed' }
    }
    finally { Pop-Location }
    dotnet publish (Join-Path $root 'src\Fitzobit.Web') -c Release -o $app
    if ($LASTEXITCODE) { throw 'dotnet publish failed' }

    if ($Publish -and (Get-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue)) {
        Start-ScheduledTask -TaskName $taskName
        Write-Host "Published. Restarted the '$taskName' task: http://localhost:$Port"
        return
    }
}

New-Item -ItemType Directory -Force $logDir | Out-Null
$log = Join-Path $logDir 'server.log'
if (Test-Path $log) { Move-Item $log (Join-Path $logDir 'server.previous.log') -Force }

$env:ASPNETCORE_ENVIRONMENT = 'Production'
$env:ASPNETCORE_URLS = "http://localhost:$Port"
# Production mode does not read user secrets, so pass on the local PostgreSQL connection string from them
# (unless Database__ConnectionString is already set).
if (-not $env:Database__ConnectionString) {
    $secrets = (dotnet user-secrets list --json --project (Join-Path $root 'src\Fitzobit.Web')) -join "`n"
    $start = $secrets.IndexOf('{'); $end = $secrets.LastIndexOf('}')
    if ($start -ge 0 -and $end -gt $start) {
        $env:Database__ConnectionString = ($secrets.Substring($start, $end - $start + 1) | ConvertFrom-Json).'Database:ConnectionString'
    }
    if (-not $env:Database__ConnectionString) { throw 'Set the database connection string first (README, "Local PostgreSQL").' }
}
# Cookies are Secure whenever the request is HTTPS (everything through Tailscale Funnel), and still work for
# http://localhost on this PC, which is needed for the first-run administrator setup.
$env:Auth__RequireHttpsCookies = 'false'

Write-Host "Fitzobit is running on http://localhost:$Port (Ctrl+C to stop). Log: $log"
# The working folder must be the app folder: it holds appsettings.json and the UI (wwwroot).
Start-Process -FilePath $exe -WorkingDirectory $app -NoNewWindow -Wait `
    -RedirectStandardOutput $log -RedirectStandardError (Join-Path $logDir 'server.errors.log')
