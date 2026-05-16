<#
.SYNOPSIS
    Audit NuGet package metadata across all packable csprojs.
    Run before a release to spot missing Description / PackageTags / packageId issues
    that would either reject the package at nuget.org or yield a confusing listing.
    Operator's guide: docs/workbooks/nuget-publishing.md (Failure → recovery).

.EXAMPLE
    pwsh scripts/versioning/Audit-NuGetMetadata.ps1
#>

[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$RepoRoot = Resolve-Path (Join-Path $PSScriptRoot '..' '..')
Set-Location $RepoRoot

# All csprojs under src/ that aren't opted out via <IsPackable>false</IsPackable> —
# either in the csproj itself OR in any parent Directory.Build.props (which MSBuild
# imports transitively up the folder tree).
$repoRootString = $RepoRoot.ToString()

function Test-Packable {
    param([string]$CsprojPath)
    if ((Get-Content $CsprojPath -Raw) -match '<IsPackable>\s*false\s*</IsPackable>') { return $false }
    # Walk upward checking every Directory.Build.props
    $dir = Split-Path $CsprojPath -Parent
    while ($dir -and $dir.Length -ge $repoRootString.Length) {
        $props = Join-Path $dir 'Directory.Build.props'
        if (Test-Path $props) {
            if ((Get-Content $props -Raw) -match '<IsPackable>\s*false\s*</IsPackable>') { return $false }
        }
        $parent = Split-Path $dir -Parent
        if ($parent -eq $dir) { break }
        $dir = $parent
    }
    return $true
}

$all = Get-ChildItem src -Filter '*.csproj' -Recurse | Where-Object {
    Test-Packable -CsprojPath $_.FullName
}

$results = foreach ($f in $all) {
    $relPath = (Resolve-Path $f.FullName -Relative) -replace '\\', '/' -replace '^\./', ''
    $xml = [xml](Get-Content $f.FullName -Raw)

    $description = $xml.SelectSingleNode('//Description')
    $tags        = $xml.SelectSingleNode('//PackageTags')
    $kind        = $xml.SelectSingleNode('//KoanPackageKind')

    [PSCustomObject]@{
        Project        = [IO.Path]::GetFileNameWithoutExtension($f.FullName)
        RelPath        = $relPath
        Kind           = if ($kind) { $kind.InnerText.Trim() } else { '(unset)' }
        HasDescription = [bool]$description
        HasPackageTags = [bool]$tags
        Description    = if ($description) { $description.InnerText.Trim() } else { $null }
    }
}

$noDescription = @($results | Where-Object { -not $_.HasDescription })
$noTags        = @($results | Where-Object { -not $_.HasPackageTags })
$noKind        = @($results | Where-Object { $_.Kind -eq '(unset)' })

Write-Host "NuGet metadata audit" -ForegroundColor Cyan
Write-Host "  Total packable csprojs : $($results.Count)"
Write-Host "  Missing <Description>  : $($noDescription.Count)"
Write-Host "  Missing <PackageTags>  : $($noTags.Count)"
Write-Host "  Missing <KoanPackageKind>: $($noKind.Count)"
Write-Host ""

if ($noDescription.Count -gt 0) {
    Write-Host "Missing <Description>:" -ForegroundColor Yellow
    $noDescription | ForEach-Object { Write-Host "  $($_.RelPath)" }
    Write-Host ""
}

if ($noTags.Count -gt 0) {
    Write-Host "Missing <PackageTags>:" -ForegroundColor Yellow
    $noTags | ForEach-Object { Write-Host "  $($_.RelPath)" }
    Write-Host ""
}

if ($noKind.Count -gt 0) {
    Write-Host "Missing <KoanPackageKind>:" -ForegroundColor Red
    $noKind | ForEach-Object { Write-Host "  $($_.RelPath)" }
    Write-Host ""
}

# Exit code: 0 if all clean, 1 otherwise
if ($noDescription.Count -gt 0 -or $noTags.Count -gt 0 -or $noKind.Count -gt 0) {
    exit 1
}
Write-Host "All packable csprojs have complete metadata." -ForegroundColor Green
