<#
.SYNOPSIS
    One-shot migration of all src/ csproj files to ARCH-0082 two-tier versioning.

.DESCRIPTION
    For every packable csproj under src/:
      - Removes existing <Version>, <AssemblyVersion>, <FileVersion> lines
        (these are now managed centrally via build/versions.props)
      - Adds <KoanPackageKind>Kernel|Periphery</KoanPackageKind> to the first
        PropertyGroup based on build/kernel-manifest.txt

    Non-packable projects (samples, tests, IsPackable=false) are left alone.

    This script is idempotent — re-running it produces the same result. Safe
    to run after merge conflicts, and reviewable as a diff against pre-migration.

.PARAMETER DryRun
    Print the planned changes per csproj without writing.
#>

[CmdletBinding()]
param(
    [switch]$DryRun
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$RepoRoot = Resolve-Path (Join-Path $PSScriptRoot '..' '..')
Set-Location $RepoRoot

$KernelManifestPath = Join-Path $RepoRoot 'build/kernel-manifest.txt'
if (-not (Test-Path $KernelManifestPath)) {
    throw "Kernel manifest not found: $KernelManifestPath"
}
$KernelPaths = Get-Content $KernelManifestPath |
    Where-Object { $_ -and -not $_.StartsWith('#') } |
    ForEach-Object { $_.Trim() -replace '\\', '/' }

function Test-Packable {
    param([string]$Content)
    if ($Content -match '<IsPackable>\s*false\s*</IsPackable>') { return $false }
    return $true
}

function Update-Csproj {
    param(
        [string]$FullPath,
        [string]$Kind  # 'Kernel' or 'Periphery'
    )

    $content = Get-Content $FullPath -Raw
    $original = $content

    # 1. Remove the centrally-managed version elements wherever they live in the file.
    #    Match the element with whitespace and remove the line entirely (leading WS + element + trailing newline).
    $content = $content -replace '(?m)^\s*<Version>[^<]*</Version>\s*\r?\n', ''
    $content = $content -replace '(?m)^\s*<AssemblyVersion>[^<]*</AssemblyVersion>\s*\r?\n', ''
    $content = $content -replace '(?m)^\s*<FileVersion>[^<]*</FileVersion>\s*\r?\n', ''

    # 2. Add KoanPackageKind to the first PropertyGroup, unless already present.
    if ($content -match '<KoanPackageKind>') {
        # Already migrated — just update the kind if it differs.
        $content = $content -replace '<KoanPackageKind>[^<]*</KoanPackageKind>', "<KoanPackageKind>$Kind</KoanPackageKind>"
    } else {
        # Insert after the first opening <PropertyGroup> tag (any leading whitespace).
        # Match the line containing <PropertyGroup> (with optional attributes) and append a new line right after it.
        if ($content -notmatch '(?m)^(\s*)<PropertyGroup(?:\s[^>]*)?>') {
            throw "No <PropertyGroup> found in $FullPath — cannot insert KoanPackageKind"
        }
        $indentMatch = [regex]::Match($content, '(?m)^(\s*)<PropertyGroup(?:\s[^>]*)?>')
        $indent = $indentMatch.Groups[1].Value + '  '  # one nesting level deeper
        $insertion = "$indent<KoanPackageKind>$Kind</KoanPackageKind>"
        $content = [regex]::Replace(
            $content,
            '(?m)(^\s*<PropertyGroup(?:\s[^>]*)?>\s*\r?\n)',
            "`$1$insertion`r`n",
            [System.Text.RegularExpressions.RegexOptions]::None,
            [TimeSpan]::FromSeconds(5)
        )
    }

    if ($content -eq $original) { return $false }

    if (-not $DryRun) {
        Set-Content -Path $FullPath -Value $content -NoNewline -Encoding UTF8
    }
    return $true
}

# ── Discover and process ─────────────────────────────────────────────────────
$csprojs = Get-ChildItem -Path (Join-Path $RepoRoot 'src') -Filter '*.csproj' -Recurse
$kernelChanged = @()
$peripheryChanged = @()
$skipped = @()

foreach ($f in $csprojs) {
    $content = Get-Content $f.FullName -Raw
    if (-not (Test-Packable -Content $content)) {
        $skipped += $f.FullName
        continue
    }

    $relPath = (Resolve-Path $f.FullName -Relative) -replace '\\', '/' -replace '^\./', ''
    $isKernel = $KernelPaths -contains $relPath
    $kind = if ($isKernel) { 'Kernel' } else { 'Periphery' }

    $changed = Update-Csproj -FullPath $f.FullName -Kind $kind
    if ($changed) {
        if ($isKernel) { $kernelChanged += $relPath } else { $peripheryChanged += $relPath }
    }
}

# ── Report ───────────────────────────────────────────────────────────────────
Write-Host "Migration $(if ($DryRun) { '(dry run)' } else { 'applied' }):" -ForegroundColor Cyan
Write-Host "  Kernel csprojs updated   : $($kernelChanged.Count)"
Write-Host "  Periphery csprojs updated: $($peripheryChanged.Count)"
Write-Host "  Non-packable skipped     : $($skipped.Count)"
Write-Host ""

if ($VerbosePreference -eq 'Continue') {
    if ($kernelChanged) {
        Write-Verbose "Kernel:"
        $kernelChanged | ForEach-Object { Write-Verbose "  $_" }
    }
    if ($peripheryChanged) {
        Write-Verbose "Periphery:"
        $peripheryChanged | ForEach-Object { Write-Verbose "  $_" }
    }
}

if ($DryRun) {
    Write-Host "DRY RUN — no files were modified." -ForegroundColor Yellow
}
