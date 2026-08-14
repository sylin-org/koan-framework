[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $ProductSurfacePath,

    [Parameter(Mandatory)]
    [string] $OutputDirectory,

    [Parameter(Mandatory)]
    [string] $ExpectedVersion,

    [ValidateNotNullOrEmpty()]
    [string] $Configuration = 'Release'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repository = Split-Path -Parent $PSScriptRoot
$surfacePath = [IO.Path]::GetFullPath((Join-Path (Get-Location) $ProductSurfacePath))
$outputPath = [IO.Path]::GetFullPath((Join-Path (Get-Location) $OutputDirectory))

if (-not (Test-Path -LiteralPath $surfacePath -PathType Leaf)) {
    throw "Product surface '$surfacePath' does not exist."
}

$surface = Get-Content -LiteralPath $surfacePath -Raw | ConvertFrom-Json
$releaseTrain = [string]$surface.releaseTrain
if ([string]::IsNullOrWhiteSpace($releaseTrain)) {
    throw 'The product surface does not declare releaseTrain.'
}
if ($ExpectedVersion -notmatch ('^' + [Regex]::Escape($releaseTrain) + '\.[0-9]+$')) {
    throw "Expected version '$ExpectedVersion' is not part of release train '$releaseTrain'."
}

$packageRecords = @($surface.packages)
if ($packageRecords.Count -eq 0) {
    throw 'The product surface contains no package inventory.'
}
$duplicateIds = @($packageRecords | Group-Object packageId | Where-Object Count -ne 1)
if ($duplicateIds.Count -ne 0) {
    throw "Package ID '$($duplicateIds[0].Name)' appears $($duplicateIds[0].Count) times in the product surface."
}
$releaseIds = @($packageRecords.packageId | Sort-Object)

$projects = @(
    foreach ($packageId in $releaseIds) {
        $owners = @($surface.packages | Where-Object { $_.packageId -eq $packageId })
        if ($owners.Count -ne 1) {
            throw "Package '$packageId' matched $($owners.Count) inventory records; expected exactly one."
        }

        $relativeProject = [string]$owners[0].projectPath
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

        [pscustomobject]@{
            PackageId = [string]$packageId
            ProjectPath = $project
        }
    }
)

$duplicateProjects = @($projects | Group-Object ProjectPath | Where-Object Count -ne 1)
if ($duplicateProjects.Count -ne 0) {
    throw "Multiple package IDs resolve to project '$($duplicateProjects[0].Name)'."
}

New-Item -ItemType Directory -Path $outputPath -Force | Out-Null
$existing = @(Get-ChildItem -LiteralPath $outputPath -File |
    Where-Object { $_.Name -match '\.(s)?nupkg$' })
if ($existing.Count -ne 0) {
    throw "Output directory '$outputPath' already contains package artifacts."
}

foreach ($package in ($projects | Sort-Object PackageId)) {
    Write-Host "PACK|$($package.PackageId)|$($package.ProjectPath)"
    $arguments = @(
        'pack',
        $package.ProjectPath,
        '--configuration', $Configuration,
        '--output', $outputPath,
        '--no-build',
        '--no-restore',
        '--property:PublicRelease=true',
        '--nologo'
    )
    & dotnet @arguments
    if ($LASTEXITCODE -ne 0) {
        throw "Packing '$($package.PackageId)' failed."
    }
}

Write-Host "PACKAGE-TRAIN|PACKED|$($projects.Count)|$ExpectedVersion"
