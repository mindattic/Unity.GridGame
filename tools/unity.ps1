# Shared helper: resolves the Unity editor for this project and runs a batchmode method.
#   pwsh tools/unity.ps1 -Method GridGame.EditorTools.GaidenBuild.CompileCheck [-Log name]
param(
    [Parameter(Mandatory = $true)][string]$Method,
    [string]$Log = "unity",
    [switch]$NoGraphics,
    [string[]]$Extra = @()
)
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot

# GG-T159: repair missing sibling-repo package links before Unity ever gets a chance to choke on
# them. Packages/manifest.json pulls com.mindattic.capture/telemetry via "file:../../<Repo>/..."
# paths, two levels above the project root. In the main checkout that lands on the real sibling
# repo (<WorkspaceRoot>\<Repo>). In a per-ticket worktree
# (<WorkspaceRoot>.worktrees\Unity.GridGame\<Ticket>\...) the same climb instead lands on
# <WorkspaceRoot>.worktrees\Unity.GridGame\<Repo>, which has no repo of its own unless something
# put one there -- Unity then refuses with "invalid dependencies ... cannot be found". Auto-create
# a directory junction at that spot, pointing at the real sibling repo, so a fresh worktree builds
# with no manual setup. Junctions (not symlinks) because creating a symlink needs
# admin/Developer-Mode privilege on Windows and junctions don't.
function Resolve-RealWorkspaceRoot([string]$Path) {
    $dir = Get-Item -LiteralPath $Path -ErrorAction SilentlyContinue
    while ($dir) {
        if ($dir.Name -like "*.worktrees") {
            $baseName = $dir.Name.Substring(0, $dir.Name.Length - ".worktrees".Length)
            return Join-Path $dir.Parent.FullName $baseName
        }
        $dir = $dir.Parent
    }
    return $null
}
function Repair-SiblingPackageLinks([string]$ProjectRoot) {
    $manifestPath = Join-Path $ProjectRoot "Packages\manifest.json"
    if (-not (Test-Path $manifestPath)) { return }
    $manifest = Get-Content -Raw $manifestPath | ConvertFrom-Json
    if (-not $manifest.dependencies) { return }
    $packagesDir = Join-Path $ProjectRoot "Packages"

    foreach ($prop in $manifest.dependencies.PSObject.Properties) {
        $value = [string]$prop.Value
        if (-not $value.StartsWith("file:")) { continue }
        $relPath = $value.Substring(5)
        $resolved = [IO.Path]::GetFullPath((Join-Path $packagesDir $relPath))
        if (Test-Path $resolved) { continue } # already resolves - nothing to do

        # Walk the relative path's leading ".." segments off Packages\ to find the first missing
        # directory: that's the sibling-repo folder itself (e.g. "...\Unity.GridGame\MindAttic.
        # Capture"), not the package subfolder beneath it.
        $segments = $relPath -split '[\\/]'
        $upCount = 0
        while ($upCount -lt $segments.Count -and $segments[$upCount] -eq '..') { $upCount++ }
        if ($upCount -eq 0 -or $upCount -ge $segments.Count) { continue }
        $cursor = $packagesDir
        for ($i = 0; $i -lt $upCount; $i++) { $cursor = Split-Path -Parent $cursor }
        $siblingName = $segments[$upCount]
        $siblingLinkPath = Join-Path $cursor $siblingName
        if (Test-Path $siblingLinkPath) { continue } # exists; the gap is deeper - not ours to fix

        # Only auto-heal inside a "<root>.worktrees\..." tree; never touch the main checkout.
        $realWorkspaceRoot = Resolve-RealWorkspaceRoot $cursor
        if (-not $realWorkspaceRoot) { continue }
        $realSibling = Join-Path $realWorkspaceRoot $siblingName
        if (-not (Test-Path $realSibling)) {
            Write-Host "tools/unity.ps1: $($prop.Name) needs $siblingLinkPath but $realSibling does not exist either; leaving as-is." -ForegroundColor Yellow
            continue
        }

        New-Item -ItemType Junction -Path $siblingLinkPath -Target $realSibling | Out-Null
        Write-Host "tools/unity.ps1: linked $siblingLinkPath -> $realSibling (GG-T159)" -ForegroundColor Cyan
    }
}
Repair-SiblingPackageLinks $root

$ver = (Select-String -Path "$root/ProjectSettings/ProjectVersion.txt" -Pattern "m_EditorVersion: (.+)").Matches[0].Groups[1].Value.Trim()
$unity = "C:\Program Files\Unity\Hub\Editor\$ver\Editor\Unity.exe"
if (-not (Test-Path $unity)) { throw "Unity $ver not installed at $unity" }
# Unity's Editor window title includes the project folder's leaf name (e.g. "Unity.GridGame" in
# the main checkout, or the ticket folder name in a per-ticket worktree) -- match that instead of
# a hardcoded pre-Fleet-Rename name so this guard actually fires wherever the project lives.
$projectName = Split-Path -Leaf $root
if (Get-Process -Name Unity -ErrorAction SilentlyContinue | Where-Object { $_.MainWindowTitle -like "*$projectName*" }) {
    throw "$projectName is open in the Unity Editor. Close it (File > Quit) and re-run."
}
New-Item -ItemType Directory -Force "$root/Logs" | Out-Null
$logFile = "$root/Logs/$Log.log"
$uargs = @("-batchmode", "-quit", "-projectPath", $root, "-executeMethod", $Method, "-logFile", $logFile)
if ($NoGraphics) { $uargs = @("-nographics") + $uargs }
$uargs += $Extra
$p = Start-Process -FilePath $unity -ArgumentList $uargs -Wait -PassThru -NoNewWindow
$errors = Select-String -Path $logFile -Pattern "error CS\d+" | Select-Object -First 40
if ($errors) { $errors | ForEach-Object { Write-Host $_.Line -ForegroundColor Red } }
Select-String -Path $logFile -Pattern "\[Build\]" | ForEach-Object { Write-Host $_.Line }
Write-Host "Unity exit code: $($p.ExitCode)  (log: $logFile)"
exit $p.ExitCode
