[CmdletBinding()]
param(
    [string]$RepositoryRoot,
    [string]$InitiativeRoot,
    [string]$PrimerPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if (-not $RepositoryRoot) { $RepositoryRoot = (& git rev-parse --show-toplevel).Trim() }
$RepositoryRoot = (Resolve-Path -LiteralPath $RepositoryRoot).ProviderPath
if (-not $InitiativeRoot) { $InitiativeRoot = Join-Path $RepositoryRoot 'docs/initiatives/data-adapter-conformance' }
$InitiativeRoot = (Resolve-Path -LiteralPath $InitiativeRoot).ProviderPath
if (-not $PrimerPath) { $PrimerPath = Join-Path $RepositoryRoot 'docs/architecture/data-adapter-development-primer.md' }
$PrimerPath = (Resolve-Path -LiteralPath $PrimerPath).ProviderPath
$failures = New-Object System.Collections.Generic.List[string]

function Get-Ids([string]$Text, [string]$Pattern) {
    @([regex]::Matches($Text, $Pattern) | ForEach-Object { $_.Groups[1].Value } | Sort-Object -Unique)
}

$cards = @(Get-ChildItem -LiteralPath (Join-Path $InitiativeRoot 'work-items') -Filter 'DAC-*.md' -File | Sort-Object Name)
$cardIds = New-Object System.Collections.Generic.List[string]
$cardDeps = @{}
foreach ($card in $cards) {
    $fileMatch = [regex]::Match($card.Name, '^(DAC-\d{2})-')
    if (-not $fileMatch.Success) { $failures.Add("invalid card filename '$($card.Name)'"); continue }
    $id = $fileMatch.Groups[1].Value
    $text = Get-Content -LiteralPath $card.FullName -Raw
    $heading = [regex]::Match($text, '(?m)^# (DAC-\d{2})\b')
    if (-not $heading.Success -or $heading.Groups[1].Value -ne $id) { $failures.Add("card heading/file mismatch for $($card.Name)") }
    $cardIds.Add($id)
    $dependsLine = [regex]::Match($text, '(?m)^\| Depends on \| (.*?) \|$')
    if (-not $dependsLine.Success) { $failures.Add("missing Depends on row in $id"); $cardDeps[$id] = @() }
    else { $cardDeps[$id] = @(Get-Ids $dependsLine.Groups[1].Value '(DAC-\d{2})') }
}
$duplicateCards = @($cardIds | Group-Object | Where-Object Count -ne 1)
foreach ($duplicate in $duplicateCards) { $failures.Add("duplicate static card ID '$($duplicate.Name)'") }

$progressPath = Join-Path $InitiativeRoot 'PROGRESS.md'
$roadmapPath = Join-Path $InitiativeRoot 'ROADMAP.md'
$progressText = Get-Content -LiteralPath $progressPath -Raw
$roadmapText = Get-Content -LiteralPath $roadmapPath -Raw
$progressRows = @([regex]::Matches($progressText, '(?m)^\| (DAC-\d{2}) \| (pending|ready|in-progress|blocked|passed|declined) \| (.*?) \|') | ForEach-Object {
        [pscustomobject]@{ id = $_.Groups[1].Value; state = $_.Groups[2].Value; dependencyText = $_.Groups[3].Value }
    })
$roadmapRows = @([regex]::Matches($roadmapText, '(?m)^\| (DAC-\d{2}) \| .*? \| (.*?) \|$') | ForEach-Object {
        [pscustomobject]@{ id = $_.Groups[1].Value; dependencyText = $_.Groups[2].Value }
    })
foreach ($set in @(
        [pscustomobject]@{ name = 'progress'; ids = @($progressRows.id) },
        [pscustomobject]@{ name = 'roadmap'; ids = @($roadmapRows.id) }
    )) {
    foreach ($duplicate in @($set.ids | Group-Object | Where-Object Count -ne 1)) { $failures.Add("duplicate $($set.name) row '$($duplicate.Name)'") }
    foreach ($missing in @($cardIds | Where-Object { $set.ids -notcontains $_ })) { $failures.Add("$($set.name) is missing '$missing'") }
    foreach ($extra in @($set.ids | Where-Object { $cardIds -notcontains $_ })) { $failures.Add("$($set.name) has unknown '$extra'") }
}

$knownCards = @($cardIds)
foreach ($cardId in $knownCards) {
    foreach ($dependency in @($cardDeps[$cardId])) {
        if ($knownCards -notcontains $dependency) { $failures.Add("$cardId has unknown dependency '$dependency'") }
        if ($dependency -eq $cardId) { $failures.Add("$cardId depends on itself") }
    }
    $progressRow = @($progressRows | Where-Object id -eq $cardId)
    $roadmapRow = @($roadmapRows | Where-Object id -eq $cardId)
    if ($progressRow.Count -eq 1) {
        $progressDeps = Get-Ids $progressRow[0].dependencyText '(DAC-\d{2})'
        if ((@($cardDeps[$cardId]) -join '|') -ne (@($progressDeps) -join '|')) { $failures.Add("$cardId card/progress dependency mismatch") }
    }
    if ($roadmapRow.Count -eq 1) {
        $roadmapDeps = Get-Ids $roadmapRow[0].dependencyText '(DAC-\d{2})'
        if ((@($cardDeps[$cardId]) -join '|') -ne (@($roadmapDeps) -join '|')) { $failures.Add("$cardId card/roadmap dependency mismatch") }
    }
}

$visiting = New-Object 'System.Collections.Generic.HashSet[string]'
$visited = New-Object 'System.Collections.Generic.HashSet[string]'
function Visit-Card([string]$Id, [System.Collections.Generic.List[string]]$Failures) {
    if ($visited.Contains($Id)) { return }
    if ($visiting.Contains($Id)) { $Failures.Add("dependency cycle reaches '$Id'"); return }
    [void]$visiting.Add($Id)
    foreach ($dependency in @($cardDeps[$Id])) {
        if ($cardDeps.ContainsKey($dependency)) { Visit-Card $dependency $Failures }
    }
    [void]$visiting.Remove($Id)
    [void]$visited.Add($Id)
}
foreach ($cardId in $knownCards) { Visit-Card $cardId $failures }

$inProgress = @($progressRows | Where-Object state -eq 'in-progress')
$dac30Passed = @($progressRows | Where-Object { $_.id -eq 'DAC-30' -and $_.state -eq 'passed' }).Count -eq 1
if (-not $dac30Passed -and $inProgress.Count -gt 1) { $failures.Add('more than one card is in-progress before DAC-30 passed') }

$primerText = Get-Content -LiteralPath $PrimerPath -Raw
$catalogIds = Get-Ids $primerText '\*\*([A-HPV]-\d{2})\*\*'
if ($catalogIds.Count -ne 105) { $failures.Add("primer catalog has $($catalogIds.Count) IDs; expected 105") }
$initiativeFiles = @(Get-ChildItem -LiteralPath $InitiativeRoot -Filter '*.md' -File -Recurse)
foreach ($file in $initiativeFiles) {
    $text = Get-Content -LiteralPath $file.FullName -Raw
    foreach ($reference in Get-Ids $text '(?<![A-Z0-9-])([A-HPV]-\d{2})(?!\d)') {
        if ($catalogIds -notcontains $reference) { $failures.Add("unknown primer ID '$reference' in $([IO.Path]::GetRelativePath($InitiativeRoot,$file.FullName))") }
    }
    foreach ($linkMatch in [regex]::Matches($text, '\[[^\]]*\]\(([^)]+)\)')) {
        $target = $linkMatch.Groups[1].Value.Trim('<', '>')
        if ($target -match '^(?:https?://|mailto:|#)' -or -not $target) { continue }
        $pathPart = ($target -split '#', 2)[0]
        if (-not $pathPart) { continue }
        $resolved = [IO.Path]::GetFullPath((Join-Path $file.DirectoryName $pathPart))
        if (-not (Test-Path -LiteralPath $resolved)) {
            $failures.Add("unresolved local link '$target' in $([IO.Path]::GetRelativePath($InitiativeRoot,$file.FullName))")
        }
    }
}

$portfolio = Join-Path $InitiativeRoot 'evidence/portfolio'
$packetIndexPath = Join-Path $portfolio 'packet-index.json'
if (-not (Test-Path -LiteralPath $packetIndexPath)) { $failures.Add('packet index is missing') }
else {
    $packetIndex = Get-Content -LiteralPath $packetIndexPath -Raw | ConvertFrom-Json
    foreach ($scope in @($packetIndex.scopes)) {
        foreach ($required in @($packetIndex.requiredFiles)) {
            if (-not (Test-Path -LiteralPath (Join-Path $InitiativeRoot "evidence/$($scope.id)/$required") -PathType Leaf)) {
                $failures.Add("packet '$($scope.id)' is missing '$required'")
            }
        }
        if ($scope.id -in @('sqlite', 'mongodb')) {
            foreach ($required in @($packetIndex.goldRequiredFiles)) {
                if (-not (Test-Path -LiteralPath (Join-Path $InitiativeRoot "evidence/$($scope.id)/$required") -PathType Leaf)) {
                    $failures.Add("gold packet '$($scope.id)' is missing '$required'")
                }
            }
        }
    }
}

if ($failures.Count -gt 0) {
    foreach ($failure in @($failures | Sort-Object -Unique)) { Write-Error "INITIATIVE: $failure" -ErrorAction Continue }
    exit 1
}
Write-Output "INITIATIVE PASS cards=$($cardIds.Count) progress=$($progressRows.Count) roadmap=$($roadmapRows.Count) primerIds=$($catalogIds.Count) packets=$(@($packetIndex.scopes).Count) inProgress=$($inProgress.Count)"
