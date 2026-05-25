<#
.SYNOPSIS
    Copies the latest published exe + native DLLs into installer\dist\ so
    Inno Setup can compile 4H-Unfolder-vX.X.X.X-Setup.exe.

.USAGE
    From the repo root:
        .\scripts\prepare-installer.ps1
    Then compile:
        & "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" installer\4H-Unfolder.iss
#>

$ErrorActionPreference = "Stop"

$PublishDir = "$PSScriptRoot\..\publish"
$DistDir    = "$PSScriptRoot\..\installer\dist"

# The published exe lives in publish\v<latest>\
$latestVer = Get-ChildItem $PublishDir -Directory |
             Where-Object { $_.Name -match "^v\d" } |
             Sort-Object Name -Descending |
             Select-Object -First 1

if (-not $latestVer) { throw "No versioned publish folder found in $PublishDir" }
Write-Host "Using publish version: $($latestVer.Name)" -ForegroundColor Cyan

New-Item -ItemType Directory -Force -Path $DistDir | Out-Null

$files = @(
    "4H-Unfolder.exe",
    "wpfgfx_cor3.dll",
    "PresentationNative_cor3.dll",
    "D3DCompiler_47_cor3.dll",
    "PenImc_cor3.dll",
    "vcruntime140_cor3.dll",
    "assimp.dll"
)

foreach ($f in $files) {
    $src = Join-Path $latestVer.FullName $f
    if (-not (Test-Path $src)) { throw "Missing: $src" }
    Copy-Item $src $DistDir -Force
    Write-Host "  Copied $f" -ForegroundColor Green
}

Write-Host "`nDist ready at: $DistDir" -ForegroundColor Yellow
Write-Host "Next step: & `"C:\Program Files (x86)\Inno Setup 6\ISCC.exe`" installer\4H-Unfolder.iss" -ForegroundColor Yellow
