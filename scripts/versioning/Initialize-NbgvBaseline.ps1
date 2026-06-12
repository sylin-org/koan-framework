#Requires -Version 7
<#
.SYNOPSIS
    ARCH-0085: baseline every packable Koan package at a uniform major.minor and let
    Nerdbank.GitVersioning compute the patch from per-package git height.

.DESCRIPTION
    Writes one version.json per packable csproj under src/ with pathFilters ["."], so each
    package's patch = the number of commits touching ITS OWN folder since its major.minor last
    changed (independent versioning, no spam of unchanged packages). Also writes a repo-root
    version.json as the fallback for non-packable / test / sample projects.

    Operators bump a single package by editing the "version" field in its version.json. nbgv
    does the patch automatically. Compatibility across packages is enforced separately by
    build/compat-ranges.targets (ARCH-0085 §3), not by lockstepped version numbers.

    Idempotent — re-run to pick up newly-added packages or to re-baseline.

.PARAMETER BaselineVersion
    The major.minor every package starts at. Default 0.17.

.PARAMETER WhatIf
    Print what would be written without writing.
#>
param(
    [string]$BaselineVersion = '0.17',
    [switch]$WhatIf
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..' '..')).Path
$Schema = 'https://raw.githubusercontent.com/dotnet/Nerdbank.GitVersioning/main/src/NerdBank.GitVersioning/version.schema.json'

function Test-Packable([string]$FullPath) {
    $content = Get-Content $FullPath -Raw
    if ($content -match '<IsPackable>\s*false\s*</IsPackable>') { return $false }
    # Walk up: IsPackable=false in any ancestor Directory.Build.props cascades (src/Services, samples).
    $dir = Split-Path $FullPath -Parent
    while ($dir -and $dir.Length -ge $RepoRoot.Length) {
        $props = Join-Path $dir 'Directory.Build.props'
        if ((Test-Path $props) -and ((Get-Content $props -Raw) -match '<IsPackable>\s*false\s*</IsPackable>')) {
            return $false
        }
        $parent = Split-Path $dir -Parent
        if ($parent -eq $dir) { break }
        $dir = $parent
    }
    return $true
}

function Write-VersionJson([string]$Dir, [object]$PathFilters) {
    # versionHeightOffset -1: the commit that introduces a version.json is git-height 1, which would
    # make the baseline 0.17.1. Offsetting by -1 makes the introducing commit land on 0.17.0, and the
    # next commit touching the folder on 0.17.1 (matches "baseline at .0, change -> .1"). Safe: only
    # commits that carry version.json are built, so height is always >= 1 and height+offset >= 0.
    $obj = [ordered]@{ '$schema' = $Schema; version = $BaselineVersion; versionHeightOffset = -1 }
    if ($null -ne $PathFilters) { $obj['pathFilters'] = $PathFilters }
    $json = $obj | ConvertTo-Json -Depth 5
    $target = Join-Path $Dir 'version.json'
    if ($WhatIf) { Write-Host "WOULD write $target"; return }
    Set-Content -Path $target -Value $json -Encoding UTF8
}

Write-Host "Baselining packable packages at $BaselineVersion (repo: $RepoRoot)" -ForegroundColor Cyan

# Root fallback (no pathFilters -> repo-wide height; only reaches unpublished tests/samples).
Write-VersionJson -Dir $RepoRoot -PathFilters $null
Write-Host "  root version.json (fallback)"

$csprojs = Get-ChildItem -Path (Join-Path $RepoRoot 'src') -Filter *.csproj -Recurse |
    Where-Object { Test-Packable $_.FullName }

$count = 0
foreach ($c in $csprojs) {
    Write-VersionJson -Dir (Split-Path $c.FullName -Parent) -PathFilters @('.')
    $count++
}

Write-Host "  $count packable package version.json files" -ForegroundColor Green
Write-Host "Done. Patch = per-package git height; bump major.minor by editing a package's version.json." -ForegroundColor Cyan
