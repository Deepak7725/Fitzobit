<#
.SYNOPSIS
  Builds the UI and publishes Fitzobit into a zip ready for Azure App Service (Windows, .NET 8).

.DESCRIPTION
  Output: artifacts\fitzobit-azure.zip. Upload it with Kudu "Zip Push Deploy"
  (https://<app-name>.scm.azurewebsites.net/ZipDeployUI) or the Azure CLI. See deploy\AZURE.md.
#>
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$ui = Join-Path $root 'src\Fitzobit.UI'
$out = Join-Path $root 'artifacts\publish'
$zip = Join-Path $root 'artifacts\fitzobit-azure.zip'

Push-Location $ui
try {
    if (-not (Test-Path 'node_modules')) { npm ci; if ($LASTEXITCODE) { throw 'npm ci failed' } }
    npm run build; if ($LASTEXITCODE) { throw 'UI build failed' }
}
finally { Pop-Location }

if (Test-Path $out) { Remove-Item $out -Recurse -Force }
dotnet publish (Join-Path $root 'src\Fitzobit.Web') -c Release -o $out
if ($LASTEXITCODE) { throw 'dotnet publish failed' }

foreach ($required in 'Fitzobit.Web.dll', 'web.config', 'wwwroot\index.html', 'Fitzobit_WhatsApp_Catalogue.xlsx') {
    if (-not (Test-Path (Join-Path $out $required))) { throw "Publish output is missing $required" }
}

if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path (Join-Path $out '*') -DestinationPath $zip
Write-Host "Ready: $zip ($([math]::Round((Get-Item $zip).Length / 1MB, 1)) MB)"
