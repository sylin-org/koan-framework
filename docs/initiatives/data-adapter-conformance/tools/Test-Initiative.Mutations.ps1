[CmdletBinding()]
param(
    [string]$RepositoryRoot,
    [string]$TempRoot
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if (-not $RepositoryRoot) { $RepositoryRoot = (& git rev-parse --show-toplevel).Trim() }
$RepositoryRoot = (Resolve-Path -LiteralPath $RepositoryRoot).ProviderPath
$initiativeRoot = Join-Path $RepositoryRoot 'docs/initiatives/data-adapter-conformance'
$primerPath = Join-Path $RepositoryRoot 'docs/architecture/data-adapter-development-primer.md'
$validator = Join-Path $initiativeRoot 'tools/Test-Initiative.ps1'
$greenfieldValidator = Join-Path $initiativeRoot 'tools/Test-GreenfieldReplacement.ps1'
$checkpointTool = Join-Path $initiativeRoot 'tools/New-InitiativeCheckpoint.ps1'
if (-not $TempRoot) { $TempRoot = [IO.Path]::GetTempPath() }
$TempRoot = [IO.Path]::GetFullPath($TempRoot)
$runRoot = Join-Path $TempRoot ('koan-dac00-mutations-' + [guid]::NewGuid().ToString('n'))
New-Item -ItemType Directory -Path $runRoot -Force | Out-Null
$passed = New-Object System.Collections.Generic.List[string]

function Invoke-Initiative([string]$Root) {
    $output = & pwsh -NoProfile -File $validator -RepositoryRoot $RepositoryRoot -InitiativeRoot $Root -PrimerPath $primerPath 2>&1
    [pscustomobject]@{ ExitCode = $LASTEXITCODE; Output = (($output | ForEach-Object ToString) -join "`n") }
}

function New-InitiativeCopy([string]$Name) {
    $target = Join-Path $runRoot $Name
    Copy-Item -LiteralPath $initiativeRoot -Destination $target -Recurse
    $target
}

function Require-Failure([string]$Name, [scriptblock]$Mutation) {
    $copy = New-InitiativeCopy $Name
    & $Mutation $copy
    $result = Invoke-Initiative $copy
    if ($result.ExitCode -eq 0) { throw "Mutation '$Name' incorrectly passed.`n$($result.Output)" }
    $passed.Add($Name)
}

function Write-Json([string]$Path, [object]$Value) {
    $Value | ConvertTo-Json -Depth 30 | Set-Content -LiteralPath $Path -Encoding utf8
}

function Invoke-Greenfield([string]$Root) {
    $output = & pwsh -NoProfile -File $greenfieldValidator -PacketRoot $Root 2>&1
    [pscustomobject]@{ ExitCode = $LASTEXITCODE; Output = (($output | ForEach-Object ToString) -join "`n") }
}

function New-GreenfieldFixture([string]$Name) {
    $root = Join-Path $runRoot ('greenfield-' + $Name)
    New-Item -ItemType Directory -Path (Join-Path $root 'rewrite') -Force | Out-Null
    New-Item -ItemType Directory -Path (Join-Path $root 'restricted') -Force | Out-Null
    Write-Json (Join-Path $root 'restricted/retirement.json') ([ordered]@{
            schemaVersion = 1; status = 'sealed'; provider = 'sqlite'
            expected = @([ordered]@{ path = 'legacy/sqlite/Old.cs'; kind = 'source' })
            absence = @([ordered]@{ path = 'legacy/sqlite/Old.cs'; absent = $true; evidence = 'scan-1' })
        })
    Write-Json (Join-Path $root 'rewrite/replacement.json') ([ordered]@{
            schemaVersion = 1; status = 'sealed'; provider = 'sqlite'; commonBase = 'base-001'
            startedEmpty = $true
            sourceExport = @([ordered]@{ path = 'candidate/sqlite/New.cs'; sha256 = '2222' })
            compileItems = @('candidate/sqlite/New.cs'); registrations = @('sqlite-factory')
            movingParts = @([ordered]@{ id = 'repository'; kind = 'contract'; reason = 'Owns provider dispatch.' })
            executionPaths = @('sqlite-repository'); shadowPaths = @(); retirementRef = '../restricted/retirement.json'
        })
    $root
}

function Require-GreenfieldFailure([string]$Name, [scriptblock]$Mutation) {
    $root = New-GreenfieldFixture $Name
    & $Mutation $root
    $result = Invoke-Greenfield $root
    if ($result.ExitCode -eq 0) { throw "Greenfield mutation '$Name' incorrectly passed.`n$($result.Output)" }
    $passed.Add('greenfield-' + $Name)
}

try {
    $clean1 = Invoke-Initiative $initiativeRoot
    $clean2 = Invoke-Initiative $initiativeRoot
    if ($clean1.ExitCode -ne 0 -or $clean2.ExitCode -ne 0) { throw "Clean initiative validation failed.`n$($clean1.Output)`n$($clean2.Output)" }
    if ($clean1.Output -ne $clean2.Output) { throw 'Clean initiative validation output is not deterministic.' }
    $passed.Add('clean-deterministic-twice')

    Require-Failure 'duplicate-card' {
        param($root)
        Copy-Item -LiteralPath (Join-Path $root 'work-items/DAC-00-bootstrap-roster-and-evidence.md') -Destination (Join-Path $root 'work-items/DAC-00-duplicate.md')
    }
    Require-Failure 'unknown-dependency' {
        param($root)
        $path = Join-Path $root 'work-items/DAC-01-koan-data-public-surface-audit.md'
        (Get-Content -LiteralPath $path -Raw).Replace('| Depends on | DAC-00 |', '| Depends on | DAC-98 |') | Set-Content -LiteralPath $path -Encoding utf8
    }
    Require-Failure 'dependency-cycle' {
        param($root)
        $path = Join-Path $root 'work-items/DAC-00-bootstrap-roster-and-evidence.md'
        (Get-Content -LiteralPath $path -Raw).Replace('| Depends on | — |', '| Depends on | DAC-01 |') | Set-Content -LiteralPath $path -Encoding utf8
    }
    Require-Failure 'unknown-primer-id' {
        param($root)
        Add-Content -LiteralPath (Join-Path $root 'NOW.md') -Value "`nInvalid mutation reference A-99.`n"
    }
    Require-Failure 'second-in-progress' {
        param($root)
        $path = Join-Path $root 'PROGRESS.md'
        $text = Get-Content -LiteralPath $path -Raw
        $text = $text.Replace('| DAC-01 | pending |', '| DAC-01 | in-progress |').Replace('| DAC-01 | ready |', '| DAC-01 | in-progress |')
        $text = $text.Replace('| DAC-02 | pending |', '| DAC-02 | in-progress |')
        $text | Set-Content -LiteralPath $path -Encoding utf8
    }
    Require-Failure 'unresolved-local-link' {
        param($root)
        Add-Content -LiteralPath (Join-Path $root 'NOW.md') -Value "`n[missing](definitely-not-present.md)`n"
    }

    $validGreenfield = New-GreenfieldFixture 'valid'
    $validResult = Invoke-Greenfield $validGreenfield
    if ($validResult.ExitCode -ne 0) { throw "Valid greenfield fixture failed.`n$($validResult.Output)" }
    $passed.Add('greenfield-valid')
    Require-GreenfieldFailure 'not-empty-start' {
        param($root)
        $path = Join-Path $root 'rewrite/replacement.json'; $json = Get-Content $path -Raw | ConvertFrom-Json
        $json.startedEmpty = $false; Write-Json $path $json
    }
    Require-GreenfieldFailure 'incomplete-retirement' {
        param($root)
        $path = Join-Path $root 'restricted/retirement.json'; $json = Get-Content $path -Raw | ConvertFrom-Json
        $json.absence[0].absent = $false; Write-Json $path $json
    }
    Require-GreenfieldFailure 'duplicate-selected-path' {
        param($root)
        $path = Join-Path $root 'rewrite/replacement.json'; $json = Get-Content $path -Raw | ConvertFrom-Json
        $json.sourceExport = @($json.sourceExport[0], $json.sourceExport[0]); Write-Json $path $json
    }
    Require-GreenfieldFailure 'duplicate-registration' {
        param($root)
        $path = Join-Path $root 'rewrite/replacement.json'; $json = Get-Content $path -Raw | ConvertFrom-Json
        $json.registrations = @('sqlite-factory', 'SQLITE-FACTORY'); Write-Json $path $json
    }
    Require-GreenfieldFailure 'unexplained-moving-part' {
        param($root)
        $path = Join-Path $root 'rewrite/replacement.json'; $json = Get-Content $path -Raw | ConvertFrom-Json
        $json.movingParts[0].reason = ''; Write-Json $path $json
    }
    Require-GreenfieldFailure 'multiple-execution-paths' {
        param($root)
        $path = Join-Path $root 'rewrite/replacement.json'; $json = Get-Content $path -Raw | ConvertFrom-Json
        $json.executionPaths = @('sqlite-repository', 'sqlite-legacy'); Write-Json $path $json
    }
    Require-GreenfieldFailure 'shadow-path' {
        param($root)
        $path = Join-Path $root 'rewrite/replacement.json'; $json = Get-Content $path -Raw | ConvertFrom-Json
        $json.shadowPaths = @('sqlite-compat'); Write-Json $path $json
    }

    $redGate = Join-Path $RepositoryRoot 'docs/initiatives/data-adapter-conformance/evidence/framework/DAC-09-red-gate-fixture.json'
    $redResult = & pwsh -NoProfile -File $checkpointTool -RepositoryRoot $RepositoryRoot `
        -ArtifactRoot (Join-Path $runRoot 'checkpoint-red') -Scope Workspace -CheckpointName mutation-red `
        -GateReceiptPath $redGate -SkipReplay 2>&1
    $redOutput = (($redResult | ForEach-Object ToString) -join "`n")
    if ($LASTEXITCODE -eq 0 -or $redOutput -notmatch 'cannot seal') {
        throw "Checkpoint tool did not reject the RED gate receipt correctly.`n$redOutput"
    }
    $passed.Add('checkpoint-rejects-red')

    Write-Output "MUTATIONS PASS cases=$($passed.Count)"
    foreach ($name in $passed) { Write-Output "  PASS $name" }
}
finally {
    $resolvedRun = [IO.Path]::GetFullPath($runRoot)
    if (-not $resolvedRun.StartsWith($TempRoot, [StringComparison]::OrdinalIgnoreCase) -or $resolvedRun -eq $TempRoot) {
        throw "Unsafe mutation cleanup target '$resolvedRun'."
    }
    if (Test-Path -LiteralPath $resolvedRun) { Remove-Item -LiteralPath $resolvedRun -Recurse -Force }
}
