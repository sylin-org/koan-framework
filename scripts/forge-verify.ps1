<#
.SYNOPSIS
  Runs Koan data-adapter conformance and binds every result to the development primer.

.DESCRIPTION
  The primer is the only acceptance catalog. Shared TestKit facts begin with an exact
  <Acceptance ID>/<Case>/<Owner> row key; this script validates those bindings, runs the selected adapter suite,
  and reads the keys back from TRX. It never derives semantics from method names and never restores packages.

  Default mode preserves the bounded AODB gate. Strict mode additionally validates the adapter's versioned packet
  through Koan.Testing, without duplicating packet semantics in PowerShell.

  Exit codes: 0 GREEN, 1 RED behavior/false claim, 2 DEFERRED/inconclusive, 3 structural/protocol error,
              4 provider infrastructure unavailable.

.EXAMPLE
  pwsh scripts/forge-verify.ps1 -Adapter Sqlite -Plane record
  pwsh scripts/forge-verify.ps1 -Adapter Sqlite -Plane record -Strict
  pwsh scripts/forge-verify.ps1 -DockerFree -Output json
  pwsh scripts/forge-verify.ps1 -CatalogOnly -CatalogOutput src/Koan.Testing/Conformance/data-conformance-catalog.json
#>
[CmdletBinding()]
param(
    [string]$Adapter,
    [ValidateSet('', 'record', 'vector')][string]$Plane = '',
    [switch]$All,
    [switch]$DockerFree,
    [switch]$CatalogOnly,
    [switch]$Strict,
    [string]$PacketRoot = 'docs/initiatives/data-adapter-conformance/evidence',
    [string]$CatalogOutput,
    [ValidateSet('table', 'json')][string]$Output = 'table',
    [string]$Configuration = 'Debug',
    [ValidateRange(1, 3600)][int]$DeadlineSeconds = 600,
    [switch]$NoBuild
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).ProviderPath
$primerPath = Join-Path $repoRoot 'docs/architecture/data-adapter-development-primer.md'
$dataSuitesRoot = Join-Path $repoRoot 'tests/Suites/Data'
$expectedCatalogCells = 105
$expectedProfiles = 39
$rowPattern = '^(?<id>[A-HPV]-\d{2})/(?<case>[a-z0-9]+(?:-[a-z0-9]+)*)/(?<owner>Framework|Family|Adapter): (?<title>.+)$'
$protocolVersion = 'data-adapter-conformance/1'
$catalogSchemaVersion = 1
$packetValidationProject = Join-Path $repoRoot 'tests/Suites/Testing/Koan.Testing.Tests/Koan.Testing.Tests.csproj'
$packetEnvironmentVariable = 'KOAN_DATA_CONFORMANCE_PACKET'
$packetStatusMarker = 'KOAN_DATA_CONFORMANCE_STATUS='
$resultsRoot = $null

function Get-RelativePath([string]$Path) {
    [IO.Path]::GetRelativePath($repoRoot, $Path).Replace('\', '/')
}

function Get-Sha256([string]$Path) {
    (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

function Read-PrimerCatalog {
    $text = Get-Content -LiteralPath $primerPath -Raw
    $matches = [regex]::Matches(
        $text,
        '(?m)^- \*\*(?<id>[A-HPV]-\d{2})\*\* \[(?<evidence>[A-Z, ]+)\] (?<summary>[^\r\n]+)(?<continuation>(?:\r?\n  [^\r\n]+)*)')
    $catalog = @($matches | ForEach-Object {
            $requirement = ($_.Groups['summary'].Value + ' ' +
                ($_.Groups['continuation'].Value -replace '(?:\r?\n)\s*', ' ')) -replace '\s+', ' '
            [pscustomobject]@{
                id = $_.Groups['id'].Value
                evidenceKinds = @($_.Groups['evidence'].Value.Split(',') | ForEach-Object { $_.Trim() })
                summary = $requirement.Trim()
                verifier = 'Koan.Testing.DataAdapterConformanceSpecs.Acceptance_cell_has_complete_evidence'
            }
        } | Sort-Object id)
    if ($catalog.Count -ne $expectedCatalogCells) {
        throw "Primer catalog contains $($catalog.Count) requirements; expected $expectedCatalogCells."
    }
    foreach ($duplicate in @($catalog.id | Group-Object | Where-Object Count -ne 1)) {
        throw "Primer catalog contains duplicate ID '$($duplicate.Name)'."
    }
    $catalog
}

function Expand-AcceptanceIds([string]$Text) {
    $ids = New-Object System.Collections.Generic.List[string]
    foreach ($match in [regex]::Matches($Text, '(?<prefix>[A-HPV])-(?<first>\d{2})(?:[–—](?:(?<endPrefix>[A-HPV])-)?(?<last>\d{2}))?')) {
        $prefix = $match.Groups['prefix'].Value
        $first = [int]$match.Groups['first'].Value
        $last = if ($match.Groups['last'].Success) { [int]$match.Groups['last'].Value } else { $first }
        $endPrefix = if ($match.Groups['endPrefix'].Success) { $match.Groups['endPrefix'].Value } else { $prefix }
        if ($endPrefix -ne $prefix -or $last -lt $first) {
            throw "Invalid acceptance range '$($match.Value)' in profile table."
        }
        for ($number = $first; $number -le $last; $number++) {
            $ids.Add(('{0}-{1:d2}' -f $prefix, $number)) | Out-Null
        }
    }
    @($ids | Sort-Object -Unique)
}

function Read-PrimerProfiles([object[]]$Catalog) {
    $knownIds = @{}; foreach ($item in $Catalog) { $knownIds[$item.id] = $true }
    $text = Get-Content -LiteralPath $primerPath -Raw
    $start = $text.IndexOf('## 8. Conformance profiles', [StringComparison]::Ordinal)
    $end = $text.IndexOf('## 9. Normative requirement catalog', [StringComparison]::Ordinal)
    if ($start -lt 0 -or $end -le $start) { throw 'Primer conformance profile table cannot be located.' }
    $section = $text.Substring($start, $end - $start)
    $profiles = @([regex]::Matches($section, '(?m)^\| (?<id>[^|]+?) \| (?<applicability>[^|]+?) \| (?<cells>[^|]+?) \|$') |
        ForEach-Object {
            $id = $_.Groups['id'].Value.Trim()
            $acceptanceIds = @(Expand-AcceptanceIds $_.Groups['cells'].Value)
            if ($id -eq 'Profile or capability claim' -or $id -match '^-+$') { return }
            if ($acceptanceIds.Count -eq 0) { throw "Profile '$id' selects no acceptance cells." }
            foreach ($acceptanceId in $acceptanceIds) {
                if (-not $knownIds.ContainsKey($acceptanceId)) {
                    throw "Profile '$id' references unknown acceptance cell '$acceptanceId'."
                }
            }
            [pscustomobject]@{
                id = $id
                applicability = $_.Groups['applicability'].Value.Trim()
                acceptanceIds = $acceptanceIds
            }
        })
    foreach ($duplicate in @($profiles.id | Group-Object | Where-Object Count -ne 1)) {
        throw "Primer contains duplicate profile '$($duplicate.Name)'."
    }
    if ($profiles.Count -ne $expectedProfiles) { throw "Primer contains $($profiles.Count) profiles; expected $expectedProfiles." }
    @($profiles)
}

function New-GeneratedCatalog([object[]]$Catalog, [object[]]$Profiles) {
    [ordered]@{
        schemaVersion = $catalogSchemaVersion
        protocolVersion = $protocolVersion
        primerPath = Get-RelativePath $primerPath
        primerSha256 = Get-Sha256 $primerPath
        cells = @($Catalog | ForEach-Object {
                [ordered]@{
                    id = $_.id
                    evidence = @($_.evidenceKinds)
                    requirement = $_.summary
                    verifier = $_.verifier
                }
            })
        profiles = @($Profiles | ForEach-Object {
                [ordered]@{
                    id = $_.id
                    applicability = $_.applicability
                    acceptanceIds = @($_.acceptanceIds)
                }
            })
    }
}

function Write-GeneratedCatalog([object]$Document, [string]$Path) {
    $resolved = if ([IO.Path]::IsPathRooted($Path)) { [IO.Path]::GetFullPath($Path) }
        else { [IO.Path]::GetFullPath((Join-Path $repoRoot $Path)) }
    $parent = [IO.Path]::GetDirectoryName($resolved)
    if (-not (Test-Path -LiteralPath $parent)) { New-Item -ItemType Directory -Path $parent | Out-Null }
    $Document | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $resolved -Encoding utf8
    $resolved
}

function Test-GeneratedCatalog([object]$Document, [string]$Path) {
    if (-not (Test-Path -LiteralPath $Path)) { throw "Generated conformance catalog is missing: $(Get-RelativePath $Path)." }
    $actual = Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json
    if ($actual.schemaVersion -ne $Document.schemaVersion -or $actual.protocolVersion -ne $Document.protocolVersion) {
        throw 'Generated conformance catalog has a stale protocol identity.'
    }
    if ($actual.primerSha256 -ne $Document.primerSha256) {
        throw 'Generated conformance catalog has a stale primer fingerprint. Regenerate it with -CatalogOutput.'
    }
    if (@($actual.cells).Count -ne @($Document.cells).Count -or @($actual.profiles).Count -ne @($Document.profiles).Count) {
        throw 'Generated conformance catalog has stale cell/profile cardinality.'
    }
}

function Read-SuiteCells([string]$Path, [object[]]$Catalog) {
    $knownIds = @{}; foreach ($item in $Catalog) { $knownIds[$item.id] = $item }
    $text = Get-Content -LiteralPath $Path -Raw
    $facts = [regex]::Matches($text, '\[Fact\(DisplayName\s*=\s*"(?<display>[^"]+)"\)\]')
    $cells = New-Object System.Collections.Generic.List[object]
    foreach ($fact in $facts) {
        $display = $fact.Groups['display'].Value
        $row = [regex]::Match($display, $rowPattern)
        if (-not $row.Success) {
            throw "Conformance fact in '$(Get-RelativePath $Path)' has no exact primer row key: $display"
        }
        $id = $row.Groups['id'].Value
        if (-not $knownIds.ContainsKey($id)) {
            throw "Conformance fact in '$(Get-RelativePath $Path)' uses unknown primer ID '$id'."
        }
        $cells.Add([pscustomobject]@{
                key = "$id/$($row.Groups['case'].Value)/$($row.Groups['owner'].Value)"
                id = $id
                case = $row.Groups['case'].Value
                owner = $row.Groups['owner'].Value
                title = $row.Groups['title'].Value
                evidenceKinds = @($knownIds[$id].evidenceKinds)
            }) | Out-Null
    }
    if ($cells.Count -eq 0) { throw "Conformance suite '$(Get-RelativePath $Path)' declares no primer cells." }
    foreach ($duplicate in @($cells.key | Group-Object | Where-Object Count -ne 1)) {
        throw "Conformance suite '$(Get-RelativePath $Path)' contains duplicate row '$($duplicate.Name)'."
    }
    @($cells | Sort-Object key)
}

function Find-Project([IO.FileInfo]$Spec) {
    $directory = $Spec.Directory
    while ($directory) {
        $projects = @(Get-ChildItem -LiteralPath $directory.FullName -Filter '*.csproj' -File)
        if ($projects.Count -eq 1) { return $projects[0].FullName }
        if ($projects.Count -gt 1) { throw "More than one project owns '$(Get-RelativePath $Spec.FullName)'." }
        $directory = $directory.Parent
    }
    $null
}

function Discover-Targets([hashtable]$CellsByPlane) {
    $targets = New-Object System.Collections.Generic.List[object]
    $specs = @(Get-ChildItem -LiteralPath $dataSuitesRoot -Recurse -Filter '*AodbConformanceSpec.cs' -File |
            Sort-Object FullName)
    foreach ($spec in $specs) {
        $isVector = $spec.Name.EndsWith('VectorAodbConformanceSpec.cs', [StringComparison]::Ordinal)
        $targetPlane = if ($isVector) { 'vector' } else { 'record' }
        $suffix = if ($isVector) { 'VectorAodbConformanceSpec.cs' } else { 'AodbConformanceSpec.cs' }
        $name = $spec.Name.Substring(0, $spec.Name.Length - $suffix.Length)
        $targets.Add([pscustomobject]@{
                adapter = $name
                plane = $targetPlane
                key = "$targetPlane/$name"
                className = $spec.BaseName
                project = Find-Project $spec
                spec = Get-RelativePath $spec.FullName
                expectedCells = @($CellsByPlane[$targetPlane])
            }) | Out-Null
    }
    foreach ($duplicate in @($targets.key | Group-Object | Where-Object Count -ne 1)) {
        throw "Conformance target '$($duplicate.Name)' is declared more than once."
    }
    @($targets | Sort-Object plane, adapter)
}

function Read-ResultCell([object]$Result, [hashtable]$Definitions, [hashtable]$CatalogById) {
    $definition = $Definitions[[string]$Result.testId]
    $candidates = @([string]$Result.testName)
    if ($definition) { $candidates += [string]$definition.name }
    $row = $null
    foreach ($candidate in @($candidates | Select-Object -Unique)) {
        $match = [regex]::Match($candidate, $rowPattern)
        if ($match.Success) { $row = $match; break }
    }
    if (-not $row) {
        return [pscustomobject]@{
            key = $null; id = $null; case = $null; owner = $null; title = $null
            outcome = 'Unparseable'; evidenceKinds = @(); reason = "TRX result has no primer row key: $($candidates -join ' | ')"
        }
    }

    $id = $row.Groups['id'].Value
    $outcome = switch ([string]$Result.outcome) {
        'Passed' { 'Passed' }
        'Failed' { 'Failed' }
        { $_ -in @('NotExecuted', 'Skipped') } { 'Skipped' }
        default { 'Unknown' }
    }
    $reason = ''
    $messageNode = $Result.SelectSingleNode('./*[local-name()="Output"]/*[local-name()="ErrorInfo"]/*[local-name()="Message"]')
    $stdoutNode = $Result.SelectSingleNode('./*[local-name()="Output"]/*[local-name()="StdOut"]')
    if ($messageNode) { $reason = ([string]$messageNode.InnerText).Trim() }
    elseif ($stdoutNode) { $reason = ([string]$stdoutNode.InnerText).Trim() }
    [pscustomobject]@{
        key = "$id/$($row.Groups['case'].Value)/$($row.Groups['owner'].Value)"
        id = $id
        case = $row.Groups['case'].Value
        owner = $row.Groups['owner'].Value
        title = $row.Groups['title'].Value
        outcome = $outcome
        evidenceKinds = if ($CatalogById.ContainsKey($id)) { @($CatalogById[$id].evidenceKinds) } else { @() }
        reason = $reason
    }
}

function Get-EvidencePacketId([object]$Target) {
    if ($Target.plane -eq 'vector' -and $Target.adapter -ieq 'InMemory') { return 'vector-inmemory' }
    $Target.adapter.ToLowerInvariant()
}

function Invoke-PacketValidation([string]$PacketPath) {
    if (-not (Test-Path -LiteralPath $PacketPath)) {
        return [pscustomobject]@{
            status = 'Deferred'
            exitCode = 2
            reason = "strict packet is missing: $(Get-RelativePath $PacketPath)"
        }
    }

    $prior = [Environment]::GetEnvironmentVariable($packetEnvironmentVariable, 'Process')
    try {
        [Environment]::SetEnvironmentVariable($packetEnvironmentVariable, $PacketPath, 'Process')
        $arguments = @(
            'test', $packetValidationProject, '--no-restore', '--configuration', $Configuration,
            '--filter', 'FullyQualifiedName~ForgePacketValidationTests.Packet_from_environment_is_valid',
            '--logger', 'console;verbosity=minimal'
        )
        if ($NoBuild) { $arguments += '--no-build' }
        $outputLines = @(& dotnet @arguments 2>&1)
        $testExit = $LASTEXITCODE
        if ($testExit -eq 0) {
            return [pscustomobject]@{ status = 'Pass'; exitCode = 0; reason = '' }
        }

        $outputText = $outputLines -join "`n"
        $statusMatch = [regex]::Match($outputText, [regex]::Escape($packetStatusMarker) + '(?<status>Pass|Red|Deferred|Error|Infrastructure)')
        if (-not $statusMatch.Success) {
            return [pscustomobject]@{
                status = 'Infrastructure'
                exitCode = 4
                reason = "packet validator failed without a protocol status (exit $testExit): " +
                    (($outputLines | Select-Object -Last 8) -join ' ')
            }
        }

        $status = $statusMatch.Groups['status'].Value
        $exitCode = switch ($status) {
            'Pass' { 0 }
            'Red' { 1 }
            'Deferred' { 2 }
            'Error' { 3 }
            'Infrastructure' { 4 }
        }
        [pscustomobject]@{
            status = $status
            exitCode = $exitCode
            reason = (($outputLines | Where-Object { $_ -match [regex]::Escape($packetStatusMarker) } |
                        Select-Object -First 1) -replace '^\s+', '').Trim()
        }
    }
    finally {
        [Environment]::SetEnvironmentVariable($packetEnvironmentVariable, $prior, 'Process')
    }
}

try {
    $catalog = @(Read-PrimerCatalog)
    $profiles = @(Read-PrimerProfiles $catalog)
    $generatedCatalog = New-GeneratedCatalog $catalog $profiles
    $embeddedCatalogPath = Join-Path $repoRoot 'src/Koan.Testing/Conformance/data-conformance-catalog.json'
    if ($CatalogOutput) {
        $embeddedCatalogPath = Write-GeneratedCatalog $generatedCatalog $CatalogOutput
    }
    Test-GeneratedCatalog $generatedCatalog $embeddedCatalogPath
    $catalogById = @{}; foreach ($item in $catalog) { $catalogById[$item.id] = $item }
    $suiteSources = [ordered]@{
        record = Join-Path $dataSuitesRoot 'AdapterSurface/Koan.Data.AdapterSurface.TestKit/AodbConformanceSpecsBase.cs'
        vector = Join-Path $dataSuitesRoot 'VectorAdapterSurface/Koan.Data.VectorAdapterSurface.TestKit/VectorAodbConformanceSpecsBase.cs'
    }
    $cellsByPlane = @{}
    foreach ($entry in $suiteSources.GetEnumerator()) {
        $cellsByPlane[$entry.Key] = @(Read-SuiteCells $entry.Value $catalog)
    }
    $boundIds = @($cellsByPlane.Values | ForEach-Object { $_.id } | Sort-Object -Unique)
    $catalogReport = [pscustomobject]@{
        count = $catalog.Count
        primer = Get-RelativePath $primerPath
        fingerprint = Get-Sha256 $primerPath
        generatedCatalog = Get-RelativePath $embeddedCatalogPath
        generatedFingerprint = Get-Sha256 $embeddedCatalogPath
        profiles = $profiles.Count
        boundIds = $boundIds
        unboundIds = @($catalog.id | Where-Object { $boundIds -notcontains $_ })
    }
    if ($CatalogOnly) {
        $report = [pscustomobject]@{
            protocolVersion = $protocolVersion
            catalog = $catalogReport
            profiles = $profiles
            suites = @($suiteSources.GetEnumerator() | Sort-Object Key | ForEach-Object {
                    [pscustomobject]@{
                        plane = $_.Key
                        source = Get-RelativePath $_.Value
                        fingerprint = Get-Sha256 $_.Value
                        cells = @($cellsByPlane[$_.Key])
                    }
                })
        }
        if ($Output -eq 'json') { $report | ConvertTo-Json -Depth 10 }
        else {
            @($report.suites | ForEach-Object {
                    [pscustomobject]@{ Plane = $_.plane; Cells = $_.cells.Count; Source = $_.source }
                }) | Format-Table -AutoSize | Out-String | Write-Host
            Write-Host "FORGE CATALOG cells=$($catalog.Count) profiles=$($profiles.Count) sha256=$($catalogReport.generatedFingerprint)"
        }
        exit 0
    }

    $targets = @(Discover-Targets $cellsByPlane)
    $dockerFreeTargets = @('record/InMemory', 'record/Json', 'record/Sqlite', 'vector/InMemory', 'vector/SqliteVec')
    $selected = @($targets)
    if ($DockerFree) {
        $selected = @($selected | Where-Object { $dockerFreeTargets -contains $_.key })
    }
    elseif ($Adapter) {
        $selected = @($selected | Where-Object {
                $_.adapter -ieq $Adapter -and ($Plane -eq '' -or $_.plane -eq $Plane)
            })
    }
    elseif (-not $All) {
        throw 'Specify -Adapter <name> [-Plane record|vector], -DockerFree, -All, or -CatalogOnly.'
    }
    if ($selected.Count -eq 0) { throw 'No conformance target matched the selection.' }

    $resolvedPacketRoot = if ([IO.Path]::IsPathRooted($PacketRoot)) { [IO.Path]::GetFullPath($PacketRoot) }
        else { [IO.Path]::GetFullPath((Join-Path $repoRoot $PacketRoot)) }
    $resultsRoot = Join-Path ([IO.Path]::GetTempPath()) ('koan-forge-' + [guid]::NewGuid().ToString('n'))
    New-Item -ItemType Directory -Path $resultsRoot | Out-Null
    $adapterReports = New-Object System.Collections.Generic.List[object]

    foreach ($target in $selected) {
        if (-not $target.project) {
            $adapterReports.Add([pscustomobject]@{
                    adapter = $target.adapter; plane = $target.plane; verdict = 'ERROR'; project = $null
                    expectedCells = @($target.expectedCells); cells = @(); missingCells = @($target.expectedCells.key)
                    reason = 'No project owns the conformance spec.'
                }) | Out-Null
            continue
        }

        $trxName = "$($target.plane)-$($target.adapter).trx"
        $trxPath = Join-Path $resultsRoot $trxName
        $arguments = @(
            'test', $target.project, '--no-restore', '--configuration', $Configuration,
            '--filter', "FullyQualifiedName~$($target.className)",
            '--logger', "trx;LogFileName=$trxName", '--results-directory', $resultsRoot,
            '--blame-hang-timeout', "$($DeadlineSeconds)s", '--blame-hang-dump-type', 'none'
        )
        if ($NoBuild) { $arguments += '--no-build' }
        Write-Host "forge: $($target.key)" -ForegroundColor DarkGray
        $testOutput = & dotnet @arguments 2>&1
        $testExit = $LASTEXITCODE
        if (-not (Test-Path -LiteralPath $trxPath)) {
            $adapterReports.Add([pscustomobject]@{
                    adapter = $target.adapter; plane = $target.plane; verdict = 'ERROR'
                    project = Get-RelativePath $target.project; expectedCells = @($target.expectedCells); cells = @()
                    missingCells = @($target.expectedCells.key)
                    reason = "dotnet test produced no TRX (exit $testExit): " + (($testOutput | Select-Object -Last 8) -join ' ')
                }) | Out-Null
            continue
        }

        [xml]$trx = Get-Content -LiteralPath $trxPath -Raw
        $definitions = @{}
        foreach ($definition in @($trx.TestRun.TestDefinitions.UnitTest)) {
            $definitions[[string]$definition.id] = $definition
        }
        $cells = @($trx.TestRun.Results.UnitTestResult | ForEach-Object {
                Read-ResultCell $_ $definitions $catalogById
            } | Sort-Object key)
        $expectedKeys = @($target.expectedCells.key)
        $actualKeys = @($cells | Where-Object key | ForEach-Object key)
        $missing = @($expectedKeys | Where-Object { $actualKeys -notcontains $_ })
        $unexpected = @($actualKeys | Where-Object { $expectedKeys -notcontains $_ })
        $duplicates = @($actualKeys | Group-Object | Where-Object Count -ne 1 | ForEach-Object Name)
        $failed = @($cells | Where-Object outcome -eq 'Failed').Count
        $skipped = @($cells | Where-Object outcome -eq 'Skipped').Count
        $unparseable = @($cells | Where-Object { -not $_.key -or $_.outcome -eq 'Unknown' }).Count
        $structural = $missing.Count + $unexpected.Count + $duplicates.Count + $unparseable
        $behaviorVerdict = if ($failed -gt 0) { 'RED' }
            elseif ($structural -gt 0 -or $testExit -ne 0) { 'ERROR' }
            elseif ($skipped -gt 0) { 'INCONCLUSIVE' }
            else { 'GREEN' }
        $reasons = New-Object System.Collections.Generic.List[string]
        if ($testExit -ne 0 -and $failed -eq 0) { $reasons.Add("dotnet test exited $testExit without a failed conformance cell") }
        if ($missing.Count) { $reasons.Add("missing: $($missing -join ', ')") }
        if ($unexpected.Count) { $reasons.Add("unexpected: $($unexpected -join ', ')") }
        if ($duplicates.Count) { $reasons.Add("duplicate: $($duplicates -join ', ')") }
        if ($unparseable) { $reasons.Add("unparseable or unknown outcomes: $unparseable") }
        $packet = [pscustomobject]@{ status = 'NotRun'; exitCode = 0; reason = ''; path = $null }
        if ($Strict) {
            $packetId = Get-EvidencePacketId $target
            $packetPath = Join-Path (Join-Path $resolvedPacketRoot $packetId) 'conformance.json'
            $packetResult = Invoke-PacketValidation $packetPath
            $packet = [pscustomobject]@{
                status = $packetResult.status
                exitCode = $packetResult.exitCode
                reason = $packetResult.reason
                path = Get-RelativePath $packetPath
            }
            if ($packet.reason) { $reasons.Add($packet.reason) }
        }
        $verdict = if ($behaviorVerdict -eq 'ERROR' -or $packet.status -eq 'Error') { 'ERROR' }
            elseif ($behaviorVerdict -eq 'RED' -or $packet.status -eq 'Red') { 'RED' }
            elseif ($packet.status -eq 'Infrastructure' -or ($Strict -and $behaviorVerdict -eq 'INCONCLUSIVE')) { 'INFRASTRUCTURE' }
            elseif ($packet.status -eq 'Deferred' -or $behaviorVerdict -eq 'INCONCLUSIVE') { 'DEFERRED' }
            else { 'GREEN' }
        $adapterReports.Add([pscustomobject]@{
                adapter = $target.adapter
                plane = $target.plane
                verdict = $verdict
                behaviorVerdict = $behaviorVerdict
                packet = $packet
                project = Get-RelativePath $target.project
                spec = $target.spec
                expectedCells = @($target.expectedCells)
                cells = $cells
                missingCells = $missing
                unexpectedCells = $unexpected
                duplicateCells = $duplicates
                reason = $reasons -join '; '
                consumedSources = @(
                    [pscustomobject]@{ path = Get-RelativePath $primerPath; sha256 = Get-Sha256 $primerPath },
                    [pscustomobject]@{ path = Get-RelativePath $suiteSources[$target.plane]; sha256 = Get-Sha256 $suiteSources[$target.plane] },
                    [pscustomobject]@{ path = $target.spec; sha256 = Get-Sha256 (Join-Path $repoRoot $target.spec) },
                    [pscustomobject]@{ path = Get-RelativePath $target.project; sha256 = Get-Sha256 $target.project }
                )
            }) | Out-Null
    }

    $red = @($adapterReports | Where-Object verdict -eq 'RED').Count
    $errors = @($adapterReports | Where-Object verdict -eq 'ERROR').Count
    $deferred = @($adapterReports | Where-Object verdict -eq 'DEFERRED').Count
    $infrastructure = @($adapterReports | Where-Object verdict -eq 'INFRASTRUCTURE').Count
    $green = @($adapterReports | Where-Object verdict -eq 'GREEN').Count
    $verdict = if ($errors) { 'ERROR' } elseif ($red) { 'RED' } elseif ($infrastructure) { 'INFRASTRUCTURE' }
        elseif ($deferred) { 'DEFERRED' } else { 'GREEN' }
    $report = [pscustomobject]@{
        protocolVersion = $protocolVersion
        strict = [bool]$Strict
        verdict = $verdict
        catalog = $catalogReport
        summary = [pscustomobject]@{
            adapters = $adapterReports.Count; green = $green; red = $red
            deferred = $deferred; infrastructure = $infrastructure; errors = $errors
        }
        adapters = @($adapterReports | ForEach-Object { $_ })
    }

    if ($Output -eq 'json') { $report | ConvertTo-Json -Depth 12 }
    else {
        $rows = foreach ($adapterReport in $adapterReports) {
            if ($adapterReport.cells.Count -eq 0) {
                [pscustomobject]@{ Adapter = $adapterReport.adapter; Plane = $adapterReport.plane; Cell = '-'; Outcome = $adapterReport.verdict }
            }
            else {
                foreach ($cell in $adapterReport.cells) {
                    [pscustomobject]@{ Adapter = $adapterReport.adapter; Plane = $adapterReport.plane; Cell = $cell.key; Outcome = $cell.outcome }
                }
            }
        }
        $rows | Format-Table -AutoSize | Out-String | Write-Host
        foreach ($adapterReport in $adapterReports) {
            $suffix = if ($adapterReport.reason) { " - $($adapterReport.reason)" } else { '' }
            Write-Host "$($adapterReport.verdict) $($adapterReport.plane)/$($adapterReport.adapter)$suffix"
        }
        Write-Host "FORGE $verdict adapters=$($adapterReports.Count) green=$green red=$red deferred=$deferred infrastructure=$infrastructure errors=$errors protocol=$protocolVersion"
    }

    if ($errors) { exit 3 }
    if ($red) { exit 1 }
    if ($infrastructure) { exit 4 }
    if ($deferred) { exit 2 }
    exit 0
}
catch {
    Write-Error "FORGE: $($_.Exception.Message) [$($_.ScriptStackTrace)]" -ErrorAction Continue
    exit 3
}
finally {
    if ($resultsRoot -and (Test-Path -LiteralPath $resultsRoot)) {
        $resolvedTemp = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\')
        $resolvedResults = [IO.Path]::GetFullPath($resultsRoot)
        if ([IO.Path]::GetDirectoryName($resolvedResults).TrimEnd('\') -eq $resolvedTemp -and
            [IO.Path]::GetFileName($resolvedResults).StartsWith('koan-forge-', [StringComparison]::Ordinal)) {
            Remove-Item -LiteralPath $resolvedResults -Recurse -Force -ErrorAction SilentlyContinue
        }
    }
}
