#Requires -Version 5.1
<#
.SYNOPSIS
    Shuts down any running GridGameGaiden, clears the release build output, rebuilds the Windows
    Release player from scratch, and publishes a standalone copy to C:\Apps\GridGameGaiden\ — same
    process UnityEve/Automata/KdpPublish/IdiotProof use to always have a fresh, independent deployed
    copy instead of a possibly-stale one.

.DESCRIPTION
    1. Stops any running GridGameGaiden.exe process launched from *this* repo's Build\ output or
       from the deployed copy at C:\Apps\GridGameGaiden\ — never another git worktree's dev player
       (GG-T21: Get-Process only sees the process name, so an unscoped Stop-Process -Force here
       would silently kill a sibling worktree's unrelated long-running session, indistinguishable
       to that session from an unexplained crash).
    2. Removes Build\WindowsRelease — the build OUTPUT only, never Library\ (Unity's asset
       import / script-compile cache, which survives exactly as it does for a normal
       Editor rebuild) — so the player build below is never a stale incremental copy.
    3. Runs tools\unity.ps1 -Method GridGame.EditorTools.GaidenBuild.BuildWindowsRelease in Unity
       batch mode (requires the Editor closed) -> Build\WindowsRelease\GridGameGaiden.exe.
    4. Copies Build\WindowsRelease\* -> C:\Apps\GridGameGaiden\.

    Steps 1-4 are skipped when the deployed commit is up to date, unless -Force. The repo-root
    run.bat (the Desktop shortcut's target) opens a menu first (MAT-T67): 1) Deploy & Run calls
    this script with -Force -Launch; 2) Run starts the published exe without calling it. This
    script never prompts, so agents and /deploy run it unattended.

.PARAMETER Launch
    After publishing, immediately run the deployed GridGameGaiden.exe.

.PARAMETER Force
    Rebuild and republish even when the deployed commit is up to date. It skips only that check.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File tools\run.ps1
    powershell -ExecutionPolicy Bypass -File tools\run.ps1 -Launch
    powershell -ExecutionPolicy Bypass -File tools\run.ps1 -Force -Launch
#>
param(
    [switch]$Launch,
    [switch]$Force
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoDir  = Split-Path $PSScriptRoot   # tools\ -> Unity.GridGame\
$out      = 'C:\Apps\GridGameGaiden'
$exeName  = 'GridGameGaiden.exe'
$buildDir = Join-Path $repoDir 'Build\WindowsRelease'
$runBat = Join-Path $repoDir 'run.bat'
$marker    = Join-Path $out '.deploy-commit'

# ── Pull latest, skip the rebuild entirely if nothing changed ──────────────
# Untracked files (e.g. .prose/ session state) don't block a pull; changes to tracked files (staged
# or not) skip it, as does a pull that fails: the desktop shortcut runs this, and the ticket tools
# keep docs/USER_STORIES.md modified, so throwing here left the game unopenable (GG-T162). It then
# deploys the commit already checked out.
$dirty = git -C $repoDir status --porcelain | Where-Object { $_ -notmatch '^\?\? ' }
if ($dirty) {
    Write-Host "  $repoDir has uncommitted changes - not pulling; deploying the current commit." -ForegroundColor DarkYellow
} else {
    git -C $repoDir pull --ff-only
    if ($LASTEXITCODE -ne 0) { Write-Host "  git pull --ff-only failed in $repoDir - deploying the current commit." -ForegroundColor DarkYellow }
}
$head = (git -C $repoDir rev-parse HEAD).Trim()
# Up to date when nothing the player is built from (Assets, Packages, ProjectSettings) differs between
# the deployed commit and HEAD: a docs- or tickets-only commit doesn't rebuild the game (GG-T162).
$upToDate = $false
if (-not $Force -and (Test-Path (Join-Path $out $exeName)) -and (Test-Path $marker)) {
    $deployed = (Get-Content -LiteralPath $marker -Raw).Trim()
    git -C $repoDir diff --quiet $deployed $head -- Assets Packages ProjectSettings 2>$null
    $upToDate = $LASTEXITCODE -eq 0
    if ($upToDate -and $deployed -ne $head) { Set-Content -LiteralPath $marker -Value $head -NoNewline }
}
if ($upToDate) {
    Write-Host "  Already deployed at $head (no player source changed) - skipping rebuild." -ForegroundColor Green
    if ($Launch) { Start-Process -FilePath (Join-Path $out $exeName) -WorkingDirectory $out }
    exit 0
}

# ── Stop running instance ──────────────────────────────────────────────────
Write-Host ''
Write-Host '  Stopping running instance...' -ForegroundColor Yellow
$repoDirPrefix = $repoDir.TrimEnd('\') + '\'
$outPrefix     = $out.TrimEnd('\') + '\'
$procs = Get-Process 'GridGameGaiden' -ErrorAction SilentlyContinue | Where-Object {
    # Trailing-separator prefix match, not a raw StartsWith: a bare prefix would also match a
    # sibling worktree whose name extends this one's (GG-T7 vs. GG-T70/GG-T71..79) — the exact
    # cross-worktree kill GG-T21 exists to prevent (GG-T153). $_.Path can also throw (access
    # denied) for a process owned by another user/elevation level; treat that as "not ours".
    try { $exePath = $_.Path } catch { $exePath = $null }
    $exePath -and ($exePath.StartsWith($repoDirPrefix, [System.StringComparison]::OrdinalIgnoreCase) `
                   -or $exePath.StartsWith($outPrefix, [System.StringComparison]::OrdinalIgnoreCase))
}
if ($procs) {
    $procs | Stop-Process -Force
    Write-Host "    Stopped ($(@($procs).Count) process(es))" -ForegroundColor DarkYellow
    Start-Sleep -Milliseconds 800
} else {
    Write-Host '    Nothing running.' -ForegroundColor DarkGray
}

# ── Clear build output ───────────────────────────────────────────────────────
# Build\WindowsRelease only — never Library\, which is Unity's import/compile cache, not
# build output, and must survive a redeploy exactly as it survives a normal rebuild.
Write-Host ''
Write-Host '  Clearing build output (Build\WindowsRelease)...' -ForegroundColor Cyan
if (Test-Path $buildDir) {
    Remove-Item -Recurse -Force $buildDir
}

# ── Build ────────────────────────────────────────────────────────────────────
Write-Host ''
Write-Host '  Building Windows Release player (Unity batch mode)...' -ForegroundColor Cyan
& (Join-Path $PSScriptRoot 'unity.ps1') -Method GridGame.EditorTools.GaidenBuild.BuildWindowsRelease -Log deploy-build
if ($LASTEXITCODE -ne 0) {
    Write-Host "Build failed (exit $LASTEXITCODE)." -ForegroundColor Red
    exit $LASTEXITCODE
}

$builtExe = Join-Path $buildDir $exeName
if (-not (Test-Path $builtExe)) {
    Write-Host "Build completed but $exeName not found at $builtExe." -ForegroundColor Red
    exit 1
}

# ── Publish ─────────────────────────────────────────────────────────────────
Write-Host ''
Write-Host "  Publishing $builtExe" -ForegroundColor Cyan
Write-Host "    -> $out\$exeName"

if (Test-Path $out) { Remove-Item -Recurse -Force $out }
New-Item -ItemType Directory -Force $out | Out-Null
Copy-Item -Recurse -Force (Join-Path $buildDir '*') $out

$exe = Join-Path $out $exeName
if (-not (Test-Path $exe)) {
    Write-Host "Publish completed but $exeName not found at $exe." -ForegroundColor Red
    exit 1
}

Set-Content -LiteralPath $marker -Value $head -NoNewline

# ── Desktop shortcut ─────────────────────────────────────────────────────────
# Points at run.bat (not the exe directly), which asks first: Deploy & Run, or Run (MAT-T67).
$shortcut = Join-Path ([Environment]::GetFolderPath('Desktop')) 'GridGame Gaiden.lnk'
$link = (New-Object -ComObject WScript.Shell).CreateShortcut($shortcut)
$link.TargetPath       = $runBat
$link.WorkingDirectory = $repoDir
$link.IconLocation     = "$exe,0"
$link.Description      = 'GridGame Gaiden: 1) Deploy & Run (pull, rebuild, launch) or 2) Run the published build'
$link.Save()
Write-Host "    Shortcut -> $shortcut" -ForegroundColor Gray

# ── Summary ────────────────────────────────────────────────────────────────
Write-Host ''
Write-Host '  Published successfully.' -ForegroundColor Green
Write-Host "    Exe : $exe ($([math]::Round(((Get-Item $exe).Length / 1MB), 1)) MB)" -ForegroundColor Gray
Write-Host ''

if ($Launch) {
    Write-Host '  Launching...' -ForegroundColor Cyan
    Start-Process $exe
}
