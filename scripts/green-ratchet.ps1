<#
.SYNOPSIS
  The Koan green ratchet — the single merge gate for the foundation-consolidation work.
  (Facet 0 of docs/architecture/foundation-consolidation-plan.md.)

.DESCRIPTION
  Runs the consolidation gate and fails if any leg fails:

    A.  Build       dotnet build Koan.sln   — framework + every dogfood sample
                    (the samples live in Koan.sln, so one build covers both).
    A'. Test        dotnet test  Koan.sln    — container-gated integration specs skip
                    cleanly when infra is absent. Skip with -SkipTests.
    B.  Docs lint   scripts/docs-lint.ps1    — links / front-matter / anchors / terms.
                    Errors are fatal; warnings are not.
    C.  Doc code    scripts/validate-code-examples.ps1 — compiles C# blocks in the
                    INSTRUCTIONAL docs a change touches (diff-scoped vs -Base). Decision /
                    design / proposal / archive docs are out of scope by design. Use
                    -FullDocs for the manual full-surface sweep. A `<!-- validate:skip -->`
                    marker before a fence exempts an intentionally non-compiling snippet.

  Exit code is 0 (GREEN) only when every run leg passes; otherwise 1 (RED).

.PARAMETER Base
  Git ref to diff instructional docs against for Leg C. Default: origin/main if present, else main.

.EXAMPLE
  pwsh scripts/green-ratchet.ps1                 # full gate, diff-scoped doc-code vs main
  pwsh scripts/green-ratchet.ps1 -SkipTests      # build + lints only (fast)
  pwsh scripts/green-ratchet.ps1 -FullDocs       # run the whole-surface doc-code sweep
#>
param(
    [string]$Configuration = "Debug",
    [string]$Base = "",
    [switch]$SkipTests,
    [switch]$FullDocs
)

$ErrorActionPreference = 'Continue'
$root = (Resolve-Path "$PSScriptRoot/..").ProviderPath
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
    Write-Host "[ratchet] config=$Configuration  docs-base=$Base  skipTests=$SkipTests  fullDocs=$FullDocs"

    Invoke-Leg 'A. build' { & dotnet build "$root/Koan.sln" -c $Configuration --nologo }

    if (-not $SkipTests) {
        Invoke-Leg "A'. test" { & dotnet test "$root/Koan.sln" -c $Configuration --no-build --nologo }
    }

    Invoke-Leg 'B. docs-lint' { & "$root/scripts/docs-lint.ps1" -Output table }

    if ($FullDocs) {
        Invoke-Leg 'C. doc-code (full)' { & "$root/scripts/validate-code-examples.ps1" -Full }
    }
    else {
        Invoke-Leg 'C. doc-code (diff)' { & "$root/scripts/validate-code-examples.ps1" -Base $Base }
    }

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
