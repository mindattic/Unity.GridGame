# Builds the Windows development player (with the automation endpoint compiled in).
#   pwsh tools/build.ps1 [-Release] [-Target Windows|Android|WebGL] [-SkipProse]
# GG-T355: first pulls the latest published story text from Prose into Content/Text (tools/prose-sync.ps1); with the
# Hub offline it warns and builds with the committed snapshot. -SkipProse (or GG_SKIP_PROSE=1) skips the pull.
param(
    [switch]$Release,
    [ValidateSet("Windows", "Android", "WebGL")]
    [string]$Target = "Windows",
    [switch]$SkipProse
)
& "$PSScriptRoot/prose-sync.ps1" -Skip:$SkipProse
# The Unity-side pre-build hook (GaidenBuild.SyncProse) sees this and does not pull a second time.
$env:GG_PROSE_SYNCED = "1"
$method = switch ($Target) {
    "Android" { "GridGame.EditorTools.GaidenBuild.BuildAndroid" }
    "WebGL" { "GridGame.EditorTools.GaidenBuild.BuildWebGL" }
    "Windows" { if ($Release) { "GridGame.EditorTools.GaidenBuild.BuildWindowsRelease" } else { "GridGame.EditorTools.GaidenBuild.BuildWindowsDev" } }
}
& "$PSScriptRoot/unity.ps1" -Method $method -Log "build-$Target"
exit $LASTEXITCODE
