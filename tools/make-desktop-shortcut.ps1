#Requires -Version 5.1
<#
.SYNOPSIS
    Creates (or refreshes) the "GridGame Gaiden" Desktop shortcut, pointing at run.bat, whose
    menu asks first: 1) Deploy & Run (pull, rebuild if changed, launch) or 2) Run the published build.

.DESCRIPTION
    tools\run.ps1 already recreates this same shortcut on every run; this script exists to
    (re)create it standalone (e.g. after moving the Desktop folder) without doing a full deploy.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File tools\make-desktop-shortcut.ps1
#>
$ErrorActionPreference = 'Stop'

$repoDir = Split-Path $PSScriptRoot   # tools\ -> Unity.GridGame\
$runBat = Join-Path $repoDir 'run.bat'
$exe = 'C:\Apps\GridGameGaiden\GridGameGaiden.exe'
$lnk = Join-Path ([Environment]::GetFolderPath('Desktop')) 'GridGame Gaiden.lnk'

$shell = New-Object -ComObject WScript.Shell
$shortcut = $shell.CreateShortcut($lnk)
$shortcut.TargetPath = $runBat
$shortcut.WorkingDirectory = $repoDir
if (Test-Path $exe) { $shortcut.IconLocation = "$exe,0" }
$shortcut.Description = 'GridGame Gaiden: 1) Deploy & Run (pull, rebuild, launch) or 2) Run the published build'
$shortcut.Save()

Write-Host "Shortcut created: $lnk -> $runBat" -ForegroundColor Green
