<#
.SYNOPSIS
  Builds (Release, only when the sources changed since the last build) and starts Thumbnail Studio (GG-T222),
  the standalone editor for each class's "Thumbnail" (Zoom / X / Y) in classes.json. Every run also refreshes
  the "Thumbnail Studio" desktop shortcut (GG-T223).
.PARAMETER Rebuild
  Build even if the exe looks current.
.PARAMETER Shortcut
  Build if needed and create / refresh the desktop shortcut, without starting the studio.
.PARAMETER WaitPid
  Wait for this process to exit first (the studio passes its own pid when it hands off a rebuild).
.EXAMPLE
  powershell -File tools\thumbnail-studio.ps1
  powershell -File tools\thumbnail-studio.ps1 -Shortcut
  powershell -File tools\thumbnail-studio.ps1 --select alchemist
.NOTES
  Any other arguments are passed to the app (--repo, --classes, --select, --no-deploy, --demo ...).
  The shortcut targets the exe itself (no console window). When the studio starts and finds its sources newer
  than build.stamp, it relaunches through this script in a hidden PowerShell, which rebuilds and starts it again.
  Windows PowerShell 5.1 compatible.
#>
param(
    [switch]$Rebuild,
    [switch]$Shortcut,
    [int]$WaitPid = 0,
    [Parameter(ValueFromRemainingArguments = $true)][string[]]$AppArgs
)
$ErrorActionPreference = "Stop"

$proj = Join-Path $PSScriptRoot "ThumbnailStudio"
$bin = Join-Path $proj "bin\Release\net10.0-windows"
$exe = Join-Path $bin "ThumbnailStudio.exe"
$stamp = Join-Path $bin "build.stamp"
$repo = Split-Path $PSScriptRoot -Parent
$logDir = Join-Path $env:LOCALAPPDATA "MindAttic\ThumbnailStudio"
$buildLog = Join-Path $logDir "build.log"

if ($WaitPid -gt 0) {
    try { Wait-Process -Id $WaitPid -Timeout 20 -ErrorAction Stop } catch { }
}

# Stale when any source is newer than the last successful build of this script (build.stamp): the apphost exe
# isn't rewritten on every build, so its own timestamp can't tell.
$stale = $Rebuild -or -not (Test-Path $exe) -or -not (Test-Path $stamp)
if (-not $stale) {
    $built = (Get-Item $stamp).LastWriteTimeUtc
    $newer = Get-ChildItem $proj -File -Recurse -Include *.cs, *.xaml, *.csproj, *.ico |
        Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' -and $_.LastWriteTimeUtc -gt $built }
    $stale = $null -ne $newer
}

$buildFailed = $false
if ($stale) {
    Write-Host "Building Thumbnail Studio (Release)..." -ForegroundColor Cyan
    New-Item -ItemType Directory -Force $logDir | Out-Null
    $errLog = "$buildLog.err"
    $p = Start-Process -FilePath "dotnet" -ArgumentList @("build", "`"$(Join-Path $proj 'ThumbnailStudio.csproj')`"", "-c", "Release", "--nologo", "-v", "quiet") `
        -NoNewWindow -Wait -PassThru -RedirectStandardOutput $buildLog -RedirectStandardError $errLog
    if ($p.ExitCode -ne 0) {
        Get-Content $buildLog, $errLog -ErrorAction SilentlyContinue | Write-Host
        Write-Host "Build failed (exit $($p.ExitCode)); log: $buildLog" -ForegroundColor Red
        if (-not (Test-Path $exe)) { exit $p.ExitCode }
        $buildFailed = $true
    } else {
        Set-Content -LiteralPath $stamp -Value (Get-Date -Format o)
    }
}

# Desktop shortcut: straight to the exe, so it opens with no console window.
$lnk = Join-Path ([Environment]::GetFolderPath('Desktop')) 'Thumbnail Studio.lnk'
$link = (New-Object -ComObject WScript.Shell).CreateShortcut($lnk)
$link.TargetPath = $exe
$link.Arguments = ''
$link.WorkingDirectory = $repo
$link.IconLocation = "$exe,0"
$link.Description = 'Thumbnail Studio: frame each unit''s thumbnail; edits autosave, commit, push and redeploy the game on close'
$link.Save()
Write-Host "Shortcut -> $lnk" -ForegroundColor Gray

if ($Shortcut) { if ($buildFailed) { exit 1 } else { exit 0 } }

$argList = @("--repo", $repo, "--no-rebuild-check")
if ($buildFailed) { $argList += "--build-failed" }
if ($AppArgs) { $argList += $AppArgs }
# Windows PowerShell's Start-Process joins arguments with spaces and doesn't quote them.
$argList = $argList | ForEach-Object { if ($_ -match '\s') { '"' + $_ + '"' } else { $_ } }
Start-Process -FilePath $exe -ArgumentList $argList -WorkingDirectory $repo | Out-Null
