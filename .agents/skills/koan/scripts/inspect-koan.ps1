[CmdletBinding()]
param(
    [Parameter(Position = 0)]
    [string]$Path = ".",

    [ValidateSet("Text", "Json")]
    [string]$Format = "Text"
)

$ErrorActionPreference = "Stop"

$resolved = Resolve-Path -LiteralPath $Path
$root = $resolved.Path
if (-not (Test-Path -LiteralPath $root -PathType Container)) {
    throw "Path must resolve to a directory: $Path"
}

function Get-RelativePath([string]$ItemPath) {
    [IO.Path]::GetRelativePath($root, $ItemPath).Replace("\", "/")
}

function Test-IsBuildOutput([string]$ItemPath) {
    (Get-RelativePath $ItemPath) -match "(^|/)(bin|obj)(/|$)"
}

$projects = foreach ($projectFile in Get-ChildItem -LiteralPath $root -Recurse -File -Filter "*.csproj" |
    Where-Object { -not (Test-IsBuildOutput $_.FullName) } |
    Sort-Object FullName) {
    try {
        [xml]$projectXml = Get-Content -LiteralPath $projectFile.FullName -Raw

        $targetFrameworks = @($projectXml.SelectNodes("//*[local-name()='TargetFramework' or local-name()='TargetFrameworks']") |
            ForEach-Object { $_.InnerText -split ";" } |
            Where-Object { $_ } |
            Sort-Object -Unique)

        $koanReferences = @($projectXml.SelectNodes("//*[local-name()='PackageReference']") |
            ForEach-Object {
                $include = $_.GetAttribute("Include")
                if (-not $include) { $include = $_.GetAttribute("Update") }
                if ($include -like "Sylin.Koan.*" -or $include -like "Koan.*") { $include }
            } |
            Where-Object { $_ } |
            Sort-Object -Unique)

        [pscustomobject]@{
            path = Get-RelativePath $projectFile.FullName
            targetFrameworks = $targetFrameworks
            koanReferences = $koanReferences
            parseError = $null
        }
    }
    catch {
        [pscustomobject]@{
            path = Get-RelativePath $projectFile.FullName
            targetFrameworks = @()
            koanReferences = @()
            parseError = $_.Exception.Message
        }
    }
}

$sourceFiles = @(Get-ChildItem -LiteralPath $root -Recurse -File -Filter "*.cs" |
    Where-Object { -not (Test-IsBuildOutput $_.FullName) } |
    Sort-Object FullName)

$signalPatterns = [ordered]@{
    addKoan = "\bAddKoan\s*\("
    entity = "\bEntity\s*<"
    entityController = "\bEntityController\s*<"
    dataRouting = "\b(DataAdapter|EntityContext\.(Adapter|Source|Partition))\b"
    tenancy = "\b(Tenant\.(Use|WithTenant)|TenantContext)\b"
    jobs = "\b(Job|Jobs|JobContext)\b"
    communication = "\b(Communication|Occurrence|Snapshot|Transport)\b"
    cache = "\b(Cache|CacheBehavior)\b"
    storageOrMedia = "\b(Storage|Media|Asset|Recipe)\b"
    ai = "\b(Client\.(Chat|Embed)|Prompt|Ai|AI)\b"
    vector = "\b(Embedding|Vector|SemanticSearch)\b"
    mcp = "\b(Mcp|MCP|McpEntity|McpTool)\b"
    canon = "\b(Canon|Canonical)\b"
}

$signals = [ordered]@{}
foreach ($entry in $signalPatterns.GetEnumerator()) {
    $matchingFiles = foreach ($sourceFile in $sourceFiles) {
        if (Select-String -LiteralPath $sourceFile.FullName -Pattern $entry.Value -Quiet) {
            Get-RelativePath $sourceFile.FullName
        }
    }

    $signals[$entry.Key] = [pscustomobject]@{
        fileCount = @($matchingFiles).Count
        files = @($matchingFiles | Select-Object -First 20)
        truncated = @($matchingFiles).Count -gt 20
    }
}

$configurationFiles = @(Get-ChildItem -LiteralPath $root -Recurse -File |
    Where-Object {
        -not (Test-IsBuildOutput $_.FullName) -and
        ($_.Name -like "appsettings*.json" -or $_.Name -in @(
            "koan.lock.json",
            "compose.yaml",
            "compose.yml",
            "docker-compose.yaml",
            "docker-compose.yml"
        ))
    } |
    Sort-Object FullName |
    ForEach-Object { Get-RelativePath $_.FullName })

$result = [pscustomobject]@{
    schemaVersion = "1"
    root = $root
    projects = @($projects)
    evidence = [pscustomobject]@{
        configurationFileNames = $configurationFiles
        koanLockPresent = $configurationFiles -contains "koan.lock.json"
    }
    codeSignals = [pscustomobject]$signals
    notes = @(
        "Configuration contents and secret values were not read.",
        "Code signals are orientation hints; verify behavior and provider selection from current application evidence."
    )
}

if ($Format -eq "Json") {
    $result | ConvertTo-Json -Depth 8
    exit 0
}

Write-Output "Koan application snapshot"
Write-Output "Root: $root"
Write-Output "Projects: $($result.projects.Count)"
foreach ($project in $result.projects) {
    $frameworks = if ($project.targetFrameworks.Count) { $project.targetFrameworks -join ", " } else { "unknown" }
    Write-Output "- $($project.path) [$frameworks]"
    foreach ($reference in $project.koanReferences) {
        Write-Output "    $reference"
    }
}
Write-Output "Koan lock present: $($result.evidence.koanLockPresent)"
Write-Output "Configuration files (names only): $($result.evidence.configurationFileNames.Count)"
Write-Output "Code signals:"
foreach ($property in $result.codeSignals.psobject.Properties) {
    Write-Output "- $($property.Name): $($property.Value.fileCount) file(s)"
}
