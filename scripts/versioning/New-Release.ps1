<#
.SYNOPSIS
    Finalizes a release: validates state, tags the repo, optionally pushes packages.
    Implements the release half of ARCH-0082.
    Operator's guide: docs/workbooks/nuget-publishing.md.

.DESCRIPTION
    Prerequisites — script will refuse to proceed if any are missing:
      - Clean working tree (no staged or modified files)
      - build/versions.props exists and is committed
      - The kernel version in versions.props matches -Version
      - No existing tag matches release/v<Version>
      - HEAD is on a release-eligible branch (default: main, dev, or feat/*)

    What it does:
      1. Validates the preconditions above
      2. Creates an annotated tag release/v<Version> at HEAD
      3. (optional, with -Push) pushes the tag to origin
      4. (optional, with -PublishNuGet) runs `dotnet pack` on every packable
         csproj and runs `dotnet nuget push` for each .nupkg. Requires
         NUGET_API_KEY env var.

    All optional steps are off by default — this script is safe to run as a
    "verify the release is shippable" check.

.PARAMETER Version
    The kernel version being released, in MAJOR.MINOR.PATCH form (e.g., 0.7.1).
    Must match $(KoanKernelVersion) in build/versions.props.

.PARAMETER Push
    Push the new tag to origin after creating it.

.PARAMETER PublishNuGet
    Run dotnet pack + dotnet nuget push for every packable csproj.
    Requires NUGET_API_KEY environment variable.

.PARAMETER NuGetSource
    NuGet feed URL. Default: https://api.nuget.org/v3/index.json.

.PARAMETER DryRun
    Print the planned actions without executing them.

.EXAMPLE
    pwsh scripts/versioning/New-Release.ps1 -Version 0.7.1 -DryRun
    # Verify preconditions; print plan; make no changes

.EXAMPLE
    pwsh scripts/versioning/New-Release.ps1 -Version 0.7.1 -Push
    # Create and push the tag

.EXAMPLE
    $env:NUGET_API_KEY = 'oy2...'
    pwsh scripts/versioning/New-Release.ps1 -Version 0.7.1 -Push -PublishNuGet
    # Full release: tag + push + NuGet
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$Version,

    [switch]$Push,

    [switch]$PublishNuGet,

    [string]$NuGetSource = 'https://api.nuget.org/v3/index.json',

    [switch]$DryRun
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$RepoRoot = Resolve-Path (Join-Path $PSScriptRoot '..' '..')
Set-Location $RepoRoot

$Tag = "release/v$Version"

Write-Host "Koan release $Version" -ForegroundColor Cyan
Write-Host "  Tag           : $Tag"
Write-Host "  Push          : $Push"
Write-Host "  Publish NuGet : $PublishNuGet"
Write-Host "  Dry run       : $DryRun"
Write-Host ""

# ── Precondition 1: clean working tree ────────────────────────────────────────
$status = git status --porcelain
if ($status) {
    Write-Host "Preconditions FAILED — working tree is not clean:" -ForegroundColor Red
    Write-Host $status
    throw "Commit or stash your changes before releasing."
}
Write-Host "  [OK] Working tree clean" -ForegroundColor Green

# ── Precondition 2: versions.props exists ─────────────────────────────────────
$VersionsPropsPath = Join-Path $RepoRoot 'build/versions.props'
if (-not (Test-Path $VersionsPropsPath)) {
    throw "build/versions.props not found. Run scripts/versioning/Update-Versions.ps1 first."
}
Write-Host "  [OK] build/versions.props present" -ForegroundColor Green

# ── Precondition 3: kernel version in versions.props matches ──────────────────
$xml = [xml](Get-Content $VersionsPropsPath -Raw)
$kernelNode = $xml.SelectSingleNode("//KoanKernelVersion")
if (-not $kernelNode) {
    throw "build/versions.props is missing <KoanKernelVersion>."
}
$kernelVersion = $kernelNode.InnerText.Trim()
if ($kernelVersion -ne $Version) {
    throw "Version mismatch: -Version $Version but versions.props has $kernelVersion. Update versions.props first (Update-Versions.ps1 -BumpKernel ...) or pass the matching -Version."
}
Write-Host "  [OK] Kernel version $kernelVersion matches release version" -ForegroundColor Green

# ── Precondition 4: tag doesn't already exist ────────────────────────────────
$existingTag = git tag --list $Tag
if ($existingTag) {
    throw "Tag $Tag already exists. Use a different version or delete the existing tag."
}
Write-Host "  [OK] Tag $Tag is available" -ForegroundColor Green

# ── Precondition 5: branch check (informational, not fatal) ──────────────────
$branch = (git rev-parse --abbrev-ref HEAD).Trim()
Write-Host "  Branch        : $branch"
if ($branch -notin @('main', 'dev') -and -not $branch.StartsWith('feat/') -and -not $branch.StartsWith('release/')) {
    Write-Host "  [WARN] Releasing from branch '$branch' — usually releases tag main or dev." -ForegroundColor Yellow
}

Write-Host ""

if ($DryRun) {
    Write-Host "DRY RUN — actions skipped. Re-run without -DryRun to execute." -ForegroundColor Yellow
    return
}

# ── Create the tag ────────────────────────────────────────────────────────────
$tagMessage = "Koan kernel v$Version

Periphery package versions captured in build/versions.props at this commit."
Write-Host "Creating tag $Tag ..." -ForegroundColor Cyan
git tag -a $Tag -m $tagMessage
if ($LASTEXITCODE -ne 0) { throw "git tag failed" }
Write-Host "  [OK] Tag created locally" -ForegroundColor Green

# ── Push tag ──────────────────────────────────────────────────────────────────
if ($Push) {
    Write-Host "Pushing tag to origin ..." -ForegroundColor Cyan
    git push origin $Tag
    if ($LASTEXITCODE -ne 0) { throw "git push failed" }
    Write-Host "  [OK] Tag pushed to origin" -ForegroundColor Green
}

# ── NuGet publish ─────────────────────────────────────────────────────────────
if ($PublishNuGet) {
    if (-not $env:NUGET_API_KEY) {
        throw "NUGET_API_KEY environment variable is not set. Required for -PublishNuGet."
    }

    Write-Host "Building Release artifacts ..." -ForegroundColor Cyan
    dotnet build Koan.sln -c Release --nologo "-p:NuGetAudit=false"
    if ($LASTEXITCODE -ne 0) { throw "Release build failed — refusing to publish." }

    Write-Host "Packing ..." -ForegroundColor Cyan
    $artifactsDir = Join-Path $RepoRoot 'artifacts/nuget'
    if (Test-Path $artifactsDir) { Remove-Item $artifactsDir -Recurse -Force }
    New-Item -ItemType Directory -Path $artifactsDir -Force | Out-Null

    dotnet pack Koan.sln -c Release --no-build --nologo `
        -p:PackageOutputPath=$artifactsDir `
        "-p:NuGetAudit=false"
    if ($LASTEXITCODE -ne 0) { throw "pack failed" }

    $nupkgs = Get-ChildItem -Path $artifactsDir -Filter '*.nupkg' -File
    Write-Host "Publishing $($nupkgs.Count) packages ..." -ForegroundColor Cyan
    foreach ($pkg in $nupkgs) {
        Write-Host "  → $($pkg.Name)"
        dotnet nuget push $pkg.FullName --source $NuGetSource --api-key $env:NUGET_API_KEY --skip-duplicate
        if ($LASTEXITCODE -ne 0) {
            Write-Host "  [FAIL] Push failed for $($pkg.Name) — continuing with remaining packages." -ForegroundColor Red
        }
    }
    Write-Host "  [OK] NuGet publish complete" -ForegroundColor Green
}

Write-Host ""
Write-Host "Release complete." -ForegroundColor Green
