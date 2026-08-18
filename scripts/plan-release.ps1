[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $InventoryPath,

    [Parameter(Mandatory)]
    [string] $OutputPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# The whole release reduces to one set difference: Nerdbank.GitVersioning knows what version each
# project is at, and nuget.org knows what already exists. A package whose version is already
# published did not change, so it is not rebuilt, not packed, and not pushed. Everything downstream
# consumes this plan; nothing else recomputes a version or decides what ships.

$repository = Split-Path -Parent $PSScriptRoot
$inventoryFile = [IO.Path]::GetFullPath((Join-Path (Get-Location) $InventoryPath))
$outputFile = [IO.Path]::GetFullPath((Join-Path (Get-Location) $OutputPath))

if (-not (Test-Path -LiteralPath $inventoryFile -PathType Leaf)) {
    throw "Package inventory '$inventoryFile' does not exist."
}

$trainFile = Join-Path $repository 'version.json'
if (-not (Test-Path -LiteralPath $trainFile -PathType Leaf)) {
    throw "Repository version file '$trainFile' does not exist."
}
$train = [string](Get-Content -LiteralPath $trainFile -Raw | ConvertFrom-Json).version
if ($train -notmatch '^[0-9]+\.[0-9]+$') {
    throw "Repository version.json declares '$train'; expected a major.minor compatibility train."
}

$inventory = @(Get-Content -LiteralPath $inventoryFile -Raw | ConvertFrom-Json)
if ($inventory.Count -eq 0) {
    throw 'The package inventory is empty.'
}

# Map every project file to the package it produces so ProjectReference edges become package edges.
# The inventory already excludes analyzer and ReferenceOutputAssembly=false references, so every
# remaining edge is a real runtime dependency.
$byProject = [Collections.Generic.Dictionary[string, string]]::new([StringComparer]::OrdinalIgnoreCase)
foreach ($entry in $inventory) {
    $full = [IO.Path]::GetFullPath((Join-Path $repository ([string]$entry.projectPath)))
    if (-not $byProject.TryAdd($full, [string]$entry.packageId)) {
        throw "Project '$($entry.projectPath)' produces more than one package."
    }
}

$records = [Collections.Generic.Dictionary[string, object]]::new([StringComparer]::OrdinalIgnoreCase)
$pending = [Collections.Generic.Dictionary[string, Collections.Generic.HashSet[string]]]::new(
    [StringComparer]::OrdinalIgnoreCase)
foreach ($entry in $inventory) {
    $packageId = [string]$entry.packageId
    $dependencies = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($reference in @($entry.projectReferences)) {
        $full = [IO.Path]::GetFullPath([string]$reference)
        $dependencyId = $null
        if ($byProject.TryGetValue($full, [ref]$dependencyId)) {
            $null = $dependencies.Add($dependencyId)
        }
    }
    if (-not $records.TryAdd($packageId, $entry)) {
        throw "Package ID '$packageId' appears more than once in the inventory."
    }
    $pending.Add($packageId, $dependencies)
}

# Dependency-first order, so a dependency is on the feed before anything that needs it.
$order = [Collections.Generic.List[string]]::new()
while ($pending.Count -gt 0) {
    $ready = @($pending.GetEnumerator() | Where-Object { $_.Value.Count -eq 0 } | ForEach-Object Key | Sort-Object)
    if ($ready.Count -eq 0) {
        $blocked = @($pending.GetEnumerator() | Sort-Object Key | ForEach-Object { "$($_.Key) -> $($_.Value -join ', ')" })
        throw "Package dependency cycle detected: $($blocked -join '; ')"
    }
    foreach ($id in $ready) {
        $order.Add($id)
        $null = $pending.Remove($id)
        foreach ($set in $pending.Values) { $null = $set.Remove($id) }
    }
}

function Test-PublishedVersion([Net.Http.HttpClient] $Client, [string] $PackageId, [string] $Version) {
    $id = $PackageId.ToLowerInvariant()
    $value = $Version.ToLowerInvariant()
    $uri = "https://api.nuget.org/v3-flatcontainer/$id/$value/$id.$value.nupkg"
    $request = [Net.Http.HttpRequestMessage]::new([Net.Http.HttpMethod]::Head, $uri)
    try { $response = $Client.SendAsync($request).GetAwaiter().GetResult() }
    finally { $request.Dispose() }
    try {
        $status = [int]$response.StatusCode
        if ($status -eq 200) { return $true }
        if ($status -eq 404) { return $false }
        throw "NuGet lookup for '$PackageId $Version' returned HTTP $status."
    }
    finally { $response.Dispose() }
}

$client = [Net.Http.HttpClient]::new()
$client.Timeout = [TimeSpan]::FromMinutes(2)
$packages = [Collections.Generic.List[object]]::new()
try {
    foreach ($packageId in $order) {
        $entry = $records[$packageId]
        $projectPath = [string]$entry.projectPath
        $projectDirectory = Split-Path -Parent ([IO.Path]::GetFullPath((Join-Path $repository $projectPath)))

        $version = (& dotnet nbgv get-version --public-release=true -v NuGetPackageVersion -p $projectDirectory 2>&1 |
            Select-Object -Last 1)
        if ($LASTEXITCODE -ne 0) {
            throw "Nerdbank.GitVersioning failed for '$packageId': $version"
        }
        $version = ([string]$version).Trim()
        if ($version -notmatch ('^' + [Regex]::Escape($train) + '\.[0-9]+$')) {
            throw "Package '$packageId' resolved version '$version', which is not on release train '$train'."
        }

        $published = Test-PublishedVersion $client $packageId $version
        # Whether a consumer can name this package in a PackageReference. Template and tool packages
        # carry no assembly to reference; the release proof injects only the referenceable ones.
        # MSBuild leaves PackageType empty for an ordinary library and labels only the special shapes,
        # so this excludes what cannot be referenced rather than requiring a label that is usually absent.
        $packageType = [string]$entry.packageType
        $referenceable = -not [bool]$entry.packAsTool -and $packageType -ne 'Template'

        $packages.Add([ordered]@{
            packageId = $packageId
            projectPath = $projectPath
            version = $version
            publish = (-not $published)
            referenceable = $referenceable
        })
        Write-Host ("PLAN|{0}|{1}|{2}" -f $packageId, $version, $(if ($published) { 'published' } else { 'new' }))
    }
}
finally {
    $client.Dispose()
}

$plan = [ordered]@{
    train = $train
    packages = $packages
}

New-Item -ItemType Directory -Path (Split-Path -Parent $outputFile) -Force | Out-Null
$plan | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $outputFile -Encoding utf8

$changed = @($packages | Where-Object { $_.publish })
Write-Host "RELEASE-PLAN|train=$train|inventory=$($packages.Count)|publish=$($changed.Count)"
foreach ($package in $changed) {
    Write-Host "RELEASE-PLAN|PUBLISH|$($package.packageId)|$($package.version)"
}
