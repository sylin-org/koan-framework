[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $PlanPath,

    [Parameter(Mandatory)]
    [string] $FeedDirectory,

    [switch] $KeepConsumer
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# Proves the release through the only thing that matters: a package-only application restoring and
# running against it. The staged feed holds just the changed packages, so the rest resolve from
# nuget.org. That mix IS what a developer gets, which the old all-local feed never exercised — a
# dependency range that excluded an already published package used to pass here and fail for them.

function Invoke-DotNet([string[]] $Arguments, [string] $WorkingDirectory) {
    Push-Location -LiteralPath $WorkingDirectory
    try {
        & dotnet @Arguments
        if ($LASTEXITCODE -ne 0) {
            throw "dotnet $($Arguments -join ' ') failed with exit code $LASTEXITCODE."
        }
    }
    finally { Pop-Location }
}

$repository = Split-Path -Parent $PSScriptRoot
$planFile = [IO.Path]::GetFullPath((Join-Path (Get-Location) $PlanPath))
$feedPath = [IO.Path]::GetFullPath((Join-Path (Get-Location) $FeedDirectory))

if (-not (Test-Path -LiteralPath $planFile -PathType Leaf)) {
    throw "Release plan '$planFile' does not exist."
}
if (-not (Test-Path -LiteralPath $feedPath -PathType Container)) {
    throw "Package feed '$feedPath' does not exist."
}

$plan = Get-Content -LiteralPath $planFile -Raw | ConvertFrom-Json
$planned = [Collections.Generic.Dictionary[string, string]]::new([StringComparer]::OrdinalIgnoreCase)
foreach ($package in @($plan.packages)) {
    $planned[[string]$package.packageId] = [string]$package.version
}
if (-not $planned.ContainsKey('Sylin.Koan.App')) {
    throw 'The release plan does not contain Sylin.Koan.App.'
}
$appVersion = $planned['Sylin.Koan.App']

$publishing = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
foreach ($package in @($plan.packages | Where-Object { $_.publish })) { $null = $publishing.Add([string]$package.packageId) }

# The feed must hold exactly the packages the plan selected: nothing missing, nothing extra.
$expectedFeed = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
foreach ($package in @($plan.packages | Where-Object { $_.publish })) {
    $null = $expectedFeed.Add("$($package.packageId).$($package.version).nupkg")
}
$actualFeed = @(Get-ChildItem -LiteralPath $feedPath -File -Filter *.nupkg | ForEach-Object Name)
foreach ($name in $actualFeed) {
    if (-not $expectedFeed.Contains($name)) { throw "Feed contains unplanned package '$name'." }
}
foreach ($name in $expectedFeed) {
    if ($actualFeed -notcontains $name) { throw "Feed is missing planned package '$name'." }
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
    Get-ChildItem -LiteralPath $fixture -Force | Copy-Item -Destination $consumerRoot -Recurse -Force

    # NuGet resolves the LOWEST version satisfying a range, so a changed dependency is not pulled in
    # merely by being on the feed: Sylin.Koan.App pins Core >= 1.0.0 and would restore the published
    # 1.0.0 while the release under proof sits unused beside it. Reference every publishable library
    # explicitly at its planned version so the proof exercises the set actually being shipped.
    $referenceable = @($plan.packages | Where-Object { $_.publish -and $_.referenceable })
    if ($referenceable.Count -gt 0) {
        $projectFile = Join-Path $consumerRoot 'PackageConsumer.csproj'
        $projectXml = Get-Content -LiteralPath $projectFile -Raw
        $injected = ($referenceable | ForEach-Object {
            "    <PackageReference Include=`"$($_.packageId)`" Version=`"[$($_.version)]`" />"
        }) -join "`n"
        # -replace takes no count; a single insertion needs Regex.Replace.
        $projectXml = [regex]::new('<ItemGroup>').Replace($projectXml, "<ItemGroup>`n$injected", 1)
        Set-Content -LiteralPath $projectFile -Value $projectXml -Encoding utf8
        Write-Host "VERIFY|EXERCISING|$(($referenceable | ForEach-Object packageId) -join ', ')"
    }
    $feedForXml = [Security.SecurityElement]::Escape($feedPath)

    # Koan packages may come from either source: the changed ones from the staged feed, the rest from
    # nuget.org. Everything else stays pinned to nuget.org.
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
      <!-- The Koan patterns must be repeated here, not left to "*". NuGet selects sources by the
           MOST SPECIFIC matching pattern, so listing Sylin.Koan.* only under the local feed makes
           nuget.org ineligible for Koan packages entirely — and an unchanged package is not in the
           local feed. Both sources must carry the specific patterns for the mix to resolve. -->
      <package pattern="Sylin.Koan" />
      <package pattern="Sylin.Koan.*" />
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
        'build', $project,
        '--configuration', 'Release',
        '--no-restore',
        "--property:KoanTrainVersion=$appVersion",
        '--nologo'
    ) $consumerRoot

    # Every Koan package the application actually resolved must be the version this release plans,
    # whether it came from the staged feed or from nuget.org.
    $assets = Get-Content -LiteralPath (Join-Path $consumerRoot 'obj/project.assets.json') -Raw |
        ConvertFrom-Json -AsHashtable
    $resolved = [Collections.Generic.List[string]]::new()
    foreach ($entry in $assets.libraries.GetEnumerator()) {
        $parts = $entry.Key -split '/', 2
        $id = [string]$parts[0]
        if ($id -ne 'Sylin.Koan' -and -not $id.StartsWith('Sylin.Koan.', [StringComparison]::OrdinalIgnoreCase)) {
            continue
        }
        if ([string]$entry.Value.type -ne 'package') {
            throw "Resolved Koan dependency '$($entry.Key)' is not a NuGet package."
        }
        if (-not $planned.ContainsKey($id)) {
            throw "Resolved Koan dependency '$id' is not part of the release inventory."
        }
        # A package this release publishes must resolve at exactly the version being published; it was
        # referenced explicitly above. Anything else may legitimately resolve lower -- that simply means
        # the closure did not need a newer one -- but it must still be a version of this train.
        if ($publishing.Contains($id)) {
            if ($parts[1] -ne $planned[$id]) {
                throw "Published package '$id' resolved as '$($parts[1])', expected '$($planned[$id])'."
            }
        }
        elseif ($parts[1] -notmatch ('^' + [Regex]::Escape([string]$plan.train) + '\.[0-9]+$')) {
            throw "Resolved Koan dependency '$id' has version '$($parts[1])', which is not on train '$($plan.train)'."
        }
        $resolved.Add($id)
    }
    if (-not $resolved.Contains('Sylin.Koan.App')) {
        throw 'The clean consumer did not resolve Sylin.Koan.App.'
    }

    $assembly = Join-Path $consumerRoot 'bin/Release/net10.0/Koan.PackageConsumer.AppJson.dll'
    $output = & dotnet $assembly $dataRoot
    if ($LASTEXITCODE -ne 0) { throw "Package consumer exited with code $LASTEXITCODE." }
    if (($output -join [Environment]::NewLine) -notmatch 'PACKAGE-CONSUMER\|APP-JSON\|PASS') {
        throw 'Package consumer did not report success.'
    }

    Write-Host "VERIFY|OK|resolved=$($resolved.Count)|staged=$($actualFeed.Count)"
}
finally {
    if ($KeepConsumer) {
        Write-Host "PACKAGE-CONSUMER|KEPT|$tempRoot"
    }
    elseif (Test-Path -LiteralPath $tempRoot) {
        Remove-Item -LiteralPath $tempRoot -Recurse -Force
    }
}
