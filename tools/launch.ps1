<#
.SYNOPSIS
  Deploys the current Release build to C:\Apps\GridGameGaiden, then offers Normal Play, Demo +
  Capture, or Quit.
.PARAMETER Sound
  Launch with audio. Without it, launches muted: Unity.Shared's session mute (-muted /
  MINDATTIC_CLI_MUTED=1), in effect for this run only -- nothing is written to the player's
  settings.
.NOTES
  Always redeploys from source first (tools\run.ps1), so this never runs a stale build.
  Requires the Unity Editor closed for this project.
#>
param(
    [switch]$Sound
)
$ErrorActionPreference = "Stop"

& (Join-Path $PSScriptRoot "run.ps1")
if ($LASTEXITCODE -ne 0) {
    Write-Host "Deploy failed (exit $LASTEXITCODE); not launching." -ForegroundColor Red
    exit $LASTEXITCODE
}

$exe = "C:\Apps\GridGameGaiden\GridGameGaiden.exe"
if (-not (Test-Path $exe)) {
    Write-Host "Deploy reported success but $exe is missing." -ForegroundColor Red
    exit 1
}

# GG-T205: muted by default (Unity.Shared's session mute), -Sound opts back in. The env var is set only
# around Start-Process so it never leaks into the rest of this shell.
$muteArgs = @()
if (-not $Sound) { $muteArgs += "-muted" }
$previousMuted = $env:MINDATTIC_CLI_MUTED
if ($Sound) { Remove-Item Env:MINDATTIC_CLI_MUTED -ErrorAction SilentlyContinue } else { $env:MINDATTIC_CLI_MUTED = "1" }

$muteLabel = if ($Sound) { "" } else { " (muted)" }
Write-Host ""
Write-Host "  GridGameGaiden" -ForegroundColor Cyan
Write-Host "  [1] Normal Play$muteLabel"
Write-Host "  [2] Demo + Capture$muteLabel"
Write-Host "  [3] Quit"
Write-Host ""
$choice = Read-Host "Select (default 1)"
if ([string]::IsNullOrWhiteSpace($choice)) { $choice = "1" }

try {
    # Start-Process rejects an empty -ArgumentList (Normal Play with -Sound), so pass it only when there are arguments.
    function Start-Game([string[]]$GameArgs) {
        $launch = @{ FilePath = $exe }
        if ($GameArgs.Count -gt 0) { $launch.ArgumentList = $GameArgs }
        Start-Process @launch
    }
    switch ($choice) {
        "1" { Start-Game $muteArgs }
        "2" { Start-Game (@("-demo", "-capture", "video", "screen", "5") + $muteArgs) }
        "3" { exit 0 }
        default {
            Write-Host "Unrecognized choice, launching Normal Play." -ForegroundColor Yellow
            Start-Game $muteArgs
        }
    }
} finally {
    if ($null -eq $previousMuted) { Remove-Item Env:MINDATTIC_CLI_MUTED -ErrorAction SilentlyContinue } else { $env:MINDATTIC_CLI_MUTED = $previousMuted }
}
