<#
.SYNOPSIS
  Composition-lockfile drift gate (P1.1).

.DESCRIPTION
  Fails if any git-tracked koan.lock.json differs from what the current build produced — i.e. a change
  altered an app's composition (a module added/removed, a framework minor bump) but the refreshed
  lockfile was not committed. Run AFTER a build: the Sylin.Koan.Core build target regenerates
  koan.lock.json, and this script checks `git diff` on the tracked file(s).

  Clean skip (exit 0) when no koan.lock.json is tracked. Consumers wire this into their own PR gate;
  in this repo green-ratchet.ps1 invokes it (so CI == local).

.EXAMPLE
  dotnet build Koan.sln
  pwsh scripts/compare-koan-lock.ps1
#>
param()

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path "$PSScriptRoot/..").ProviderPath
Push-Location $root
try {
    $tracked = @(& git ls-files | Where-Object { $_ -match '(^|/)koan\.lock\.json$' })
    if ($tracked.Count -eq 0) {
        Write-Host "[compare-koan-lock] no tracked koan.lock.json — nothing to compare (skip)." -ForegroundColor Yellow
        exit 0
    }

    & git diff --exit-code -- $tracked | Out-Null
    if ($LASTEXITCODE -ne 0) {
        Write-Host "[compare-koan-lock] DRIFT — a build refreshed koan.lock.json but the change is uncommitted:" -ForegroundColor Red
        & git --no-pager diff --stat -- $tracked
        Write-Host "  Commit the refreshed lockfile to record the new composition." -ForegroundColor Red
        exit 1
    }

    Write-Host "[compare-koan-lock] OK — $($tracked.Count) lockfile(s) match the committed composition." -ForegroundColor Green
    exit 0
}
finally {
    Pop-Location
}
