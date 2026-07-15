<#
.SYNOPSIS
  The Koan green ratchet — the single merge gate for the foundation-consolidation work.
  (Facet 0 of docs/architecture/foundation-consolidation-plan.md.)

.DESCRIPTION
  Runs the consolidation gate and fails if any leg fails:

    0.  Tools       dotnet tool restore      — pinned repository tools used by executable proofs.
    A.  Build       dotnet build Koan.sln   — framework + every dogfood sample
                    (the samples live in Koan.sln, so one build covers both).
    A'. Test        dotnet test  Koan.sln    — runs every runnable suite with bounded project fan-out
                    and per-host hang detection. Container-gated integration specs skip cleanly when
                    infra is absent. Skip with -SkipTests.
    B.  Docs lint   scripts/docs-lint.ps1    — links / front-matter / anchors / terms.
                    Errors are fatal; warnings are not.
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
    F.  Blueprint   scripts/blueprint-lint.ps1 -Strict — the ARCH-0094 Adapter Blueprint grounding gate:
                    every `<!-- obligation: Type.Member @ file -->` token's cited member must be alive in the
                    shipped source (anti-drift), plus EXTEND-required frontmatter + catalogue parity.

  Exit code is 0 (GREEN) only when every run leg passes; otherwise 1 (RED).

.PARAMETER Base
  Git ref to diff instructional docs against for Leg C. Default: origin/main if present, else main.

.PARAMETER PublicRelease
  Build the exact public package identity and enable the release audit floor. Used by the dev release compiler.

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
    [switch]$PublicRelease
)

$ErrorActionPreference = 'Continue'
$root = (Resolve-Path "$PSScriptRoot/..").ProviderPath
# Certification policy: provider suites remain comprehensive, but heavyweight test projects do not all
# compete for one host at once and an inactive test host cannot retain the release queue indefinitely.
$testProjectConcurrency = 2
$testHostHangTimeout = '5m'
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
    Write-Host "[ratchet] test-project-concurrency=$testProjectConcurrency  test-host-hang-timeout=$testHostHangTimeout"

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
            $arguments = @(
                'test', "$root/Koan.sln",
                '-c', $Configuration,
                '--no-build',
                '--nologo',
                "-m:$testProjectConcurrency",
                '--blame-hang-timeout', $testHostHangTimeout,
                '--blame-hang-dump-type', 'none'
            )
            & dotnet @arguments
        }
    }

    Invoke-Leg 'B. docs-lint' { & "$root/scripts/docs-lint.ps1" -Output table }

    if ($FullDocs) {
        Invoke-Leg 'C. doc-code (full)' { & "$root/scripts/validate-code-examples.ps1" -Full }
    }
    else {
        Invoke-Leg 'C. doc-code (diff)' { & "$root/scripts/validate-code-examples.ps1" -Base $Base }
    }

    # D. Skills lint (DX-0048) — skill contract: dir==name, frontmatter, version pins, link/card resolution.
    # -Strict (H10 complete): link/card resolution + catalog parity are now fatal, not warnings.
    Invoke-Leg 'D. skills-lint' { & "$root/scripts/skills-lint.ps1" -Strict }

    # F. Blueprint lint (ARCH-0094 Phase 3) — the Adapter Blueprint grounding gate: every obligation token's cited
    # member must be alive in the shipped source (anti-drift), plus the EXTEND-required frontmatter + catalogue parity.
    Invoke-Leg 'F. blueprint-lint' { & "$root/scripts/blueprint-lint.ps1" -Strict }

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
