#!/usr/bin/env pwsh

[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path $PSScriptRoot -Parent
$srcFolder = Join-Path $repoRoot 'src'

if (-not (Test-Path $srcFolder)) {
    Write-Error "src folder not found at $srcFolder"
    exit 1
}

$projects = Get-ChildItem -Path $srcFolder -Filter '*.csproj' -Recurse
if ($projects.Count -eq 0) {
    Write-Warning 'No project files detected under src/'
    exit 0
}

$failures = @()

foreach ($proj in $projects) {
    try {
        [xml]$xml = Get-Content -Path $proj.FullName
    } catch {
        $failures += "Failed to parse XML for $($proj.FullName): $_"
        continue
    }

    $propertyGroups = @($xml.Project.PropertyGroup)
    if ($propertyGroups.Count -eq 0) {
        $failures += "No <PropertyGroup> found in $($proj.FullName)"
        continue
    }

    $isPackableValue = ($propertyGroups | Where-Object { $_.IsPackable }).IsPackable
    if ($isPackableValue -and $isPackableValue.Trim().ToLowerInvariant() -eq 'false') {
        # Explicitly non-packable project; skip validation
        continue
    }

    $hasDescription = $propertyGroups | Where-Object { $_.Description }
    if (-not $hasDescription) {
        $failures += "Missing <Description> in $($proj.FullName)"
    }

    $hasTags = $propertyGroups | Where-Object { $_.PackageTags }
    if (-not $hasTags) {
        $failures += "Missing <PackageTags> in $($proj.FullName)"
    }

    $langVersionNode = $propertyGroups | Where-Object { $_.LangVersion }
    if ($langVersionNode) {
        $value = $langVersionNode | Select-Object -ExpandProperty LangVersion -First 1
        if ($value -and $value.Trim().ToLowerInvariant() -ne 'latestmajor') {
            $failures += "LangVersion override ($value) detected in $($proj.FullName); use repo default or latestMajor."
        }
    }

    $dir = Split-Path $proj.FullName
    if (-not (Test-Path (Join-Path $dir 'README.md'))) {
        $failures += "README.md missing for $($proj.FullName)"
    }
}

if ($failures.Count -gt 0) {
    foreach ($failure in $failures) {
        Write-Error $failure
    }
    exit 1
}

Write-Host 'Packaging validation passed.' -ForegroundColor Green
