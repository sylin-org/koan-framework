<#
.SYNOPSIS
  Bump version, fast-forward main from dev, tag vX.Y.Z, and push.

.DESCRIPTION
  Implements the desired release flow:
  - Update version.json by incrementing Major/Minor/Patch
  - Push change to dev
  - Fast-forward merge dev -> main (preserves dev)
  - Create annotated tag vX.Y.Z on main and push
  The tag triggers the nuget-release GitHub Action to publish packages.

.PARAMETER Part
  Which part to increment: Major|Minor|Patch

.PARAMETER Remote
  Git remote name (default: origin)

.PARAMETER DevBranch
  Source branch to release from (default: dev)

.PARAMETER MainBranch
  Target branch to publish (default: main)

.PARAMETER DryRun
  Show intended actions without pushing/tagging.

.PARAMETER PrOnly
  PR-only mode: bump and push dev, then stop before touching main/tag. Use when main requires PRs.

.PARAMETER CreatePr
  With -PrOnly, attempt to open a dev -> main pull request automatically (uses GitHub CLI if available).
#>
param(
  [ValidateSet('Major','Minor','Patch')][string]$Part,
  [string]$Remote = 'origin',
  [string]$DevBranch = 'dev',
  [string]$MainBranch = 'main',
  [switch]$DryRun,
  [switch]$PrOnly,
  [switch]$CreatePr
)

$ErrorActionPreference = 'Stop'
# Resolve repo root once so all git commands run from a stable working directory
$Script:RepoRoot = $null

function Invoke-Git {
  param([Parameter(Mandatory=$true)][string[]]$Args)
  $display = ($Args -join ' ')
  Write-Host "git $display" -ForegroundColor DarkGray
  if (-not $Script:RepoRoot) { $Script:RepoRoot = Get-RepoRoot }
  & git -C $Script:RepoRoot @Args
  if ($LASTEXITCODE -ne 0) { throw "Git failed: git $display" }
}

function Get-CurrentBranch {
  $b = & git rev-parse --abbrev-ref HEAD
  if ($LASTEXITCODE -ne 0) { throw 'Unable to determine current branch.' }
  return $b.Trim()
}

function Get-RepoRoot {
  $p = & git rev-parse --show-toplevel
  if ($LASTEXITCODE -ne 0) { throw 'Not a git repository.' }
  return $p.Trim()
}

function Ensure-CleanWorkingTree {
  $s = & git status --porcelain
  if ($LASTEXITCODE -ne 0) { throw 'git status failed' }
  if ($s) { throw 'Working tree has uncommitted changes. Commit or stash before running release.' }
}

function Read-VersionJson([string]$Path) {
  if (-not (Test-Path $Path)) { throw "version.json not found at $Path" }
  return Get-Content -Raw $Path | ConvertFrom-Json
}

function Write-VersionJson($Obj, [string]$Path) {
  $Obj | ConvertTo-Json -Depth 10 | Out-File -Encoding UTF8 $Path
}

function Bump-Version([string]$v, [string]$part) {
  if (-not ($v -match '^(\d+)\.(\d+)\.(\d+)')) { throw "Invalid version: $v" }
  $maj = [int]$Matches[1]; $min = [int]$Matches[2]; $pat = [int]$Matches[3]
  switch ($part) {
    'Major' { $maj++; $min = 0; $pat = 0 }
    'Minor' { $min++; $pat = 0 }
    'Patch' { $pat++ }
  }
  return "$maj.$min.$pat"
}

function Prompt-For-Part([string]$current) {
  $nextMajor = Bump-Version $current 'Major'
  $nextMinor = Bump-Version $current 'Minor'
  $nextPatch = Bump-Version $current 'Patch'
  Write-Host "Current version: $current" -ForegroundColor Cyan
  Write-Host "Choose which part to bump:" -ForegroundColor Cyan
  Write-Host "  [1] Major -> $nextMajor" -ForegroundColor Gray
  Write-Host "  [2] Minor -> $nextMinor" -ForegroundColor Gray
  Write-Host "  [3] Patch -> $nextPatch" -ForegroundColor Gray
  while ($true) {
    $ans = Read-Host 'Enter 1 (Major), 2 (Minor), or 3 (Patch)'
    switch ($ans) {
      '1' { return 'Major' }
      '2' { return 'Minor' }
      '3' { return 'Patch' }
      'major' { return 'Major' }
      'minor' { return 'Minor' }
      'patch' { return 'Patch' }
      default { Write-Host 'Invalid choice. Please enter 1, 2, or 3.' -ForegroundColor Yellow }
    }
  }
}

Write-Host "[release] Preparing to release from $DevBranch to $MainBranch (remote: $Remote), bumping $Part" -ForegroundColor Cyan

Ensure-CleanWorkingTree
Invoke-Git @("fetch", $Remote, "--tags")

Invoke-Git @("checkout", $DevBranch)
# Ensure we are on the expected development branch before proceeding
if ((Get-CurrentBranch) -ne $DevBranch) { throw "Expected to be on '$DevBranch' after checkout, but found '$(Get-CurrentBranch)'." }
Invoke-Git @("pull", $Remote, $DevBranch)

$root = Get-RepoRoot
$Script:RepoRoot = $root
$verPath = Join-Path $root 'version.json'
$ver = Read-VersionJson $verPath
$old = $ver.version
if (-not $Part) { $Part = Prompt-For-Part $old }
$new = Bump-Version $old $Part

Write-Host "[release] version: $old -> $new (bump: $Part)" -ForegroundColor Green
$ver.version = $new
Write-VersionJson $ver $verPath

Invoke-Git @("add", "--", "version.json")
Invoke-Git @("commit", "-m", "chore: bump version to $new")

if (-not $DryRun) {
  # Push explicitly from dev to remote dev to avoid accidental HEAD pushes
  if ((Get-CurrentBranch) -ne $DevBranch) { throw "Safety check failed: not on '$DevBranch' when pushing development branch." }
  Invoke-Git @("push", $Remote, "${DevBranch}:${DevBranch}")
} else { Write-Host "[dry-run] skip push dev" -ForegroundColor Yellow }

# If running in PR-only mode, do not touch main or create a tag
if ($PrOnly) {
  $tag = "v$new"
  Write-Host "[release] PR-only mode: opened release commit on '$DevBranch' for $tag. Next: create a PR into '$MainBranch' and, after merge, tag $tag on main." -ForegroundColor Cyan

  if ($CreatePr) {
    $gh = Get-Command gh -ErrorAction SilentlyContinue
    if ($null -ne $gh) {
      try {
        Write-Host "[release] Creating PR dev -> main using GitHub CLI..." -ForegroundColor DarkCyan
        $title = "Release $tag"
        $body = @(
          "Automated release PR for $tag.",
          "",
          "Notes:",
          "- This should be a fast-forward merge from '$DevBranch' into '$MainBranch'.",
          "- After merging, create annotated tag $tag on '$MainBranch' to trigger the NuGet publish workflow."
        ) -join "`n"
        & gh pr create --base $MainBranch --head $DevBranch --title $title --body $body | Write-Host
        if ($LASTEXITCODE -ne 0) { throw "gh pr create failed" }
      } catch {
        Write-Warning "Failed to create PR automatically. Create it manually: base '$MainBranch', head '$DevBranch'."
      }
    } else {
      Write-Host "[release] GitHub CLI (gh) not found. Create the PR manually: base '$MainBranch', head '$DevBranch'." -ForegroundColor Yellow
    }
  } else {
    Write-Host "[release] Skipping automatic PR creation. Create the PR manually: base '$MainBranch', head '$DevBranch'." -ForegroundColor Yellow
  }

  Write-Host "[release] Exiting before touching '$MainBranch' or creating tag (PR-only)." -ForegroundColor Cyan
  exit 0
}

Invoke-Git @("checkout", $MainBranch)
Invoke-Git @("pull", $Remote, $MainBranch)

try {
  Invoke-Git @("merge", "--ff-only", $DevBranch)
} catch {
  Write-Error "Fast-forward merge failed. Make sure $MainBranch can fast-forward to $DevBranch (no extra commits on $MainBranch). Abort."; exit 1
}

$tag = "v$new"
Invoke-Git @("tag", "-a", $tag, "-m", "Release $tag")

if (-not $DryRun) {
  Invoke-Git @("push", $Remote, $MainBranch)
  Invoke-Git @("push", $Remote, $tag)
  Write-Host "[release] Pushed $MainBranch and tag $tag. The GitHub Action should start shortly." -ForegroundColor Green
} else {
  Write-Host "[dry-run] skip push main and tag $tag" -ForegroundColor Yellow
}
