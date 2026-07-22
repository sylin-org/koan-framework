<#
.SYNOPSIS
  forge-verify.ps1 — the ARCH-0094 Adapter Forge Conformance Gate runner (Phase 2).

.DESCRIPTION
  Runs the AODB Conformance Gate — the real-store xUnit conformance kit (AodbConformanceSpecsBase /
  VectorAodbConformanceSpecsBase, capability-driven via CapabilityConformanceGate, ARCH-0094 Phase 1) — for one
  adapter (or all), against real instances, and emits a MACHINE-PARSEABLE Gate VERDICT. This is the orchestrable
  step the Forge's agent loop consumes (Phase 4: agent -> blueprint -> gate -> retry), plus a human-readable table.

  It does NOT re-implement the harness. It discovers each adapter's `*AodbConformanceSpec` test project and delegates
  the exact `FullyQualifiedName~Aodb` selection, deadline, process-tree ownership, TRX parsing, and required-result
  verdict to `Koan.Packaging admission`. It maps each returned cell to its AODB mode (Declares / Streaming / Shared /
  Container / Database for record adapters;
  Declares / Shared / Container / Database for vector adapters), and aggregates a per-adapter verdict:

    GREEN    — every cell passed (the adapter realizes every mode it declares; shippable)
    RED      — a cell FAILED (an isolation lie: declared-but-not-realized, or a leak)
    SKIPPED  — every expected mode ran but a cell was skipped (e.g. Docker unavailable, or a capability-driven Skip)
               and none failed; inconclusive
    ERROR    — a structural problem: no .csproj, no TRX produced, or an expected mode (cell) never ran / was
               undiscovered. NEVER green — the gate could not be assessed.

  The gate-level verdict aggregates these as GREEN / RED / INCONCLUSIVE. General over ANY project carrying a
  `*AodbConformanceSpec` under tests/Suites/Data (the `<Adapter>[Vector]AodbConformanceSpec` naming + a sibling
  .csproj), so a future agent-authored adapter plugs in identically.

  Exit code: 0 = all GREEN  ·  1 = any RED (fix the adapter)  ·  3 = any ERROR (fix the project/structure)  ·
             2 = no RED/ERROR but some SKIPPED (fix the environment, e.g. start Docker).

.EXAMPLE
  pwsh scripts/forge-verify.ps1 -Adapter Mongo
  pwsh scripts/forge-verify.ps1 -DockerFree
  pwsh scripts/forge-verify.ps1 -All -Output json
#>
[CmdletBinding()]
param(
    [string]$Adapter,
    [ValidateSet('', 'record', 'vector')][string]$Plane = '',
    [switch]$All,
    [switch]$DockerFree,
    [ValidateSet('table', 'json')][string]$Output = 'table',
    [string]$Configuration = 'Debug',
    [ValidateRange(1, 3600)][int]$DeadlineSeconds = 600,
    [switch]$NoBuild
)

$ErrorActionPreference = 'Stop'
$resultsRoot = $null
Push-Location (Resolve-Path "$PSScriptRoot/..")
try {
    $repoRoot = (Get-Location).ProviderPath

    # The Docker-free conformance surfaces (in-process / file) — fast local + agent-iteration runs without containers.
    $dockerFreeSet = @('record/InMemory', 'record/Json', 'record/Sqlite', 'vector/InMemory', 'vector/SqliteVec')
    $modeOrder = @('Declares', 'Streaming', 'Shared', 'Container', 'Database')

    function Get-Mode([string]$methodName) {
        switch -regex ($methodName) {
            'Declares'  { return 'Declares' }
            'Streaming' { return 'Streaming' }
            'Shared'    { return 'Shared' }
            'Container' { return 'Container' }
            'Database'  { return 'Database' }
            default     { return $methodName }
        }
    }

    # ---- discover the conformance projects (exactly one *AodbConformanceSpec per adapter/plane) ----
    $specs = Get-ChildItem -Path (Join-Path $repoRoot 'tests/Suites/Data') -Recurse -Filter '*AodbConformanceSpec.cs' |
        Sort-Object FullName
    $targets = New-Object System.Collections.Generic.List[object]
    foreach ($s in $specs) {
        $isVector = $s.Name -match 'VectorAodbConformanceSpec\.cs$'
        $tPlane = if ($isVector) { 'vector' } else { 'record' }
        $name = $s.Name -replace 'VectorAodbConformanceSpec\.cs$', '' -replace 'AodbConformanceSpec\.cs$', ''
        # nearest .csproj walking up from the spec's directory
        $dir = $s.Directory
        while ($dir -and -not (Get-ChildItem -Path $dir.FullName -Filter '*.csproj' -File)) { $dir = $dir.Parent }
        $proj = if ($dir) { (Get-ChildItem -Path $dir.FullName -Filter '*.csproj' -File | Select-Object -First 1).FullName } else { $null }
        $targets.Add([PSCustomObject]@{ Adapter = $name; Plane = $tPlane; Key = "$tPlane/$name"; Project = $proj }) | Out-Null
    }

    # ---- select ----
    $selected = $targets
    if ($DockerFree) { $selected = $selected | Where-Object { $dockerFreeSet -contains $_.Key } }
    elseif ($Adapter) { $selected = $selected | Where-Object { $_.Adapter -ieq $Adapter -and ($Plane -eq '' -or $_.Plane -eq $Plane) } }
    elseif (-not $All) {
        Write-Host "forge-verify: specify -Adapter <name> [-Plane record|vector], -DockerFree, or -All." -ForegroundColor Yellow
        exit 2
    }
    $selected = @($selected)
    if ($selected.Count -eq 0) { Write-Host "forge-verify: no conformance projects matched the selection." -ForegroundColor Yellow; exit 2 }

    # ---- run + parse each ----
    $resultsRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("forge-verify-" + [guid]::NewGuid().ToString('n'))
    New-Item -ItemType Directory -Path $resultsRoot -Force | Out-Null

    $adapterReports = New-Object System.Collections.Generic.List[object]
    foreach ($t in $selected) {
        $expectedModes = if ($t.Plane -eq 'record') { @($modeOrder) } else { @('Declares', 'Shared', 'Container', 'Database') }
        if (-not $t.Project) {
            $adapterReports.Add([PSCustomObject]@{ Adapter = $t.Adapter; Plane = $t.Plane; Verdict = 'ERROR'; Project = $null; Cells = @(); ExpectedModes = $expectedModes; MissingModes = $expectedModes; Reason = 'no .csproj found next to the spec' }) | Out-Null
            continue
        }
        $admissionPath = Join-Path $resultsRoot "$($t.Plane)-$($t.Adapter).admission.json"
        $relativeProject = [System.IO.Path]::GetRelativePath($repoRoot, $t.Project).Replace('\', '/')
        $dotnetArgs = @(
            'run', '--configuration', $Configuration, '--project', 'tools/Koan.Packaging/Koan.Packaging.csproj', '--',
            'admission', '--id', "aodb:$($t.Plane):$($t.Adapter.ToLowerInvariant())", '--project', $relativeProject,
            '--filter', 'FullyQualifiedName~Aodb', '--lane', 'native', '--phase', 'lifecycle',
            '--deadline-seconds', $DeadlineSeconds.ToString(), '--configuration', $Configuration,
            '--output', $admissionPath
        )
        if ($NoBuild) { $dotnetArgs += '--no-build' }
        Write-Host ("forge-verify: running {0}/{1} ..." -f $t.Plane, $t.Adapter) -ForegroundColor DarkGray
        $out = & dotnet @dotnetArgs 2>&1
        $code = $LASTEXITCODE

        if (-not (Test-Path $admissionPath)) {
            $tail = ($out | Select-Object -Last 14) -join "`n"
            $adapterReports.Add([PSCustomObject]@{ Adapter = $t.Adapter; Plane = $t.Plane; Verdict = 'ERROR'; Project = $t.Project; Cells = @(); ExpectedModes = $expectedModes; MissingModes = $expectedModes; Reason = "admission produced no result (exit $code):`n$tail" }) | Out-Null
            continue
        }

        $admission = Get-Content -LiteralPath $admissionPath -Raw | ConvertFrom-Json
        $cells = New-Object System.Collections.Generic.List[object]
        foreach ($r in @($admission.results)) {
            $mode = Get-Mode ([string]$r.name)
            $outcome = switch ([string]$r.outcome) { 'Passed' { 'Passed' } 'Failed' { 'Failed' } { $_ -in @('NotExecuted', 'Skipped') } { 'Skipped' } default { [string]$r.outcome } }
            $cells.Add([PSCustomObject]@{ Mode = $mode; Method = [string]$r.name; Outcome = $outcome; Reason = [string]$r.detail }) | Out-Null
        }
        $cellArr = @($cells | Sort-Object { [array]::IndexOf($modeOrder, $_.Mode) })
        $failed = @($cellArr | Where-Object { $_.Outcome -eq 'Failed' }).Count
        $skipped = @($cellArr | Where-Object { $_.Outcome -eq 'Skipped' }).Count
        $passed = @($cellArr | Where-Object { $_.Outcome -eq 'Passed' }).Count
        $presentModes = @($cellArr | ForEach-Object { $_.Mode } | Select-Object -Unique)
        $missingModes = @($expectedModes | Where-Object { $presentModes -notcontains $_ })
        $unexpectedModes = @($presentModes | Where-Object { $expectedModes -notcontains $_ })
        $duplicateModes = @($cellArr | Group-Object Mode | Where-Object Count -ne 1 | ForEach-Object Name)
        $unknownOutcomes = [int]$admission.unknown
        $verdict = if ($failed -gt 0) { 'RED' }
            elseif ($missingModes.Count -gt 0 -or $unexpectedModes.Count -gt 0 -or $duplicateModes.Count -gt 0 -or
                $unknownOutcomes -gt 0 -or [bool]$admission.timedOut -or [int]$admission.processExitCode -ne 0 -or $code -ne 0) { 'ERROR' }
            elseif ($skipped -gt 0) { 'SKIPPED' }
            elseif ([string]$admission.verdict -ne 'passed') { 'ERROR' }
            else { 'GREEN' }
        $structuralReasons = New-Object System.Collections.Generic.List[string]
        if ($missingModes.Count -gt 0) { $structuralReasons.Add("expected mode(s) not run: $($missingModes -join ', ')") }
        if ($unexpectedModes.Count -gt 0) { $structuralReasons.Add("unexpected mode(s): $($unexpectedModes -join ', ')") }
        if ($duplicateModes.Count -gt 0) { $structuralReasons.Add("mode(s) did not run exactly once: $($duplicateModes -join ', ')") }
        foreach ($message in @($admission.reasons)) { if ($message) { $structuralReasons.Add([string]$message) } }
        $reason = $structuralReasons -join '; '
        $adapterReports.Add([PSCustomObject]@{ Adapter = $t.Adapter; Plane = $t.Plane; Verdict = $verdict; Project = $t.Project; Cells = $cellArr; ExpectedModes = $expectedModes; Passed = $passed; Failed = $failed; Skipped = $skipped; MissingModes = $missingModes; Reason = $reason }) | Out-Null
    }

    # ---- aggregate the gate verdict ----
    $green = @($adapterReports | Where-Object { $_.Verdict -eq 'GREEN' }).Count
    $red = @($adapterReports | Where-Object { $_.Verdict -eq 'RED' }).Count
    $errored = @($adapterReports | Where-Object { $_.Verdict -eq 'ERROR' }).Count
    $skippedAdapters = @($adapterReports | Where-Object { $_.Verdict -eq 'SKIPPED' }).Count
    $inconclusive = $errored + $skippedAdapters
    $gateVerdict = if ($red -gt 0) { 'RED' }
        elseif ($adapterReports.Count -gt 0 -and $green -eq $adapterReports.Count) { 'GREEN' }
        else { 'INCONCLUSIVE' }

    if ($Output -eq 'json') {
        $report = [PSCustomObject]@{
            timestamp = (Get-Date).ToUniversalTime().ToString('o')
            gate      = 'aodb-conformance'
            verdict   = $gateVerdict
            summary   = [PSCustomObject]@{ adapters = $adapterReports.Count; green = $green; red = $red; errored = $errored; skipped = $skippedAdapters }
            adapters  = @($adapterReports | ForEach-Object {
                    [PSCustomObject]@{
                        adapter       = $_.Adapter; plane = $_.Plane; verdict = $_.Verdict; project = $_.Project
                        expectedCells = @($_.ExpectedModes).Count
                        actualCells   = @($_.Cells).Count
                        missingModes  = @($_.MissingModes)
                        cells         = @($_.Cells | ForEach-Object { [PSCustomObject]@{ mode = $_.Mode; outcome = $_.Outcome; reason = $_.Reason } })
                        reason        = $_.Reason
                    }
                })
        }
        $report | ConvertTo-Json -Depth 8
    }
    else {
        $flat = foreach ($a in $adapterReports) {
            if ($a.Cells.Count -eq 0) {
                [PSCustomObject]@{ Adapter = $a.Adapter; Plane = $a.Plane; Mode = '-'; Outcome = $a.Verdict; Reason = (($a.Reason -split "`n")[0]) }
            }
            else {
                foreach ($c in $a.Cells) {
                    $rf = if ($c.Reason) { ($c.Reason -split "`n")[0] } else { '' }
                    if ($rf.Length -gt 70) { $rf = $rf.Substring(0, 67) + '...' }
                    [PSCustomObject]@{ Adapter = $a.Adapter; Plane = $a.Plane; Mode = $c.Mode; Outcome = $c.Outcome; Reason = $rf }
                }
            }
        }
        $flat | Format-Table -AutoSize | Out-String | Write-Host
        foreach ($a in ($adapterReports | Sort-Object Plane, Adapter)) {
            $color = switch ($a.Verdict) { 'GREEN' { 'Green' } 'RED' { 'Red' } default { 'Yellow' } }
            $note = if ($a.Reason) { '  — ' + (($a.Reason -split "`n")[0]) } else { '' }
            Write-Host ("  {0,-7} {1}/{2}{3}" -f $a.Verdict, $a.Plane, $a.Adapter, $note) -ForegroundColor $color
        }
        $gateColor = switch ($gateVerdict) { 'GREEN' { 'Green' } 'RED' { 'Red' } default { 'Yellow' } }
        Write-Host ""
        Write-Host ("GATE aodb-conformance: {0}  ({1} adapters · {2} green · {3} red · {4} error · {5} skipped)" -f $gateVerdict, $adapterReports.Count, $green, $red, $errored, $skippedAdapters) -ForegroundColor $gateColor
    }

    # Exit: RED (a real failure) dominates, then ERROR (structural), then SKIPPED (environment), then GREEN.
    if ($red -gt 0) { exit 1 }
    if ($errored -gt 0) { exit 3 }
    if ($gateVerdict -ne 'GREEN') { exit 2 }
    exit 0
}
finally {
    Pop-Location
    if ($resultsRoot -and (Test-Path $resultsRoot)) { Remove-Item -Recurse -Force $resultsRoot -ErrorAction SilentlyContinue }
}
