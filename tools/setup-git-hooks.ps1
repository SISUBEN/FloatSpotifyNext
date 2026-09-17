#Requires -Version 5.1
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = (& git rev-parse --show-toplevel 2>$null)
if ($LASTEXITCODE -ne 0 -or -not $repoRoot) {
    throw 'Run this script from inside the FloatSpotify Git repository.'
}

$repoRoot = $repoRoot.Trim()
$expectedRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
if ([IO.Path]::GetFullPath($repoRoot) -ne [IO.Path]::GetFullPath($expectedRoot)) {
    throw "The script belongs to '$expectedRoot', but Git resolved '$repoRoot'."
}

git -C $repoRoot config --local core.hooksPath .githooks
if ($LASTEXITCODE -ne 0) { throw 'Failed to configure core.hooksPath.' }

git -C $repoRoot config --local commit.template .gitmessage
if ($LASTEXITCODE -ne 0) { throw 'Failed to configure commit.template.' }

Write-Host 'Commit conventions enabled for this clone:' -ForegroundColor Green
Write-Host '  hooks path : .githooks'
Write-Host '  template   : .gitmessage'
