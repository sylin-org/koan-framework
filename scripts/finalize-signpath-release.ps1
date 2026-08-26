[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $PlanPath,

    [Parameter(Mandatory)]
    [string] $FeedDirectory,

    [Parameter(Mandatory)]
    [string] $SignedDirectory
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Get-AbsolutePath([string] $Path) {
    if ([IO.Path]::IsPathFullyQualified($Path)) { return [IO.Path]::GetFullPath($Path) }
    return [IO.Path]::GetFullPath((Join-Path (Get-Location) $Path))
}

$planFile = Get-AbsolutePath $PlanPath
$feedPath = Get-AbsolutePath $FeedDirectory
$signedPath = Get-AbsolutePath $SignedDirectory

if (-not (Test-Path -LiteralPath $planFile -PathType Leaf)) {
    throw "Release plan '$planFile' does not exist."
}
if (-not (Test-Path -LiteralPath $feedPath -PathType Container)) {
    throw "Package feed '$feedPath' does not exist."
}
if (-not (Test-Path -LiteralPath $signedPath -PathType Container)) {
    throw "Signed package directory '$signedPath' does not exist."
}

$plan = Get-Content -LiteralPath $planFile -Raw | ConvertFrom-Json
$selected = @($plan.packages | Where-Object { $_.publish })
if ($selected.Count -eq 0) {
    throw 'SignPath finalization was requested for an empty release plan.'
}

$expected = [Collections.Generic.Dictionary[string, object]]::new([StringComparer]::OrdinalIgnoreCase)
foreach ($package in $selected) {
    $name = "$($package.packageId).$($package.version).nupkg"
    $expected[$name] = $package
}

$signedPackages = @(Get-ChildItem -LiteralPath $signedPath -Recurse -File -Filter *.nupkg)
if ($signedPackages.Count -ne $expected.Count) {
    throw "SignPath returned $($signedPackages.Count) primary packages; the plan requires $($expected.Count)."
}

foreach ($signedPackage in $signedPackages) {
    if (-not $expected.ContainsKey($signedPackage.Name)) {
        throw "SignPath returned unplanned package '$($signedPackage.Name)'."
    }

    $unsignedPackage = Join-Path $feedPath $signedPackage.Name
    if (-not (Test-Path -LiteralPath $unsignedPackage -PathType Leaf)) {
        throw "Unsigned feed is missing planned package '$($signedPackage.Name)'."
    }

    $unsignedHash = (Get-FileHash -LiteralPath $unsignedPackage -Algorithm SHA256).Hash
    $signedHash = (Get-FileHash -LiteralPath $signedPackage.FullName -Algorithm SHA256).Hash
    if ($unsignedHash -eq $signedHash) {
        throw "SignPath returned '$($signedPackage.Name)' without changing its bytes."
    }

    $verificationOutput = @(& dotnet nuget verify --all --verbosity detailed $signedPackage.FullName 2>&1)
    $verificationExitCode = $LASTEXITCODE
    $verificationOutput | ForEach-Object { Write-Host $_ }
    $verified = ($verificationOutput -join [Environment]::NewLine) -match 'Successfully verified package'
    if ($verificationExitCode -ne 0 -or -not $verified) {
        throw "NuGet signature verification failed for '$($signedPackage.Name)'."
    }

    Copy-Item -LiteralPath $signedPackage.FullName -Destination $unsignedPackage -Force
    Write-Host "SIGNPATH|FINALIZED|$($signedPackage.Name)"
}

$finalPackages = @(Get-ChildItem -LiteralPath $feedPath -File -Filter *.nupkg)
foreach ($name in $expected.Keys) {
    if ($finalPackages.Name -notcontains $name) { throw "Final feed is missing '$name'." }
}
foreach ($package in $finalPackages) {
    if (-not $expected.ContainsKey($package.Name)) { throw "Final feed contains unplanned package '$($package.Name)'." }
}

Write-Host "SIGNPATH|OK|packages=$($expected.Count)"
