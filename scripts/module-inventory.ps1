param(
    [string]$Root = "src",
    [string]$Output = "artifacts/module-inventory.json",
    [string]$LedgerOutput = "artifacts/module-inventory.md"
)

$ErrorActionPreference = "Stop"

$projects = Get-ChildItem -Path $Root -Filter *.csproj -Recurse | ForEach-Object {
    $projPath = $_.FullName
    $projName = $_.BaseName
    $projDir = Split-Path -Parent $projPath
    $xml = [xml](Get-Content -LiteralPath $projPath)
    $refs = @()
    foreach ($ig in $xml.Project.ItemGroup) {
        if ($ig.ProjectReference) {
            foreach ($pref in $ig.ProjectReference) {
                $refs += [System.IO.Path]::GetFileNameWithoutExtension($pref.Include)
            }
        }
    }
    $sortedRefs = $refs | Sort-Object -Unique
    if (-not $sortedRefs) {
        $sortedRefs = @()
    }
    [PSCustomObject]@{
        Name       = $projName
        Path       = $projPath
        References = @($sortedRefs)
        Docs       = [PSCustomObject]@{
            Readme    = Test-Path -LiteralPath (Join-Path $projDir 'README.md')
            Technical = Test-Path -LiteralPath (Join-Path $projDir 'TECHNICAL.md')
        }
    }
}

$projects = $projects | Sort-Object Name

# Build reverse dependency map
$referenceMap = @{}
foreach ($project in $projects) {
    $referenceMap[$project.Name] = [System.Collections.Generic.List[string]]::new()
}

foreach ($project in $projects) {
    foreach ($reference in $project.References) {
        if (-not $referenceMap.ContainsKey($reference)) {
            $referenceMap[$reference] = [System.Collections.Generic.List[string]]::new()
        }
        $referenceMap[$reference].Add($project.Name)
    }
}

$enriched = $projects | ForEach-Object {
    $dependents = @()
    if ($referenceMap.ContainsKey($_.Name)) {
        $dependents = $referenceMap[$_.Name] | Sort-Object -Unique
    }
    [PSCustomObject]@{
        Name         = $_.Name
        Path         = $_.Path
        References   = @($_.References)
        ReferencedBy = @($dependents)
        Docs         = $_.Docs
    }
}

$enriched | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $Output
Write-Host "Wrote $($enriched.Count) project entries to $Output"

if ($LedgerOutput) {
    $formatList = {
        param($items)
        if (-not $items -or $items.Count -eq 0) {
            return "–"
        }
        return ($items | Sort-Object) -join ", "
    }

    $rows = @()
    $rows += "# Module Inventory Ledger"
    $rows += ""
    foreach ($project in $enriched) {
        $rows += "### $($project.Name)"
        $rows += "- Depends on: $(& $formatList $project.References)"
        $rows += "- Depended by: $(& $formatList $project.ReferencedBy)"
        $rows += "- Documentation: README $(if ($project.Docs.Readme) { '✅' } else { '❌' }) · TECHNICAL $(if ($project.Docs.Technical) { '✅' } else { '❌' })"
        $rows += ""
    }

    $rows | Set-Content -LiteralPath $LedgerOutput
    Write-Host "Wrote ledger to $LedgerOutput"
}
