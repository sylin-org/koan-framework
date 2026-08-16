[CmdletBinding()]
param(
    [Parameter(Position = 0)]
    [string]$Path = ".",

    [ValidateSet("Text", "Json")]
    [string]$Format = "Text"
)

$ErrorActionPreference = "Stop"

$root = (Resolve-Path -LiteralPath $Path).Path
if (-not (Test-Path -LiteralPath $root -PathType Container)) {
    throw "Path must resolve to a directory: $Path"
}

function Get-RelativePath([string]$ItemPath) {
    [IO.Path]::GetRelativePath($root, $ItemPath).Replace("\", "/")
}

function Test-IsExcluded([string]$ItemPath) {
    $relative = Get-RelativePath $ItemPath
    if ($relative -match "(^|/)(\.git|bin|obj|node_modules|artifacts)(/|$)") { return $true }
    if ($relative -match '(^|/)(?:attic|shelved|tmp)(/|$)') { return $true }
    if ($relative -match '(^|/)\.(?:scratch[^/]*|vs|Koan)(/|$)') { return $true }
    if ($ItemPath -match '[\\/]\.agents[\\/]skills[\\/](?:koan|koan-explain|koan-upgrade)(?:[\\/]|$)') { return $true }
    if ($relative -match '^\.agents/skills/(?:koan|koan-explain|koan-upgrade)(?:/|$)') { return $true }
    return $false
}

$rules = @(
    [pscustomobject]@{
        id = "legacy-package-id"
        category = "package-identity"
        confidence = "high"
        pattern = '<Package(?:Reference|Version)[^>]+(?:Include|Update)\s*=\s*["'']Koan\.'
        guidance = "Resolve the exact current dependency ID; namespaces may legitimately remain Koan.*."
    },
    [pscustomobject]@{
        id = "data-query-options"
        category = "removed-api"
        confidence = "high"
        pattern = "\bDataQueryOptions\b"
        guidance = "Verify the current query contract and migrate members semantically."
    },
    [pscustomobject]@{
        id = "manual-mcp-bootstrap"
        category = "composition-review"
        confidence = "high-for-ordinary-apps"
        pattern = "\b(AddKoanMcp|MapKoanMcpEndpoints)\s*\("
        guidance = "Verify current MCP composition and preserve genuine custom extension seams."
    },
    [pscustomobject]@{
        id = "manual-web-bootstrap"
        category = "composition-review"
        confidence = "review"
        pattern = "\bAddKoanWeb\s*\("
        guidance = "Verify current Web composition; preserve real options and policy configuration."
    },
    [pscustomobject]@{
        id = "legacy-query-capability"
        category = "api-review"
        confidence = "review"
        pattern = "\bQueryCaps\b"
        guidance = "Check the current capability API; do not rewrite without exact evidence."
    },
    [pscustomobject]@{
        id = "legacy-payload-transformer"
        category = "api-review"
        confidence = "review"
        pattern = "\bIPayloadTransformer\b"
        guidance = "Locate the current public extension seam before replacing this integration token."
    },
    [pscustomobject]@{
        id = "legacy-mcp-contract"
        category = "api-review"
        confidence = "review"
        pattern = "\b(McpToolSchema|IMcpTool)\b"
        guidance = "Verify the current MCP tool and resource contract."
    },
    [pscustomobject]@{
        id = "legacy-vector-annotation"
        category = "api-review"
        confidence = "review"
        pattern = "\[\s*VectorField(?:Attribute)?\b"
        guidance = "Verify current embedding and vector conventions; provider or data migration is separate."
    }
)

$extensions = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
@(".csproj", ".props", ".targets", ".cs", ".fs", ".vb", ".md", ".json", ".yaml", ".yml") |
    ForEach-Object { $extensions.Add($_) | Out-Null }
$ripgrep = Get-Command "rg" -ErrorAction SilentlyContinue
$scanGlobArguments = @(
    "-g", "*.csproj", "-g", "*.props", "-g", "*.targets", "-g", "*.cs", "-g", "*.fs", "-g", "*.vb",
    "-g", "*.md", "-g", "*.json", "-g", "*.yaml", "-g", "*.yml",
    "-g", "!**/.git/**", "-g", "!**/bin/**", "-g", "!**/obj/**", "-g", "!**/node_modules/**", "-g", "!**/artifacts/**",
    "-g", "!**/.scratch*/**", "-g", "!**/.vs/**", "-g", "!**/.Koan/**",
    "-g", "!**/attic/**", "-g", "!**/shelved/**", "-g", "!**/tmp/**",
    "-g", "!.agents/skills/koan/**", "-g", "!.agents/skills/koan-explain/**", "-g", "!.agents/skills/koan-upgrade/**"
)
$rootLeaf = Split-Path -Leaf $root
$rootParentLeaf = Split-Path -Leaf (Split-Path -Parent $root)
if ($rootLeaf -eq '.agents') {
    $scanGlobArguments += @("-g", "!skills/koan/**", "-g", "!skills/koan-explain/**", "-g", "!skills/koan-upgrade/**")
}
elseif ($rootLeaf -eq 'skills' -and $rootParentLeaf -eq '.agents') {
    $scanGlobArguments += @("-g", "!koan/**", "-g", "!koan-explain/**", "-g", "!koan-upgrade/**")
}
if ($ripgrep) {
    Push-Location $root
    try {
        $fileNames = @(& $ripgrep.Source --files --hidden --no-ignore @scanGlobArguments ".")
        if ($LASTEXITCODE -notin @(0, 1)) { throw "rg file discovery failed with exit code $LASTEXITCODE" }
        $files = @($fileNames | ForEach-Object {
            $fullPath = [IO.Path]::GetFullPath((Join-Path $root $_))
            if (Test-Path -LiteralPath $fullPath -PathType Leaf) { [IO.FileInfo]::new($fullPath) }
        } | Sort-Object FullName)

        $assetNames = @(& $ripgrep.Source --files --hidden --no-ignore -g "project.assets.json" -g "!**/.git/**" -g "!**/bin/**" -g "!**/node_modules/**" -g "!**/artifacts/**" -g "!**/.scratch*/**" -g "!**/.vs/**" -g "!**/.Koan/**" -g "!**/attic/**" -g "!**/shelved/**" -g "!**/tmp/**" ".")
        if ($LASTEXITCODE -notin @(0, 1)) { throw "rg asset discovery failed with exit code $LASTEXITCODE" }
        $assetFiles = @($assetNames | ForEach-Object {
            $fullPath = [IO.Path]::GetFullPath((Join-Path $root $_))
            if (Test-Path -LiteralPath $fullPath -PathType Leaf) { [IO.FileInfo]::new($fullPath) }
        } | Sort-Object FullName)
    }
    finally {
        Pop-Location
    }
}
else {
    $skippedDirectoryNames = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    @(".git", "bin", "node_modules", "artifacts", ".vs", ".Koan", "attic", "shelved", "tmp") | ForEach-Object { $skippedDirectoryNames.Add($_) | Out-Null }
    $discoveredFiles = [Collections.Generic.List[IO.FileInfo]]::new()
    $discoveredAssets = [Collections.Generic.List[IO.FileInfo]]::new()
    $pendingDirectories = [Collections.Generic.Stack[IO.DirectoryInfo]]::new()
    $pendingDirectories.Push([IO.DirectoryInfo]::new($root))
    while ($pendingDirectories.Count -gt 0) {
        $directory = $pendingDirectories.Pop()
        try {
            foreach ($file in $directory.GetFiles()) {
                # Resolved assets are authoritative for installed versions, so discover them wherever
                # they sit — not only under obj. This keeps the fallback walker's results identical to
                # the ripgrep path; otherwise the inventory would silently depend on ripgrep presence.
                if ($file.Name.Equals("project.assets.json", [StringComparison]::OrdinalIgnoreCase)) {
                    $discoveredAssets.Add($file) | Out-Null
                }
                if ($extensions.Contains($file.Extension) -and -not (Test-IsExcluded $file.FullName)) {
                    $discoveredFiles.Add($file) | Out-Null
                }
            }
            foreach ($child in $directory.GetDirectories()) {
                if (($child.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { continue }
                if ($skippedDirectoryNames.Contains($child.Name)) { continue }
                if ($child.Name -match '^\.scratch') { continue }
                if ($child.Name.Equals("obj", [StringComparison]::OrdinalIgnoreCase)) {
                    $assetsPath = Join-Path $child.FullName "project.assets.json"
                    if (Test-Path -LiteralPath $assetsPath -PathType Leaf) {
                        $discoveredAssets.Add([IO.FileInfo]::new($assetsPath)) | Out-Null
                    }
                    continue
                }
                $pendingDirectories.Push($child)
            }
        }
        catch [UnauthorizedAccessException] {
            continue
        }
    }
    $files = @($discoveredFiles | Sort-Object FullName)
    $assetFiles = @($discoveredAssets | Sort-Object FullName)
}

$combinedPatternParts = @()
for ($ruleIndex = 0; $ruleIndex -lt $rules.Count; $ruleIndex++) {
    $combinedPatternParts += "(?<R$ruleIndex>$($rules[$ruleIndex].pattern))"
}
$combinedRegex = [regex]::new(
    ($combinedPatternParts -join '|'),
    [Text.RegularExpressions.RegexOptions]::CultureInvariant
)

$findings = [Collections.Generic.List[object]]::new()
if ($ripgrep) {
    Push-Location $root
    try {
        foreach ($rule in $rules) {
            $events = @(& $ripgrep.Source --json --line-number --hidden --no-ignore @scanGlobArguments --regexp $rule.pattern ".")
            if ($LASTEXITCODE -notin @(0, 1)) { throw "rg rule '$($rule.id)' failed with exit code $LASTEXITCODE" }
            foreach ($eventLine in $events) {
                $event = $eventLine | ConvertFrom-Json
                if ($event.type -ne "match") { continue }
                $eventPath = [string]$event.data.path.text
                $fullPath = [IO.Path]::GetFullPath((Join-Path $root $eventPath))
                $findings.Add([pscustomobject]@{
                    rule = $rule.id
                    category = $rule.category
                    confidence = $rule.confidence
                    path = Get-RelativePath $fullPath
                    line = [int]$event.data.line_number
                    guidance = $rule.guidance
                }) | Out-Null
            }
        }
    }
    finally {
        Pop-Location
    }
}
else {
    foreach ($file in $files) {
        $content = Get-Content -LiteralPath $file.FullName -Raw
        if ([string]::IsNullOrEmpty($content)) { continue }
        $lineNumber = 1
        $cursor = 0
        $lastFindingKey = ""
        foreach ($match in $combinedRegex.Matches($content)) {
            if ($match.Index -gt $cursor) {
                $lineNumber += [regex]::Matches($content.Substring($cursor, $match.Index - $cursor), "`n").Count
                $cursor = $match.Index
            }
            for ($ruleIndex = 0; $ruleIndex -lt $rules.Count; $ruleIndex++) {
                if (-not $match.Groups["R$ruleIndex"].Success) { continue }
                $rule = $rules[$ruleIndex]
                $findingKey = "$lineNumber|$($rule.id)"
                if ($findingKey -ne $lastFindingKey) {
                    $findings.Add([pscustomobject]@{
                        rule = $rule.id
                        category = $rule.category
                        confidence = $rule.confidence
                        path = Get-RelativePath $file.FullName
                        line = $lineNumber
                        guidance = $rule.guidance
                    }) | Out-Null
                    $lastFindingKey = $findingKey
                }
                break
            }
        }
    }
}

$packageEntries = [Collections.Generic.List[object]]::new()
foreach ($file in $files | Where-Object { $_.Extension -in @(".csproj", ".props", ".targets") }) {
    try { [xml]$xml = Get-Content -LiteralPath $file.FullName -Raw } catch { continue }
    foreach ($node in $xml.SelectNodes("//*[local-name()='PackageReference' or local-name()='PackageVersion']")) {
        $id = $node.GetAttribute("Include")
        if (-not $id) { $id = $node.GetAttribute("Update") }
        if ($id -ne "Sylin.Koan" -and $id -notlike "Sylin.Koan.*" -and $id -ne "Koan" -and $id -notlike "Koan.*") { continue }
        $version = $node.GetAttribute("Version")
        if (-not $version) {
            $versionNode = $node.SelectSingleNode("*[local-name()='Version']")
            if ($versionNode) { $version = $versionNode.InnerText }
        }
        $packageEntries.Add([pscustomobject]@{
            id = $id
            version = $version
            path = Get-RelativePath $file.FullName
            evidence = "declared-project"
        }) | Out-Null
    }
}

# Existing assets provide exact resolved versions without restore. Generic content scanning still
# excludes obj because generated source/noise is not a migration fingerprint surface.
foreach ($assetFile in $assetFiles) {
    try { $assets = Get-Content -LiteralPath $assetFile.FullName -Raw | ConvertFrom-Json -ErrorAction Stop } catch { continue }
    foreach ($library in @($assets.libraries.PSObject.Properties)) {
        if ($library.Name -notmatch '^(?<id>(?:Sylin\.)?Koan(?:\.[^/]+)?)/(?<version>.+)$') { continue }
        $packageEntries.Add([pscustomobject]@{
            id = $Matches.id
            version = $Matches.version
            path = Get-RelativePath $assetFile.FullName
            evidence = "resolved-assets"
        }) | Out-Null
    }
}

$result = [pscustomobject]@{
    schemaVersion = "2.0"
    root = $root
    packages = @($packageEntries | Sort-Object id, path)
    findings = @($findings | Sort-Object path, line, rule)
    cautions = @(
        "Findings are review candidates; prove each current replacement against the chosen target.",
        "Only resolved-assets rows establish exact installed versions; declared-project rows may inherit a central or floating version.",
        "The inventory is read-only; it did not change files, dependencies, build outputs, configuration, data, or external state."
    )
}

if ($Format -eq "Json") {
    $result | ConvertTo-Json -Depth 7
    return
}

Write-Output "Koan upgrade inventory"
Write-Output "Root: $root"
Write-Output "Package entries: $($result.packages.Count)"
foreach ($package in $result.packages) {
    $version = if ($package.version) { $package.version } else { "version not declared in this file" }
    Write-Output "- $($package.id) ($version; $($package.evidence)) - $($package.path)"
}
Write-Output "Review findings: $($result.findings.Count)"
foreach ($finding in $result.findings) {
    Write-Output "- [$($finding.confidence)] $($finding.rule) at $($finding.path):$($finding.line)"
    Write-Output "    $($finding.guidance)"
}
foreach ($caution in $result.cautions) { Write-Output "Note: $caution" }
