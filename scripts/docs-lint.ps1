[CmdletBinding()]
param(
    [string[]]$Roots = @("docs"),
    [string]$TermMapPath = "docs/_term-map.json",
    [string]$RedirectStubRoot = "documentation",
    [switch]$FailOnWarning
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = (Get-Location).ProviderPath
$modulePath = Join-Path $PSScriptRoot "KoanDocs.Tools.psm1"
Import-Module $modulePath -Force

$termMap = @{}
$termMapFile = Join-Path $repoRoot $TermMapPath
if (Test-Path $termMapFile) {
    try {
        $termMap = Get-Content -Path $termMapFile -Raw | ConvertFrom-Json
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

    $documentCache[$relativePath] = Resolve-DocData -RelativePath $relativePath -Cache $documentCache
}

$issues = New-Object System.Collections.Generic.List[object]

$allowedTypes = @("REF", "GUIDE", "ARCH", "DEV", "SUPPORT")
$allowedDomains = @("core", "data", "web", "ai", "flow", "messaging", "storage", "media", "orchestration", "scheduling")
$allowedStatuses = @("current", "draft", "deprecated")
$allowedAudience = @("developers", "architects", "ai-agents")

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

foreach ($entry in $documentCache.Values) {
    $relativePath = $entry.Path

    if ($relativePath -like 'docs/api/*') {
        continue
    }

    if ($entry.IsEmpty) {
        Add-Issue -Path $relativePath -Severity "Error" -Check "Content" -Message "Document has no content"
        continue
    }

    $frontMatter = $entry.FrontMatter
    if ($frontMatter.Count -eq 0) {
        Add-Issue -Path $relativePath -Severity "Error" -Check "FrontMatter" -Message "Missing front-matter block"
        continue
    }

    foreach ($required in @("type", "domain", "audience", "status", "last_updated", "framework_version", "validation")) {
        if (-not $frontMatter.Contains($required)) {
            Add-Issue -Path $relativePath -Severity "Error" -Check "FrontMatter" -Message "Missing required key '$required'"
        }
    }

    if ($frontMatter.Contains("type")) {
        $typeValue = $frontMatter["type"]
        if ($allowedTypes -notcontains $typeValue) {
            Add-Issue -Path $relativePath -Severity "Error" -Check "FrontMatter" -Message "type '$typeValue' must be one of: $($allowedTypes -join ', ')"
        }
    }

    if ($frontMatter.Contains("domain")) {
        $domainValue = $frontMatter["domain"]
        if ($allowedDomains -notcontains $domainValue) {
            Add-Issue -Path $relativePath -Severity "Error" -Check "FrontMatter" -Message "domain '$domainValue' must be one of: $($allowedDomains -join ', ')"
        }
    }

    if ($frontMatter.Contains("status")) {
        $statusValue = $frontMatter["status"]
        if ($allowedStatuses -notcontains $statusValue) {
            Add-Issue -Path $relativePath -Severity "Error" -Check "FrontMatter" -Message "status '$statusValue' must be one of: $($allowedStatuses -join ', ')"
        }
    }

    if ($frontMatter.Contains("audience")) {
        $audienceValues = Get-AudienceValues -Value $frontMatter["audience"]
        if ($audienceValues.Count -eq 0) {
            Add-Issue -Path $relativePath -Severity "Error" -Check "FrontMatter" -Message "audience must list at least one value"
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
            Add-Issue -Path $relativePath -Severity "Error" -Check "FrontMatter" -Message "last_updated '$lastUpdated' must use YYYY-MM-DD"
        }
    }

    if ($frontMatter.Contains("framework_version")) {
        $frameworkVersionValue = $frontMatter["framework_version"]
        if ($frameworkVersionValue -notmatch '^v\d+\.\d+\.\d+$') {
            Add-Issue -Path $relativePath -Severity "Error" -Check "FrontMatter" -Message "framework_version '$frameworkVersionValue' must follow semantic versioning (v0.x.y)"
        }
    }

    if ($frontMatter.Contains("validation")) {
        $validation = $frontMatter["validation"]
        if ($validation -isnot [System.Collections.IDictionary]) {
            Add-Issue -Path $relativePath -Severity "Error" -Check "FrontMatter" -Message "validation block must be a mapping"
        }
        else {
            if ($validation.Contains("date_last_tested")) {
                $validationDate = $validation["date_last_tested"]
                if ($validationDate -and -not [datetime]::TryParseExact($validationDate, "yyyy-MM-dd", $null, [System.Globalization.DateTimeStyles]::None, [ref]([datetime]::MinValue))) {
                    Add-Issue -Path $relativePath -Severity "Error" -Check "FrontMatter" -Message "validation.date_last_tested '$validationDate' must use YYYY-MM-DD"
                }
            }
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
    if ($termMap.Count -gt 0) {
        foreach ($preferred in $termMap.PSObject.Properties.Name) {
            $discouraged = $termMap.$preferred
            if ($null -eq $discouraged) { continue }
            foreach ($term in $discouraged) {
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

$errors = @($issues | Where-Object { $_.Severity -eq "Error" })
$warnings = @($issues | Where-Object { $_.Severity -eq "Warning" })

$issueCount = $issues.Count
$errorCount = $errors.Count
$warningCount = $warnings.Count

if ($issueCount -eq 0) {
    Write-Host "All documentation checks passed"
}
else {
    $issues | Sort-Object Path, Severity | Format-Table -AutoSize
    Write-Host "Errors: $errorCount; Warnings: $warningCount"
}

if ($errorCount -gt 0) {
    exit 1
}

if ($FailOnWarning -and $warningCount -gt 0) {
    exit 1
}

exit 0
