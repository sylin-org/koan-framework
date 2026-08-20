<#
.SYNOPSIS
  Merge per-project run records and their TRX files into one deterministic test manifest (PMC-020).

.DESCRIPTION
  The ratchet's test leg prints a per-project summary, which a buffered or truncated supervising console
  cannot reconstruct afterwards — and the failing or hung project is exactly what gets lost first. This
  aggregator turns that transient output into one retained artifact.

  It is deliberately a separate script rather than an inline block, so the failure and evidence-gap paths
  can be exercised directly against crafted inputs instead of only on whichever run happens to be red.

  **It never decides the verdict.** The ratchet owns pass/fail from process exit codes; this script only
  describes what happened. An unreadable or absent TRX is recorded as an evidence gap, never reinterpreted
  as a passing or failing test.

.PARAMETER RunRecordPath
  JSON array written by the ratchet: project, lane, exitCode, elapsedSeconds, startedUtc, trx.

.PARAMETER OutputPath
  Where to write the manifest.

.PARAMETER RepositoryRoot
  Root used to relativize paths, so a manifest produced locally and one produced in CI compare equal.
#>
param(
    [Parameter(Mandatory)][string]$RunRecordPath,
    [Parameter(Mandatory)][string]$OutputPath,
    [Parameter(Mandatory)][string]$RepositoryRoot,
    [string]$Configuration = 'Debug',
    [int]$Concurrency = 0,
    [string]$HangTimeout = '',
    [string]$StartedUtc = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if (-not (Test-Path -LiteralPath $RunRecordPath)) {
    throw "Run-record file '$RunRecordPath' does not exist; the test leg must write it before aggregating."
}

$records = @(Get-Content -Raw -LiteralPath $RunRecordPath | ConvertFrom-Json)
$root = (Resolve-Path -LiteralPath $RepositoryRoot).Path

function ConvertTo-RepoRelative([string]$path) {
    if ([string]::IsNullOrWhiteSpace($path)) { return '' }
    return ([System.IO.Path]::GetRelativePath($root, $path) -replace '\\', '/')
}

# Sorted by project path so two runs of the same set produce byte-identical ordering regardless of which
# host finished first. Completion order is recoverable from startedUtc + elapsedSeconds.
$projects = foreach ($record in ($records | Sort-Object project)) {
    $total = $null; $passed = $null; $failed = $null; $skipped = $null
    $trxState = 'missing'

    if (-not [string]::IsNullOrWhiteSpace($record.trx) -and (Test-Path -LiteralPath $record.trx)) {
        try {
            [xml]$trx = Get-Content -Raw -LiteralPath $record.trx
            $counters = $trx.TestRun.ResultSummary.Counters
            $total = [int]$counters.total
            $executed = [int]$counters.executed
            $passed = [int]$counters.passed
            # Anything that did not pass but did run is a failure for reporting purposes; VSTest splits it
            # across four attributes and a manifest that only read 'failed' would under-report.
            $failed = [int]$counters.failed + [int]$counters.error + [int]$counters.timeout + [int]$counters.aborted
            $skipped = $total - $executed
            $trxState = ConvertTo-RepoRelative $record.trx
        }
        catch {
            $trxState = 'unreadable'
        }
    }

    [pscustomobject]@{
        project        = ($record.project -replace '\\', '/')
        lane           = $record.lane
        status         = $(if ($record.exitCode -eq 0) { 'PASS' } else { 'FAIL' })
        exitCode       = [int]$record.exitCode
        startedUtc     = $record.startedUtc
        elapsedSeconds = [Math]::Round([double]$record.elapsedSeconds, 3)
        total          = $total
        passed         = $passed
        failed         = $failed
        skipped        = $skipped
        trx            = $trxState
    }
}
$projects = @($projects)

function Measure-Column([string]$name) {
    $present = @($projects | Where-Object { $null -ne $_.$name })
    if ($present.Count -eq 0) { return 0 }
    return [int]($present | Measure-Object -Property $name -Sum).Sum
}

# A project with no readable TRX did not finish reporting. A host killed by --blame-hang-timeout looks
# exactly like this, so name it rather than leaving a silent hole in the totals.
$unreported = @($projects | Where-Object { $_.trx -in @('missing', 'unreadable') } | ForEach-Object project)
$failedProjects = @($projects | Where-Object status -eq 'FAIL' | ForEach-Object project)

$manifest = [ordered]@{
    schemaVersion = 1
    configuration = $Configuration
    startedUtc    = $StartedUtc
    completedUtc  = ([DateTimeOffset]::UtcNow).ToString('o')
    concurrency   = $Concurrency
    hangTimeout   = $HangTimeout
    aggregate     = [ordered]@{
        projects           = $projects.Count
        projectsFailed     = $failedProjects.Count
        total              = Measure-Column 'total'
        passed             = Measure-Column 'passed'
        failed             = Measure-Column 'failed'
        skipped            = Measure-Column 'skipped'
        elapsedSecondsSum  = [Math]::Round((@($projects | Measure-Object -Property elapsedSeconds -Sum).Sum), 3)
        failedProjects     = $failedProjects
        unreportedProjects = $unreported
    }
    projects      = $projects
}

$outputDirectory = Split-Path -Parent $OutputPath
if ($outputDirectory -and -not (Test-Path -LiteralPath $outputDirectory)) {
    New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
}
$manifest | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $OutputPath -Encoding utf8

$aggregate = $manifest.aggregate
# One line a truncated console can still carry out of a long run.
Write-Host ("[ratchet] TEST-AGGREGATE|projects={0}|projectsFailed={1}|total={2}|passed={3}|failed={4}|skipped={5}|manifest={6}" -f `
        $aggregate.projects, $aggregate.projectsFailed, $aggregate.total, $aggregate.passed, `
        $aggregate.failed, $aggregate.skipped, (ConvertTo-RepoRelative $OutputPath))

if ($failedProjects.Count -gt 0) {
    Write-Host "[ratchet] TEST-FAILED-PROJECTS|$($failedProjects -join ',')" -ForegroundColor Red
}
if ($unreported.Count -gt 0) {
    Write-Host "[ratchet] TEST-UNREPORTED-PROJECTS|$($unreported -join ',')" -ForegroundColor Yellow
}

# Describing a failed run is a successful aggregation. The ratchet decides the verdict.
exit 0
