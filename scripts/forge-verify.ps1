<#
.SYNOPSIS
  forge-verify.ps1 — the ARCH-0094 Adapter Forge Conformance Gate runner (Phase 2).

.DESCRIPTION
  Runs the AODB Conformance Gate — the real-store xUnit conformance kit (AodbConformanceSpecsBase /
  VectorAodbConformanceSpecsBase, capability-driven via CapabilityConformanceGate, ARCH-0094 Phase 1) — for one
  adapter (or all), against real instances, and emits a MACHINE-PARSEABLE Gate VERDICT. This is the orchestrable
  step the Forge's agent loop consumes (Phase 4: agent -> blueprint -> gate -> retry), plus a human-readable table.

  It does NOT re-implement the harness. It discovers each adapter's `*AodbConformanceSpec` test project, runs its
  conformance cells via `dotnet test --filter FullyQualifiedName~Aodb` with a TRX logger, parses the TRX, maps each
  cell to its AODB mode (Declares / Shared / Container / Database), and aggregates a per-adapter verdict:

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
    [switch]$NoBuild
)

$ErrorActionPreference = 'Stop'
$resultsRoot = $null
Push-Location (Resolve-Path "$PSScriptRoot/..")
try {
    $repoRoot = (Get-Location).ProviderPath

    # The Docker-free conformance surfaces (in-process / file) — fast local + agent-iteration runs without containers.
    $dockerFreeSet = @('record/InMemory', 'record/Json', 'record/Sqlite', 'vector/InMemory', 'vector/SqliteVec')
    $modeOrder = @('Declares', 'Shared', 'Container', 'Database')

    function Get-Mode([string]$methodName) {
        switch -regex ($methodName) {
            'Declares'  { return 'Declares' }
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
        if (-not $t.Project) {
            $adapterReports.Add([PSCustomObject]@{ Adapter = $t.Adapter; Plane = $t.Plane; Verdict = 'ERROR'; Project = $null; Cells = @(); MissingModes = @($modeOrder); Reason = 'no .csproj found next to the spec' }) | Out-Null
            continue
        }
        $trxName = "$($t.Plane)-$($t.Adapter).trx"
        $trx = Join-Path $resultsRoot $trxName
        $dotnetArgs = @('test', $t.Project, '-c', $Configuration, '--filter', 'FullyQualifiedName~Aodb',
            '--logger', "trx;LogFileName=$trxName", '--results-directory', $resultsRoot)
        if ($NoBuild) { $dotnetArgs += '--no-build' }
        Write-Host ("forge-verify: running {0}/{1} ..." -f $t.Plane, $t.Adapter) -ForegroundColor DarkGray
        $out = & dotnet @dotnetArgs 2>&1
        $code = $LASTEXITCODE

        if (-not (Test-Path $trx)) {
            $tail = ($out | Select-Object -Last 14) -join "`n"
            $adapterReports.Add([PSCustomObject]@{ Adapter = $t.Adapter; Plane = $t.Plane; Verdict = 'ERROR'; Project = $t.Project; Cells = @(); MissingModes = @($modeOrder); Reason = "dotnet test produced no TRX (exit $code):`n$tail" }) | Out-Null
            continue
        }

        [xml]$xml = Get-Content -Raw $trx
        $idToMethod = @{}
        foreach ($ut in $xml.TestRun.TestDefinitions.UnitTest) { $idToMethod[[string]$ut.id] = [string]$ut.TestMethod.name }
        $cells = New-Object System.Collections.Generic.List[object]
        foreach ($r in $xml.TestRun.Results.UnitTestResult) {
            $method = $idToMethod[[string]$r.testId]
            if (-not $method) { $method = '[unknown]' }   # testId absent from TestDefinitions — surfaced, never silent
            $mode = Get-Mode $method
            $outcome = switch ([string]$r.outcome) { 'Passed' { 'Passed' } 'Failed' { 'Failed' } 'NotExecuted' { 'Skipped' } default { [string]$r.outcome } }
            $reason = ''
            if ($r.Output -and $r.Output.ErrorInfo -and $r.Output.ErrorInfo.Message) { $reason = ([string]$r.Output.ErrorInfo.Message).Trim() }
            elseif ($r.Output -and $r.Output.StdOut) { $reason = ([string]$r.Output.StdOut).Trim() }   # defensive fallback
            $cells.Add([PSCustomObject]@{ Mode = $mode; Method = $method; Outcome = $outcome; Reason = $reason }) | Out-Null
        }
        $cellArr = @($cells | Sort-Object { [array]::IndexOf($modeOrder, $_.Mode) })
        $failed = @($cellArr | Where-Object { $_.Outcome -eq 'Failed' }).Count
        $skipped = @($cellArr | Where-Object { $_.Outcome -eq 'Skipped' }).Count
        $passed = @($cellArr | Where-Object { $_.Outcome -eq 'Passed' }).Count
        # Every conformance spec inherits all four cells (Declares/Shared/Container/Database), so all four MUST appear in
        # the TRX. A missing mode (or zero cells) = a discovery/filter failure — an ERROR, NEVER a silent GREEN.
        $presentModes = @($cellArr | ForEach-Object { $_.Mode } | Select-Object -Unique)
        $missingModes = @($modeOrder | Where-Object { $presentModes -notcontains $_ })
        # Precedence: RED (a real failure) > ERROR (a mode never ran) > SKIPPED (ran but skipped) > GREEN (all four passed).
        $verdict = if ($failed -gt 0) { 'RED' }
            elseif ($missingModes.Count -gt 0) { 'ERROR' }
            elseif ($skipped -gt 0) { 'SKIPPED' }
            else { 'GREEN' }
        $reason = if ($verdict -eq 'ERROR') { "expected mode(s) not run: $($missingModes -join ', ')" } else { '' }
        $adapterReports.Add([PSCustomObject]@{ Adapter = $t.Adapter; Plane = $t.Plane; Verdict = $verdict; Project = $t.Project; Cells = $cellArr; Passed = $passed; Failed = $failed; Skipped = $skipped; MissingModes = $missingModes; Reason = $reason }) | Out-Null
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
                        expectedCells = $modeOrder.Count
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
