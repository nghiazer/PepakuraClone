#Requires -Version 5.1
<#
.SYNOPSIS
    Builds the 4H-Unfolder installer using Inno Setup.

.PARAMETER Version
    The version suffix to package, e.g. "0.0.3.H".
    Expects publish\v<Version>\ to already exist (run dotnet publish first).

.PARAMETER IsccPath
    Optional override for ISCC.exe path.

.EXAMPLE
    .\build-installer.ps1 -Version 0.0.3.H
#>
param(
    [string]$Version  = "0.0.3.H",
    [string]$IsccPath = ""
)
Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

if (-not $IsccPath) {
    $candidates = @(
        "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
        "C:\Program Files (x86)\Inno Setup 6\ISCC.exe",
        "C:\Program Files\Inno Setup 6\ISCC.exe"
    )
    foreach ($c in $candidates) { if (Test-Path $c) { $IsccPath = $c; break } }
}
if (-not $IsccPath -or -not (Test-Path $IsccPath)) {
    Write-Error "ISCC.exe not found. Install Inno Setup: winget install JRSoftware.InnoSetup"
}

$ScriptDir   = $PSScriptRoot
$ProjectRoot = Split-Path $ScriptDir -Parent
$IssFile     = Join-Path $ScriptDir "4H-Unfolder.iss"
$PublishDir  = Join-Path $ProjectRoot "publish\v$Version"
$ExpectedOut = Join-Path $ProjectRoot "publish\4H-Unfolder-v${Version}-setup.exe"

if (-not (Test-Path $IssFile))    { Write-Error "Script not found: $IssFile" }
if (-not (Test-Path $PublishDir)) { Write-Error "Publish dir not found: $PublishDir  (run: dotnet publish -c Release -r win-x64 --self-contained)" }
if (-not (Test-Path (Join-Path $PublishDir "4H-Unfolder.exe"))) { Write-Error "4H-Unfolder.exe not found in $PublishDir" }

Write-Host ""
Write-Host "=== 4H-Unfolder Installer Build ===" -ForegroundColor Cyan
Write-Host "  Version : $Version"
Write-Host "  ISCC    : $IsccPath"
Write-Host "  Source  : $PublishDir"
Write-Host "  Output  : $ExpectedOut"
Write-Host ""

$startTime = Get-Date
& $IsccPath "/DMyAppVersion=$Version" $IssFile
if ($LASTEXITCODE -ne 0) { Write-Error "ISCC.exe failed (exit $LASTEXITCODE)" }

if (Test-Path $ExpectedOut) {
    $item    = Get-Item $ExpectedOut
    $sizeMB  = [math]::Round($item.Length / 1048576, 1)
    $elapsed = [math]::Round(((Get-Date) - $startTime).TotalSeconds, 0)
    Write-Host ""
    Write-Host ("Done in {0}s -- {1} MB  -->  {2}" -f $elapsed, $sizeMB, $ExpectedOut) -ForegroundColor Green
} else {
    Write-Host "ERROR: expected output not found: $ExpectedOut" -ForegroundColor Red
    exit 1
}
