<#
.SYNOPSIS
  Bundles dated snapshots of the capability map and connector matrix into the koan skill,
  so agents on restricted or offline networks still have package identifiers.

.DESCRIPTION
  Copies docs/reference/{capability-map,connector-matrix}.md verbatim into
  .agents/skills/koan/references/generated/ and writes SNAPSHOT.meta.json recording the
  source commit and time. Verbatim copies keep the markdown (frontmatter, tables) intact;
  relative links inside a snapshot may resolve against docs/reference and can 404 from the
  new location - recorded in the meta file as a known limitation.

  With -Check: verifies the committed snapshots are byte-identical to the current docs.
  Exit 1 with instructions on drift. Deterministic: identical inputs produce identical
  files; only SNAPSHOT.meta.json carries timestamps and it is excluded from comparison.

.EXAMPLE
  pwsh scripts/update-agent-skills.ps1            # regenerate after editing the docs
  pwsh scripts/update-agent-skills.ps1 -Check     # CI gate
#>
[CmdletBinding()]
param(
    [switch]$Check
)
$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path $PSScriptRoot -Parent
$generatedRoot = '.agents/skills/koan/references/generated'
$targets = @(
    @{ Source = 'docs/reference/capability-map.md';  Destination = "$generatedRoot/capability-map.md" },
    @{ Source = 'docs/reference/connector-matrix.md'; Destination = "$generatedRoot/connector-matrix.md" },
    @{ Source = 'docs/capabilities/index.md'; Destination = "$generatedRoot/capabilities/index.md" },
    @{ Source = 'docs/capabilities/ai.md'; Destination = "$generatedRoot/capabilities/ai.md" },
    @{ Source = 'docs/capabilities/ai/semantic-search.md'; Destination = "$generatedRoot/capabilities/ai/semantic-search.md" },
    @{ Source = 'docs/capabilities/ai/embedding/portable.md'; Destination = "$generatedRoot/capabilities/ai/embedding/portable.md" }
)

$metaPath = Join-Path $repoRoot '.agents/skills/koan/references/generated/SNAPSHOT.meta.json'

function Get-Fresh([string]$source, [string]$destination) {
    $sourcePath = Join-Path $repoRoot ($source -replace '/', [IO.Path]::DirectorySeparatorChar)
    if (-not (Test-Path -LiteralPath $sourcePath)) { throw "missing source: $source" }
    [pscustomobject]@{
        Source      = $source
        Destination = $destination
        Content     = [IO.File]::ReadAllBytes($sourcePath)
    }
}

$fresh = foreach ($t in $targets) { Get-Fresh $t.Source $t.Destination }

if ($Check) {
    $failed = $false
    foreach ($item in $fresh) {
        $destination = Join-Path $repoRoot ($item.Destination -replace '/', [IO.Path]::DirectorySeparatorChar)
        if (-not (Test-Path -LiteralPath $destination)) {
            Write-Error "snapshot missing: $destination - run: pwsh scripts/update-agent-skills.ps1"
            $failed = $true
            continue
        }
        $current = [IO.File]::ReadAllBytes($destination)
        if (-not ([Linq.Enumerable]::SequenceEqual($current, $item.Content))) {
            Write-Error "snapshot stale: $destination - run: pwsh scripts/update-agent-skills.ps1"
            $failed = $true
        }
    }
    if ($failed) { exit 1 }
    Write-Output 'agent-skill snapshots up to date.'
    exit 0
}

$commit = ''
try { $commit = (git -C $repoRoot rev-parse HEAD).Trim() } catch { $commit = 'unknown' }
$generatedUtc = (Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ')

foreach ($item in $fresh) {
    $destination = Join-Path $repoRoot ($item.Destination -replace '/', [IO.Path]::DirectorySeparatorChar)
    New-Item -ItemType Directory -Force -Path (Split-Path $destination) | Out-Null
    [IO.File]::WriteAllBytes($destination, $item.Content)
}

$meta = [ordered]@{
    schema       = 1
    generatedUtc = $generatedUtc
    sourceCommit = $commit
    note         = 'Verbatim copies of the named docs at main. Relative links inside a snapshot may point back into docs/reference and can 404 from this location; treat paths as repo-relative text.'
    files        = @($targets | ForEach-Object { $_.Destination })
}
$meta | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $metaPath -Encoding utf8NoBOM

Write-Output "wrote $($targets.Count) snapshots + SNAPSHOT.meta.json (commit $commit)."
Write-Output 'Commit the generated folder together with any docs change that touched the sources.'
