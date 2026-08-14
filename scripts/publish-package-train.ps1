[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $ProductSurfacePath,

    [Parameter(Mandatory)]
    [string] $FeedDirectory,

    [Parameter(Mandatory)]
    [string] $ExpectedVersion,

    [switch] $PlanOnly
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Resolve-PublicationOrder($Surface) {
    $packages = @($Surface.packages)
    if ($packages.Count -eq 0) { throw 'The certified product surface contains no packages.' }

    $byId = [Collections.Generic.Dictionary[string, object]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($package in $packages) {
        $id = [string]$package.packageId
        if ([string]::IsNullOrWhiteSpace($id) -or -not $byId.TryAdd($id, $package)) {
            throw "Missing or duplicate package ID '$id' in the certified product surface."
        }
    }

    $remaining = [Collections.Generic.Dictionary[string, Collections.Generic.HashSet[string]]]::new(
        [StringComparer]::OrdinalIgnoreCase)
    foreach ($package in $packages) {
        $id = [string]$package.packageId
        $dependencies = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
        foreach ($dependencyValue in @($package.dependencies)) {
            $dependency = [string]$dependencyValue
            if (-not $byId.ContainsKey($dependency)) {
                throw "Package '$id' depends on missing train package '$dependency'."
            }
            if (-not $dependencies.Add($dependency)) {
                throw "Package '$id' repeats dependency '$dependency'."
            }
        }
        $remaining.Add($id, $dependencies)
    }

    $order = [Collections.Generic.List[string]]::new()
    while ($remaining.Count -gt 0) {
        $ready = @($remaining.GetEnumerator() |
            Where-Object { $_.Value.Count -eq 0 } |
            ForEach-Object Key |
            Sort-Object)
        if ($ready.Count -eq 0) {
            $blocked = @($remaining.GetEnumerator() |
                Sort-Object Key |
                ForEach-Object { "$($_.Key) -> $($_.Value -join ', ')" })
            throw "Package dependency cycle detected: $($blocked -join '; ')"
        }
        foreach ($id in $ready) {
            $order.Add($id)
            $null = $remaining.Remove($id)
            foreach ($dependencies in $remaining.Values) { $null = $dependencies.Remove($id) }
        }
    }

    return ,$order
}

function Test-NuGetPackageExists(
    [Net.Http.HttpClient] $Client,
    [string] $PackageId,
    [string] $Version) {
    $id = $PackageId.ToLowerInvariant()
    $versionValue = $Version.ToLowerInvariant()
    $uri = "https://api.nuget.org/v3-flatcontainer/$id/$versionValue/$id.$versionValue.nupkg"
    $request = [Net.Http.HttpRequestMessage]::new([Net.Http.HttpMethod]::Head, $uri)
    try {
        $response = $Client.SendAsync($request).GetAwaiter().GetResult()
    }
    finally {
        $request.Dispose()
    }
    try {
        $status = [int]$response.StatusCode
        if ($status -eq 200) { return $true }
        if ($status -eq 404) { return $false }
        throw "NuGet package lookup for '$PackageId' returned HTTP $status."
    }
    finally {
        $response.Dispose()
    }
}

function Get-RemotePackage(
    [Net.Http.HttpClient] $Client,
    [string] $PackageId,
    [string] $Version,
    [string] $Destination) {
    $id = $PackageId.ToLowerInvariant()
    $versionValue = $Version.ToLowerInvariant()
    $uri = "https://api.nuget.org/v3-flatcontainer/$id/$versionValue/$id.$versionValue.nupkg"
    $response = $Client.GetAsync($uri).GetAwaiter().GetResult()
    try {
        $status = [int]$response.StatusCode
        if ($status -eq 404) { return $false }
        if ($status -ne 200) { throw "NuGet package lookup for '$PackageId' returned HTTP $status." }
        $bytes = $response.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult()
        [IO.File]::WriteAllBytes($Destination, $bytes)
        return $true
    }
    finally {
        $response.Dispose()
    }
}

function Get-ZipContentIndex([string] $Path, [bool] $AllowRepositorySignature) {
    $archive = [IO.Compression.ZipFile]::OpenRead($Path)
    try {
        $entries = [Collections.Generic.Dictionary[string, object]]::new([StringComparer]::Ordinal)
        $signatureEntries = 0
        foreach ($entry in $archive.Entries) {
            if ([string]::Equals($entry.FullName, '.signature.p7s', [StringComparison]::Ordinal)) {
                $signatureEntries++
                if (-not $AllowRepositorySignature) {
                    throw "Certified package '$Path' unexpectedly contains .signature.p7s."
                }
                continue
            }
            if ($entries.ContainsKey($entry.FullName)) {
                throw "Package '$Path' repeats ZIP entry '$($entry.FullName)'."
            }
            $stream = $entry.Open()
            $algorithm = [Security.Cryptography.SHA256]::Create()
            try { $hash = [Convert]::ToBase64String($algorithm.ComputeHash($stream)) }
            finally { $algorithm.Dispose(); $stream.Dispose() }
            $entries.Add($entry.FullName, [pscustomobject]@{
                Length = $entry.Length
                Hash = $hash
            })
        }
        if ($signatureEntries -gt 1) {
            throw "Remote package '$Path' contains $signatureEntries repository-signature entries."
        }
        return ,$entries
    }
    finally {
        $archive.Dispose()
    }
}

function Assert-EquivalentPackageContent([string] $LocalPath, [string] $RemotePath) {
    $local = Get-ZipContentIndex $LocalPath $false
    $remote = Get-ZipContentIndex $RemotePath $true
    if ($local.Count -ne $remote.Count) {
        throw "Remote package content differs from '$LocalPath' (entry count $($remote.Count), expected $($local.Count))."
    }
    foreach ($entry in $local.GetEnumerator()) {
        $remoteEntry = $null
        if (-not $remote.TryGetValue($entry.Key, [ref]$remoteEntry)) {
            throw "Remote package is missing certified entry '$($entry.Key)'."
        }
        if ($entry.Value.Length -ne $remoteEntry.Length -or
            -not [string]::Equals($entry.Value.Hash, $remoteEntry.Hash, [StringComparison]::Ordinal)) {
            throw "Remote package entry '$($entry.Key)' differs from the certified bytes."
        }
    }
}

function Invoke-NuGetPush(
    [string] $Path,
    [bool] $IsSymbol,
    [bool] $AllowDuplicate) {
    $arguments = [Collections.Generic.List[string]]::new()
    foreach ($argument in @(
        'nuget', 'push', $Path,
        '--source', 'https://api.nuget.org/v3/index.json',
        '--api-key', $env:NUGET_API_KEY)) {
        $arguments.Add($argument)
    }
    if (-not $IsSymbol) { $arguments.Add('--no-symbols') }
    if ($AllowDuplicate) { $arguments.Add('--skip-duplicate') }

    & dotnet @arguments
    if ($LASTEXITCODE -ne 0) {
        throw "NuGet push failed for '$([IO.Path]::GetFileName($Path))'."
    }
}

$surfacePath = [IO.Path]::GetFullPath((Join-Path (Get-Location) $ProductSurfacePath))
$feedPath = [IO.Path]::GetFullPath((Join-Path (Get-Location) $FeedDirectory))
if (-not (Test-Path -LiteralPath $surfacePath -PathType Leaf)) {
    throw "Certified product surface '$surfacePath' does not exist."
}
if (-not (Test-Path -LiteralPath $feedPath -PathType Container)) {
    throw "Certified package feed '$feedPath' does not exist."
}

$surface = Get-Content -LiteralPath $surfacePath -Raw | ConvertFrom-Json
$order = Resolve-PublicationOrder $surface
$primaryPaths = [Collections.Generic.Dictionary[string, string]]::new([StringComparer]::OrdinalIgnoreCase)
$symbolPaths = [Collections.Generic.Dictionary[string, string]]::new([StringComparer]::OrdinalIgnoreCase)
$expectedPrimaryPaths = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
$allowedSymbolPaths = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
foreach ($id in $order) {
    $primaryPath = [IO.Path]::GetFullPath((Join-Path $feedPath "$id.$ExpectedVersion.nupkg"))
    if (-not (Test-Path -LiteralPath $primaryPath -PathType Leaf)) {
        throw "Certified package '$id' is missing at '$primaryPath'."
    }
    $primaryPaths.Add($id, $primaryPath)
    $null = $expectedPrimaryPaths.Add($primaryPath)

    $symbolPath = [IO.Path]::GetFullPath((Join-Path $feedPath "$id.$ExpectedVersion.snupkg"))
    $null = $allowedSymbolPaths.Add($symbolPath)
    if (Test-Path -LiteralPath $symbolPath -PathType Leaf) {
        $symbolPaths.Add($id, $symbolPath)
    }
}
$actualPackages = @(Get-ChildItem -LiteralPath $feedPath -File -Filter *.nupkg)
if ($actualPackages.Count -ne $expectedPrimaryPaths.Count) {
    throw "Certified feed contains $($actualPackages.Count) primary packages; expected $($expectedPrimaryPaths.Count)."
}
foreach ($package in $actualPackages) {
    if (-not $expectedPrimaryPaths.Contains($package.FullName)) {
        throw "Certified feed contains unexpected primary package '$($package.Name)'."
    }
}
$actualSymbols = @(Get-ChildItem -LiteralPath $feedPath -File -Filter *.snupkg)
foreach ($symbol in $actualSymbols) {
    if (-not $allowedSymbolPaths.Contains($symbol.FullName)) {
        throw "Certified feed contains unexpected symbol package '$($symbol.Name)'."
    }
}

Write-Host "PUBLISH-ORDER|$($order.Count)|$($order -join ' -> ')"
if ($PlanOnly) { return }
if ([string]::IsNullOrWhiteSpace($env:NUGET_API_KEY)) {
    throw 'NUGET_API_KEY is required to publish the certified train.'
}

$runAttempt = 1
$runAttemptValue = [Environment]::GetEnvironmentVariable('GITHUB_RUN_ATTEMPT')
if (-not [string]::IsNullOrWhiteSpace($runAttemptValue) -and
    (-not [int]::TryParse($runAttemptValue, [ref]$runAttempt) -or $runAttempt -lt 1)) {
    throw "GITHUB_RUN_ATTEMPT '$runAttemptValue' is not a positive integer."
}
$isRecovery = $runAttempt -gt 1

$client = [Net.Http.HttpClient]::new()
$client.Timeout = [TimeSpan]::FromMinutes(2)
try {
    if (-not $isRecovery) {
        $collisions = [Collections.Generic.List[string]]::new()
        foreach ($id in $order) {
            if (Test-NuGetPackageExists $client $id $ExpectedVersion) {
                $collisions.Add("$id.$ExpectedVersion")
            }
        }
        if ($collisions.Count -ne 0) {
            throw "NuGet already contains train identities: $($collisions -join ', '). No package was pushed."
        }
    }

    $remotePath = [IO.Path]::GetTempFileName()
    try {
        foreach ($id in $order) {
            $packagePath = $primaryPaths[$id]
            if ($isRecovery -and (Get-RemotePackage $client $id $ExpectedVersion $remotePath)) {
                Assert-EquivalentPackageContent $packagePath $remotePath
                Write-Host "PUBLISH|PRIMARY-VERIFIED|$id|$ExpectedVersion"
            }
            else {
                Write-Host "PUBLISH|PRIMARY-PUSH|$id|$ExpectedVersion"
                Invoke-NuGetPush $packagePath $false $false
            }
        }
    }
    finally {
        Remove-Item -LiteralPath $remotePath -Force -ErrorAction SilentlyContinue
    }

    foreach ($id in $order) {
        if ($symbolPaths.ContainsKey($id)) {
            $mode = if ($isRecovery) { 'RESUME' } else { 'PUSH' }
            Write-Host "PUBLISH|SYMBOL-$mode|$id|$ExpectedVersion"
            Invoke-NuGetPush $symbolPaths[$id] $true $isRecovery
        }
    }
}
finally {
    $client.Dispose()
}
