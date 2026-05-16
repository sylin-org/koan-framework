<#
.SYNOPSIS
    Read-only diagnostic for the ARCH-0082 versioning system.
    Shows current versions, classification, and what would bump on the next release.

.DESCRIPTION
    Wraps Update-Versions.ps1 in -DryRun mode plus extra summary tables. Safe to
    run anywhere, anytime — no writes, no side effects.

.EXAMPLE
    pwsh scripts/versioning/Show-VersionStatus.ps1

.EXAMPLE
    pwsh scripts/versioning/Show-VersionStatus.ps1 -SinceTag release/v0.7.0
    # What-if: pretend that's the last release
#>

[CmdletBinding()]
param(
    [string]$SinceTag
)

$ErrorActionPreference = 'Stop'

$updateArgs = @{ DryRun = $true }
if ($SinceTag) { $updateArgs['SinceTag'] = $SinceTag }

& (Join-Path $PSScriptRoot 'Update-Versions.ps1') @updateArgs
