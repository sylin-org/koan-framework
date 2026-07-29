[CmdletBinding()]
param(
    [string]$RepositoryRoot,
    [string]$ManifestPath,
    [string]$Destination,
    [switch]$Force
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if (-not $RepositoryRoot) { $RepositoryRoot = (& git rev-parse --show-toplevel).Trim() }
$RepositoryRoot = (Resolve-Path -LiteralPath $RepositoryRoot).ProviderPath
if (-not $ManifestPath) { $ManifestPath = Join-Path $RepositoryRoot 'artifacts/data-adapter-conformance/checkpoints/dac00-current.json' }
$ManifestPath = (Resolve-Path -LiteralPath $ManifestPath).ProviderPath
if (-not $Destination) { $Destination = Join-Path $RepositoryRoot 'artifacts/data-adapter-conformance/audit/dac00-source' }
$Destination = [IO.Path]::GetFullPath($Destination)
$artifactBoundary = [IO.Path]::GetFullPath((Join-Path $RepositoryRoot 'artifacts/data-adapter-conformance'))
if (-not $Destination.StartsWith($artifactBoundary, [StringComparison]::OrdinalIgnoreCase) -or $Destination -eq $artifactBoundary) {
    throw "Destination must be a child of '$artifactBoundary'."
}
if (Test-Path -LiteralPath $Destination) {
    if (-not $Force) { throw "Destination already exists: $Destination" }
    Remove-Item -LiteralPath $Destination -Recurse -Force
}
New-Item -ItemType Directory -Path $Destination -Force | Out-Null

$manifest = Get-Content -LiteralPath $ManifestPath -Raw | ConvertFrom-Json -DateKind String
$bundlePath = [string]$manifest.bundle.path
if (-not (Test-Path -LiteralPath $bundlePath -PathType Leaf)) { throw "Missing checkpoint bundle: $bundlePath" }
$bundleHash = (Get-FileHash -LiteralPath $bundlePath -Algorithm SHA256).Hash.ToLowerInvariant()
if ($bundleHash -ne [string]$manifest.bundle.sha256) { throw 'Checkpoint bundle hash mismatch.' }
$archivePath = Join-Path (Split-Path -Parent $Destination) ('base-' + [guid]::NewGuid().ToString('n') + '.zip')
try {
    & git -C $RepositoryRoot archive --format=zip --output=$archivePath ([string]$manifest.baseCommit)
    if ($LASTEXITCODE -ne 0) { throw 'git archive failed.' }
    Expand-Archive -LiteralPath $archivePath -DestinationPath $Destination -Force
    Expand-Archive -LiteralPath $bundlePath -DestinationPath $Destination -Force
    foreach ($relative in @($manifest.deleted)) {
        $target = [IO.Path]::GetFullPath((Join-Path $Destination ([string]$relative)))
        if (-not $target.StartsWith($Destination, [StringComparison]::OrdinalIgnoreCase) -or $target -eq $Destination) {
            throw "Unsafe declared deletion '$target'."
        }
        if (Test-Path -LiteralPath $target) { Remove-Item -LiteralPath $target -Recurse -Force }
    }
    foreach ($file in @($manifest.files)) {
        $target = Join-Path $Destination ([string]$file.path)
        if (-not (Test-Path -LiteralPath $target -PathType Leaf)) { throw "Export is missing '$($file.path)'." }
        $hash = (Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash.ToLowerInvariant()
        if ($hash -ne [string]$file.sha256) { throw "Export hash mismatch for '$($file.path)'." }
    }
}
catch {
    if (Test-Path -LiteralPath $Destination) { Remove-Item -LiteralPath $Destination -Recurse -Force }
    throw
}
finally {
    if (Test-Path -LiteralPath $archivePath) { Remove-Item -LiteralPath $archivePath -Force }
}
Write-Output "CHECKPOINT EXPANDED base=$($manifest.baseCommit) files=$(@($manifest.files).Count) destination=$Destination"
