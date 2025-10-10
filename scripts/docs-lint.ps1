[CmdletBinding()]
param(
    [string[]]$Roots = @("docs"),
    [string]$TermMapPath = "docs/_term-map.json",
    [string]$RedirectStubRoot = "documentation",
    [string[]]$Exclude = @('docs/archive/**', 'docs/migration/**', 'docs/proposals/**', 'docs/external/**', 'docs/templates/**', 'docs/reference/_generated/**'),
    [switch]$EnforceFrontMatter,
    [switch]$FailOnWarning,
    [ValidateSet('table','list','json')]
    [string]$Output = 'table',
    [switch]$ValidateToc
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = (Get-Location).ProviderPath
$modulePath = Join-Path $PSScriptRoot "KoanDocs.Tools.psm1"
Import-Module $modulePath -Force

$repoVersion = $null
try {
    $verPath = Join-Path $repoRoot 'version.json'
    if (Test-Path $verPath) {
        $verJson = Get-Content -Path $verPath -Raw | ConvertFrom-Json
        if ($verJson.version) { $repoVersion = "v$($verJson.version)" }
    }
}
catch {
    Write-Verbose "Unable to read version.json: $_"
}
$hasYaml = $false
try {
    if (Get-Command ConvertFrom-Yaml -ErrorAction SilentlyContinue) { $hasYaml = $true }
    else { Import-Module powershell-yaml -ErrorAction Stop | Out-Null; $hasYaml = $true }
}
catch { }


$termMap = @{}
$termMapFile = Join-Path $repoRoot $TermMapPath
if (Test-Path $termMapFile) {
    try {
        $tm = Get-Content -Path $termMapFile -Raw | ConvertFrom-Json
        # Normalize to hashtable for StrictMode safety
        if ($null -ne $tm) {
            if ($tm -is [System.Collections.IDictionary]) {
                $termMap = $tm
            }
            else {
                $h = @{}
                foreach ($p in $tm.PSObject.Properties) { $h[$p.Name] = $p.Value }
                $termMap = $h
            }
        }
    }
    catch {
        Write-Warning "Unable to parse term map at ${TermMapPath}: $_"
        $termMap = @{}
    }
}

function Resolve-DocData {
    param(
        [string]$RelativePath,
        [hashtable]$Cache
    )

    if ($Cache.ContainsKey($RelativePath)) {
        return $Cache[$RelativePath]
    }

    $fullPath = Join-Path $repoRoot $RelativePath
    if (-not (Test-Path $fullPath)) {
        return $null
    }

    $content = Get-Content -Path $fullPath -Raw
    if ($null -eq $content) { $content = "" }

    $isEmpty = [string]::IsNullOrEmpty($content)
    if ($isEmpty) {
        $frontMatter = @{}
        $headings = @()
        $links = @()
        $anchorSlugs = @{}
    }
    else {
        $frontMatter = Get-KoanDocFrontMatter -Content $content
        $headings = Get-KoanDocHeadings -Content $content
        $links = Get-KoanDocLinks -Content $content -FilePath $fullPath -RepositoryRoot $repoRoot
        $anchorSlugs = @{}
        foreach ($heading in $headings) {
            $slug = Get-KoanAnchorSlug -HeadingText $heading.Text
            if (-not $anchorSlugs.ContainsKey($slug)) {
                $anchorSlugs[$slug] = 0
            }
            $anchorSlugs[$slug]++
        }
    }

    $data = [PSCustomObject]@{
        Path        = $RelativePath
        Content     = $content
        FrontMatter = $frontMatter
        Headings    = $headings
        Links       = $links
        AnchorSlugs = $anchorSlugs
        IsEmpty     = $isEmpty
    }

    $Cache[$RelativePath] = $data
    return $data
}

$files = @()
foreach ($root in $Roots) {
    $absoluteRoot = Join-Path $repoRoot $root
    if (-not (Test-Path $absoluteRoot)) { continue }
    $files += Get-ChildItem -Path $absoluteRoot -Recurse -Filter *.md | Where-Object { -not $_.PSIsContainer }
}

$files = $files | Sort-Object FullName -Unique

$documentCache = @{}
foreach ($file in $files) {
    $relativePath = Get-KoanRelativePath -FullPath $file.FullName -RepositoryRoot $repoRoot
    if ($null -eq $relativePath) { continue }

    # Apply exclude patterns (wildcards)
    $skip = $false
    foreach ($pattern in $Exclude) {
        if ($relativePath -like $pattern) { $skip = $true; break }
    }
    if ($skip) { continue }

    $documentCache[$relativePath] = Resolve-DocData -RelativePath $relativePath -Cache $documentCache
}

$issues = New-Object System.Collections.Generic.List[object]

$allowedTypes = @("REF", "GUIDE", "ARCH", "DEV", "SUPPORT", "ARCHITECTURE", "REFERENCE", "ENGINEERING", "DESIGN", "SPEC")
$allowedDomains = @("core", "data", "web", "ai", "flow", "messaging", "storage", "media", "orchestration", "scheduling", "framework", "architecture", "engineering", "performance", "troubleshooting", "platform", "canon")
$allowedStatuses = @("current", "draft", "deprecated")
$allowedAudience = @("developers", "architects", "ai-agents", "maintainers", "support-engineers", "security-engineers", "technical-leads", "ai-engineers")

function Add-Issue {
    param(
        [string]$Path,
        [string]$Severity,
        [string]$Check,
        [string]$Message
    )

    $issues.Add([PSCustomObject]@{
            Path     = $Path
            Severity = $Severity
            Check    = $Check
            Message  = $Message
        }) | Out-Null
}

function Get-TocEntries {
    param([string]$TocFile)

    $entries = New-Object System.Collections.Generic.List[object]
    if (-not (Test-Path $TocFile)) { return $entries }

    if (-not $hasYaml) {
        Add-Issue -Path 'docs/toc.yml' -Severity 'Warning' -Check 'TOC' -Message 'YAML module not available; skipping TOC href validation'
        return $entries
    }

    try {
        $toc = Get-Content -Path $TocFile -Raw | ConvertFrom-Yaml
        if ($null -eq $toc) { return $entries }

        function Walk($node) {
            if ($null -eq $node) { return }
            if ($node -is [System.Collections.IEnumerable] -and -not ($node -is [string])) {
                foreach ($n in $node) { Walk $n }
                return
            }
            $ps = $node.PSObject
            if ($ps -and $ps.Properties["href"]) {
                $href = $ps.Properties["href"].Value
                if ($href) { $entries.Add([pscustomobject]@{ href = $href }) | Out-Null }
            }
            if ($ps -and $ps.Properties["items"]) { Walk $ps.Properties["items"].Value }
        }

        Walk $toc
        return $entries
    }
    catch {
        Add-Issue -Path 'docs/toc.yml' -Severity 'Error' -Check 'TOC' -Message "Failed to parse YAML: $($_.Exception.Message)"
        return $entries
    }

    return $entries
}

function Get-AudienceValues {
    param($Value)

    if ($null -eq $Value) { return @() }

    if ($Value -is [string]) {
        return @($Value)
    }

    if ($Value -is [System.Collections.IEnumerable]) {
        $items = @()
        foreach ($entry in $Value) {
            $items += $entry
        }
        return $items
    }

    return @($Value.ToString())
}

$docEntries = @($documentCache.Values)
foreach ($entry in $docEntries) {
    $relativePath = $entry.Path

    if ($relativePath -like 'docs/api/*') {
        continue
    }

    if ($entry.IsEmpty) {
        Add-Issue -Path $relativePath -Severity "Error" -Check "Content" -Message "Document has no content"
        continue
    }

    $frontMatter = $entry.FrontMatter
    if ((-not ($frontMatter -is [System.Collections.IDictionary])) -or ($frontMatter.Count -eq 0)) {
        $sev = if ($EnforceFrontMatter) { "Error" } else { "Warning" }
        Add-Issue -Path $relativePath -Severity $sev -Check "FrontMatter" -Message "Missing front-matter block"
        continue
    }

    foreach ($required in @("type", "domain", "audience", "status", "last_updated", "framework_version", "validation")) {
        if (-not $frontMatter.Contains($required)) {
            $sev = if ($EnforceFrontMatter) { "Error" } else { "Warning" }
            Add-Issue -Path $relativePath -Severity $sev -Check "FrontMatter" -Message "Missing required key '$required'"
        }
    }

    if ($frontMatter.Contains("type")) {
        $typeValue = $frontMatter["type"]
        if ($allowedTypes -notcontains $typeValue) {
            $sev = if ($EnforceFrontMatter) { "Error" } else { "Warning" }
            Add-Issue -Path $relativePath -Severity $sev -Check "FrontMatter" -Message "type '$typeValue' should be one of: $($allowedTypes -join ', ')"
        }
    }

    if ($frontMatter.Contains("domain")) {
        $domainValue = $frontMatter["domain"]
        if ($allowedDomains -notcontains $domainValue) {
            $sev = if ($EnforceFrontMatter) { "Error" } else { "Warning" }
            Add-Issue -Path $relativePath -Severity $sev -Check "FrontMatter" -Message "domain '$domainValue' should be one of: $($allowedDomains -join ', ')"
        }
    }

    if ($frontMatter.Contains("status")) {
        $statusValue = $frontMatter["status"]
        if ($allowedStatuses -notcontains $statusValue) {
            $sev = if ($EnforceFrontMatter) { "Error" } else { "Warning" }
            Add-Issue -Path $relativePath -Severity $sev -Check "FrontMatter" -Message "status '$statusValue' should be one of: $($allowedStatuses -join ', ')"
        }
    }

    if ($frontMatter.Contains("audience")) {
        $audienceValues = Get-AudienceValues -Value $frontMatter["audience"]
        if ((@($audienceValues)).Count -eq 0) {
            $sev = if ($EnforceFrontMatter) { "Error" } else { "Warning" }
            Add-Issue -Path $relativePath -Severity $sev -Check "FrontMatter" -Message "audience must list at least one value"
        }
        foreach ($aud in $audienceValues) {
            if ($allowedAudience -notcontains $aud) {
                Add-Issue -Path $relativePath -Severity "Warning" -Check "FrontMatter" -Message "audience value '$aud' is not in the preferred set ($($allowedAudience -join ', '))"
            }
        }
    }

    if ($frontMatter.Contains("last_updated")) {
        $lastUpdated = $frontMatter["last_updated"]
        if (-not [datetime]::TryParseExact($lastUpdated, "yyyy-MM-dd", $null, [System.Globalization.DateTimeStyles]::None, [ref]([datetime]::MinValue))) {
            $sev = if ($EnforceFrontMatter) { "Error" } else { "Warning" }
            Add-Issue -Path $relativePath -Severity $sev -Check "FrontMatter" -Message "last_updated '$lastUpdated' must use YYYY-MM-DD"
        }
    }

    if ($frontMatter.Contains("framework_version")) {
        $frameworkVersionValue = $frontMatter["framework_version"]
        # Normalize stray quotes from certain front-matter serializers
        if ($frameworkVersionValue -is [string]) { $frameworkVersionValue = $frameworkVersionValue.Trim('"', "'") }
        if ($frameworkVersionValue -notmatch '^v\d+\.\d+\.\d+(\+)?$') {
            $sev = if ($EnforceFrontMatter) { "Error" } else { "Warning" }
            Add-Issue -Path $relativePath -Severity $sev -Check "FrontMatter" -Message "framework_version '$frameworkVersionValue' should follow semantic versioning (v0.x.y[+])"
        }
        elseif ($repoVersion -and ($frameworkVersionValue -ne $repoVersion -and $frameworkVersionValue -ne "$repoVersion+")) {
            Add-Issue -Path $relativePath -Severity "Warning" -Check "FrontMatter" -Message "framework_version '$frameworkVersionValue' does not match repo version '$repoVersion'"
        }
    }

    if ($frontMatter.Contains("validation")) {
        $validation = $frontMatter["validation"]
        if ($validation -is [string]) {
            # Accept legacy single date string
            if (-not [datetime]::TryParseExact($validation, "yyyy-MM-dd", $null, [System.Globalization.DateTimeStyles]::None, [ref]([datetime]::MinValue))) {
                $sev = if ($EnforceFrontMatter) { "Error" } else { "Warning" }
                Add-Issue -Path $relativePath -Severity $sev -Check "FrontMatter" -Message "validation '$validation' should use YYYY-MM-DD"
            }
        }
        elseif ($validation -is [System.Collections.IDictionary]) {
            if ($validation.Contains("date_last_tested")) {
                $validationDate = $validation["date_last_tested"]
                if ($validationDate -and -not [datetime]::TryParseExact($validationDate, "yyyy-MM-dd", $null, [System.Globalization.DateTimeStyles]::None, [ref]([datetime]::MinValue))) {
                    $sev = if ($EnforceFrontMatter) { "Error" } else { "Warning" }
                    Add-Issue -Path $relativePath -Severity $sev -Check "FrontMatter" -Message "validation.date_last_tested '$validationDate' must use YYYY-MM-DD"
                }
            }
        }
        else {
            $sev = if ($EnforceFrontMatter) { "Error" } else { "Warning" }
            Add-Issue -Path $relativePath -Severity $sev -Check "FrontMatter" -Message "validation block should be a date string or mapping"
        }
    }

    # Link checks
    foreach ($link in $entry.Links) {
        if ($link.IsExternal) { continue }

        if ($null -eq $link.Path) {
            if (-not [string]::IsNullOrEmpty($link.Raw) -and $link.Raw -match '^#' -and $link.Raw.Length -gt 1) {
                $slug = $link.Raw.TrimStart('#')
                if (-not $entry.AnchorSlugs.ContainsKey($slug)) {
                    Add-Issue -Path $relativePath -Severity "Error" -Check "Links" -Message "Anchor '#$slug' not found in document"
                }
            }
            continue
        }

        if ($link.Path -match '(^|/)documentation/') {
            Add-Issue -Path $relativePath -Severity "Error" -Check "Redirects" -Message "Link references legacy /documentation path '$($link.Raw)'"
            continue
        }

        $targetFullPath = Join-Path $repoRoot $link.Path
        if (-not (Test-Path $targetFullPath)) {
            Add-Issue -Path $relativePath -Severity "Error" -Check "Links" -Message "Target '$($link.Raw)' does not exist"
            continue
        }

        if (-not [string]::IsNullOrEmpty($link.Anchor)) {
            $targetData = Resolve-DocData -RelativePath $link.Path -Cache $documentCache
            if ($null -eq $targetData) {
                continue
            }

            $anchorSlug = Get-KoanAnchorSlug -HeadingText $link.Anchor
            if (-not $targetData.AnchorSlugs.ContainsKey($anchorSlug)) {
                Add-Issue -Path $relativePath -Severity "Error" -Check "Links" -Message "Anchor '#$($link.Anchor)' missing in '$($link.Path)'"
            }
        }
    }

    # Term map enforcement
    if ((@($termMap.Keys)).Count -gt 0) {
        foreach ($preferred in $termMap.Keys) {
            $discouraged = $termMap[$preferred]
            if ($null -eq $discouraged) { continue }
            foreach ($term in @($discouraged)) {
                if ([string]::IsNullOrWhiteSpace($term)) { continue }
                if ($entry.Content -match "(?i)\b$([regex]::Escape($term))\b") {
                    Add-Issue -Path $relativePath -Severity "Warning" -Check "Terminology" -Message "Use '$preferred' instead of '$term'"
                }
            }
        }
    }

    if ($entry.Content -match '(?i)/documentation/') {
        Add-Issue -Path $relativePath -Severity "Warning" -Check "Redirects" -Message "Document references '/documentation/' in raw text"
    }
}

    # Optional TOC validation gate
    if ($ValidateToc) {
        $tocPath = Join-Path $repoRoot 'docs/toc.yml'
        if (-not (Test-Path $tocPath)) {
            Add-Issue -Path 'docs/toc.yml' -Severity 'Error' -Check 'TOC' -Message 'TOC not found at docs/toc.yml'
        }
        else {
            $tocEntries = Get-TocEntries -TocFile $tocPath
            foreach ($e in $tocEntries) {
                $href = [string]$e.href
                if ([string]::IsNullOrWhiteSpace($href)) { continue }
                if ($href -match '^[a-z]+://') { continue } # external link
                $hrefNoAnchor = $href.Split('#')[0]
                if ([string]::IsNullOrWhiteSpace($hrefNoAnchor)) { continue }
                # Only validate markdown and YAML references
                if ($hrefNoAnchor -notmatch "\.(md|yml)$") { continue }
                $target = Join-Path (Join-Path $repoRoot 'docs') $hrefNoAnchor
                if (-not (Test-Path $target)) {
                    Add-Issue -Path 'docs/toc.yml' -Severity 'Error' -Check 'TOC' -Message "Missing target for href '$href'"
                }
            }
        }
    }

$errors = @($issues | Where-Object { $_.Severity -eq "Error" })
$warnings = @($issues | Where-Object { $_.Severity -eq "Warning" })

$issueCount = $issues.Count
$errorCount = $errors.Count
$warningCount = $warnings.Count

if ($issueCount -eq 0) {
    Write-Host "All documentation checks passed"
}
else {
    $sorted = $issues | Sort-Object Path, Severity
    switch ($Output) {
        'json' { $sorted | ConvertTo-Json -Depth 6 | Write-Output }
        'list' { $sorted | Format-List Path, Severity, Check, Message }
        default { $sorted | Format-Table -AutoSize }
    }
    if ($Output -ne 'json') {
        Write-Host "Errors: $errorCount; Warnings: $warningCount"
    }
}

if ($errorCount -gt 0) {
    exit 1
}

if ($FailOnWarning -and $warningCount -gt 0) {
    exit 1
}

exit 0
