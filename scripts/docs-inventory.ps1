param(
    [string[]]$Roots = @("docs", "documentation"),
    [string]$OutputMarkdown = "docs/_inventory.md",
    [string]$OutputJson = "artifacts/docs-inventory.json"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = (Get-Location).ProviderPath

function Get-FrameworkVersion {
    param([string]$Root)

    $versionFile = Join-Path $Root "version.json"
    if (-not (Test-Path $versionFile)) {
        return "v0.0.0"
    }

    try {
        $json = Get-Content -Path $versionFile -Raw | ConvertFrom-Json
        if ($null -ne $json.version -and $json.version -ne "") {
            return "v$($json.version)"
        }
    }
    catch {
        Write-Warning "Unable to parse version.json: $_"
    }

    return "v0.0.0"
}

$frameworkVersion = Get-FrameworkVersion -Root $repoRoot
$today = (Get-Date).ToString("yyyy-MM-dd")

function Get-FrontMatter {
    param([string]$Content)

    $result = [ordered]@{}

    if (-not $Content.StartsWith("---`n") -and -not $Content.StartsWith("---`r`n")) {
        return $result
    }

    $lines = $Content -split "`n"
    if ($lines.Length -lt 3) {
        return $result
    }

    $endIndex = -1
    for ($i = 1; $i -lt $lines.Length; $i++) {
        if ($lines[$i].Trim() -eq "---") {
            $endIndex = $i
            break
        }
    }

    if ($endIndex -lt 0) {
        return $result
    }

    if ($endIndex -le 1) {
        return $result
    }

    $frontMatterLines = $lines[1..($endIndex - 1)]

    for ($i = 0; $i -lt $frontMatterLines.Count; $i++) {
        $line = $frontMatterLines[$i]
        if ([string]::IsNullOrWhiteSpace($line)) {
            continue
        }

        if ($line -match '^(?<key>[A-Za-z0-9_\-]+):\s*(?<value>.*)$') {
            $key = $Matches.key
            $value = $Matches.value.Trim()

            if ($value -eq '') {
                # Check for nested block (e.g., audience list or validation)
                $childLines = @()
                $j = $i + 1
                while ($j -lt $frontMatterLines.Count) {
                    $childLine = $frontMatterLines[$j]
                    if ($childLine -match '^[A-Za-z0-9_\-]+:\s*') {
                        break
                    }
                    if (-not [string]::IsNullOrWhiteSpace($childLine)) {
                        $childLines += $childLine
                    }
                    $j++
                }

                if ($childLines.Count -gt 0) {
                    if ($childLines[0].Trim().StartsWith("-")) {
                        $items = @()
                        foreach ($childLine in $childLines) {
                            if ($childLine -match '^\s*\-\s*(?<item>.+)$') {
                                $items += $Matches.item.Trim()
                            }
                        }
                        $result[$key] = $items
                    }
                    else {
                        # Treat indented properties as key-value under a hashtable
                        $subMap = [ordered]@{}
                        foreach ($childLine in $childLines) {
                            if ($childLine -match '^\s*(?<subkey>[A-Za-z0-9_\-]+):\s*(?<subvalue>.*)$') {
                                $subMap[$Matches.subkey] = $Matches.subvalue.Trim()
                            }
                        }
                        if ($subMap.Count -gt 0) {
                            $result[$key] = $subMap
                        }
                    }

                    $i = $j - 1
                    continue
                }
            }
            elseif ($value.StartsWith("[")) {
                $trimmed = $value.Trim('[', ']').Trim()
                if ($trimmed -ne '') {
                    $items = @()
                    foreach ($item in $trimmed.Split(',')) {
                        $items += $item.Trim()
                    }
                    $result[$key] = $items
                }
                else {
                    $result[$key] = @()
                }
                continue
            }

            $result[$key] = $value
        }
    }

    return $result
}

function Get-Headings {
    param([string]$Content)

    $headingMatches = [regex]::Matches($Content, '^(?<level>#{1,6})\s+(?<text>.+)$', 'Multiline')
    $headings = @()
    foreach ($match in $headingMatches) {
        $headings += [PSCustomObject]@{
            Level = $match.Groups["level"].Value.Length
            Text  = $match.Groups["text"].Value.Trim()
        }
    }
    return $headings
}

function Get-Title {
    param([object[]]$Headings)

    foreach ($heading in $Headings) {
        if ($heading.Level -eq 1) {
            return $heading.Text
        }
    }
    return $null
}

function Get-OutboundLinks {
    param(
        [string]$Content,
        [string]$FilePath,
        [string]$Root
    )

    $directory = Split-Path -Path $FilePath -Parent
    $links = @()

    $linkMatches = [regex]::Matches($Content, '\[[^\]]+\]\((?<target>[^)]+)\)')
    foreach ($match in $linkMatches) {
        $targetRaw = $match.Groups["target"].Value.Trim()
        $anchor = $null
        $pathPart = $targetRaw

        if ($targetRaw -match '^(?<path>[^#]+)#(?<anchor>.+)$') {
            $pathPart = $Matches.path
            $anchor = $Matches.anchor
        }

        $isExternal = $false
        if ($pathPart -match '^[a-zA-Z]+://') {
            $isExternal = $true
        }
        elseif ($pathPart -like 'mailto:*' -or $pathPart -like 'tel:*') {
            $isExternal = $true
        }

        $normalizedPath = $null

        if (-not $isExternal) {
            if ($pathPart -like '#*' -or [string]::IsNullOrWhiteSpace($pathPart)) {
                $normalizedPath = $null
            }
            else {
                $candidate = $pathPart
                if ($candidate.StartsWith("/")) {
                    $candidate = $candidate.TrimStart('/')
                }

                if ($pathPart.StartsWith("/")) {
                    $full = Join-Path $Root $candidate
                }
                else {
                    $combined = Join-Path $directory $candidate
                    $full = [System.IO.Path]::GetFullPath($combined)
                }

                if ($full.StartsWith($Root, [System.StringComparison]::OrdinalIgnoreCase)) {
                    $normalizedPath = ($full.Substring($Root.Length + 1) -replace '\\', '/')
                }
            }
        }

        $links += [PSCustomObject]@{
            Raw        = $targetRaw
            Path       = $normalizedPath
            Anchor     = $anchor
            IsExternal = $isExternal -or ($null -eq $normalizedPath -and $targetRaw.StartsWith('#'))
        }
    }

    return $links
}

function Get-InferredType {
    param([string]$RelativePath, [hashtable]$FrontMatter)

    if ($FrontMatter.ContainsKey("type")) { return $FrontMatter["type"] }

    if ($RelativePath -match '^docs/getting-started/') { return "GUIDE" }
    if ($RelativePath -match '^docs/guides/') { return "GUIDE" }
    if ($RelativePath -match '^docs/reference/') { return "REF" }
    if ($RelativePath -match '^docs/architecture/') { return "ARCH" }
    if ($RelativePath -match '^docs/development/') { return "DEV" }
    if ($RelativePath -match '^docs/troubleshooting/') { return "SUPPORT" }
    if ($RelativePath -match '^documentation/getting-started/') { return "GUIDE" }
    if ($RelativePath -match '^documentation/guides/') { return "GUIDE" }
    if ($RelativePath -match '^documentation/reference/') { return "REF" }
    if ($RelativePath -match '^documentation/architecture/') { return "ARCH" }
    if ($RelativePath -match '^documentation/support/') { return "SUPPORT" }
    if ($RelativePath -match '^documentation/development/') { return "DEV" }
    return $null
}

function Get-InferredDomain {
    param([string]$RelativePath, [hashtable]$FrontMatter)

    if ($FrontMatter.ContainsKey("domain")) { return $FrontMatter["domain"] }

    $domains = @("core", "data", "web", "ai", "flow", "messaging", "storage", "media", "orchestration", "scheduling")
    foreach ($domain in $domains) {
        if ($RelativePath -match "/$domain/") {
            return $domain
        }
    }

    return $null
}

function Get-TocPathMap {
    param([string]$Root)

    $tocFile = Join-Path $Root "docs/toc.yml"
    $paths = @{}

    if (-not (Test-Path $tocFile)) {
        return $paths
    }

    $lines = Get-Content -Path $tocFile
    foreach ($line in $lines) {
        if ($line -match 'href:\s*(?<href>[^\s]+)') {
            $href = $Matches.href.Trim()
            $href = $href.Trim('"')
            if ($href.StartsWith("/")) {
                $candidate = $href.TrimStart('/')
            }
            else {
                $candidate = "docs/" + $href.TrimStart('.')
                $candidate = $candidate.TrimStart('/')
            }

            $paths[($candidate -replace '\\', '/').Trim()] = $true
        }
    }

    return $paths
}

$files = @()
foreach ($root in $Roots) {
    $absoluteRoot = Join-Path $repoRoot $root
    if (-not (Test-Path $absoluteRoot)) { continue }
    $files += Get-ChildItem -Path $absoluteRoot -Recurse -Filter *.md | Where-Object { -not $_.PSIsContainer }
}

$tocPaths = Get-TocPathMap -Root $repoRoot

$entries = @()

foreach ($file in $files) {
    $relativePath = ($file.FullName.Substring($repoRoot.Length + 1) -replace '\\', '/')
    $content = Get-Content -Path $file.FullName -Raw
    $frontMatter = Get-FrontMatter -Content $content
    $headings = Get-Headings -Content $content
    $title = Get-Title -Headings $headings
    if (-not $title) {
        $title = $file.BaseName
    }

    $links = Get-OutboundLinks -Content $content -FilePath $file.FullName -Root $repoRoot

    $entries += [PSCustomObject]@{
        Path          = $relativePath
        Title         = $title
        FrontMatter   = $frontMatter
        Type          = Get-InferredType -RelativePath $relativePath -FrontMatter $frontMatter
        Domain        = Get-InferredDomain -RelativePath $relativePath -FrontMatter $frontMatter
        Status        = $frontMatter["status"]
        LastUpdated   = $frontMatter["last_updated"]
        Headings      = $headings
        OutboundLinks = $links
        InNav         = $tocPaths.ContainsKey($relativePath)
    }
}

# Compute inbound link counts
$inboundCounts = @{}
foreach ($entry in $entries) {
    foreach ($link in $entry.OutboundLinks) {
        if ($null -eq $link.Path) { continue }
        if (-not $inboundCounts.ContainsKey($link.Path)) {
            $inboundCounts[$link.Path] = 0
        }
        $inboundCounts[$link.Path]++
    }
}

foreach ($entry in $entries) {
    $key = $entry.Path
    $inbound = if ($inboundCounts.ContainsKey($key)) { $inboundCounts[$key] } else { 0 }
    $entry | Add-Member -MemberType NoteProperty -Name InboundLinks -Value $inbound
    $outboundCount = ($entry.OutboundLinks | Where-Object { $null -ne $_.Path -or $_.IsExternal } | Measure-Object).Count
    $entry | Add-Member -MemberType NoteProperty -Name OutboundLinkCount -Value $outboundCount
}

# Ensure output directories exist
$markdownOutputDir = Split-Path -Path (Join-Path $repoRoot $OutputMarkdown) -Parent
if (-not (Test-Path $markdownOutputDir)) { New-Item -ItemType Directory -Path $markdownOutputDir | Out-Null }

$jsonOutputDir = Split-Path -Path (Join-Path $repoRoot $OutputJson) -Parent
if (-not (Test-Path $jsonOutputDir)) { New-Item -ItemType Directory -Path $jsonOutputDir | Out-Null }

# Write JSON
$entries | ConvertTo-Json -Depth 6 | Set-Content -Path (Join-Path $repoRoot $OutputJson)

# Prepare Markdown table
$tableHeader = "| path | title | type | domain | status | last_updated | in_nav? | inbound_links | outbound_links |"
$tableSeparator = "| --- | --- | --- | --- | --- | --- | --- | --- | --- |"
$tableRows = @()

function ConvertTo-TableValue {
    param([string]$Value)
    if ($null -eq $Value -or $Value -eq "") { return "" }
    return $Value.Replace("|", "\\|").Replace("\n", "<br />")
}

foreach ($entry in $entries | Sort-Object Path) {
    $typeValue = if ($entry.Type) { $entry.Type } else { "" }
    $domainValue = if ($entry.Domain) { $entry.Domain } else { "" }
    $statusValue = if ($entry.Status) { $entry.Status } else { "" }
    $lastUpdatedValue = if ($entry.LastUpdated) { $entry.LastUpdated } else { "" }
    $inNavValue = if ($entry.InNav) { "Yes" } else { "No" }

    $row = "| {0} | {1} | {2} | {3} | {4} | {5} | {6} | {7} | {8} |" -f `
    (ConvertTo-TableValue $entry.Path), `
    (ConvertTo-TableValue $entry.Title), `
    (ConvertTo-TableValue $typeValue), `
    (ConvertTo-TableValue $domainValue), `
    (ConvertTo-TableValue $statusValue), `
    (ConvertTo-TableValue $lastUpdatedValue), `
        $inNavValue, `
        $entry.InboundLinks, `
        $entry.OutboundLinkCount
    $tableRows += $row
}

$inventoryContent = @()
$inventoryContent += "---"
$inventoryContent += "type: DEV"
$inventoryContent += "domain: core"
$inventoryContent += "audience: [developers, architects, ai-agents]"
$inventoryContent += "status: current"
$inventoryContent += "last_updated: $today"
$inventoryContent += "framework_version: $frameworkVersion"
$inventoryContent += "validation:"
$inventoryContent += "  date_last_tested: $today"
$inventoryContent += "  notes: Generated by scripts/docs-inventory.ps1"
$inventoryContent += "---"
$inventoryContent += ""
$inventoryContent += "# Documentation inventory"
$inventoryContent += ""
$inventoryContent += "This file is generated by `scripts/docs-inventory.ps1` and captures authored markdown assets under `/docs` and `/documentation`."
$inventoryContent += ""
$inventoryContent += $tableHeader
$inventoryContent += $tableSeparator
$inventoryContent += $tableRows

$inventoryContentString = $inventoryContent -join [Environment]::NewLine
Set-Content -Path (Join-Path $repoRoot $OutputMarkdown) -Value $inventoryContentString

Write-Host "Inventory written to $OutputMarkdown and $OutputJson"
