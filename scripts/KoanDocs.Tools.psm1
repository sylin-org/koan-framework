Set-StrictMode -Version Latest

function Get-KoanFrameworkVersion {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string]$RepositoryRoot
    )

    $versionFile = Join-Path $RepositoryRoot "version.json"
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

function Get-KoanDocFrontMatter {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string]$Content
    )

    $result = [ordered]@{}

    if ([string]::IsNullOrWhiteSpace($Content)) {
        return $result
    }

    if (-not $Content.StartsWith("---`n") -and -not $Content.StartsWith("---`r`n")) {
        return $result
    }

    $lines = [regex]::Split($Content, "\r?\n")
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

function Get-KoanDocHeadings {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string]$Content
    )

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

function Get-KoanDocTitle {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [object[]]$Headings
    )

    foreach ($heading in $Headings) {
        if ($heading.Level -eq 1) {
            return $heading.Text
        }
    }
    return $null
}

function Get-KoanRelativePath {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string]$FullPath,
        [Parameter(Mandatory)]
        [string]$RepositoryRoot
    )

    $normalizedRoot = [System.IO.Path]::GetFullPath($RepositoryRoot)
    $normalizedFull = [System.IO.Path]::GetFullPath($FullPath)

    if ($normalizedFull.StartsWith($normalizedRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
        $relative = $normalizedFull.Substring($normalizedRoot.Length).TrimStart('\', '/')
        return ($relative -replace '\\', '/')
    }

    return $null
}

function Get-KoanDocLinks {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string]$Content,
        [Parameter(Mandatory)]
        [string]$FilePath,
        [Parameter(Mandatory)]
        [string]$RepositoryRoot
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
        if ($pathPart -match '^[a-zA-Z][a-zA-Z0-9+\-.]*://') {
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
                $candidateFull = $pathPart
                if ($pathPart.StartsWith("/")) {
                    $candidateFull = Join-Path $RepositoryRoot $pathPart.TrimStart('/')
                }
                else {
                    $candidateFull = [System.IO.Path]::GetFullPath((Join-Path $directory $pathPart))
                }

                $relativePath = Get-KoanRelativePath -FullPath $candidateFull -RepositoryRoot $RepositoryRoot
                if ($null -ne $relativePath) {
                    $normalizedPath = $relativePath
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

function Get-KoanDocTypeHint {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string]$RelativePath,
        [Parameter(Mandatory)]
        [System.Collections.IDictionary]$FrontMatter
    )

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
    if ($RelativePath -match '^documentation/development/') { return "DEV" }
    if ($RelativePath -match '^documentation/support/') { return "SUPPORT" }
    return $null
}

function Get-KoanDocDomainHint {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string]$RelativePath,
        [Parameter(Mandatory)]
        [System.Collections.IDictionary]$FrontMatter
    )

    if ($FrontMatter.ContainsKey("domain")) { return $FrontMatter["domain"] }

    $domains = @("core", "data", "web", "ai", "flow", "messaging", "storage", "media", "orchestration", "scheduling")
    foreach ($domain in $domains) {
        if ($RelativePath -match "/$domain/") {
            return $domain
        }
    }

    return $null
}

function Get-KoanTocPathMap {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string]$RepositoryRoot
    )

    $tocFile = Join-Path $RepositoryRoot "docs/toc.yml"
    $paths = @{}

    if (-not (Test-Path $tocFile)) {
        return $paths
    }

    $lines = Get-Content -Path $tocFile
    foreach ($line in $lines) {
        if ($line -match 'href:\s*(?<href>[^\s]+)') {
            $href = $Matches.href.Trim().Trim('"')
            if ($href.StartsWith("/")) {
                $candidate = $href.TrimStart('/')
            }
            else {
                $candidate = "docs/" + $href.TrimStart('.')
                $candidate = $candidate.TrimStart('/')
            }

            $normalized = ($candidate -replace '\\', '/').Trim()
            if (-not [string]::IsNullOrWhiteSpace($normalized)) {
                $paths[$normalized] = $true
            }
        }
    }

    return $paths
}

function ConvertTo-KoanTableValue {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [AllowNull()]
        [AllowEmptyString()]
        [string]$Value
    )

    if ($null -eq $Value -or $Value -eq "") { return "" }
    return $Value.Replace("|", "\\|").Replace("`n", "<br />")
}

function Get-KoanAnchorSlug {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string]$HeadingText
    )

    $normalized = $HeadingText.ToLowerInvariant()
    $normalized = [regex]::Replace($normalized, '[^a-z0-9\s-]', '')
    $normalized = $normalized.Trim()
    $normalized = [regex]::Replace($normalized, '\s+', '-')
    $normalized = [regex]::Replace($normalized, '-{2,}', '-')
    return $normalized
}

Export-ModuleMember -Function @(
    'Get-KoanFrameworkVersion',
    'Get-KoanDocFrontMatter',
    'Get-KoanDocHeadings',
    'Get-KoanDocTitle',
    'Get-KoanDocLinks',
    'Get-KoanDocTypeHint',
    'Get-KoanDocDomainHint',
    'Get-KoanTocPathMap',
    'Get-KoanRelativePath',
    'ConvertTo-KoanTableValue',
    'Get-KoanAnchorSlug'
)
