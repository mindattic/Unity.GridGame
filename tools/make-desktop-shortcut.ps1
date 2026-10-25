#Requires -Version 5.1
<#
.SYNOPSIS
    Creates (or refreshes) the "GridGame Gaiden" Desktop shortcut, pointing at deploy.bat so a
    double-click always pulls latest and only rebuilds if the source actually changed.

.DESCRIPTION
    tools\deploy.ps1 already recreates this same shortcut on every run; this script exists to
    (re)create it standalone (e.g. after moving the Desktop folder) without doing a full deploy.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File tools\make-desktop-shortcut.ps1
#>
$ErrorActionPreference = 'Stop'

$repoDir = Split-Path $PSScriptRoot   # tools\ -> Unity.GridGame\
$deployBat = Join-Path $repoDir 'deploy.bat'
$exe = 'C:\Apps\GridGameGaiden\GridGameGaiden.exe'
$lnk = Join-Path ([Environment]::GetFolderPath('Desktop')) 'GridGame Gaiden.lnk'

$shell = New-Object -ComObject WScript.Shell
$shortcut = $shell.CreateShortcut($lnk)
$shortcut.TargetPath = $deployBat
$shortcut.WorkingDirectory = $repoDir
if (Test-Path $exe) { $shortcut.IconLocation = "$exe,0" }
$shortcut.Description = 'Pull latest, build Release if changed, and launch GridGame Gaiden'
$shortcut.Save()

Write-Host "Shortcut created: $lnk -> $deployBat" -ForegroundColor Green
