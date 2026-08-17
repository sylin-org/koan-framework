[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $ProductSurfacePath,

    [Parameter(Mandatory)]
    [string] $FeedDirectory,

    [Parameter(Mandatory)]
    [string] $VersionManifestPath,

    [Parameter(Mandatory)]
    [string] $HashManifestPath,

    [switch] $KeepConsumer
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Read-PackageIdentity([string] $Path) {
    $archive = [IO.Compression.ZipFile]::OpenRead($Path)
    try {
        $entries = @($archive.Entries | Where-Object { $_.FullName -match '^[^/]+\.nuspec$' })
        if ($entries.Count -ne 1) {
            throw "Package '$Path' contains $($entries.Count) root nuspec files; expected exactly one."
        }
        $reader = [IO.StreamReader]::new($entries[0].Open())
        try {
            [xml]$nuspec = $reader.ReadToEnd()
        }
        finally {
            $reader.Dispose()
        }
        $metadata = $nuspec.SelectSingleNode("/*[local-name()='package']/*[local-name()='metadata']")
        $idNode = $metadata.SelectSingleNode("*[local-name()='id']")
        $versionNode = $metadata.SelectSingleNode("*[local-name()='version']")
        if ($null -eq $idNode -or $null -eq $versionNode) {
            throw "Package '$Path' has no nuspec id or version."
        }
        [pscustomobject]@{
            Id = [string]$idNode.InnerText
            Version = [string]$versionNode.InnerText
            Path = $Path
        }
    }
    finally {
        $archive.Dispose()
    }
}

function Invoke-DotNet([string[]] $Arguments, [string] $WorkingDirectory) {
    Push-Location $WorkingDirectory
    try {
        & dotnet @Arguments
        if ($LASTEXITCODE -ne 0) {
            throw "dotnet $($Arguments -join ' ') failed with exit code $LASTEXITCODE."
        }
    }
    finally {
        Pop-Location
    }
}

$repository = Split-Path -Parent $PSScriptRoot
$surfacePath = [IO.Path]::GetFullPath((Join-Path (Get-Location) $ProductSurfacePath))
$feedPath = [IO.Path]::GetFullPath((Join-Path (Get-Location) $FeedDirectory))
$manifestPath = [IO.Path]::GetFullPath((Join-Path (Get-Location) $HashManifestPath))

if (-not (Test-Path -LiteralPath $surfacePath -PathType Leaf)) {
    throw "Product surface '$surfacePath' does not exist."
}
if (-not (Test-Path -LiteralPath $feedPath -PathType Container)) {
    throw "Package feed '$feedPath' does not exist."
}

$surface = Get-Content -LiteralPath $surfacePath -Raw | ConvertFrom-Json
$releaseTrain = [string]$surface.releaseTrain
if ([string]::IsNullOrWhiteSpace($releaseTrain)) {
    throw 'The product surface does not declare releaseTrain.'
}
$versionManifestFile = [IO.Path]::GetFullPath((Join-Path (Get-Location) $VersionManifestPath))
if (-not (Test-Path -LiteralPath $versionManifestFile -PathType Leaf)) {
    throw "Version manifest '$versionManifestFile' does not exist."
}
$versionManifest = Get-Content -LiteralPath $versionManifestFile -Raw | ConvertFrom-Json -AsHashtable
if ($versionManifest.Count -eq 0) {
    throw 'The version manifest is empty.'
}
foreach ($entry in $versionManifest.GetEnumerator()) {
    if ([string]$entry.Value -notmatch ('^' + [Regex]::Escape($releaseTrain) + '\.[0-9]+$')) {
        throw "Package '$($entry.Key)' has version '$($entry.Value)', which is not part of release train '$releaseTrain'."
    }
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
$trainPackages = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
foreach ($packageId in $releaseIds) {
    if (-not $trainPackages.Add([string]$packageId)) { throw "Duplicate package '$packageId'." }
}
if (-not $trainPackages.Contains('Sylin.Koan.App')) {
    throw 'The package train does not contain Sylin.Koan.App.'
}

# The consumer fixture references exactly one package (asserted below), so it needs only that
# package's version. Its transitive Koan closure is pinned by the bounded dependency ranges each
# package carries, and every resolved version is checked against the manifest after restore.
$appVersion = [string]$versionManifest['Sylin.Koan.App']
if ([string]::IsNullOrWhiteSpace($appVersion)) {
    throw 'The version manifest does not contain Sylin.Koan.App.'
}

$nupkgs = @(Get-ChildItem -LiteralPath $feedPath -File -Filter *.nupkg)
if ($nupkgs.Count -ne $releaseIds.Count) {
    throw "Feed contains $($nupkgs.Count) NuGet packages; the inventory requires $($releaseIds.Count)."
}

$identities = @($nupkgs | ForEach-Object { Read-PackageIdentity $_.FullName })
foreach ($group in ($identities | Group-Object Id)) {
    if (-not $trainPackages.Contains($group.Name)) {
        throw "Feed contains package '$($group.Name)' that is absent from the inventory."
    }
    if ($group.Count -ne 1) {
        throw "Feed contains $($group.Count) packages for '$($group.Name)'; expected exactly one."
    }
    $expected = [string]$versionManifest[$group.Name]
    if ([string]::IsNullOrWhiteSpace($expected)) {
        throw "Feed contains package '$($group.Name)' that is absent from the version manifest."
    }
    if ($group.Group[0].Version -ne $expected) {
        throw "Package '$($group.Name)' has version '$($group.Group[0].Version)', expected '$expected'."
    }
}
foreach ($packageId in $releaseIds) {
    if (-not ($identities.Id -contains $packageId)) {
        throw "Package '$packageId' is missing from the feed."
    }
}

$fixture = Join-Path $repository 'tests/PackageConsumers/AppJson'
if (-not (Test-Path -LiteralPath $fixture -PathType Container)) {
    throw "Package consumer fixture '$fixture' does not exist."
}
[xml]$consumerProject = Get-Content -LiteralPath (Join-Path $fixture 'PackageConsumer.csproj') -Raw
$references = @($consumerProject.Project.ItemGroup.PackageReference)
if ($references.Count -ne 1 -or [string]$references[0].Include -ne 'Sylin.Koan.App') {
    throw 'The package consumer must reference exactly Sylin.Koan.App and no other NuGet package.'
}
if (Get-ChildItem -LiteralPath $fixture -Recurse -File | Select-String -SimpleMatch '<ProjectReference') {
    throw 'The package consumer must not contain ProjectReference items.'
}

$tempRoot = Join-Path ([IO.Path]::GetTempPath()) "koan-package-consumer-$([Guid]::NewGuid().ToString('N'))"
$consumerRoot = Join-Path $tempRoot 'app'
$packagesRoot = Join-Path $tempRoot 'packages'
$dataRoot = Join-Path $tempRoot 'workspace'
New-Item -ItemType Directory -Path $consumerRoot, $packagesRoot, $dataRoot -Force | Out-Null

try {
    Get-ChildItem -LiteralPath $fixture -Force |
        Copy-Item -Destination $consumerRoot -Recurse -Force
    $feedForXml = [Security.SecurityElement]::Escape($feedPath)
    $nugetConfig = @"
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="koan-release" value="$feedForXml" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" protocolVersion="3" />
  </packageSources>
  <packageSourceMapping>
    <packageSource key="koan-release">
      <package pattern="Sylin.Koan" />
      <package pattern="Sylin.Koan.*" />
    </packageSource>
    <packageSource key="nuget.org">
      <package pattern="*" />
    </packageSource>
  </packageSourceMapping>
</configuration>
"@
    Set-Content -LiteralPath (Join-Path $consumerRoot 'NuGet.config') -Value $nugetConfig -Encoding utf8

    $project = Join-Path $consumerRoot 'PackageConsumer.csproj'
    Invoke-DotNet @(
        'restore', $project,
        '--configfile', (Join-Path $consumerRoot 'NuGet.config'),
        '--packages', $packagesRoot,
        '--force-evaluate',
        "--property:KoanTrainVersion=$appVersion",
        '--nologo'
    ) $consumerRoot
    Invoke-DotNet @(
        'restore', $project,
        '--configfile', (Join-Path $consumerRoot 'NuGet.config'),
        '--packages', $packagesRoot,
        '--locked-mode',
        "--property:KoanTrainVersion=$appVersion",
        '--nologo'
    ) $consumerRoot
    Invoke-DotNet @(
        'build', $project,
        '--configuration', 'Release',
        '--no-restore',
        "--property:KoanTrainVersion=$appVersion",
        '--nologo'
    ) $consumerRoot

    $assetsPath = Join-Path $consumerRoot 'obj/project.assets.json'
    $assets = Get-Content -LiteralPath $assetsPath -Raw | ConvertFrom-Json -AsHashtable
    $resolvedKoan = @(
        foreach ($entry in $assets.libraries.GetEnumerator()) {
            $parts = $entry.Key -split '/', 2
            $id = [string]$parts[0]
            if ($id -eq 'Sylin.Koan' -or $id.StartsWith('Sylin.Koan.', [StringComparison]::OrdinalIgnoreCase)) {
                if ([string]$entry.Value.type -ne 'package') {
                    throw "Resolved Koan dependency '$($entry.Key)' is not a NuGet package."
                }
                $expectedResolved = [string]$versionManifest[$id]
                if ([string]::IsNullOrWhiteSpace($expectedResolved)) {
                    throw "Resolved Koan dependency '$id' is absent from the version manifest."
                }
                if ($parts[1] -ne $expectedResolved) {
                    throw "Resolved Koan dependency '$id' has version '$($parts[1])', expected '$expectedResolved'."
                }
                if (-not $trainPackages.Contains($id)) {
                    throw "Resolved Koan dependency '$id' is not in the package train."
                }
                $id
            }
        }
    )
    if ($resolvedKoan -notcontains 'Sylin.Koan.App') {
        throw 'The clean consumer did not resolve Sylin.Koan.App.'
    }

    $assembly = Join-Path $consumerRoot 'bin/Release/net10.0/Koan.PackageConsumer.AppJson.dll'
    $output = & dotnet $assembly $dataRoot
    if ($LASTEXITCODE -ne 0) { throw "Package consumer exited with code $LASTEXITCODE." }
    if (($output -join [Environment]::NewLine) -notmatch 'PACKAGE-CONSUMER\|APP-JSON\|PASS') {
        throw 'Package consumer did not report success.'
    }
}
finally {
    if ($KeepConsumer) {
        Write-Host "PACKAGE-CONSUMER|KEPT|$tempRoot"
    }
    elseif (Test-Path -LiteralPath $tempRoot) {
        Remove-Item -LiteralPath $tempRoot -Recurse -Force
    }
}

$manifestDirectory = Split-Path -Parent $manifestPath
New-Item -ItemType Directory -Path $manifestDirectory -Force | Out-Null
$publisherPath = Join-Path $manifestDirectory 'publish-package-train.ps1'
if (-not (Test-Path -LiteralPath $publisherPath -PathType Leaf)) {
    throw "Checked publisher '$publisherPath' is missing from the release artifact."
}
$artifactFiles = @(
    Get-Item -LiteralPath $surfacePath
    # The version manifest decides which identity each certified package is published under, so it
    # is a publication input and must be covered by the same hash gate as the packages themselves.
    Get-Item -LiteralPath $versionManifestFile
    Get-Item -LiteralPath $publisherPath
    Get-ChildItem -LiteralPath $feedPath -File |
        Where-Object { $_.Name -match '\.(s)?nupkg$' }
) | Sort-Object FullName
$hashLines = @(
    foreach ($file in $artifactFiles) {
        $relative = [IO.Path]::GetRelativePath($manifestDirectory, $file.FullName).Replace('\', '/')
        $hash = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash
        "$hash  $relative"
    }
)
Set-Content -LiteralPath $manifestPath -Value $hashLines -Encoding ascii

$distinctVerified = @($versionManifest.Values | Sort-Object -Unique)
Write-Host "PACKAGE-TRAIN|VERIFIED|$($releaseIds.Count)|train=$releaseTrain|distinct-versions=$($distinctVerified.Count)"
