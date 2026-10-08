<#
.SYNOPSIS
  Starts the Fitzobit server automatically when you sign in to Windows (Task Scheduler task "Fitzobit Server").

.PARAMETER Remove
  Remove the task instead.

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File deploy\install-autostart.ps1
#>
param([switch]$Remove)

$ErrorActionPreference = 'Stop'
$taskName = 'Fitzobit Server'
$script = Join-Path $PSScriptRoot 'local-server.ps1'

if ($Remove) {
    Unregister-ScheduledTask -TaskName $taskName -Confirm:$false
    Write-Host "Removed the '$taskName' task. (A server that is already running keeps running until you sign out.)"
    return
}

if (-not (Test-Path (Join-Path (Split-Path $PSScriptRoot -Parent) 'artifacts\local-server\Fitzobit.Web.exe'))) {
    throw 'Publish first: powershell -ExecutionPolicy Bypass -File deploy\local-server.ps1 -Publish'
}

$action = New-ScheduledTaskAction -Execute 'powershell.exe' `
    -Argument "-NoProfile -WindowStyle Hidden -ExecutionPolicy Bypass -File `"$script`""
$trigger = New-ScheduledTaskTrigger -AtLogOn -User "$env:USERDOMAIN\$env:USERNAME"
$settings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -StartWhenAvailable `
    -ExecutionTimeLimit ([TimeSpan]::Zero) -RestartCount 3 -RestartInterval (New-TimeSpan -Minutes 1) -MultipleInstances IgnoreNew

Register-ScheduledTask -TaskName $taskName -Action $action -Trigger $trigger -Settings $settings `
    -Description 'Runs the Fitzobit Delivery Management server on http://localhost:5180 (exposed with Tailscale Funnel).' -Force | Out-Null
Start-ScheduledTask -TaskName $taskName
Write-Host "Installed and started '$taskName'. It starts automatically each time you sign in to Windows."
