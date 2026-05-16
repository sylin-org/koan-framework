<#
.SYNOPSIS
    Computes per-package versions and writes build/versions.props.
    Implements the operational half of ARCH-0082 two-tier versioning.

.DESCRIPTION
    Discovers every Koan NuGet-packable csproj under src/, classifies it
    against build/kernel-manifest.txt, and computes its next version:

      - Kernel packages: share a single $(KoanKernelVersion). Bumped only when
        explicitly requested via -BumpKernel. Otherwise carried forward from
        the last release tag (or from the current versions.props if no tag).

      - Periphery packages: each bumped independently based on conventional
        commits in its directory since the last release tag.
          feat!: or BREAKING CHANGE: -> major
          feat:                       -> minor
          everything else             -> patch
          no commits in folder        -> no bump (version stays)

    The script writes build/versions.props with one <PropertyGroup> per
    periphery package (conditioned on MSBuildProjectName + KoanPackageKind)
    plus the global KoanKernelVersion.

    -DryRun prints what would change without writing.

.PARAMETER BumpKernel
    Force a kernel-wide bump: major | minor | patch.
    Omit to keep the current kernel version unchanged.

.PARAMETER Reason
    Human-readable rationale for a kernel bump (recorded in the script's
    output and intended to be referenced from the commit message).
    Required when -BumpKernel is specified.

.PARAMETER SinceTag
    Override the "last release" reference. Defaults to the most recent
    tag matching `release/v*`. Use this for what-if scenarios.

.PARAMETER DryRun
    Print the computed result without writing build/versions.props.

.EXAMPLE
    pwsh scripts/versioning/Update-Versions.ps1 -DryRun
    # Show what would happen on a release right now

.EXAMPLE
    pwsh scripts/versioning/Update-Versions.ps1
    # Bump periphery packages with changes; keep kernel as-is

.EXAMPLE
    pwsh scripts/versioning/Update-Versions.ps1 -BumpKernel minor -Reason "ARCH-0083 contract change"
    # Bump kernel one minor step, also pick up periphery changes
#>

[CmdletBinding()]
param(
    [ValidateSet('major', 'minor', 'patch')]
    [string]$BumpKernel,

    [string]$Reason,

    [string]$SinceTag,

    [switch]$DryRun
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

# ── Repo root (script lives in scripts/versioning/) ───────────────────────────
$RepoRoot = Resolve-Path (Join-Path $PSScriptRoot '..' '..')
Set-Location $RepoRoot

# ── Load kernel manifest ──────────────────────────────────────────────────────
$KernelManifestPath = Join-Path $RepoRoot 'build/kernel-manifest.txt'
if (-not (Test-Path $KernelManifestPath)) {
    throw "Kernel manifest not found: $KernelManifestPath"
}
$KernelPaths = Get-Content $KernelManifestPath |
    Where-Object { $_ -and -not $_.StartsWith('#') } |
    ForEach-Object { $_.Trim() -replace '\\', '/' }

if ($BumpKernel -and -not $Reason) {
    throw "-BumpKernel requires -Reason (e.g., 'ARCH-0083 contract change')"
}

# ── Find the reference tag (last release) ─────────────────────────────────────
if (-not $SinceTag) {
    $SinceTag = git tag --list 'release/v*' --sort=-v:refname | Select-Object -First 1
}
$ReferenceCommit = if ($SinceTag) {
    git rev-parse "$SinceTag" 2>$null
} else {
    $null
}
$ReferenceLabel = if ($SinceTag) { $SinceTag } else { '(no prior release tag — comparing against initial state)' }

Write-Host "Koan version computation" -ForegroundColor Cyan
Write-Host "  Repo root        : $RepoRoot"
Write-Host "  Reference tag    : $ReferenceLabel"
Write-Host "  Kernel manifest  : $(($KernelPaths | Measure-Object).Count) packages"
Write-Host ""

# ── Read current kernel version from versions.props (or use a default) ───────
$VersionsPropsPath = Join-Path $RepoRoot 'build/versions.props'
$CurrentKernelVersion = $null
$CurrentPeripheryVersions = @{}

if (Test-Path $VersionsPropsPath) {
    $xml = [xml](Get-Content $VersionsPropsPath -Raw)
    $kernelNode = $xml.SelectSingleNode("//KoanKernelVersion")
    if ($kernelNode) {
        $CurrentKernelVersion = $kernelNode.InnerText.Trim()
    }
    # Each periphery package's PropertyGroup is conditioned on MSBuildProjectName.
    # Use XPath via SelectNodes for robust attribute-aware traversal (avoids
    # PowerShell strict-mode property-access errors on Conditionless groups).
    $pgNodes = $xml.SelectNodes("/Project/PropertyGroup[@Condition]")
    foreach ($pg in $pgNodes) {
        $condition = $pg.GetAttribute('Condition')
        if (-not $condition) { continue }
        if ($condition -match "MSBuildProjectName.*?==.*?'([^']+)'") {
            $projectName = $matches[1]
            $versionNode = $pg.SelectSingleNode('Version')
            if ($versionNode) {
                $CurrentPeripheryVersions[$projectName] = $versionNode.InnerText.Trim()
            }
        }
    }
}

if (-not $CurrentKernelVersion) {
    # Bootstrap: take the highest existing <Version> across kernel csprojs, or fall back.
    $bootstrapVersions = @()
    foreach ($p in $KernelPaths) {
        $full = Join-Path $RepoRoot $p
        if (Test-Path $full) {
            $csproj = [xml](Get-Content $full -Raw)
            $v = $csproj.SelectSingleNode("//Version")
            if ($v) { $bootstrapVersions += $v.InnerText.Trim() }
        }
    }
    if ($bootstrapVersions.Count -gt 0) {
        $CurrentKernelVersion = ($bootstrapVersions | Sort-Object -Descending {[Version]$_})[0]
    } else {
        $CurrentKernelVersion = '0.7.0'
    }
    Write-Host "  Bootstrapping kernel version: $CurrentKernelVersion" -ForegroundColor Yellow
}

# ── Helpers ───────────────────────────────────────────────────────────────────
function Get-NextVersion {
    param([string]$Current, [ValidateSet('major','minor','patch')]$Bump)
    $parts = $Current.Split('.')
    while ($parts.Count -lt 3) { $parts += '0' }
    [int]$maj = $parts[0]
    [int]$min = $parts[1]
    [int]$pat = $parts[2]
    switch ($Bump) {
        'major' { return "$($maj + 1).0.0" }
        'minor' { return "$maj.$($min + 1).0" }
        'patch' { return "$maj.$min.$($pat + 1)" }
    }
}

function Get-BumpMagnitudeFromCommits {
    param($CommitSubjects)
    $arr = @($CommitSubjects | Where-Object { $_ })
    if ($arr.Count -eq 0) { return $null }
    foreach ($subject in $arr) {
        if ($subject -match '!:|BREAKING CHANGE') { return 'major' }
    }
    foreach ($subject in $arr) {
        if ($subject -match '^feat(\([^)]+\))?:') { return 'minor' }
    }
    return 'patch'
}

function Get-CommitsForPath {
    param([string]$Path)
    if ($ReferenceCommit) {
        $range = "$ReferenceCommit..HEAD"
    } else {
        $range = "HEAD"
    }
    $commits = & git log $range --format='%s' -- $Path 2>$null
    if ($LASTEXITCODE -ne 0) { return ,@() }
    # Always return an array, even for 0 or 1 commits.
    return ,@($commits | Where-Object { $_ })
}

function Get-CsprojProjectName {
    param([string]$CsprojPath)
    [IO.Path]::GetFileNameWithoutExtension($CsprojPath)
}

function Get-IsPackable {
    param([string]$FullPath)
    $content = Get-Content $FullPath -Raw
    # Treat missing <IsPackable> as packable (SDK default for library projects).
    if ($content -match '<IsPackable>\s*false\s*</IsPackable>') { return $false }
    # Skip projects that explicitly opt out via IsPackable=false anywhere.
    return $true
}

# ── Discover all packable csprojs under src/ ──────────────────────────────────
$AllCsprojs = Get-ChildItem -Path (Join-Path $RepoRoot 'src') -Filter '*.csproj' -Recurse |
    Where-Object { Get-IsPackable -FullPath $_.FullName } |
    ForEach-Object {
        $relPath = (Resolve-Path $_.FullName -Relative) -replace '\\', '/' -replace '^\./', ''
        [PSCustomObject]@{
            ProjectName = Get-CsprojProjectName -CsprojPath $_.FullName
            FullPath    = $_.FullName
            RelPath     = $relPath
            FolderPath  = Split-Path $_.FullName -Parent
            FolderRel   = (Split-Path $relPath -Parent)
            IsKernel    = $KernelPaths -contains $relPath
        }
    }

Write-Host "  Discovered       : $($AllCsprojs.Count) packable projects under src/"
Write-Host "    Kernel         : $(($AllCsprojs | Where-Object IsKernel).Count)"
Write-Host "    Periphery      : $(($AllCsprojs | Where-Object { -not $_.IsKernel }).Count)"
Write-Host ""

# ── Compute kernel version ────────────────────────────────────────────────────
$NewKernelVersion = $CurrentKernelVersion
if ($BumpKernel) {
    $NewKernelVersion = Get-NextVersion -Current $CurrentKernelVersion -Bump $BumpKernel
    Write-Host "Kernel bump: $CurrentKernelVersion -> $NewKernelVersion ($BumpKernel)" -ForegroundColor Green
    Write-Host "  Reason: $Reason" -ForegroundColor Green
} else {
    Write-Host "Kernel: held at $CurrentKernelVersion (no -BumpKernel)" -ForegroundColor Gray
}
Write-Host ""

# ── Compute periphery versions ────────────────────────────────────────────────
$PeripheryReport = @()
foreach ($proj in $AllCsprojs | Where-Object { -not $_.IsKernel } | Sort-Object ProjectName) {
    $currentVersion = $CurrentPeripheryVersions[$proj.ProjectName]
    if (-not $currentVersion) {
        # First sighting: take from csproj if present, else seed at kernel version.
        $csproj = [xml](Get-Content $proj.FullPath -Raw)
        $v = $csproj.SelectSingleNode("//Version")
        $currentVersion = if ($v) { $v.InnerText.Trim() } else { $CurrentKernelVersion }
    }

    # Without a reference tag we're in bootstrap mode — freeze current state. Bumping
    # against the full repo history would mass-bump every package on its first run.
    # Real bumps start happening once the first release tag exists.
    if ($ReferenceCommit) {
        $commits = @(Get-CommitsForPath -Path $proj.FolderRel)
        $bump = Get-BumpMagnitudeFromCommits -CommitSubjects $commits
    } else {
        $commits = @()
        $bump = $null
    }

    $newVersion = $currentVersion
    if ($bump) {
        $newVersion = Get-NextVersion -Current $currentVersion -Bump $bump
    }

    $PeripheryReport += [PSCustomObject]@{
        ProjectName    = $proj.ProjectName
        RelPath        = $proj.RelPath
        CurrentVersion = $currentVersion
        NewVersion     = $newVersion
        CommitCount    = $commits.Count
        Bump           = if ($bump) { $bump } else { 'none' }
    }
}

# ── Report ────────────────────────────────────────────────────────────────────
$bumped = @($PeripheryReport | Where-Object { $_.Bump -ne 'none' })
$held = @($PeripheryReport | Where-Object { $_.Bump -eq 'none' })

if ($bumped.Count -gt 0) {
    Write-Host "Periphery bumps ($($bumped.Count)):" -ForegroundColor Green
    $bumped | Format-Table ProjectName, CurrentVersion, NewVersion, Bump, CommitCount -AutoSize | Out-String | Write-Host
} else {
    Write-Host "Periphery: no bumps (no commits in any periphery folder since reference tag)" -ForegroundColor Gray
}

if ($held.Count -gt 0 -and $VerbosePreference -eq 'Continue') {
    Write-Verbose "Held at current version ($($held.Count)):"
    $held | Format-Table ProjectName, CurrentVersion -AutoSize | Out-String | Write-Verbose
}

# ── Write versions.props ──────────────────────────────────────────────────────
if ($DryRun) {
    Write-Host "DRY RUN — build/versions.props NOT written." -ForegroundColor Yellow
    return
}

$sb = [System.Text.StringBuilder]::new()
[void]$sb.AppendLine('<Project>')
[void]$sb.AppendLine('  <!--')
[void]$sb.AppendLine('    Generated by scripts/versioning/Update-Versions.ps1. Do not hand-edit.')
[void]$sb.AppendLine('    See ARCH-0082 + docs/guides/versioning-workbook.md.')
[void]$sb.AppendLine("    Generated   : $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss zzz')")
[void]$sb.AppendLine("    Reference   : $ReferenceLabel")
if ($BumpKernel) {
    [void]$sb.AppendLine("    Kernel bump : $BumpKernel ($Reason)")
}
[void]$sb.AppendLine('  -->')
[void]$sb.AppendLine('  <PropertyGroup>')
[void]$sb.AppendLine("    <KoanKernelVersion>$NewKernelVersion</KoanKernelVersion>")
[void]$sb.AppendLine('  </PropertyGroup>')
[void]$sb.AppendLine()
[void]$sb.AppendLine('  <!-- Periphery package versions (one PropertyGroup per package). -->')

foreach ($entry in $PeripheryReport | Sort-Object ProjectName) {
    [void]$sb.AppendLine("  <PropertyGroup Condition=`"'`$(MSBuildProjectName)' == '$($entry.ProjectName)' and '`$(KoanPackageKind)' == 'Periphery'`">")
    [void]$sb.AppendLine("    <Version>$($entry.NewVersion)</Version>")
    [void]$sb.AppendLine('  </PropertyGroup>')
}

[void]$sb.AppendLine('</Project>')

$outputDir = Split-Path $VersionsPropsPath -Parent
if (-not (Test-Path $outputDir)) {
    New-Item -ItemType Directory -Path $outputDir -Force | Out-Null
}
Set-Content -Path $VersionsPropsPath -Value $sb.ToString() -NoNewline -Encoding UTF8

Write-Host "Wrote $VersionsPropsPath" -ForegroundColor Green
Write-Host ""
Write-Host "Next steps:" -ForegroundColor Cyan
Write-Host "  1. git diff build/versions.props    # review the version delta"
Write-Host "  2. dotnet build Koan.sln            # confirm builds resolve cleanly"
Write-Host "  3. git commit + scripts/versioning/New-Release.ps1 -Version $NewKernelVersion"
