<#
.SYNOPSIS
  Orient in a Koan application from the evidence it already produces.

.DESCRIPTION
  A Koan application describes its own composition. The build writes koan.lock.json beside the project
  (referenced modules and direct application references), and a non-production boot writes the richer
  resolved twin to obj/koan.lock.resolved.json (elections, entity traits, capabilities, configuration
  KEYS). This script reads those first, because they are what the framework decided -- not what a
  pattern in a source file suggests.

  Code signals remain, demoted to what they are: hints. They matter when no lockfile exists yet
  (nothing has been built), and they are never a reason to contradict a lockfile that does.

  Configuration VALUES are never read. The resolved twin carries configuration keys only.

.EXAMPLE
  pwsh inspect-koan.ps1 . -Format Json
#>
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

function Get-JsonProperty($Object, [string]$Name) {
    if ($null -eq $Object) { return $null }
    if ($Object.PSObject.Properties.Name -notcontains $Name) { return $null }
    $Object.$Name
}

# --- composition: what the framework actually resolved -----------------------------------------
function Read-Lockfile([string]$FilePath, [string]$Kind) {
    try {
        $json = Get-Content -Raw -LiteralPath $FilePath | ConvertFrom-Json
    }
    catch {
        return [pscustomobject]@{
            path = Get-RelativePath $FilePath
            kind = $Kind
            parseError = $_.Exception.Message
        }
    }

    $app = Get-JsonProperty $json 'app'
    $elections = Get-JsonProperty $json 'elections'
    $capabilities = Get-JsonProperty $json 'capabilities'
    $entities = Get-JsonProperty $json 'entities'
    $configKeys = Get-JsonProperty $json 'configKeys'
    $directReferences = Get-JsonProperty $json 'directReferences'

    [pscustomobject]@{
        path = Get-RelativePath $FilePath
        kind = $Kind
        schema = Get-JsonProperty $json 'schema'
        app = if ($null -eq $app) { $null } else {
            [pscustomobject]@{
                name = Get-JsonProperty $app 'name'
                koan = Get-JsonProperty $app 'koan'
                tfm = Get-JsonProperty $app 'tfm'
            }
        }
        modules = @(@(Get-JsonProperty $json 'modules') | Where-Object { $_ } |
            ForEach-Object { "$(Get-JsonProperty $_ 'id') $(Get-JsonProperty $_ 'version')".Trim() })
        # Direct references are application intent; transitive modules are consequence.
        directReferences = @(@($directReferences) | Where-Object { $_ } |
            ForEach-Object { "$(Get-JsonProperty $_ 'kind'): $(Get-JsonProperty $_ 'id')" })
        # Present only in the resolved twin -- this is why a provider won.
        elections = if ($null -eq $elections) { @() } else {
            @($elections.PSObject.Properties | ForEach-Object {
                $via = Get-JsonProperty $_.Value 'via'
                $adapter = Get-JsonProperty $_.Value 'adapter'
                "$($_.Name) -> $adapter (via $via)"
            })
        }
        entities = if ($null -eq $entities) { @() } else {
            @(@($entities) | ForEach-Object {
                $traits = @(Get-JsonProperty $_ 'traits')
                $suffix = if ($traits.Count) { " [$($traits -join ', ')]" } else { "" }
                "$(Get-JsonProperty $_ 'type')$suffix"
            })
        }
        capabilities = if ($null -eq $capabilities) { @() } else {
            @($capabilities.PSObject.Properties | ForEach-Object { "$($_.Name): $(@($_.Value) -join ', ')" })
        }
        configKeyCount = @($configKeys).Count
        parseError = $null
    }
}

$lockfiles = @()
foreach ($file in @(Get-ChildItem -LiteralPath $root -Recurse -File -Filter "koan.lock.json" -ErrorAction SilentlyContinue |
    Where-Object { -not (Test-IsBuildOutput $_.FullName) } | Sort-Object FullName)) {
    $lockfiles += Read-Lockfile $file.FullName "build"
}
# The twin lives under obj/ by design (a diagnostic artifact, not a checked-in one), so it is the one
# build-output path worth reading.
foreach ($file in @(Get-ChildItem -LiteralPath $root -Recurse -File -Filter "koan.lock.resolved.json" -ErrorAction SilentlyContinue |
    Sort-Object FullName)) {
    $lockfiles += Read-Lockfile $file.FullName "resolved"
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

$hasComposition = @($lockfiles | Where-Object { -not $_.parseError }).Count -gt 0

# --- composed packages: the subtractable set ----------------------------------------------------
# The one list to compare against the capability shelf when asking "what could I add?". Two sources
# are unioned deliberately: a project file states what the application asked for, and the lockfile
# states what those references actually composed -- a bundle such as Sylin.Koan.App brings in Web and
# more, so a project-file-only view would recommend adding pieces the application already has.
#
# Namespaces are Koan.*; packages are Sylin.Koan.*. Normalizing to the package form is what makes the
# comparison against the shelf mechanical instead of eyeballed.
function ConvertTo-PackageId([string]$Identifier) {
    if ([string]::IsNullOrWhiteSpace($Identifier)) { return $null }
    if ($Identifier.StartsWith("Sylin.Koan.", [StringComparison]::OrdinalIgnoreCase)) { return $Identifier }
    if ($Identifier.StartsWith("Koan.", [StringComparison]::OrdinalIgnoreCase)) { return "Sylin.$Identifier" }
    return $null
}

$composedRaw = @()
foreach ($project in $projects) { $composedRaw += @($project.koanReferences) }
foreach ($lock in $lockfiles) {
    if ($lock.parseError) { continue }
    # modules[] carries ids; directReferences[] was flattened to "kind: id" for display.
    $composedRaw += @($lock.modules | ForEach-Object { ($_ -split '\s+')[0] })
    $composedRaw += @($lock.directReferences | ForEach-Object { ($_ -split ':\s*', 2)[-1] })
}

$composedPackages = @($composedRaw |
    ForEach-Object { ConvertTo-PackageId $_ } |
    Where-Object { $_ } |
    Sort-Object -Unique)

$result = [pscustomobject]@{
    schemaVersion = "2"
    root = $root
    composition = @($lockfiles)
    composedPackages = $composedPackages
    projects = @($projects)
    evidence = [pscustomobject]@{
        configurationFileNames = $configurationFiles
        koanLockPresent = $hasComposition
    }
    codeSignals = [pscustomobject]$signals
    notes = @(
        "Composition is read from koan.lock.json and, where present, obj/koan.lock.resolved.json.",
        "Configuration contents and secret values were not read; the resolved twin carries keys only.",
        $(if ($hasComposition) {
            "Code signals are hints only. Where they disagree with the lockfile, the lockfile is what the framework resolved."
        } else {
            "No lockfile found -- build the application to produce one. Code signals below are orientation hints, not evidence."
        })
    )
}

if ($Format -eq "Json") {
    $result | ConvertTo-Json -Depth 8
    exit 0
}

Write-Output "Koan application snapshot"
Write-Output "Root: $root"
Write-Output ""

if ($hasComposition) {
    Write-Output "Composition (framework evidence):"
    foreach ($lock in $result.composition) {
        if ($lock.parseError) {
            Write-Output "- $($lock.path) [$($lock.kind)] UNREADABLE: $($lock.parseError)"
            continue
        }
        $appName = if ($lock.app) { "$($lock.app.name) · Koan $($lock.app.koan) · $($lock.app.tfm)" } else { "unknown app" }
        Write-Output "- $($lock.path) [$($lock.kind)] $appName"
        Write-Output "    modules: $(@($lock.modules).Count)"
        foreach ($reference in $lock.directReferences) { Write-Output "    reference: $reference" }
        foreach ($election in $lock.elections) { Write-Output "    election: $election" }
        foreach ($entity in $lock.entities) { Write-Output "    entity: $entity" }
        foreach ($capability in $lock.capabilities) { Write-Output "    capability: $capability" }
        if ($lock.configKeyCount) { Write-Output "    config keys observed: $($lock.configKeyCount)" }
    }
}
else {
    Write-Output "Composition: no lockfile found. Build the application to produce koan.lock.json."
}

Write-Output ""
Write-Output "Composed packages ($($composedPackages.Count)) — subtract these from the capability shelf:"
foreach ($package in $composedPackages) { Write-Output "- $package" }

Write-Output ""
Write-Output "Projects: $($result.projects.Count)"
foreach ($project in $result.projects) {
    $frameworks = if ($project.targetFrameworks.Count) { $project.targetFrameworks -join ", " } else { "unknown" }
    Write-Output "- $($project.path) [$frameworks]"
    foreach ($reference in $project.koanReferences) {
        Write-Output "    $reference"
    }
}

Write-Output ""
Write-Output "Configuration files (names only): $($result.evidence.configurationFileNames.Count)"
Write-Output $(if ($hasComposition) { "Code signals (hints; the lockfile above is authoritative):" } else { "Code signals (no lockfile -- hints only):" })
foreach ($property in $result.codeSignals.psobject.Properties) {
    Write-Output "- $($property.Name): $($property.Value.fileCount) file(s)"
}
