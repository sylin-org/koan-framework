[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$tocPath = Join-Path $root 'docs/toc.yml'
$issues = [System.Collections.Generic.List[string]]::new()

if (-not (Test-Path $tocPath)) {
    throw 'docs/toc.yml is missing.'
}

$hrefs = Select-String -Path $tocPath -Pattern 'href:\s*(.+)$' | ForEach-Object {
    $_.Matches[0].Groups[1].Value.Trim()
}

$forbiddenNavigation = @(
    'archive/',
    'assessment/',
    'epic-assessment/',
    'initiatives/',
    'migration/',
    'proposals/'
)

foreach ($href in $hrefs) {
    $normalized = $href.Replace('\', '/')
    foreach ($prefix in $forbiddenNavigation) {
        if ($normalized.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) {
            $issues.Add("docs/toc.yml publishes non-product material: $href")
        }
    }
}

$publicFiles = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
$packageCompanionFiles = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
foreach ($relative in @('README.md', 'llms.txt', 'CLAUDE.md', 'CONTRIBUTING.md', 'samples/README.md')) {
    [void]$publicFiles.Add((Join-Path $root $relative))
}

# Package README/TECHNICAL files are public through NuGet and the generated product surface even when
# they are not part of the website curriculum.
foreach ($directory in @('src', 'packaging', 'templates', 'tools')) {
    $absolute = Join-Path $root $directory
    if (-not (Test-Path $absolute)) { continue }
    Get-ChildItem $absolute -Recurse -File | Where-Object {
        $_.Name -in @('README.md', 'TECHNICAL.md') -and
        $_.FullName -notmatch '[\\/](bin|obj)[\\/]'
    } | ForEach-Object {
        [void]$publicFiles.Add($_.FullName)
        [void]$packageCompanionFiles.Add($_.FullName)
    }
}

foreach ($href in $hrefs) {
    $candidate = [IO.Path]::GetFullPath((Join-Path (Join-Path $root 'docs') $href))
    if ((Test-Path $candidate -PathType Leaf) -and $candidate -notmatch '[\\/]docs[\\/]decisions[\\/]') {
        [void]$publicFiles.Add($candidate)
    }
}

$retiredTerms = [ordered]@{
    'KoanAutoRegistrar' = 'Use KoanModule and generated semantic module activation.'
    'auto-registrar' = 'Use KoanModule and generated semantic module activation.'
    'IKoanAutoRegistrar' = 'The compatibility registrar contract was removed.'
    'IKoanInitializer' = 'The compatibility initializer contract was removed.'
    'reflective discovery' = 'Current module activation is generated and compiled.'
    'Koan.Messaging' = 'Use Entity Events/Transport and Koan.Communication.'
    'S14.AdapterBench' = 'Only graduated samples belong in public curriculum.'
    '/api/health' = 'Use /health/live and /health/ready.'
    '0.17.x' = 'Packages own independent NBGV versions.'
    'public 0.17.0' = 'Describe the source-first/public-observation boundary without a stale package snapshot.'
}

foreach ($file in $publicFiles) {
    if (-not (Test-Path $file -PathType Leaf)) {
        $issues.Add("Public documentation target is missing: $file")
        continue
    }

    $content = Get-Content $file -Raw
    if ($null -eq $content) { $content = '' }
    foreach ($term in $retiredTerms.Keys) {
        if ($content.Contains($term, [StringComparison]::OrdinalIgnoreCase)) {
            $relative = [IO.Path]::GetRelativePath($root, $file)
            $issues.Add("$relative contains retired public term '$term'. $($retiredTerms[$term])")
        }
    }

    if ($content -match '(?m)^\s*app\.Run\(\);\s*$') {
        $relative = [IO.Path]::GetRelativePath($root, $file)
        $issues.Add("$relative uses non-awaited app.Run(); canonical public hosts use await app.RunAsync().")
    }
}

foreach ($file in $packageCompanionFiles) {
    $content = Get-Content $file -Raw
    if ($null -eq $content) { continue }
    $relative = [IO.Path]::GetRelativePath($root, $file)

    if ($content -match 'dotnet\s+add\s+package\s+Koan\.') {
        $issues.Add("$relative uses an unprefixed public package id; package ids are Sylin.Koan.*.")
    }
    if ($content -match 'PackageReference\s+Include="[^"]+"\s+Version="0\.') {
        $issues.Add("$relative hard-codes a pre-1.0 package version; packages are independently versioned by a coherent release wave.")
    }
    if ($content -match '(?m)^framework_version:\s*v?0\.') {
        $issues.Add("$relative presents one fixed framework version; current packages own independent versions.")
    }
}

$decisionChanges = @(git -C $root diff --name-only -- docs/decisions)
if ($decisionChanges.Count -gt 0) {
    $issues.Add("ADR files changed during the public documentation pass: $($decisionChanges -join ', ')")
}

if ($issues.Count -gt 0) {
    $issues | ForEach-Object { Write-Host "ERROR: $_" -ForegroundColor Red }
    exit 1
}

Write-Host "Public documentation truth gate passed ($($publicFiles.Count) current files; $($hrefs.Count) navigation targets)."
