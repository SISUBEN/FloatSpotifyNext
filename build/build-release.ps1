#Requires -Version 5.1
<#
.SYNOPSIS
    Builds every FloatSpotify Next release artifact.

.DESCRIPTION
    Produces, under artifacts/:
      publish\self-contained        payload for the offline installer
      publish\framework-dependent   payload for the online installer + the framework zip
      publish\portable              single-file portable build
      FloatSpotifyNext-x86_64-v<ver>-portable.zip
      FloatSpotifyNext-x86_64-v<ver>-framework.zip
      FloatSpotifyNext-x86_64-v<ver>-setup-offline.exe   (needs Inno Setup 6)
      FloatSpotifyNext-x86_64-v<ver>-setup-online.exe    (needs Inno Setup 6)

    Artifact naming is fixed as {ProductName}-{Arch}-v{Version}-{variant}.{ext}.
    Keep $ProductName / $Arch below in sync with the same #defines in
    installer\FloatSpotify.Next.iss.

    Installer compilation is skipped automatically when ISCC.exe is not found.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File build\build-release.ps1
    powershell -ExecutionPolicy Bypass -File build\build-release.ps1 -Version 1.2.0
    powershell -ExecutionPolicy Bypass -File build\build-release.ps1 -SkipInstallers
#>
[CmdletBinding()]
param(
    [string]$Version,
    [switch]$SkipInstallers
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repoRoot 'src\FloatSpotify\FloatSpotify.csproj'
$artifacts = Join-Path $repoRoot 'artifacts'
$publishRoot = Join-Path $artifacts 'publish'

if (-not (Test-Path $project)) {
    throw "Project not found: $project"
}

if (-not $Version) {
    $csproj = Get-Content -Raw -Path $project
    if ($csproj -match '<Version>([^<]+)</Version>') {
        $Version = $Matches[1].Trim()
    } else {
        throw 'Could not read <Version> from the csproj. Pass -Version explicitly.'
    }
}

Write-Host "FloatSpotify Next release build" -ForegroundColor Cyan
Write-Host "  version : $Version"
Write-Host "  repo    : $repoRoot"
Write-Host ''

$sdk = (& dotnet --version).Trim()
Write-Host "  .NET SDK: $sdk" -ForegroundColor DarkGray
Write-Host ''

if (Test-Path $artifacts) {
    Write-Host 'Cleaning artifacts/' -ForegroundColor DarkGray
    Remove-Item -Recurse -Force $artifacts
}
New-Item -ItemType Directory -Force -Path $publishRoot | Out-Null

function Invoke-Publish {
    param(
        [Parameter(Mandatory)] [string]$Profile,
        [Parameter(Mandatory)] [string]$OutDir
    )

    Write-Host "==> Publishing '$Profile'" -ForegroundColor Cyan
    & dotnet publish $project `
        -p:PublishProfile=$Profile `
        -p:Version=$Version `
        -o $OutDir `
        --nologo
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish failed for profile '$Profile' (exit $LASTEXITCODE)."
    }
    Write-Host ''
}

$selfContained = Join-Path $publishRoot 'self-contained'
$frameworkDependent = Join-Path $publishRoot 'framework-dependent'
$portable = Join-Path $publishRoot 'portable'

Invoke-Publish -Profile 'Windows-x64' -OutDir $selfContained
Invoke-Publish -Profile 'FrameworkDependent-x64' -OutDir $frameworkDependent
Invoke-Publish -Profile 'Portable-SingleFile' -OutDir $portable

$ProductName = 'FloatSpotifyNext'
$Arch = 'x86_64'

function New-ArtifactZip {
    param(
        [Parameter(Mandatory)] [string]$SourceDir,
        [Parameter(Mandatory)] [string]$Variant
    )

    $zip = Join-Path $artifacts "$ProductName-$Arch-v$Version-$Variant.zip"
    Write-Host "==> Packaging '$Variant' zip" -ForegroundColor Cyan
    Compress-Archive -Path (Join-Path $SourceDir '*') -DestinationPath $zip -CompressionLevel Optimal
    Write-Host ''
}

New-ArtifactZip -SourceDir $portable -Variant 'portable'
New-ArtifactZip -SourceDir $frameworkDependent -Variant 'framework'

# ---- Installers ----
$isccCandidates = @(
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe",
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
)
$iscc = $isccCandidates | Where-Object { Test-Path $_ } | Select-Object -First 1

if ($SkipInstallers) {
    Write-Host '==> Skipping installers (-SkipInstallers)' -ForegroundColor Yellow
} elseif (-not $iscc) {
    Write-Host '==> Inno Setup 6 not found - skipping installers' -ForegroundColor Yellow
    Write-Host '    Install with: winget install JRSoftware.InnoSetup' -ForegroundColor Yellow
    Write-Host '    Then re-run this script, or compile manually:' -ForegroundColor Yellow
    Write-Host "      ISCC.exe /DAppVersion=$Version installer\FloatSpotify.Next.iss" -ForegroundColor Yellow
    Write-Host "      ISCC.exe /DAppVersion=$Version /DONLINE installer\FloatSpotify.Next.iss" -ForegroundColor Yellow
} else {
    $iss = Join-Path $repoRoot 'installer\FloatSpotify.Next.iss'

    Write-Host '==> Building offline installer' -ForegroundColor Cyan
    & $iscc "/DAppVersion=$Version" $iss
    if ($LASTEXITCODE -ne 0) { throw "Inno Setup failed for the offline installer (exit $LASTEXITCODE)." }

    Write-Host '==> Building online installer' -ForegroundColor Cyan
    & $iscc "/DAppVersion=$Version" '/DONLINE' $iss
    if ($LASTEXITCODE -ne 0) { throw "Inno Setup failed for the online installer (exit $LASTEXITCODE)." }

    # Move installer output next to the other artifacts.
    $issOut = Join-Path $repoRoot 'installer\Output'
    if (Test-Path $issOut) {
        Get-ChildItem -Path $issOut -Filter '*.exe' | ForEach-Object {
            Move-Item -Force $_.FullName (Join-Path $artifacts $_.Name)
        }
    }
}

# ---- Summary ----
Write-Host ''
Write-Host 'Release artifacts' -ForegroundColor Cyan
Write-Host ('-' * 68)

function Get-DirSize {
    param([string]$Path)
    if (-not (Test-Path $Path)) { return 0 }
    $sum = (Get-ChildItem -Recurse -Force -File $Path | Measure-Object -Property Length -Sum).Sum
    if ($null -eq $sum) { return 0 }
    return $sum
}

$rows = @(
    [pscustomobject]@{ Name = 'publish\self-contained';      Path = $selfContained }
    [pscustomobject]@{ Name = 'publish\framework-dependent'; Path = $frameworkDependent }
    [pscustomobject]@{ Name = 'publish\portable';            Path = $portable }
)

foreach ($row in $rows) {
    $bytes = Get-DirSize -Path $row.Path
    $count = if (Test-Path $row.Path) { (Get-ChildItem -Recurse -Force -File $row.Path).Count } else { 0 }
    Write-Host ("  {0,-28} {1,9:N1} MB  ({2} files)" -f $row.Name, ($bytes / 1MB), $count)
}

Get-ChildItem -Path $artifacts -File | Sort-Object Name | ForEach-Object {
    Write-Host ("  {0,-28} {1,9:N1} MB" -f $_.Name, ($_.Length / 1MB))
}

Write-Host ('-' * 68)
Write-Host 'Done.' -ForegroundColor Green
