[CmdletBinding()]
param(
    [string]$RepositoryRoot,
    [string]$OutputRoot
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if (-not $RepositoryRoot) {
    $RepositoryRoot = (& git rev-parse --show-toplevel).Trim()
}
$RepositoryRoot = (Resolve-Path -LiteralPath $RepositoryRoot).ProviderPath
if (-not $OutputRoot) {
    $OutputRoot = Join-Path $RepositoryRoot 'docs/initiatives/data-adapter-conformance/evidence/portfolio'
}
New-Item -ItemType Directory -Path $OutputRoot -Force | Out-Null

function Get-RelativePath([string]$Path) {
    [IO.Path]::GetRelativePath($RepositoryRoot, $Path).Replace('\\', '/')
}

function Get-FileHashValue([string]$Path) {
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { return $null }
    (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

function Read-Project([IO.FileInfo]$Project) {
    [xml]$xml = Get-Content -LiteralPath $Project.FullName -Raw
    $references = New-Object System.Collections.Generic.List[string]
    foreach ($node in @($xml.SelectNodes('//ProjectReference'))) {
        $include = [string]$node.Include
        if (-not $include) { continue }
        $resolved = [IO.Path]::GetFullPath((Join-Path $Project.DirectoryName $include))
        $references.Add((Get-RelativePath $resolved))
    }
    [pscustomobject]@{
        Xml = $xml
        References = @($references)
    }
}

$connectorRoot = Join-Path $RepositoryRoot 'src/Connectors/Data'
$testRoot = Join-Path $RepositoryRoot 'tests/Suites/Data'
$claimPath = Join-Path $RepositoryRoot 'product/claims.json'
$claims = if (Test-Path -LiteralPath $claimPath) {
    @((Get-Content -LiteralPath $claimPath -Raw | ConvertFrom-Json).claims)
} else { @() }

$solutionProjects = @()
$solutionOutput = & dotnet sln (Join-Path $RepositoryRoot 'Koan.sln') list 2>$null
if ($LASTEXITCODE -eq 0) {
    $solutionProjects = @($solutionOutput | Where-Object { $_ -match '\.csproj$' } | ForEach-Object {
            $_.Trim().Replace('\\', '/')
        })
}

$testProjects = @(Get-ChildItem -LiteralPath $testRoot -Filter '*.csproj' -File -Recurse | Sort-Object FullName)
$testReferenceIndex = @{}
foreach ($testProject in $testProjects) {
    $testMeta = Read-Project $testProject
    foreach ($reference in $testMeta.References) {
        if (-not $testReferenceIndex.ContainsKey($reference)) {
            $testReferenceIndex[$reference] = New-Object System.Collections.Generic.List[string]
        }
        $testReferenceIndex[$reference].Add((Get-RelativePath $testProject.FullName))
    }
}

$frameworkProjects = @(
    'Koan.Core',
    'Koan.Data.Abstractions',
    'Koan.Data.Core',
    'Koan.Data.Vector.Abstractions',
    'Koan.Data.Vector'
)
$familyPattern = '^Koan\.Data\.(Relational(?:\..+)?|SearchEngine|Core\.(Document|KeyValue))$'
$adapters = New-Object System.Collections.Generic.List[object]

$projects = @(Get-ChildItem -LiteralPath $connectorRoot -Filter '*.csproj' -File -Recurse | Sort-Object FullName)
foreach ($project in $projects) {
    $meta = Read-Project $project
    $projectPath = Get-RelativePath $project.FullName
    $projectName = $project.BaseName
    $packageId = 'Sylin.' + $projectName
    $sourceFiles = @(Get-ChildItem -LiteralPath $project.DirectoryName -Filter '*.cs' -File -Recurse | Sort-Object FullName)
    $sourceText = ($sourceFiles | ForEach-Object { Get-Content -LiteralPath $_.FullName -Raw }) -join "`n"
    $hasEntityFactory = $sourceText -match '\bIDataAdapterFactory\b'
    $hasVectorSurface = $sourceText -match '\bIVector(?:Adapter|Repository|Search|Capabilities)' -or
        @($meta.References | Where-Object { $_ -match 'Koan\.Data\.(Vector|SearchEngine)' }).Count -gt 0
    $plane = if ($hasEntityFactory -and $hasVectorSurface) { 'hybrid' }
        elseif ($hasEntityFactory) { 'entity-persistence' }
        elseif ($hasVectorSurface) { 'vector' }
        else { 'unclassified' }

    $provider = $projectName -replace '^Koan\.Data\.Vector\.Connector\.', '' -replace '^Koan\.Data\.Connector\.', ''
    $id = $provider.ToLowerInvariant()
    if ($id -eq 'mongo') { $id = 'mongodb' }
    if ($plane -eq 'vector' -and $id -eq 'inmemory') { $id = 'vector-inmemory' }

    $modules = @($sourceFiles | Where-Object {
            (Get-Content -LiteralPath $_.FullName -Raw) -match ':\s*KoanModule\b'
        } | ForEach-Object { Get-RelativePath $_.FullName })
    $factories = @($sourceFiles | Where-Object {
            (Get-Content -LiteralPath $_.FullName -Raw) -match '\bclass\s+\w*AdapterFactory\b'
        } | ForEach-Object { Get-RelativePath $_.FullName })
    $capabilitySources = @($sourceFiles | Where-Object {
            (Get-Content -LiteralPath $_.FullName -Raw) -match '\b(?:DataCaps|VectorCapabilities|VectorCapability)\b'
        } | ForEach-Object { Get-RelativePath $_.FullName })

    $families = @($meta.References | ForEach-Object {
            [IO.Path]::GetFileNameWithoutExtension($_)
        } | Where-Object { $_ -match $familyPattern } | Sort-Object -Unique)
    if ($sourceText -match '\b(?:Koan\.Data\.Core\.Document|DocumentStore\s*<)') {
        $families = @($families + 'Koan.Data.Core.Document' | Sort-Object -Unique)
    }
    if ($sourceText -match '\b(?:Koan\.Data\.Core\.KeyValue|KeyValueStore\s*<)') {
        $families = @($families + 'Koan.Data.Core.KeyValue' | Sort-Object -Unique)
    }
    $framework = @($meta.References | ForEach-Object {
            [IO.Path]::GetFileNameWithoutExtension($_)
        } | Where-Object { $frameworkProjects -contains $_ } | Sort-Object -Unique)
    $testConsumers = if ($testReferenceIndex.ContainsKey($projectPath)) {
        @($testReferenceIndex[$projectPath] | Sort-Object -Unique)
    } else { @() }
    $providerTestToken = if ($provider -eq 'Mongo') { 'Mongo' } else { $provider }
    $tests = if ($plane -eq 'entity-persistence') {
        @($testConsumers | Where-Object { $_ -match "Connector\.$([regex]::Escape($providerTestToken))[/\\]" })
    } else {
        @($testConsumers | Where-Object { $_ -match "VectorAdapterSurface\.$([regex]::Escape($providerTestToken))\.Tests[/\\]" })
    }
    $docs = @('README.md', 'TECHNICAL.md' | ForEach-Object {
            $candidate = Join-Path $project.DirectoryName $_
            if (Test-Path -LiteralPath $candidate) { Get-RelativePath $candidate }
        })
    $claimMatches = @($claims | Where-Object { @($_.packages) -contains $packageId } | ForEach-Object { $_.id })

    $adapters.Add([ordered]@{
            id = $id
            provider = $provider
            plane = $plane
            project = $projectPath
            package = $packageId
            inSolution = $solutionProjects -contains $projectPath
            projectHash = Get-FileHashValue $project.FullName
            modules = @($modules)
            factories = @($factories)
            familyProjects = @($families)
            frameworkProjects = @($framework)
            tests = @($tests)
            testConsumers = @($testConsumers)
            capabilitySources = @($capabilitySources)
            docs = @($docs)
            productClaims = @($claimMatches)
        })
}

$unclassified = @($adapters | Where-Object { $_.plane -eq 'unclassified' })
if ($unclassified.Count -gt 0) {
    throw "Unable to classify adapter project(s): $(@($unclassified.project) -join ', ')"
}
$notInSolution = @($adapters | Where-Object { -not $_.inSolution })
if ($notInSolution.Count -gt 0) {
    throw "Adapter project(s) missing from Koan.sln: $(@($notInSolution.project) -join ', ')"
}

$familyNames = @($adapters.familyProjects | Sort-Object -Unique)
$families = New-Object System.Collections.Generic.List[object]
foreach ($name in $familyNames) {
    $familyProject = Get-ChildItem -LiteralPath (Join-Path $RepositoryRoot 'src') -Filter ($name + '.csproj') -File -Recurse | Select-Object -First 1
    $familySourceRoot = if ($name -eq 'Koan.Data.Core.Document') { Join-Path $RepositoryRoot 'src/Koan.Data.Core/Document' }
        elseif ($name -eq 'Koan.Data.Core.KeyValue') { Join-Path $RepositoryRoot 'src/Koan.Data.Core/KeyValue' }
        elseif ($familyProject) { $familyProject.DirectoryName }
        else { $null }
    $familySources = if ($familySourceRoot -and (Test-Path -LiteralPath $familySourceRoot)) {
        @(Get-ChildItem -LiteralPath $familySourceRoot -Filter '*.cs' -File -Recurse | Sort-Object FullName)
    } else { @() }
    $familyFactories = @($familySources | Where-Object {
            (Get-Content -LiteralPath $_.FullName -Raw) -match '\bclass\s+\w*AdapterFactory\b'
        } | ForEach-Object { Get-RelativePath $_.FullName })
    $familyModules = @($familySources | Where-Object {
            (Get-Content -LiteralPath $_.FullName -Raw) -match ':\s*KoanModule\b'
        } | ForEach-Object { Get-RelativePath $_.FullName })
    $familyTests = if ($familyProject) {
        $familyProjectPath = Get-RelativePath $familyProject.FullName
        if ($testReferenceIndex.ContainsKey($familyProjectPath)) { @($testReferenceIndex[$familyProjectPath] | Sort-Object -Unique) } else { @() }
    } else { @() }
    $families.Add([ordered]@{
            id = $name.ToLowerInvariant().Replace('koan.data.', '').Replace('.', '-')
            name = $name
            project = if ($familyProject) { Get-RelativePath $familyProject.FullName } else { $null }
            sourceRoot = if ($familySourceRoot) { Get-RelativePath $familySourceRoot } else { $null }
            factories = @($familyFactories)
            modules = @($familyModules)
            tests = @($familyTests)
            consumers = @($adapters | Where-Object { @($_.familyProjects) -contains $name } | ForEach-Object { $_.id } | Sort-Object)
        })
}

$now = (Get-Date).ToUniversalTime().ToString('o')
$primerPath = Join-Path $RepositoryRoot 'docs/architecture/data-adapter-development-primer.md'
$head = (& git -C $RepositoryRoot rev-parse HEAD).Trim()
$statusLines = @(& git -C $RepositoryRoot status --short --untracked-files=all)
$roster = [ordered]@{
    schemaVersion = 1
    generatedAt = $now
    source = [ordered]@{
        baseCommit = $head
        dirty = $statusLines.Count -gt 0
        primer = 'docs/architecture/data-adapter-development-primer.md'
        primerSha256 = Get-FileHashValue $primerPath
        primerCatalogIds = @([regex]::Matches((Get-Content -LiteralPath $primerPath -Raw), '\*\*[A-HPV]-\d{2}\*\*')).Count
        sdk = (& dotnet --version).Trim()
        os = [Environment]::OSVersion.VersionString
        architecture = [Runtime.InteropServices.RuntimeInformation]::OSArchitecture.ToString().ToLowerInvariant()
        dockerCli = [bool](Get-Command docker -ErrorAction SilentlyContinue)
        dockerDaemon = $false
    }
    counts = [ordered]@{
        adapters = $adapters.Count
        entityPersistence = @($adapters | Where-Object plane -eq 'entity-persistence').Count
        vector = @($adapters | Where-Object plane -eq 'vector').Count
        hybrid = @($adapters | Where-Object plane -eq 'hybrid').Count
        families = $families.Count
    }
    adapters = @($adapters | Sort-Object plane, id)
    families = @($families | Sort-Object id)
    adjacency = [ordered]@{
        excluded = @(
            [ordered]@{ area = 'cache'; reason = 'separate Koan.Cache adapter contract' },
            [ordered]@{ area = 'ai'; reason = 'Koan.Data.AI is not a storage adapter' }
        )
    }
}

try {
    $dockerOutput = & docker info --format '{{json .ServerVersion}}' 2>$null
    if ($LASTEXITCODE -eq 0 -and $dockerOutput) { $roster.source.dockerDaemon = $true }
} catch { }

$jsonPath = Join-Path $OutputRoot 'roster.json'
$roster | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $jsonPath -Encoding utf8

$summary = New-Object System.Collections.Generic.List[string]
$summary.Add('---')
$summary.Add('type: REFERENCE')
$summary.Add('domain: data')
$summary.Add('title: "Data Adapter Portfolio Roster"')
$summary.Add('audience: [architects, maintainers, developers, ai-agents]')
$summary.Add('status: current')
$summary.Add('last_updated: 2026-07-28')
$summary.Add('framework_version: v0.20.0')
$summary.Add('validation:')
$summary.Add('  date_last_tested: 2026-07-28')
$summary.Add('  status: verified')
$summary.Add('  scope: generated adapter and family inventory summary')
$summary.Add('---')
$summary.Add('')
$summary.Add('# Data adapter roster')
$summary.Add('')
$summary.Add("Generated from repository facts at ``$now``. The machine-readable authority is [roster.json](roster.json).")
$summary.Add('')
$summary.Add("- Adapters: $($roster.counts.adapters) ($($roster.counts.entityPersistence) Entity persistence, $($roster.counts.vector) Vector)")
$summary.Add("- Shared family seams: $($roster.counts.families)")
$summary.Add("- Source commit: ``$head`` (working tree dirty: ``$($roster.source.dirty.ToString().ToLowerInvariant())``)")
$summary.Add("- Docker: CLI ``$($roster.source.dockerCli.ToString().ToLowerInvariant())``; daemon ``$($roster.source.dockerDaemon.ToString().ToLowerInvariant())``")
$summary.Add('')
$summary.Add('| ID | Plane | Package | Family seams | Dedicated tests | Claims |')
$summary.Add('|---|---|---|---|---:|---:|')
foreach ($adapter in $roster.adapters) {
    $familyText = if (@($adapter.familyProjects).Count) { @($adapter.familyProjects) -join ', ' } else { '—' }
    $summary.Add("| $($adapter.id) | $($adapter.plane) | ``$($adapter.package)`` | $familyText | $(@($adapter.tests).Count) | $(@($adapter.productClaims).Count) |")
}
$summary.Add('')
$summary.Add('Cache adapters and Koan.Data.AI were observed only as adjacent package families and are outside this roster.')
$summary | Set-Content -LiteralPath (Join-Path $OutputRoot 'roster.md') -Encoding utf8

Write-Output "ROSTER adapters=$($roster.counts.adapters) entity=$($roster.counts.entityPersistence) vector=$($roster.counts.vector) families=$($roster.counts.families)"
Write-Output "ROSTER_JSON=$(Get-RelativePath $jsonPath)"
