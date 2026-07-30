<#
.SYNOPSIS
  Runs the ordinary AODB provider suites selected by adapter name or environment.

.DESCRIPTION
  Discovers each *AodbConformanceSpec beside its test project, runs that project through dotnet test,
  and reports test outcomes from TRX. Forge is only a selector and reporter; conformance behavior
  remains in the shared TestKits and provider tests.

  Exit code: 0 = all passed; 1 = a test failed; 2 = one or more tests skipped; 3 = runner error.

.PARAMETER NoBuild
  Trusts existing test outputs. The caller owns their freshness and configuration.

.EXAMPLE
  pwsh scripts/forge-verify.ps1 -Adapter Mongo -Plane record
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

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$resultsRoot = $null
Push-Location (Resolve-Path "$PSScriptRoot/..")
try {
    $repoRoot = (Get-Location).ProviderPath
    $dockerFreeTargets = @(
        'record/InMemory',
        'record/Json',
        'record/Sqlite',
        'vector/InMemory',
        'vector/SqliteVec'
    )

    function Find-Project([IO.FileInfo]$Spec) {
        $directory = $Spec.Directory
        while ($directory) {
            $project = Get-ChildItem -LiteralPath $directory.FullName -Filter '*.csproj' -File |
                Select-Object -First 1
            if ($project) { return $project.FullName }
            $directory = $directory.Parent
        }
        $null
    }

    $targets = @(
        Get-ChildItem -LiteralPath (Join-Path $repoRoot 'tests/Suites/Data') -Recurse -Filter '*AodbConformanceSpec.cs' -File |
            Sort-Object FullName |
            ForEach-Object {
                $isVector = $_.Name.EndsWith('VectorAodbConformanceSpec.cs', [StringComparison]::Ordinal)
                $targetPlane = if ($isVector) { 'vector' } else { 'record' }
                $suffix = if ($isVector) { 'VectorAodbConformanceSpec.cs' } else { 'AodbConformanceSpec.cs' }
                $name = $_.Name.Substring(0, $_.Name.Length - $suffix.Length)
                [pscustomobject]@{
                    Adapter = $name
                    Plane = $targetPlane
                    Key = "$targetPlane/$name"
                    Project = Find-Project $_
                }
            }
    )

    $selected = @($targets)
    if ($DockerFree) {
        $selected = @($selected | Where-Object { $_.Key -in $dockerFreeTargets })
    }
    elseif ($Adapter) {
        $selected = @($selected | Where-Object {
                $_.Adapter -ieq $Adapter -and ($Plane -eq '' -or $_.Plane -eq $Plane)
            })
    }
    elseif (-not $All) {
        Write-Error 'Specify -Adapter <name> [-Plane record|vector], -DockerFree, or -All.'
        exit 3
    }

    if ($selected.Count -eq 0) {
        Write-Error 'No adapter conformance project matched the selection.'
        exit 3
    }

    $resultsRoot = Join-Path ([IO.Path]::GetTempPath()) ('forge-verify-' + [guid]::NewGuid().ToString('n'))
    New-Item -ItemType Directory -Path $resultsRoot -Force | Out-Null
    $reports = New-Object System.Collections.Generic.List[object]

    foreach ($target in $selected) {
        if (-not $target.Project) {
            $reports.Add([pscustomobject]@{
                    Adapter = $target.Adapter
                    Plane = $target.Plane
                    Verdict = 'ERROR'
                    Passed = 0
                    Failed = 0
                    Skipped = 0
                    Reason = 'No project found beside the conformance spec.'
                }) | Out-Null
            continue
        }

        $trxName = "$($target.Plane)-$($target.Adapter).trx"
        $trxPath = Join-Path $resultsRoot $trxName
        $arguments = @(
            'test',
            $target.Project,
            '--configuration', $Configuration,
            '--filter', 'FullyQualifiedName~Aodb',
            '--logger', "trx;LogFileName=$trxName",
            '--results-directory', $resultsRoot,
            '--blame-hang-timeout', "$($DeadlineSeconds)s",
            '--blame-hang-dump-type', 'none'
        )
        if ($NoBuild) { $arguments += '--no-build' }

        Write-Host "forge: $($target.Key)" -ForegroundColor DarkGray
        $console = & dotnet @arguments 2>&1
        $exitCode = $LASTEXITCODE
        if (-not (Test-Path -LiteralPath $trxPath)) {
            $tail = (($console | Select-Object -Last 3) -join ' ').Trim()
            if (-not $tail) {
                $tail = if ($NoBuild) { 'No current test output was found; build this project first.' }
                    else { 'The test host produced no console diagnostics.' }
            }
            $reports.Add([pscustomobject]@{
                    Adapter = $target.Adapter
                    Plane = $target.Plane
                    Verdict = 'ERROR'
                    Passed = 0
                    Failed = 0
                    Skipped = 0
                    Reason = "dotnet test exited $exitCode without a TRX: $tail"
                }) | Out-Null
            continue
        }

        [xml]$trx = Get-Content -LiteralPath $trxPath -Raw
        $outcomes = @($trx.TestRun.Results.UnitTestResult | ForEach-Object { [string]$_.outcome })
        $passed = @($outcomes | Where-Object { $_ -eq 'Passed' }).Count
        $failed = @($outcomes | Where-Object { $_ -eq 'Failed' }).Count
        $skipped = @($outcomes | Where-Object { $_ -in @('NotExecuted', 'Skipped') }).Count
        $unknown = @($outcomes | Where-Object { $_ -notin @('Passed', 'Failed', 'NotExecuted', 'Skipped') }).Count

        $verdict = if ($failed -gt 0) { 'RED' }
            elseif ($exitCode -ne 0 -or $outcomes.Count -eq 0 -or $unknown -gt 0) { 'ERROR' }
            elseif ($skipped -gt 0) { 'INCONCLUSIVE' }
            else { 'GREEN' }
        $firstFailure = @($trx.TestRun.Results.UnitTestResult |
                Where-Object { [string]$_.outcome -eq 'Failed' } |
                ForEach-Object { [string]$_.Output.ErrorInfo.Message } |
                Where-Object { $_ }) | Select-Object -First 1
        $reason = if ($failed -gt 0 -and $firstFailure) { ($firstFailure -split '\r?\n')[0] }
            elseif ($failed -gt 0) { "$failed tests failed; inspect the project test output." }
            elseif ($exitCode -ne 0) { "dotnet test exited $exitCode" }
            elseif ($outcomes.Count -eq 0) { 'No tests were discovered.' }
            elseif ($unknown -gt 0) { "$unknown test outcomes were unrecognized." }
            else { '' }

        $reports.Add([pscustomobject]@{
                Adapter = $target.Adapter
                Plane = $target.Plane
                Verdict = $verdict
                Passed = $passed
                Failed = $failed
                Skipped = $skipped
                Reason = $reason
            }) | Out-Null
    }

    $red = @($reports | Where-Object Verdict -eq 'RED').Count
    $errors = @($reports | Where-Object Verdict -eq 'ERROR').Count
    $inconclusive = @($reports | Where-Object Verdict -eq 'INCONCLUSIVE').Count
    $green = @($reports | Where-Object Verdict -eq 'GREEN').Count
    $verdict = if ($red -gt 0) { 'RED' }
        elseif ($errors -gt 0) { 'ERROR' }
        elseif ($inconclusive -gt 0) { 'INCONCLUSIVE' }
        else { 'GREEN' }

    if ($Output -eq 'json') {
        [pscustomobject]@{
            gate = 'aodb-conformance'
            verdict = $verdict
            summary = [pscustomobject]@{
                adapters = $reports.Count
                green = $green
                red = $red
                inconclusive = $inconclusive
                errors = $errors
            }
            adapters = $reports.ToArray()
        } | ConvertTo-Json -Depth 5
    }
    else {
        $reports | Sort-Object Plane, Adapter | Format-Table Adapter, Plane, Passed, Failed, Skipped, Verdict, Reason -AutoSize
        Write-Host "FORGE $verdict adapters=$($reports.Count) green=$green red=$red inconclusive=$inconclusive errors=$errors"
    }

    if ($red -gt 0) { exit 1 }
    if ($errors -gt 0) { exit 3 }
    if ($inconclusive -gt 0) { exit 2 }
    exit 0
}
finally {
    Pop-Location
    if ($resultsRoot -and (Test-Path -LiteralPath $resultsRoot)) {
        Remove-Item -LiteralPath $resultsRoot -Recurse -Force -ErrorAction SilentlyContinue
    }
}
