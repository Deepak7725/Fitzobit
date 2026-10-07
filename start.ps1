<#
.SYNOPSIS
  Builds the Fitzobit UI (when needed) and starts the application on http://localhost:5080.

.PARAMETER RebuildUI
  Force a fresh UI build even if src\Fitzobit.Web\wwwroot already exists.
#>
param([switch]$RebuildUI)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$ui = Join-Path $root 'src\Fitzobit.UI'
$wwwroot = Join-Path $root 'src\Fitzobit.Web\wwwroot\index.html'

if ($RebuildUI -or -not (Test-Path $wwwroot)) {
    Push-Location $ui
    try {
        if (-not (Test-Path 'node_modules')) { npm install; if ($LASTEXITCODE) { throw 'npm install failed' } }
        npm run build; if ($LASTEXITCODE) { throw 'UI build failed' }
    }
    finally { Pop-Location }
}

Write-Host 'Starting Fitzobit on http://localhost:5080 (Ctrl+C to stop)...'
dotnet run --project (Join-Path $root 'src\Fitzobit.Web') --launch-profile http
