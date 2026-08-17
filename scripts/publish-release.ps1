[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $PlanPath,

    [Parameter(Mandatory)]
    [string] $FeedDirectory,

    [switch] $PlanOnly
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# Pushes exactly what the plan selected, in the dependency-first order the plan already resolved.
# No package version is ever republished, so there is nothing to compare against a remote copy and
# no recovery mode: --skip-duplicate makes a re-run of an interrupted push a no-op for whatever
# already landed.

$planFile = [IO.Path]::GetFullPath((Join-Path (Get-Location) $PlanPath))
$feedPath = [IO.Path]::GetFullPath((Join-Path (Get-Location) $FeedDirectory))

if (-not (Test-Path -LiteralPath $planFile -PathType Leaf)) {
    throw "Certified release plan '$planFile' does not exist."
}
if (-not (Test-Path -LiteralPath $feedPath -PathType Container)) {
    throw "Certified package feed '$feedPath' does not exist."
}

$plan = Get-Content -LiteralPath $planFile -Raw | ConvertFrom-Json
$selected = @(@($plan.packages) | Where-Object { $_.publish })

if ($selected.Count -eq 0) {
    Write-Host 'PUBLISH|NOTHING-TO-DO|no package changed since the published train'
    return
}

$pushes = [Collections.Generic.List[object]]::new()
foreach ($package in $selected) {
    $packageId = [string]$package.packageId
    $version = [string]$package.version
    $primary = Join-Path $feedPath "$packageId.$version.nupkg"
    if (-not (Test-Path -LiteralPath $primary -PathType Leaf)) {
        throw "Certified package '$packageId' is missing at '$primary'."
    }
    $symbol = Join-Path $feedPath "$packageId.$version.snupkg"
    $pushes.Add([pscustomobject]@{
        PackageId = $packageId
        Version = $version
        Primary = $primary
        Symbol = $(if (Test-Path -LiteralPath $symbol -PathType Leaf) { $symbol } else { $null })
    })
}

# The feed must contain nothing beyond what is being published.
$staged = @(Get-ChildItem -LiteralPath $feedPath -File | Where-Object { $_.Name -match '\.(s)?nupkg$' })
$allowed = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
foreach ($push in $pushes) {
    $null = $allowed.Add([IO.Path]::GetFileName($push.Primary))
    $null = $allowed.Add("$($push.PackageId).$($push.Version).snupkg")
}
foreach ($file in $staged) {
    if (-not $allowed.Contains($file.Name)) {
        throw "Certified feed contains unexpected artifact '$($file.Name)'."
    }
}

Write-Host "PUBLISH-ORDER|$($pushes.Count)|$(($pushes | ForEach-Object PackageId) -join ' -> ')"
if ($PlanOnly) { return }
if ([string]::IsNullOrWhiteSpace($env:NUGET_API_KEY)) {
    throw 'NUGET_API_KEY is required to publish.'
}

function Invoke-NuGetPush([string] $Path, [bool] $IsSymbol) {
    $arguments = [Collections.Generic.List[string]]::new()
    foreach ($argument in @(
        'nuget', 'push', $Path,
        '--source', 'https://api.nuget.org/v3/index.json',
        '--api-key', $env:NUGET_API_KEY,
        '--skip-duplicate')) {
        $arguments.Add($argument)
    }
    if (-not $IsSymbol) { $arguments.Add('--no-symbols') }

    & dotnet @arguments
    if ($LASTEXITCODE -ne 0) {
        throw "NuGet push failed for '$([IO.Path]::GetFileName($Path))'."
    }
}

foreach ($push in $pushes) {
    Write-Host "PUBLISH|PRIMARY|$($push.PackageId)|$($push.Version)"
    Invoke-NuGetPush $push.Primary $false
}
foreach ($push in $pushes) {
    if ($null -ne $push.Symbol) {
        Write-Host "PUBLISH|SYMBOL|$($push.PackageId)|$($push.Version)"
        Invoke-NuGetPush $push.Symbol $true
    }
}

Write-Host "PUBLISH|DONE|$($pushes.Count)"
