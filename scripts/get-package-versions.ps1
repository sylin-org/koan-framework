[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $ProductSurfacePath,

    [Parameter(Mandatory)]
    [string] $OutputPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# Each packable project owns its version through its own version.json, so the release
# path can no longer assume one number. This asks Nerdbank.GitVersioning for every
# project's version BEFORE anything is packed, producing the manifest that pack,
# verify, and publish all agree against. Deriving it from packed artifacts instead
# would make the later checks circular: the feed would be proving itself.

$repository = Split-Path -Parent $PSScriptRoot
$surfacePath = [IO.Path]::GetFullPath((Join-Path (Get-Location) $ProductSurfacePath))
$outputFile = [IO.Path]::GetFullPath((Join-Path (Get-Location) $OutputPath))

if (-not (Test-Path -LiteralPath $surfacePath -PathType Leaf)) {
    throw "Product surface '$surfacePath' does not exist."
}

$surface = Get-Content -LiteralPath $surfacePath -Raw | ConvertFrom-Json
$releaseTrain = [string]$surface.releaseTrain
if ([string]::IsNullOrWhiteSpace($releaseTrain)) {
    throw 'The product surface does not declare releaseTrain.'
}

$packageRecords = @($surface.packages)
if ($packageRecords.Count -eq 0) {
    throw 'The product surface contains no package inventory.'
}

$versions = [ordered]@{}
foreach ($record in ($packageRecords | Sort-Object packageId)) {
    $packageId = [string]$record.packageId
    $relativeProject = [string]$record.projectPath
    if ([string]::IsNullOrWhiteSpace($relativeProject)) {
        throw "Package '$packageId' has no projectPath."
    }

    $projectDirectory = Split-Path -Parent ([IO.Path]::GetFullPath((Join-Path $repository $relativeProject)))
    if (-not (Test-Path -LiteralPath $projectDirectory -PathType Container)) {
        throw "Project directory for '$packageId' does not exist."
    }

    $version = (& dotnet nbgv get-version --public-release=true -v NuGetPackageVersion -p $projectDirectory 2>&1 | Select-Object -Last 1)
    if ($LASTEXITCODE -ne 0) {
        throw "Nerdbank.GitVersioning failed for '$packageId': $version"
    }

    $version = ([string]$version).Trim()
    if ($version -notmatch ('^' + [Regex]::Escape($releaseTrain) + '\.[0-9]+$')) {
        throw "Package '$packageId' resolved version '$version', which is not part of release train '$releaseTrain'."
    }

    $versions[$packageId] = $version
    Write-Host "VERSION|$packageId|$version"
}

New-Item -ItemType Directory -Path (Split-Path -Parent $outputFile) -Force | Out-Null
$versions | ConvertTo-Json -Depth 3 | Set-Content -LiteralPath $outputFile -Encoding utf8

$distinct = @($versions.Values | Sort-Object -Unique)
Write-Host "PACKAGE-VERSIONS|RESOLVED|$($versions.Count)|distinct=$($distinct.Count)"
