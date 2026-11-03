<#
.SYNOPSIS
  Builds (Release, only when the sources are newer than the exe) and starts Thumbnail Studio (GG-T222), the
  standalone editor for each class's "Thumbnail" (Zoom / X / Y) in classes.json.
.PARAMETER Rebuild
  Build even if the exe looks current.
.EXAMPLE
  powershell -File tools\thumbnail-studio.ps1
  powershell -File tools\thumbnail-studio.ps1 --select alchemist
  powershell -File tools\thumbnail-studio.ps1 --classes C:\temp\classes.json
.NOTES
  Any other arguments are passed to the app (--repo, --classes, --select, --demo). Windows PowerShell 5.1 compatible.
#>
param(
    [switch]$Rebuild,
    [Parameter(ValueFromRemainingArguments = $true)][string[]]$AppArgs
)
$ErrorActionPreference = "Stop"

$proj = Join-Path $PSScriptRoot "ThumbnailStudio"
$exe = Join-Path $proj "bin\Release\net10.0-windows\ThumbnailStudio.exe"
$repo = Split-Path $PSScriptRoot -Parent

$stale = $Rebuild -or -not (Test-Path $exe)
if (-not $stale) {
    $built = (Get-Item $exe).LastWriteTimeUtc
    $newer = Get-ChildItem $proj -File -Recurse -Include *.cs, *.xaml, *.csproj |
        Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' -and $_.LastWriteTimeUtc -gt $built }
    $stale = $null -ne $newer
}

if ($stale) {
    Write-Host "Building Thumbnail Studio (Release)..." -ForegroundColor Cyan
    & dotnet build (Join-Path $proj "ThumbnailStudio.csproj") -c Release --nologo -v quiet
    if ($LASTEXITCODE -ne 0) {
        Write-Host "Build failed (exit $LASTEXITCODE)." -ForegroundColor Red
        exit $LASTEXITCODE
    }
}

$argList = @("--repo", $repo)
if ($AppArgs) { $argList += $AppArgs }
# Windows PowerShell's Start-Process joins arguments with spaces and doesn't quote them.
$argList = $argList | ForEach-Object { if ($_ -match '\s') { '"' + $_ + '"' } else { $_ } }
Start-Process -FilePath $exe -ArgumentList $argList -WorkingDirectory $repo | Out-Null
