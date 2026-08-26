[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $PlanPath,

    [Parameter(Mandatory)]
    [string] $OutputDirectory,

    [ValidateNotNullOrEmpty()]
    [string] $Configuration = 'Release',

    [bool] $PublisherSigningEnabled = $false
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# Packs only what the plan marks for publication. Packing an unchanged package would be pointless
# work and actively misleading: the build stamps the current commit into the assembly, so an
# unchanged package rebuilt at a later commit is a different artifact wearing an already published
# version. Not building it is what makes "no change, no new package" true rather than aspirational.

function Get-AbsolutePath([string] $Path) {
    if ([IO.Path]::IsPathFullyQualified($Path)) { return [IO.Path]::GetFullPath($Path) }
    return [IO.Path]::GetFullPath((Join-Path (Get-Location) $Path))
}

$repository = Split-Path -Parent $PSScriptRoot
$planFile = Get-AbsolutePath $PlanPath
$outputPath = Get-AbsolutePath $OutputDirectory
$codeSigningPolicy = 'Code signing policy: https://github.com/sylin-org/koan-framework/blob/main/CODE_SIGNING_POLICY.md.'
if ($PublisherSigningEnabled) {
    $codeSigningPolicy += ' Free code signing provided by SignPath.io, certificate by SignPath Foundation.'
}
# MSBuild treats commas in /property values as property separators. Percent-encoding survives the
# command-line parser and is decoded before the value is written to the package nuspec.
$codeSigningPolicyArgument = $codeSigningPolicy.Replace(',', '%2C')

if (-not (Test-Path -LiteralPath $planFile -PathType Leaf)) {
    throw "Release plan '$planFile' does not exist."
}

$plan = Get-Content -LiteralPath $planFile -Raw | ConvertFrom-Json
$selected = @(@($plan.packages) | Where-Object { $_.publish })

New-Item -ItemType Directory -Path $outputPath -Force | Out-Null
$existing = @(Get-ChildItem -LiteralPath $outputPath -File | Where-Object { $_.Name -match '\.(s)?nupkg$' })
if ($existing.Count -ne 0) {
    throw "Output directory '$outputPath' already contains package artifacts."
}

if ($selected.Count -eq 0) {
    Write-Host 'PACK|NOTHING-TO-DO|no package changed since the published train'
    Write-Host "PACK|PACKED|0"
    return
}

foreach ($package in $selected) {
    $packageId = [string]$package.packageId
    $relativeProject = [string]$package.projectPath
    if ([string]::IsNullOrWhiteSpace($relativeProject)) {
        throw "Package '$packageId' has no projectPath."
    }

    $project = [IO.Path]::GetFullPath((Join-Path $repository $relativeProject))
    $repositoryPrefix = [IO.Path]::GetFullPath($repository).TrimEnd(
        [IO.Path]::DirectorySeparatorChar,
        [IO.Path]::AltDirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    if (-not $project.StartsWith($repositoryPrefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Project path '$relativeProject' for '$packageId' escapes the repository."
    }
    if (-not (Test-Path -LiteralPath $project -PathType Leaf)) {
        throw "Project '$relativeProject' for '$packageId' does not exist."
    }

    Write-Host "PACK|$packageId|$($package.version)"
    & dotnet pack $project `
        --configuration $Configuration `
        --output $outputPath `
        --property:PublicRelease=true `
        "--property:PackageReleaseNotes=$codeSigningPolicyArgument" `
        --nologo
    if ($LASTEXITCODE -ne 0) {
        throw "Packing '$packageId' failed."
    }

    $expected = Join-Path $outputPath "$packageId.$($package.version).nupkg"
    if (-not (Test-Path -LiteralPath $expected -PathType Leaf)) {
        throw "Packing '$packageId' did not produce '$([IO.Path]::GetFileName($expected))'."
    }
}

$produced = @(Get-ChildItem -LiteralPath $outputPath -File -Filter *.nupkg)
if ($produced.Count -ne $selected.Count) {
    throw "Feed contains $($produced.Count) packages; the plan selected $($selected.Count)."
}

Write-Host "PACK|PACKED|$($selected.Count)"
