[CmdletBinding()]
param(
    [switch]$Inventory,
    [ValidateSet('table', 'json')]
    [string]$Output = 'table'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$tocPath = Join-Path $root 'docs/toc.yml'
$issues = [System.Collections.Generic.List[string]]::new()
$historicalPattern = '^docs/(archive|assessment|epic-assessment|initiatives|migration|proposals|decisions)/'

if (-not (Test-Path -LiteralPath $tocPath)) {
    throw 'docs/toc.yml is missing.'
}

$trackedPaths = @(git -C $root ls-files --) | ForEach-Object { $_.Replace('\', '/') }
if ($LASTEXITCODE -ne 0) {
    throw 'Unable to enumerate repository-tracked paths.'
}

$tracked = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
$trackedPaths | ForEach-Object { [void]$tracked.Add($_) }
$entries = @{}

function Add-PublicAsset {
    param(
        [Parameter(Mandatory)] [string]$Path,
        [Parameter(Mandatory)] [string]$Origin,
        [Parameter(Mandatory)] [string]$Purpose,
        [switch]$Historical
    )

    $normalized = $Path.Replace('\', '/')
    if ($normalized.StartsWith('./', [StringComparison]::Ordinal)) {
        $normalized = $normalized.Substring(2)
    }
    if (-not $tracked.Contains($normalized)) {
        $issues.Add("Public asset is missing or untracked: $normalized ($Origin).")
        return
    }

    if (-not $entries.ContainsKey($normalized)) {
        $entries[$normalized] = [PSCustomObject]@{
            Path = $normalized
            Origins = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
            Purposes = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
            Historical = [bool]$Historical
        }
    }

    $entry = $entries[$normalized]
    [void]$entry.Origins.Add($Origin)
    [void]$entry.Purposes.Add($Purpose)
    if (-not $Historical) { $entry.Historical = $false }
}

function Resolve-RepositoryLink {
    param(
        [Parameter(Mandatory)] [string]$FromPath,
        [Parameter(Mandatory)] [string]$Target
    )

    $candidate = $Target.Trim()
    if ($candidate.StartsWith('<') -and $candidate.EndsWith('>')) {
        $candidate = $candidate.Substring(1, $candidate.Length - 2)
    }
    $candidate = $candidate.Split('#')[0]
    if ([string]::IsNullOrWhiteSpace($candidate) -or $candidate -match '^[a-zA-Z][a-zA-Z0-9+.-]*:') {
        return $null
    }

    $fromDirectory = Split-Path -Parent (Join-Path $root $FromPath)
    $fullPath = if ($candidate.StartsWith('/')) {
        [IO.Path]::GetFullPath((Join-Path $root $candidate.TrimStart('/')))
    }
    else {
        [IO.Path]::GetFullPath((Join-Path $fromDirectory $candidate))
    }

    if (-not $fullPath.StartsWith($root, [StringComparison]::OrdinalIgnoreCase)) {
        return $null
    }

    return [IO.Path]::GetRelativePath($root, $fullPath).Replace('\', '/')
}

function Get-MarkdownLinks {
    param([Parameter(Mandatory)] [string]$Path)

    if ($Path -notmatch '\.(md|txt)$') { return @() }
    $content = Get-Content -Raw -LiteralPath (Join-Path $root $Path)
    if ($null -eq $content) { return @() }

    $links = [System.Collections.Generic.List[string]]::new()
    foreach ($match in [regex]::Matches($content, '\[[^\]]*\]\((?<target>[^)]+)\)')) {
        $resolved = Resolve-RepositoryLink -FromPath $Path -Target $match.Groups['target'].Value
        if ($resolved -and $tracked.Contains($resolved)) {
            $links.Add($resolved)
        }
    }
    return $links
}

function Get-CurriculumPurpose {
    param([string]$Path)

    switch -Regex ($Path) {
        '^(README\.md|docs/index\.md)$' { return 'orient' }
        '^(llms\.txt|CLAUDE\.md|\.github/copilot-instructions\.md)$' { return 'agent-orient' }
        '^CONTRIBUTING\.md$' { return 'contribute' }
        '^docs/getting-started/' { return 'start' }
        '^docs/guides/' { return 'apply' }
        '^docs/reference/' { return 'evaluate' }
        '^docs/architecture/' { return 'understand' }
        '^docs/support/' { return 'troubleshoot' }
        '^samples/' { return 'learn' }
        default { return 'learn' }
    }
}

$tocHrefs = @(Select-String -Path $tocPath -Pattern 'href:\s*(.+)$' | ForEach-Object {
    $_.Matches[0].Groups[1].Value.Trim().Trim('"')
})

$forbiddenNavigation = @(
    'archive/',
    'assessment/',
    'epic-assessment/',
    'initiatives/',
    'migration/',
    'proposals/'
)
foreach ($href in $tocHrefs) {
    $normalized = $href.Replace('\', '/')
    foreach ($prefix in $forbiddenNavigation) {
        if ($normalized.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) {
            $issues.Add("docs/toc.yml publishes non-product material: $href")
        }
    }
}

$curriculumSeeds = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
foreach ($path in @(
    'README.md',
    'docs/index.md',
    'samples/README.md',
    'llms.txt',
    'CLAUDE.md',
    'CONTRIBUTING.md',
    '.github/copilot-instructions.md'
)) {
    [void]$curriculumSeeds.Add($path)
}

foreach ($href in $tocHrefs) {
    $fullPath = [IO.Path]::GetFullPath((Join-Path (Join-Path $root 'docs') $href))
    if ($fullPath.StartsWith($root, [StringComparison]::OrdinalIgnoreCase)) {
        [void]$curriculumSeeds.Add([IO.Path]::GetRelativePath($root, $fullPath).Replace('\', '/'))
    }
}

$visited = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
$pending = [System.Collections.Generic.Queue[string]]::new()
$curriculumSeeds | ForEach-Object { $pending.Enqueue($_) }
while ($pending.Count -gt 0) {
    $path = $pending.Dequeue()
    if (-not $visited.Add($path)) { continue }

    $historical = $path -match $historicalPattern
    Add-PublicAsset -Path $path -Origin $(if ($historical) { 'history-boundary' } else { 'curriculum' }) `
        -Purpose $(if ($historical) { 'evaluate-history' } else { Get-CurriculumPurpose $path }) `
        -Historical:$historical
    if ($historical -or -not $tracked.Contains($path)) { continue }

    foreach ($link in Get-MarkdownLinks -Path $path) {
        if (-not $visited.Contains($link)) { $pending.Enqueue($link) }
    }
}

# NuGet-delivered companion documents are public even when not linked from the website curriculum.
foreach ($path in $trackedPaths | Where-Object {
    $_ -match '^(src|packaging|templates|tools)/.+/(README|TECHNICAL)\.md$'
}) {
    $purpose = if ($path.EndsWith('/TECHNICAL.md', [StringComparison]::OrdinalIgnoreCase)) { 'extend-operate' } else { 'apply-evaluate' }
    Add-PublicAsset -Path $path -Origin 'package' -Purpose $purpose
}

# Generated maturity/package projections and their irreducible claim source form one product-truth surface.
foreach ($path in @(
    'product/claims.json',
    'docs/reference/product-surface.json',
    'docs/reference/product-surface.md',
    'docs/reference/package-quality.json',
    'docs/reference/package-quality.md'
)) {
    Add-PublicAsset -Path $path -Origin 'product-truth' -Purpose 'evaluate'
}

# Listing a sample once in samples/README.md admits its entire tracked directory as public curriculum.
$sampleDirectories = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
foreach ($link in Get-MarkdownLinks -Path 'samples/README.md') {
    if ($link -match '^samples/.+/README\.md$') {
        [void]$sampleDirectories.Add(($link.Substring(0, $link.Length - '/README.md'.Length)).TrimEnd('/') + '/')
    }
}
foreach ($directory in $sampleDirectories) {
    $directProjects = @($trackedPaths | Where-Object {
        $_.StartsWith($directory, [StringComparison]::OrdinalIgnoreCase) -and
        $_.Substring($directory.Length) -notmatch '/' -and
        $_.EndsWith('.csproj', [StringComparison]::OrdinalIgnoreCase)
    })
    if ($directProjects.Count -eq 0) { continue }

    $launcher = $directory.TrimEnd('/') + '/start.bat'
    if (-not $tracked.Contains($launcher)) {
        $issues.Add("Graduated sample root '$($directory.TrimEnd('/'))' is missing start.bat; add the standard root-local dotnet launcher.")
        continue
    }

    $launcherContent = Get-Content -Raw -LiteralPath (Join-Path $root $launcher)
    foreach ($required in @('pushd "%~dp0"', 'set "koan_exit=%errorlevel%"', 'popd', 'exit /b %koan_exit%')) {
        if (-not $launcherContent.Contains($required, [StringComparison]::OrdinalIgnoreCase)) {
            $issues.Add("$launcher is missing '$required'; keep the standard working-directory and exit-code contract.")
        }
    }

    $runMatch = [regex]::Match(
        $launcherContent,
        '(?im)^\s*dotnet\s+run\s+--project\s+"(?<project>[^"]+\.csproj)"\s+--\s+%\*\s*$')
    if (-not $runMatch.Success) {
        $issues.Add($launcher + ' must invoke one explicit project with dotnet run --project "<project>.csproj" -- %*.')
        continue
    }

    $projectPath = ($directory + $runMatch.Groups['project'].Value).Replace('\', '/')
    if (-not $tracked.Contains($projectPath)) {
        $issues.Add("$launcher targets missing or untracked project '$projectPath'.")
    }
}
foreach ($path in $trackedPaths) {
    foreach ($directory in $sampleDirectories) {
        if ($path.StartsWith($directory, [StringComparison]::OrdinalIgnoreCase)) {
            Add-PublicAsset -Path $path -Origin 'sample' -Purpose 'learn-apply'
            break
        }
    }
}

foreach ($path in $trackedPaths | Where-Object { $_ -match '^templates/(koan-console|koan-web)/' }) {
    Add-PublicAsset -Path $path -Origin 'template' -Purpose 'start'
}
foreach ($path in $trackedPaths | Where-Object { $_ -match '^\.claude/skills/' }) {
    Add-PublicAsset -Path $path -Origin 'agent-skill' -Purpose 'agent-apply'
}
foreach ($path in $trackedPaths | Where-Object { $_ -match '^\.claude/agents/' }) {
    Add-PublicAsset -Path $path -Origin 'agent-guidance' -Purpose 'agent-apply'
}
foreach ($path in @(
    '.github/ISSUE_TEMPLATE/bug_report.md',
    '.github/ISSUE_TEMPLATE/feature_request.md',
    '.github/pull_request_template.md'
)) {
    Add-PublicAsset -Path $path -Origin 'feedback' -Purpose 'contribute'
}

# Every nonhistorical Markdown file under docs must either join the current public graph or declare
# an evidence status. This closes the otherwise silent gap where a present-tense page can be added
# without navigation or any public-content validation.
$evidenceStatuses = '^(archived|deprecated|superseded|draft|proposed|proposal(?:,.*)?|open-for-review|resolved|active)$'
foreach ($path in $trackedPaths | Where-Object { $_ -match '^docs/.+\.md$' -and $_ -notmatch $historicalPattern }) {
    if ($entries.ContainsKey($path)) { continue }

    $content = Get-Content -Raw -LiteralPath (Join-Path $root $path)
    $status = if ($content -match '(?m)^status:\s*([^\r\n]+)\s*$') { $Matches[1].Trim() } else { '' }
    if ($status -eq 'current') {
        Add-PublicAsset -Path $path -Origin 'current-doc' -Purpose (Get-CurriculumPurpose $path)
    }
    elseif ($status -match $evidenceStatuses) {
        Add-PublicAsset -Path $path -Origin 'history-boundary' -Purpose 'evaluate-history' -Historical
    }
    else {
        $issues.Add("$path is outside the public graph without a current or evidence status; classify it, link it, or remove it.")
    }
}

$textPattern = '\.(md|txt|cs|json|ya?ml|html?|css|js|csproj|props|targets|ps1|sh|template)$'
$retiredTerms = [ordered]@{
    'KoanAutoRegistrar' = 'Use KoanModule and generated semantic module activation.'
    'auto-registrar' = 'Use KoanModule and generated semantic module activation.'
    'IKoanAutoRegistrar' = 'The compatibility registrar contract was removed.'
    'IKoanInitializer' = 'The compatibility initializer contract was removed.'
    'reflective discovery' = 'Current module activation is generated and compiled.'
    'Koan.Messaging' = 'Use Entity Events/Transport and Koan.Communication.'
    'Koan.Flow' = 'Use Entity lifecycle, Jobs, Communication, or a normal application service at the owning boundary.'
    'FlowPipeline' = 'Use Entity lifecycle, Jobs, Communication, or a normal application service at the owning boundary.'
    'Flow pipelines' = 'Name the actual owning Entity, Jobs, Communication, or application pipeline.'
    'Flow adapters' = 'Name the actual provider or capability owner.'
    'Flow handlers' = 'Use a contributor, Entity lifecycle hook, Communication receiver, or application handler at the owning boundary.'
    'S14.AdapterBench' = 'Only graduated samples belong in public curriculum.'
    '/api/health' = 'Use /health/live and /health/ready.'
    '0.17.x' = 'Use the 0.20 preview line without pinning an exact patch.'
    'public 0.17.0' = 'Describe the 0.20 preview boundary without a stale package snapshot.'
    'source-first' = 'Describe the 0.20 preview and its exact current publication state.'
}

$packageCompanionFiles = @($entries.Values | Where-Object { $_.Origins.Contains('package') })
$currentTextEntries = @($entries.Values | Where-Object {
    -not $_.Historical -and ($_.Path -match $textPattern -or $_.Path -in @('DCO', 'LICENSE', 'NOTICE'))
})
foreach ($entry in $currentTextEntries) {
    $content = Get-Content -Raw -LiteralPath (Join-Path $root $entry.Path)
    if ($null -eq $content) { $content = '' }
    foreach ($term in $retiredTerms.Keys) {
        if ($content.Contains($term, [StringComparison]::OrdinalIgnoreCase)) {
            $issues.Add("$($entry.Path) contains retired public term '$term'. $($retiredTerms[$term])")
        }
    }

    if ($content -match '(?m)^\s*app\.Run\(\);\s*(?://.*)?$') {
        $issues.Add("$($entry.Path) uses non-awaited app.Run(); canonical public hosts use await app.RunAsync().")
    }
    if ($content -match '(?m)^\s*app\.MapControllers\(\);\s*(?://.*)?$') {
        $issues.Add("$($entry.Path) maps controllers manually; referenced Web modules contribute the canonical pipeline.")
    }
    if ($content -match 'dotnet\s+(add\s+package|new\s+install)\s+Koan\.') {
        $issues.Add("$($entry.Path) uses an unprefixed public package id; package ids are Sylin.Koan.*.")
    }
    if ($content -cmatch '(?<![A-Za-z0-9])S\d{1,2}\.[A-Z][A-Za-z]+') {
        $issues.Add("$($entry.Path) exposes an initiative-era numbered sample name; use the graduated semantic sample name.")
    }
    if ($entry.Path.EndsWith('.md', [StringComparison]::OrdinalIgnoreCase) -and
        $content -match '(?m)^status:\s*(deprecated|archived|superseded)\s*$') {
        $issues.Add("$($entry.Path) is a retired document in the current public graph; unlink and preserve it only as historical evidence.")
    }
    if ($entry.Path.EndsWith('.md', [StringComparison]::OrdinalIgnoreCase) -and
        $entry.Path -notin @('docs/reference/product-surface.md', 'docs/reference/package-quality.md') -and
        $content -match '(?i)(?<![\w.])v?0\.(17|18|19)(?:\.\d+|\.x)?(?![\w.])') {
        $issues.Add("$($entry.Path) presents an older release line as current narrative; write from the 0.20 preview boundary or move the evidence to history.")
    }
    if ($entry.Path -match '^docs/.+\.md$' -and
        $content -match '(?m)^framework_version:\s*([^\r\n]+)\s*$' -and
        $Matches[1].Trim() -ne 'v0.20.0') {
        $issues.Add("$($entry.Path) declares framework_version '$($Matches[1].Trim())'; current public docs declare v0.20.0.")
    }
}

foreach ($entry in $packageCompanionFiles) {
    $content = Get-Content -Raw -LiteralPath (Join-Path $root $entry.Path)
    if ($null -eq $content) { continue }

    if ($content -match 'PackageReference\s+Include="[^"]+"\s+Version="0\.') {
        $issues.Add("$($entry.Path) hard-codes a pre-1.0 package version; packages are independently patched within their compatibility line.")
    }
    if ($content -match '(?m)^framework_version:\s*v?0\.') {
        $issues.Add("$($entry.Path) presents one fixed framework version; package pages should state their generated package line and claim instead.")
    }
}

$frontDoorRequirements = [ordered]@{
    'README.md' = @('0.20', 'Sylin.Koan.Templates', 'AddKoan()', 'Entity<', 'product surface')
    'docs/index.md' = @('0.20', 'Sylin.Koan.Templates', 'AddKoan()', 'Entity<', 'product surface')
    'llms.txt' = @('0.20', 'Sylin.Koan.Templates', 'AddKoan()', 'Entity<', 'product surface')
    'CLAUDE.md' = @('0.20', 'AddKoan()', 'Entity<', 'product-surface.md')
    '.github/copilot-instructions.md' = @('0.20', 'AddKoan()', 'Entity<', 'product-surface.md')
    'templates/README.md' = @('0.20', 'dotnet new install Sylin.Koan.Templates', 'AddKoan()', 'Entity<')
    'samples/README.md' = @('0.20', 'FirstUse', 'GoldenJourney', 'product surface')
    'docs/reference/product-surface.md' = @('Maturity vocabulary', 'supported-foundation', 'supported-extension', 'verified', 'demonstrated', 'experimental', 'specified', 'unassessed', 'deprecated', 'retired')
}
foreach ($path in $frontDoorRequirements.Keys) {
    $content = Get-Content -Raw -LiteralPath (Join-Path $root $path)
    foreach ($phrase in $frontDoorRequirements[$path]) {
        if (-not $content.Contains($phrase, [StringComparison]::OrdinalIgnoreCase)) {
            $issues.Add("$path is missing canonical front-door phrase '$phrase'.")
        }
    }
}

$decisionChanges = @(git -C $root diff --name-only -- docs/decisions)
if ($decisionChanges.Count -gt 0) {
    $issues.Add("ADR files changed during the public documentation pass: $($decisionChanges -join ', ')")
}

$inventoryRows = @($entries.Values | Sort-Object Path | ForEach-Object {
    [PSCustomObject]@{
        path = $_.Path
        origins = @($_.Origins | Sort-Object)
        purposes = @($_.Purposes | Sort-Object)
        historical = $_.Historical
    }
})
if ($Inventory) {
    if ($Output -eq 'json') {
        $inventoryRows | ConvertTo-Json -Depth 5 | Write-Output
    }
    else {
        $inventoryRows | Select-Object path,
            @{ Name = 'origins'; Expression = { $_.origins -join ',' } },
            @{ Name = 'purposes'; Expression = { $_.purposes -join ',' } },
            historical | Format-Table -AutoSize
    }
}

if ($issues.Count -gt 0) {
    $issues | Sort-Object -Unique | ForEach-Object { Write-Host "ERROR: $_" -ForegroundColor Red }
    exit 1
}

$historicalCount = @($entries.Values | Where-Object Historical).Count
$currentCount = $entries.Count - $historicalCount
$summary = 'Public documentation truth gate passed ({0} current assets; {1} current text surfaces; ' +
    '{2} historical boundaries; {3} navigation targets; {4} graduated sample roots).'
Write-Host ($summary -f $currentCount, $currentTextEntries.Count, $historicalCount, $tocHrefs.Count, $sampleDirectories.Count)
