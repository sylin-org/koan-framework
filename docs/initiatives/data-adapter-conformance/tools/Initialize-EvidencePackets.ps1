[CmdletBinding()]
param(
    [string]$RepositoryRoot,
    [switch]$Force
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if (-not $RepositoryRoot) {
    $RepositoryRoot = (& git rev-parse --show-toplevel).Trim()
}
$RepositoryRoot = (Resolve-Path -LiteralPath $RepositoryRoot).ProviderPath
$initiativeRoot = Join-Path $RepositoryRoot 'docs/initiatives/data-adapter-conformance'
$evidenceRoot = Join-Path $initiativeRoot 'evidence'
$rosterPath = Join-Path $evidenceRoot 'portfolio/roster.json'
if (-not (Test-Path -LiteralPath $rosterPath)) { throw 'Run New-AdapterRoster.ps1 first.' }
$roster = Get-Content -LiteralPath $rosterPath -Raw | ConvertFrom-Json

function Write-GeneratedFile([string]$Path, [string]$Content) {
    if ((Test-Path -LiteralPath $Path) -and -not $Force) { return }
    $parent = Split-Path -Parent $Path
    New-Item -ItemType Directory -Path $parent -Force | Out-Null
    if ([IO.Path]::GetExtension($Path) -eq '.md' -and -not $Content.StartsWith('---')) {
        $scopeName = Split-Path -Leaf $parent
        $documentName = [IO.Path]::GetFileNameWithoutExtension($Path)
        $title = ($scopeName + ' ' + $documentName + ' evidence').Replace('"', "'")
        $frontMatter = @"
---
type: REFERENCE
domain: data
title: "$title"
audience: [architects, maintainers, developers, ai-agents]
status: draft
last_updated: 2026-07-28
framework_version: v0.20.0
validation:
  date_last_tested: 2026-07-28
  status: reviewed
  scope: generated pending evidence-packet structure
---

"@
        $Content = $frontMatter + $Content
    }
    Set-Content -LiteralPath $Path -Value $Content -Encoding utf8
}

function Write-JsonFile([string]$Path, [object]$Value) {
    Write-GeneratedFile $Path ($Value | ConvertTo-Json -Depth 20)
}

function New-PacketFrontMatter([string]$Title, [string]$Scope) {
@"
---
type: REFERENCE
domain: data
title: "$Title"
audience: [architects, maintainers, developers, ai-agents]
status: draft
last_updated: 2026-07-28
framework_version: v0.20.0
validation:
  date_last_tested: 2026-07-28
  status: reviewed
  scope: $Scope
---

"@
}

$scopes = New-Object System.Collections.Generic.List[object]
$scopes.Add([pscustomobject]@{ id = 'framework'; display = 'Koan.Data Framework'; kind = 'framework'; package = $null })
foreach ($family in @($roster.families)) {
    $scopes.Add([pscustomobject]@{ id = 'family-' + $family.id; display = $family.name; kind = 'family'; package = $family.project })
}
foreach ($adapter in @($roster.adapters)) {
    $scopes.Add([pscustomobject]@{ id = $adapter.id; display = $adapter.provider; kind = $adapter.plane; package = $adapter.package })
}

foreach ($scope in $scopes) {
    $root = Join-Path $evidenceRoot $scope.id
    New-Item -ItemType Directory -Path $root -Force | Out-Null
    $title = $scope.display
    Write-GeneratedFile (Join-Path $root 'identity.md') ((New-PacketFrontMatter "$title Conformance Identity" 'generated pending evidence identity') + @"
# $title identity

Status: pending

Record the sealed source checkpoint, primer/profile fingerprint, provider and driver versions, fixture, source
policies, audit date, and exact reproduction commands here.
"@)
    Write-GeneratedFile (Join-Path $root 'probes.md') ((New-PacketFrontMatter "$title Provider Probes" 'generated pending provider-probe ledger') + @"
# $title probes

Status: pending

| Probe | Concern | Version | Least privilege | Command / fixture | Observation | Artifact | Official source |
|---|---|---|---|---|---|---|---|
"@)
    Write-GeneratedFile (Join-Path $root 'surfaces.md') ((New-PacketFrontMatter "$title Surface Inventory" 'generated pending public-surface inventory') + @"
# $title surfaces

Status: pending

| Surface | Public entry | Claim | Source posture | Effect / result | Semantic owner | Native owner | Failure path | Cells | Unsupported outcome |
|---|---|---|---|---|---|---|---|---|---|
"@)
    Write-GeneratedFile (Join-Path $root 'remediation.md') ((New-PacketFrontMatter "$title Remediation Ledger" 'generated pending remediation ledger') + @"
# $title remediation

Status: pending

| Remediation | Disposition | Owner | Invalidated consumers | Re-entry proof |
|---|---|---|---|---|
"@)
    Write-GeneratedFile (Join-Path $root 'README.md') ((New-PacketFrontMatter "$title Conformance Packet" 'generated pending conformance packet summary') + @"
# $title conformance packet

Status: pending

This packet is generated from the portfolio roster. A directory is not evidence: every claim, scorecard row, artifact,
dependency, and reproduction command must resolve before certification.
"@)
    Write-JsonFile (Join-Path $root 'claims.json') ([ordered]@{
            schemaVersion = 1; scope = $scope.id; status = 'pending'; claims = @()
        })
    Write-JsonFile (Join-Path $root 'scorecard.json') ([ordered]@{
            schemaVersion = 1; scope = $scope.id; status = 'pending'; rows = @(); verdict = 'UNASSESSED'
        })
    Write-JsonFile (Join-Path $root 'evidence.json') ([ordered]@{
            schemaVersion = 1; scope = $scope.id; status = 'pending'; artifacts = @()
        })
    Write-JsonFile (Join-Path $root 'dependencies.json') ([ordered]@{
            schemaVersion = 1; scope = $scope.id; status = 'pending'; dependencies = @()
        })

    if ($scope.id -in @('sqlite', 'mongodb')) {
        Write-GeneratedFile (Join-Path $root 'harvest/lessons.md') ((New-PacketFrontMatter "$title Provider Lessons" 'generated pending provider lesson ledger') + @"
# $title provider lessons

Status: pending

Only provider facts, public behavior, black-box cases, performance traps, and negative lessons may enter this file.
Legacy implementation recipes and internal structure are forbidden author inputs.
"@)
        Write-JsonFile (Join-Path $root 'harvest/compatibility.json') ([ordered]@{
                schemaVersion = 1; status = 'pending'; provider = $scope.id; candidates = @()
            })
        Write-JsonFile (Join-Path $root 'harvest/black-box.json') ([ordered]@{
                schemaVersion = 1; status = 'pending'; provider = $scope.id; scenarios = @()
            })
        Write-JsonFile (Join-Path $root 'restricted/retirement.json') ([ordered]@{
                schemaVersion = 1; status = 'pending'; provider = $scope.id; expected = @(); absence = @()
            })
        Write-JsonFile (Join-Path $root 'rewrite/replacement.json') ([ordered]@{
                schemaVersion = 1; status = 'pending'; provider = $scope.id; commonBase = $null
                startedEmpty = $null; sourceExport = @(); compileItems = @(); registrations = @()
                movingParts = @(); executionPaths = @(); shadowPaths = @()
                retirementRef = '../restricted/retirement.json'
            })
        Write-GeneratedFile (Join-Path $root 'rewrite/lineage.md') ((New-PacketFrontMatter "$title Replacement Architecture Review" 'generated pending replacement architecture review') + @"
# $title replacement architecture review

Status: pending

Record whether every moving part owns a contract or measured hot-path need, plus the checks for copied structure,
compatibility bridges, shadow/fallback paths, duplicate ownership, and warm-path discovery.
"@)
    }
}

$index = [ordered]@{
    schemaVersion = 1
    generatedFrom = 'portfolio/roster.json'
    rosterHash = (Get-FileHash -LiteralPath $rosterPath -Algorithm SHA256).Hash.ToLowerInvariant()
    requiredFiles = @('identity.md', 'probes.md', 'claims.json', 'surfaces.md', 'scorecard.json', 'evidence.json', 'dependencies.json', 'remediation.md', 'README.md')
    goldRequiredFiles = @('harvest/lessons.md', 'harvest/compatibility.json', 'harvest/black-box.json', 'restricted/retirement.json', 'rewrite/replacement.json', 'rewrite/lineage.md')
    scopes = @($scopes | ForEach-Object { [ordered]@{ id = $_.id; kind = $_.kind; display = $_.display } })
}
Write-JsonFile (Join-Path $evidenceRoot 'portfolio/packet-index.json') $index
Write-Output "PACKETS scopes=$($scopes.Count) adapters=$(@($roster.adapters).Count) families=$(@($roster.families).Count)"
