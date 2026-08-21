<#
.SYNOPSIS
  The Koan green ratchet — the single merge gate for the foundation-consolidation work.
  (Facet 0 of docs/architecture/foundation-consolidation-plan.md.)

.DESCRIPTION
  Runs the consolidation gate and fails if any leg fails:

    0.  Tools       dotnet tool restore      — pinned repository tools used by executable proofs.
    A.  Build       dotnet build Koan.sln   — framework + every dogfood sample
                    (the samples live in Koan.sln, so one build covers both).
    A'. Test        dotnet test per project  — runs every deterministic suite in its own host lifecycle
                    through a bounded parallel wave with per-host hang detection. Real provider
                    boundaries are owned by direct workflow checks. Skip with -SkipTests.
                    Every project emits a TRX; scripts/aggregate-test-evidence.ps1 merges them into
                    artifacts/ratchet/test-manifest.json with per-project and aggregate
                    total/passed/failed/skipped/duration, the failing projects, and any project that
                    produced no readable TRX (what a hang looks like). The manifest is written before
                    the verdict, so a red run keeps its evidence. It never changes pass/fail.
    B.  Docs lint   scripts/docs-lint.ps1    — links / front-matter / anchors / terms.
                    Errors are fatal; warnings are not.
    B'. Public docs scripts/public-docs-lint.ps1 — current navigation boundary, retired
                    product vocabulary, canonical host lifetime, and ADR immutability.
    C.  Doc code    scripts/validate-code-examples.ps1 — compiles the C# blocks in the
                    INSTRUCTIONAL docs a change touches (diff-scoped vs -Base) that are
                    marked `<!-- validate -->` (OPT-IN: a block is compiled only when an
                    author asserts it is a complete, self-contained example; everything else
                    is prose-grade). Decision/design/proposal/archive docs are out of scope.
                    Use -FullDocs for the full-surface sweep. As of DX-0048 the
                    .claude/skills/ canonical-pattern blocks are in scope here too.
    D.  Skills lint scripts/skills-lint.ps1 -Strict — the DX-0048 skill contract:
                    dir==name, frontmatter (name/description), no version pins, and
                    link/card resolution + catalog parity (now fatal — H10 complete).
    E.  Lockfile    scripts/compare-koan-lock.ps1 — composition-lockfile drift (P1.1): the build
                    regenerates each app's koan.lock.json; fail if one drifted uncommitted.
  NOT in this gate: the NativeAOT claim. ILC forbids things the JIT allows, so no leg here can see a
  binary that fails to publish or dies on startup. That is `scripts/aot-verify.ps1`, which publishes and
  runs the AotRelational sample and is scheduled daily in aot-verify.yml rather than run from here --
  a manual boundary is the status that let the previous AOT proof decay for five weeks (PMC-050).

  Exit code is 0 (GREEN) only when every run leg passes; otherwise 1 (RED).

.PARAMETER Base
  Git ref to diff instructional docs against for Leg C. Default: origin/main if present, else main.

.PARAMETER PublicRelease
  Build the exact public package identity and enable the release audit floor. Used by the dev release compiler.

.PARAMETER TestProjectConcurrency
  Maximum number of isolated test-project processes in flight. Zero selects the processor-count-derived default,
  capped at four. Set an explicit positive value only for a constrained or intentionally wider host.

.EXAMPLE
  pwsh scripts/green-ratchet.ps1                 # full gate, diff-scoped doc-code vs main
  pwsh scripts/green-ratchet.ps1 -SkipTests      # build + lints only (fast)
  pwsh scripts/green-ratchet.ps1 -FullDocs       # run the whole-surface doc-code sweep
#>
param(
    [string]$Configuration = "Debug",
    [string]$Base = "",
    [switch]$SkipTests,
    [switch]$FullDocs,
    [switch]$PublicRelease,
    [ValidateRange(0, 16)]
    [int]$TestProjectConcurrency = 0
)

$ErrorActionPreference = 'Continue'
$root = (Resolve-Path "$PSScriptRoot/..").ProviderPath
# Certification policy: provider suites remain comprehensive and project-isolated; shared provider
# resources cannot make release evidence order-dependent, and an inactive host cannot retain the queue.
$testProjectMarker = 'Microsoft.NET.Test.Sdk'
# PMC-020: a run that only prints its evidence has none. A buffered or truncated supervising console
# cannot reconstruct aggregate counts afterwards, and the failing or hung project is exactly what gets
# lost first. Every project emits a TRX; one deterministic manifest aggregates them.
$ratchetEvidenceRoot = Join-Path $root 'artifacts/ratchet'
$ratchetTrxRoot = Join-Path $ratchetEvidenceRoot 'trx'
$ratchetTestManifest = Join-Path $ratchetEvidenceRoot 'test-manifest.json'
$testHostHangTimeout = '5m'
$effectiveTestProjectConcurrency = if ($TestProjectConcurrency -gt 0) {
    $TestProjectConcurrency
}
else {
    [Math]::Min(4, [Math]::Max(1, [Environment]::ProcessorCount))
}
Push-Location $root

$results = [ordered]@{}
function Invoke-Leg {
    param([string]$Name, [scriptblock]$Action)
    Write-Host "`n=== [ratchet] $Name ===" -ForegroundColor Cyan
    & $Action
    $ok = ($LASTEXITCODE -eq 0)
    $results[$Name] = $ok
    if ($ok) { Write-Host "[ratchet] $Name : PASS" -ForegroundColor Green }
    else { Write-Host "[ratchet] $Name : FAIL (exit $LASTEXITCODE)" -ForegroundColor Red }
}

try {
    if (-not $Base) {
        & git rev-parse --verify --quiet origin/main *> $null
        $Base = if ($LASTEXITCODE -eq 0) { 'origin/main' } else { 'main' }
    }
    Write-Host "[ratchet] config=$Configuration  docs-base=$Base  skipTests=$SkipTests  fullDocs=$FullDocs  publicRelease=$PublicRelease"
    Write-Host "[ratchet] test-project-isolation=one-process-per-project  test-project-concurrency=$effectiveTestProjectConcurrency  test-host-hang-timeout=$testHostHangTimeout"

    Invoke-Leg '0. tools' { & dotnet tool restore }

    Invoke-Leg 'A. build' {
        $arguments = @('build', "$root/Koan.sln", '-c', $Configuration, '--nologo')
        if ($PublicRelease) {
            $arguments += @(
                '-p:PublicRelease=true',
                '-p:NuGetAuditMode=all',
                '-p:NuGetAuditLevel=high',
                '-p:WarningsAsErrors=NU1903%3BNU1904'
            )
        }
        & dotnet @arguments
    }

    # E. Lockfile drift (P1.1) — the build regenerates each app's koan.lock.json; fail if one drifted
    # without being committed (composition changed but not recorded). Clean skip when none are tracked.
    Invoke-Leg 'E. lockfile' { & "$root/scripts/compare-koan-lock.ps1" }

    if (-not $SkipTests) {
        Invoke-Leg "A'. test" {
            $testProjects = @(
                Get-ChildItem -Path "$root/tests" -Recurse -Filter '*.csproj' -File |
                    Where-Object {
                        $projectXml = [System.IO.File]::ReadAllText($_.FullName)
                        $projectXml.Contains($testProjectMarker, [StringComparison]::Ordinal) -and
                            -not $projectXml.Contains('<IsTestProject>false</IsTestProject>', [StringComparison]::OrdinalIgnoreCase)
                    } |
                    Sort-Object FullName
            )

            if ($testProjects.Count -eq 0) {
                Write-Error "No runnable test projects were discovered under '$root/tests'."
                $global:LASTEXITCODE = 1
                return
            }

            # Packaging tests launch their own dotnet build/run processes. Running that nested process tree inside
            # the outer wave made it 5-10x slower under CI contention, so keep the same suite complete but give it
            # one uncontended host after the ordinary project wave.
            # Fresh per run: a stale TRX from a previous run would silently attribute old counts to this one.
            if (Test-Path $ratchetTrxRoot) { Remove-Item -LiteralPath $ratchetTrxRoot -Recurse -Force }
            New-Item -ItemType Directory -Path $ratchetTrxRoot -Force | Out-Null
            $runStartedUtc = [DateTimeOffset]::UtcNow

            $isolatedTestProjects = @($testProjects | Where-Object Name -eq 'Koan.Packaging.Tests.csproj')
            $parallelTestProjects = @($testProjects | Where-Object Name -ne 'Koan.Packaging.Tests.csproj')
            Write-Host "[ratchet] runnable-test-projects=$($testProjects.Count)  parallel=$($parallelTestProjects.Count)  nested-process-isolated=$($isolatedTestProjects.Count)  concurrency=$effectiveTestProjectConcurrency"
            $testResults = @(
                $parallelTestProjects | ForEach-Object -Parallel {
                    $project = $_
                    $testRoot = $using:root
                    $configuration = $using:Configuration
                    $hangTimeout = $using:testHostHangTimeout
                    $relativeProject = [System.IO.Path]::GetRelativePath($testRoot, $project.FullName)
                    $trxRoot = $using:ratchetTrxRoot
                    $trxName = $relativeProject.Replace([char]92, '_').Replace('/', '_') + '.trx'
                    $startedUtc = [DateTimeOffset]::UtcNow
                    $timer = [System.Diagnostics.Stopwatch]::StartNew()
                    Write-Host "[ratchet] test-project started: $relativeProject"
                    $arguments = @(
                        'test', $project.FullName,
                        '-c', $configuration,
                        '--no-build',
                        '--nologo',
                        '--filter', 'KoanLane!=native',
                        '--results-directory', $trxRoot,
                        '--logger', "trx;LogFileName=$trxName",
                        '--blame-hang-timeout', $hangTimeout,
                        '--blame-hang-dump-type', 'none'
                    )
                    $output = @(& dotnet @arguments 2>&1 | ForEach-Object { $_.ToString() })
                    $exitCode = $LASTEXITCODE
                    $timer.Stop()
                    $outputText = $output -join [Environment]::NewLine
                    $status = if ($exitCode -eq 0) { 'PASS' } else { 'FAIL' }
                    Write-Host ("`n=== [ratchet] test-project $status : $relativeProject ($([Math]::Round($timer.Elapsed.TotalSeconds, 1))s) ===`n$outputText")
                    [pscustomobject]@{
                        Project = $relativeProject
                        ExitCode = $exitCode
                        ElapsedSeconds = $timer.Elapsed.TotalSeconds
                        StartedUtc = $startedUtc
                        Trx = (Join-Path $trxRoot $trxName)
                        Lane = 'parallel'
                    }
                } -ThrottleLimit $effectiveTestProjectConcurrency

                $isolatedTestProjects | ForEach-Object {
                    $project = $_
                    $relativeProject = [System.IO.Path]::GetRelativePath($root, $project.FullName)
                    $trxName = $relativeProject.Replace([char]92, '_').Replace('/', '_') + '.trx'
                    $startedUtc = [DateTimeOffset]::UtcNow
                    $timer = [System.Diagnostics.Stopwatch]::StartNew()
                    Write-Host "[ratchet] nested-process test-project started in isolation: $relativeProject"
                    $arguments = @(
                        'test', $project.FullName,
                        '-c', $Configuration,
                        '--no-build',
                        '--nologo',
                        '--filter', 'KoanLane!=native',
                        '--results-directory', $ratchetTrxRoot,
                        '--logger', "trx;LogFileName=$trxName",
                        '--blame-hang-timeout', $testHostHangTimeout,
                        '--blame-hang-dump-type', 'none'
                    )
                    $output = @(& dotnet @arguments 2>&1 | ForEach-Object { $_.ToString() })
                    $exitCode = $LASTEXITCODE
                    $timer.Stop()
                    $outputText = $output -join [Environment]::NewLine
                    $status = if ($exitCode -eq 0) { 'PASS' } else { 'FAIL' }
                    Write-Host ("`n=== [ratchet] test-project $status : $relativeProject ($([Math]::Round($timer.Elapsed.TotalSeconds, 1))s) ===`n$outputText")
                    [pscustomobject]@{
                        Project = $relativeProject
                        ExitCode = $exitCode
                        ElapsedSeconds = $timer.Elapsed.TotalSeconds
                        StartedUtc = $startedUtc
                        Trx = (Join-Path $ratchetTrxRoot $trxName)
                        Lane = 'isolated'
                    }
                }
            )

            # Aggregate BEFORE deciding the verdict: evidence matters most on the run that failed, and a
            # manifest written only on success would be missing exactly when it is needed. The aggregator
            # is a separate script so its failure and evidence-gap paths can be exercised directly.
            $runRecordPath = Join-Path $ratchetEvidenceRoot 'test-run-records.json'
            New-Item -ItemType Directory -Path $ratchetEvidenceRoot -Force | Out-Null
            @($testResults | ForEach-Object {
                [ordered]@{
                    project        = $_.Project
                    lane           = $_.Lane
                    exitCode       = $_.ExitCode
                    elapsedSeconds = $_.ElapsedSeconds
                    startedUtc     = $_.StartedUtc.ToString('o')
                    trx            = $_.Trx
                }
            }) | ConvertTo-Json -Depth 4 -AsArray | Set-Content -LiteralPath $runRecordPath -Encoding utf8

            & "$root/scripts/aggregate-test-evidence.ps1" `
                -RunRecordPath $runRecordPath `
                -OutputPath $ratchetTestManifest `
                -RepositoryRoot $root `
                -Configuration $Configuration `
                -Concurrency $effectiveTestProjectConcurrency `
                -HangTimeout $testHostHangTimeout `
                -StartedUtc $runStartedUtc.ToString('o')

            $failedProjects = @($testResults | Where-Object ExitCode -ne 0 | Sort-Object Project)
            Write-Host "`n[ratchet] test-project summary ($($testResults.Count) project(s))"
            foreach ($testResult in ($testResults | Sort-Object Project)) {
                $status = if ($testResult.ExitCode -eq 0) { 'PASS' } else { 'FAIL' }
                Write-Host ("  {0,-6} {1,8:N1}s  {2}" -f $status, $testResult.ElapsedSeconds, $testResult.Project) `
                    -ForegroundColor $(if ($testResult.ExitCode -eq 0) { 'Green' } else { 'Red' })
            }

            if ($failedProjects.Count -gt 0) {
                Write-Host "[ratchet] failed-test-projects=$($failedProjects.Count): $($failedProjects.Project -join ', ')" -ForegroundColor Red
                $global:LASTEXITCODE = 1
                return
            }
            $global:LASTEXITCODE = 0
        }
    }

    Invoke-Leg 'B. docs-lint' { & "$root/scripts/docs-lint.ps1" -Output list }
    Invoke-Leg "B'. public-docs" { & "$root/scripts/public-docs-lint.ps1" }

    if ($FullDocs) {
        Invoke-Leg 'C. doc-code (full)' { & "$root/scripts/validate-code-examples.ps1" -Full }
    }
    else {
        Invoke-Leg 'C. doc-code (diff)' { & "$root/scripts/validate-code-examples.ps1" -Base $Base }
    }

    # D. Skills lint (DX-0048) — skill contract: dir==name, frontmatter, version pins, link/card resolution.
    # -Strict (H10 complete): link/card resolution + catalog parity are now fatal, not warnings.
    Invoke-Leg 'D. skills-lint' { & "$root/scripts/skills-lint.ps1" -Strict }

    Write-Host "`n=== [ratchet] summary ===" -ForegroundColor Cyan
    $failed = @()
    foreach ($k in $results.Keys) {
        $pass = $results[$k]
        Write-Host ("  {0,-22} {1}" -f $k, $(if ($pass) { 'PASS' } else { 'FAIL' })) -ForegroundColor $(if ($pass) { 'Green' } else { 'Red' })
        if (-not $pass) { $failed += $k }
    }

    if ($failed.Count -gt 0) {
        Write-Host "`n[ratchet] RED — $($failed.Count) leg(s) failed: $($failed -join ', ')" -ForegroundColor Red
        exit 1
    }
    Write-Host "`n[ratchet] GREEN — all legs passed." -ForegroundColor Green
    exit 0
}
finally {
    Pop-Location
}
