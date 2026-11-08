# GG-T355: pull the latest PUBLISHED story text from Prose and apply it to Assets/Gaiden/Resources/Content/Text
# before a build (docs/PROSE.md, "Build-time pull"). Runs Prose's consumer kit by path: kit pull, then kit apply.
#
#   pwsh tools/prose-sync.ps1 [-Skip]
#
# Never fails a build because of Prose: with the Hub offline, the kit missing or any kit error, it logs a warning and
# exits 0, and the build ships the committed Content/Text snapshot. Skip it (offline or CI builds) with -Skip or the
# environment variable GG_SKIP_PROSE=1.
param([switch]$Skip)
$ErrorActionPreference = 'Continue'
$repo = Split-Path -Parent $PSScriptRoot
$kit = if ($env:PROSE_KIT) { $env:PROSE_KIT } else { 'D:\Projects\MindAttic\Prose\consumer-kit\kit.ps1' }
$hub = if ($env:PROSE_HUB) { $env:PROSE_HUB } else { 'http://127.0.0.1:5900' }

function Warn([string]$msg) { Write-Host "[prose-sync] WARNING: $msg; building with the committed Content/Text snapshot" -ForegroundColor Yellow }

if ($Skip -or $env:GG_SKIP_PROSE -eq '1') { Write-Host '[prose-sync] skipped (-Skip / GG_SKIP_PROSE=1): committed Content/Text snapshot'; exit 0 }
if (-not (Test-Path $kit)) { Warn "no Prose consumer kit at $kit"; exit 0 }
try { Invoke-RestMethod -Uri "$hub/api/health" -TimeoutSec 3 | Out-Null }
catch { Warn "the Prose Hub is offline at $hub"; exit 0 }

Push-Location $repo
try {
    & $kit --hub $hub pull --status published
    if ($LASTEXITCODE -ne 0) { Warn "kit pull exited $LASTEXITCODE"; exit 0 }
    & $kit --hub $hub apply
    if ($LASTEXITCODE -ne 0) { Warn "kit apply exited $LASTEXITCODE"; exit 0 }
    Write-Host '[prose-sync] Content/Text is up to date with Prose (published texts)'
}
catch { Warn $_.Exception.Message }
finally { Pop-Location }
exit 0
