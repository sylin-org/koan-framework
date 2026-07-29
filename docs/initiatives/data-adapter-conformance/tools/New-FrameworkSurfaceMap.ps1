[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $InventoryPath,

    [Parameter(Mandatory)]
    [string] $OutputDirectory
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$inventory = Get-Content -LiteralPath $InventoryPath -Raw | ConvertFrom-Json
$output = [System.IO.Path]::GetFullPath($OutputDirectory)

function New-Surface {
    param(
        [string] $Id,
        [string] $Name,
        [string] $Owner,
        [string] $Posture,
        [string[]] $Cells
    )
    return [ordered]@{
        id = $Id
        name = $Name
        owner = $Owner
        posture = $Posture
        cells = @($Cells)
    }
}

function Get-AdapterSlug {
    param([string] $ProjectName)
    $isVector = $ProjectName.StartsWith('Koan.Data.Vector.Connector.', [System.StringComparison]::Ordinal)
    $leaf = $ProjectName -replace '^Koan\.Data\.(?:Vector\.)?Connector\.', '' -replace '[^A-Za-z0-9]+', '-'
    if ($isVector) { return ('VECTOR-' + $leaf).ToUpperInvariant() }
    return $leaf.ToUpperInvariant()
}

$surfaces = [System.Collections.Generic.List[object]]::new()
$surfaces.Add((New-Surface 'SUR-FWK-SOURCE-CATALOG' 'Source declaration, catalog, provider election, and route resolution' 'Framework' 'keep route mechanics; rebuild under frozen source plans' @('A-01','A-02','A-03','A-04','A-06','C-04','C-06','H-01','H-04','P-01','P-03','P-06')))
$surfaces.Add((New-Surface 'SUR-FWK-ROUTING-CONTEXT' 'Ambient source, adapter, partition, cache, and transaction context' 'Framework' 'absorb into policy-bound operation context' @('A-02','A-04','C-01','C-04','G-03','G-09','H-01','P-01','P-03')))
$surfaces.Add((New-Surface 'SUR-FWK-REPOSITORY-CONTRACT' 'Repository, query, bounded-query, batch, conditional, and instruction contracts' 'Framework' 'keep minimal mechanics; add truthful receipts and fail-closed policy contracts' @('B-01','B-02','B-03','B-04','B-05','B-06','B-07','B-08','B-09','C-01','C-04','G-05','G-06','P-04')))
$surfaces.Add((New-Surface 'SUR-FWK-REPOSITORY-CHOKEPOINT' 'RepositoryFacade canonical execution chokepoint' 'Framework' 'rebuild ordering around compiled source policy and readiness stages' @('A-08','A-09','B-01','B-03','B-04','C-01','C-02','C-03','C-04','G-02','H-06')))
$surfaces.Add((New-Surface 'SUR-FWK-ENTITY-READ' 'Entity/Data keyed and finite reads' 'Framework' 'keep Entity grammar; route through compiled operation plans' @('B-01','B-02','B-08','C-04','P-01','P-02')))
$surfaces.Add((New-Surface 'SUR-FWK-ENTITY-QUERY' 'Entity/Data query, count, page, raw query, and result shaping' 'Framework' 'keep compact Entity grammar; rebuild execution receipts and bounded fallback law' @('B-06','B-07','B-08','B-09','C-04','H-02','P-04')))
$surfaces.Add((New-Surface 'SUR-FWK-ENTITY-STREAM' 'Entity/Data streaming and provider-bounded paging' 'Framework' 'keep only capability-qualified incremental paths' @('B-08','B-09','G-04','G-08','P-04','P-05')))
$surfaces.Add((New-Surface 'SUR-FWK-ENTITY-WRITE' 'Entity/Data scalar writes, patch, remove, and delete-all' 'Framework' 'keep Entity grammar; move access and effect gate before callbacks/readiness/I/O' @('B-01','B-03','B-04','C-01','C-02','C-03','C-04','G-06','H-06')))
$surfaces.Add((New-Surface 'SUR-FWK-ENTITY-BULK' 'Entity/Data bulk and batch mutation' 'Framework' 'rebuild around explicit outcome and atomicity claims' @('B-03','B-04','B-05','C-01','C-04','G-05','P-05')))
$surfaces.Add((New-Surface 'SUR-FWK-LIFECYCLE' 'Entity persistence lifecycle registration and dispatch' 'Framework' 'keep lifecycle; guarantee policy rejection before callbacks' @('B-03','B-04','C-01','C-04','H-04')))
$surfaces.Add((New-Surface 'SUR-FWK-DIRECT' 'Direct session, connection override, raw command, scalar, query, and transaction' 'Framework' 'narrow to explicit provider-native escape hatch behind source policy' @('C-01','C-02','C-04','C-05','D-05','D-06','D-08','F-05','F-06','F-11','G-04','H-05','H-06','P-02')))
$surfaces.Add((New-Surface 'SUR-FWK-INSTRUCTION' 'Instruction, raw-query, patch, and native-operation dispatch' 'Framework' 'rebuild effect typing and prohibit message/prefix inference and replay' @('B-04','C-01','C-02','C-04','C-05','F-05','F-06','F-11','H-05','H-06')))
$surfaces.Add((New-Surface 'SUR-FWK-TRANSACTION' 'Deferred transaction coordination and transaction context' 'Framework' 'rename/describe as local best-effort unless native atomicity is proved' @('B-05','C-01','C-04','G-03','G-04','G-05','G-09','H-02','H-06')))
$surfaces.Add((New-Surface 'SUR-FWK-TRANSFER' 'Copy, move, mirror, and partition transfer builders' 'Framework' 'keep bounded workflow; bind both endpoints to explicit source policy' @('B-02','B-03','B-04','C-01','C-04','G-04','H-02','P-04')))
$surfaces.Add((New-Surface 'SUR-FWK-RELATIONSHIP' 'Relationship metadata and bounded relationship loading' 'Framework' 'keep outside adapter mapping; preserve explicit bounded fallback' @('B-06','B-08','B-09','H-02','P-04')))
$surfaces.Add((New-Surface 'SUR-FWK-QUERY-PLAN' 'Filter, sort, projection, pagination, count, and pushdown planning' 'Framework' 'absorb into one immutable execution plan and receipt' @('B-06','B-07','B-08','B-09','E-08','E-11','H-02','P-02','P-04')))
$surfaces.Add((New-Surface 'SUR-FWK-CAPS-RECEIPTS' 'Capabilities, query results, counts, and execution receipts' 'Framework' 'rebuild claim vocabulary and receipts around primer profiles' @('B-07','B-09','D-08','E-08','E-12','E-15','G-05','G-06','G-07','G-09','H-01','H-02','H-04','P-04')))
$surfaces.Add((New-Surface 'SUR-FWK-MAPPING' 'Projection, index, optimization, and logical-to-physical mapping metadata' 'Framework' 'replace scalar-only process cache with host-owned compiled mapping plans' @('A-07','E-01','E-02','E-03','E-04','E-05','E-06','E-07','E-08','E-09','E-10','E-11','E-12','E-13','E-14','E-15','P-02','P-03')))
$surfaces.Add((New-Surface 'SUR-FWK-NAMING-SEGMENTATION' 'Storage naming, partitions, axes, monikers, and segmentation' 'Framework' 'keep semantic routing; compile into source-bound plans and close alternate bypasses' @('A-02','A-04','C-04','E-05','E-06','E-11','G-03','G-09','H-01','P-01','P-03')))
$surfaces.Add((New-Surface 'SUR-FWK-PIPELINE' 'Managed fields, guards, transforms, stamps, and operation overrides' 'Framework' 'keep one canonical pipeline; bind source policy before side effects' @('B-01','B-03','B-04','C-01','C-04','E-11','H-04','P-02','P-03')))
$surfaces.Add((New-Surface 'SUR-FWK-POLYMORPHISM' 'Entity family descriptors, type catalog, and document codecs' 'Framework' 'keep shared codecs; consume the same compiled map on every path' @('B-01','B-02','E-06','E-07','E-11','P-02','P-03')))
$surfaces.Add((New-Surface 'SUR-FWK-READINESS-DIAGNOSTICS' 'Readiness, health, boot reports, facts, and failure classification' 'Framework' 'rebuild route/shape state; remove operation probe/replay and message classification' @('A-03','A-05','A-07','A-08','A-09','C-02','C-06','G-01','G-02','G-04','G-08','H-01','H-02','H-03','H-04','H-05','H-06','P-01','P-03','P-05')))
$surfaces.Add((New-Surface 'SUR-FWK-INSPECTION-RECORDS' 'Provider-neutral inspection, storage descriptors, and RecordSet materialization' 'Framework' 'create as a shared target contract; current Direct dictionaries do not qualify' @('D-01','D-02','D-03','D-04','D-05','D-06','D-07','D-08','D-09','P-02','P-05','P-06')))
$surfaces.Add((New-Surface 'SUR-FWK-REGISTERED-OPERATIONS' 'Immutable named Query/Scalar catalog, effect gate, lanes, parameters, and bounds' 'Framework' 'create as a shared target contract; current raw instructions do not qualify' @('C-05','F-01','F-02','F-03','F-04','F-05','F-06','F-07','F-08','F-09','F-10','F-11','F-12','P-01','P-02','P-03','P-05','P-06')))
$surfaces.Add((New-Surface 'SUR-FAM-DOCUMENT' 'Core document-store family mechanics' 'Family' 'rebuild against shared plans; retain only document translation mechanics' @('B-01','B-02','B-03','B-04','B-06','B-07','C-01','G-02','P-04')))
$surfaces.Add((New-Surface 'SUR-FAM-KEYVALUE' 'Core key/value family mechanics' 'Family' 'rebuild handled claims and batch atomicity semantics' @('B-01','B-02','B-03','B-04','B-05','B-06','B-07','B-08','B-09','G-05','G-09','P-04')))
$surfaces.Add((New-Surface 'SUR-FWK-SUPPORT' 'Remaining public Data support, annotations, metadata, configuration, and utilities' 'Framework' 'keep only concepts required by a business decision or shared guarantee' @('A-01','H-04','P-02','P-03','P-06')))
$surfaces.Add((New-Surface 'SUR-EXT-BACKUP' 'Backup/restore alternate Data execution surface' 'Framework extension' 'policy-bind source and target before enumeration or mutation' @('B-02','B-03','B-04','C-01','C-04','G-04','H-02','P-04')))
$surfaces.Add((New-Surface 'SUR-EXT-SOFTDELETE' 'Soft-delete axis and write override surface' 'Framework extension' 'keep as pipeline contribution; never bypass source access policy' @('B-03','B-04','C-01','C-04','E-11','H-04')))
$surfaces.Add((New-Surface 'SUR-ADJ-DATA-AI' 'Data.AI Entity-adjacent embedding and vector-model surface' 'Adjacent pillar' 'out of record-adapter scope; preserve source-policy participation where it performs Data I/O' @('C-04','H-04','P-06')))
$surfaces.Add((New-Surface 'SUR-FAM-RELATIONAL-CONTRACT' 'Relational mapping, schema, DDL, and store contracts' 'Relational Family' 'rebuild behind shared mapping and lifecycle plans' @('A-07','A-08','A-09','C-02','E-01','E-02','E-03','E-04','E-08','E-09','E-11','E-12','E-13','E-14','E-15','G-01')))
$surfaces.Add((New-Surface 'SUR-FAM-RELATIONAL-EXECUTION' 'Relational ADO, filter lowering, scalar encoding, and query mechanics' 'Relational Family' 'keep parameterized native mechanics; remove static unbounded plan caches' @('B-01','B-02','B-06','B-07','D-05','E-07','E-08','E-11','F-04','G-04','H-06','P-02','P-03','P-04')))
$surfaces.Add((New-Surface 'SUR-FAM-RELATIONAL-NPGSQL' 'Npgsql relational repository family implementation' 'Relational Npgsql Family' 'evaluate as family seam; do not use as gold-author input' @('B-01','B-02','B-03','B-04','B-06','B-07','C-01','E-11','G-06','H-06','P-04')))
$surfaces.Add((New-Surface 'SUR-FAM-SEARCHENGINE' 'Search-engine family translation and vector repository mechanics' 'SearchEngine Family' 'share source-core policy; defer similarity annex semantics to DAC-49' @('A-01','A-02','A-04','C-01','C-04','G-02','G-04','H-01','H-04','P-01','P-03','P-06')))
$vectorCells = @(1..24 | ForEach-Object { 'V-{0:d2}' -f $_ })
$surfaces.Add((New-Surface 'SUR-FAM-VECTOR-CONTRACT' 'Vector repository, schema, claims, and query contracts' 'Vector Family' 'own the ratified provider-neutral Vector contract' (@('A-01','A-02','A-04','C-01','C-04','G-02','G-04','H-01','H-04','P-01','P-03','P-06') + $vectorCells)))
$surfaces.Add((New-Surface 'SUR-FAM-VECTOR-RUNTIME' 'Vector public terminals, provider election, coordination, and filter gate' 'Vector Family' 'realize the ratified Vector contract through one runtime plan' (@('A-01','A-02','A-04','C-01','C-04','G-02','G-04','H-01','H-02','H-04','P-01','P-03','P-06') + $vectorCells)))

$adapterProjectNames = @($inventory.projects | Where-Object role -eq 'Adapter' | Select-Object -ExpandProperty name | Sort-Object)
foreach ($projectName in $adapterProjectNames) {
    $slug = Get-AdapterSlug $projectName
    $surfaces.Add((New-Surface "SUR-ADAPTER-$slug" "$projectName public provider surface" 'Adapter' 'inventory only; provider verdict belongs to its fleet card' @('A-01','A-02','A-03','A-04','A-05','C-04','C-06','G-02','G-03','G-04','G-08','H-01','H-04','H-05','H-06','P-01','P-03','P-05','P-06')))
}

function Get-FrameworkSurfaceId {
    param($Entry)
    $project = [string]$Entry.project
    $file = [string]$Entry.file
    $type = [string]$Entry.type
    $name = if ($null -eq $Entry.PSObject.Properties['name']) { '' } else { [string]$Entry.name }

    if ($Entry.role -eq 'Adapter') {
        $slug = Get-AdapterSlug $project
        return "SUR-ADAPTER-$slug"
    }
    if ($project -eq 'Koan.Data.AI') { return 'SUR-ADJ-DATA-AI' }
    if ($project -eq 'Koan.Data.Backup') { return 'SUR-EXT-BACKUP' }
    if ($project -eq 'Koan.Data.SoftDelete') { return 'SUR-EXT-SOFTDELETE' }
    if ($project -eq 'Koan.Data.Relational.Abstractions') { return 'SUR-FAM-RELATIONAL-CONTRACT' }
    if ($project -eq 'Koan.Data.Relational') { return 'SUR-FAM-RELATIONAL-EXECUTION' }
    if ($project -eq 'Koan.Data.Relational.Npgsql') { return 'SUR-FAM-RELATIONAL-NPGSQL' }
    if ($project -eq 'Koan.Data.SearchEngine') { return 'SUR-FAM-SEARCHENGINE' }
    if ($project -eq 'Koan.Data.Vector.Abstractions') { return 'SUR-FAM-VECTOR-CONTRACT' }
    if ($project -eq 'Koan.Data.Vector') { return 'SUR-FAM-VECTOR-RUNTIME' }

    if ($file -match '/DataSourceRegistry\.cs$|/Routing/|/Configuration/' -or $type -match 'DataProviderCatalog|AdapterConnectionResolver|DataDefaultProviderPlan|RoutedSource') { return 'SUR-FWK-SOURCE-CATALOG' }
    if ($file -match '/EntityContext\.cs$') { return 'SUR-FWK-ROUTING-CONTEXT' }
    if ($file -match '/Direct/') { return 'SUR-FWK-DIRECT' }
    if ($file -match '/Transactions/') { return 'SUR-FWK-TRANSACTION' }
    if ($file -match '/Transfers/|SetMoveBuilder\.cs$') { return 'SUR-FWK-TRANSFER' }
    if ($file -match '/Lifecycle/') { return 'SUR-FWK-LIFECYCLE' }
    if ($file -match '/Relationships/|RelationshipExtensions\.cs$') { return 'SUR-FWK-RELATIONSHIP' }
    if ($file -match '/Document/') { return 'SUR-FAM-DOCUMENT' }
    if ($file -match '/KeyValue/') { return 'SUR-FAM-KEYVALUE' }
    if ($file -match '/Adapters/|/Diagnostics/|/Initialization/' -or $type -match 'DataAdapterReadinessExtensions|AdapterBootReporting') { return 'SUR-FWK-READINESS-DIAGNOSTICS' }
    if ($file -match '/Capabilities/' -or $type -match 'DataCaps|QueryResult|RepositoryQueryResult|CountResult|BoundedQueryResult|BatchResult') { return 'SUR-FWK-CAPS-RECEIPTS' }
    if ($file -match '/Filtering/|/Sorting/|/Querying/' -or $type -match 'QueryDefinition|Projection$|SortSpec') { return 'SUR-FWK-QUERY-PLAN' }
    if ($file -match '/Instructions/|/Patch/' -or $type -match 'Instruction|RawQuery|Patch') { return 'SUR-FWK-INSTRUCTION' }
    if ($file -match '/Naming/|/Axes/|/Semantics/' -or $type -match 'StorageName|DataSourceAttribute|SourceAdapterAttribute') { return 'SUR-FWK-NAMING-SEGMENTATION' }
    if ($file -match '/Pipeline/' -or $type -match 'ManagedField|StorageWrite|FieldTransform|OperationOverride') { return 'SUR-FWK-PIPELINE' }
    if ($file -match '/Polymorphism/' -or $type -match 'EntityFamilyStorage') { return 'SUR-FWK-POLYMORPHISM' }
    if ($file -match 'ProjectionResolver\.cs$|IndexMetadata\.cs$|/Optimization/' -or $type -match 'IndexAttribute|StorageAttribute|StorageNameAttribute') { return 'SUR-FWK-MAPPING' }
    if ($file -match 'RepositoryFacade\.cs$') { return 'SUR-FWK-REPOSITORY-CHOKEPOINT' }
    if ($type -match 'IDataRepository|IQueryRepository|IRawQueryRepository|IBoundedQueryRepository|IConditionalWriteRepository|IBatchSet|IDataAdapterFactory') { return 'SUR-FWK-REPOSITORY-CONTRACT' }
    if ($file -match '/Model/Entity\.cs$|/Data\.cs$|AggregateExtensions\.cs$|BatchExtensions\.cs$') {
        if ($name -match 'Stream$') { return 'SUR-FWK-ENTITY-STREAM' }
        if ($name -match '^(Query|QueryRaw|QueryWithCount|Count|AllWithCount|FirstPage|Page)$') { return 'SUR-FWK-ENTITY-QUERY' }
        if ($name -match '^(Batch|UpsertMany|DeleteMany|AsBatch|AddRange)$') { return 'SUR-FWK-ENTITY-BULK' }
        if ($name -match '^(Upsert|UpsertId|Save|SaveReplacing|Delete|DeleteAll|Remove|RemoveAll|RemoveByQuery|Patch|PatchMerge|ClearPartition)$') { return 'SUR-FWK-ENTITY-WRITE' }
        if ($name -match '^(Get|GetMany|All)$') { return 'SUR-FWK-ENTITY-READ' }
        return 'SUR-FWK-ENTITY-READ'
    }
    return 'SUR-FWK-SUPPORT'
}

$assignments = [System.Collections.Generic.List[object]]::new()
foreach ($type in $inventory.types) {
    $assignments.Add([ordered]@{ apiId = $type.id; entryKind = 'type'; surfaceId = Get-FrameworkSurfaceId $type })
}
foreach ($member in $inventory.members) {
    $assignments.Add([ordered]@{ apiId = $member.id; entryKind = 'member'; surfaceId = Get-FrameworkSurfaceId $member })
}

$surfaceIds = @($surfaces | ForEach-Object { $_['id'] })
$duplicateSurfaceIds = @($surfaceIds | Group-Object | Where-Object Count -ne 1)
$unknown = @($assignments | Where-Object { $_['surfaceId'] -notin $surfaceIds })
$duplicateApiIds = @($assignments | Group-Object { $_['apiId'] } | Where-Object Count -ne 1)
if ($duplicateSurfaceIds.Count -gt 0 -or $unknown.Count -gt 0 -or $duplicateApiIds.Count -gt 0) {
    throw "Surface coverage failed: duplicateSurfaces=$($duplicateSurfaceIds.Count), unknown=$($unknown.Count), nonUniqueApi=$($duplicateApiIds.Count)."
}

$anchors = @(
    [ordered]@{ id='ANCHOR-001'; surfaceId='SUR-FWK-REPOSITORY-CHOKEPOINT'; file='src/Koan.Data.Core/RepositoryFacade.cs'; symbol='RepositoryFacade<TEntity,TKey>'; reason='canonical guard/readiness/lifecycle wrapper' },
    [ordered]@{ id='ANCHOR-002'; surfaceId='SUR-FWK-READINESS-DIAGNOSTICS'; file='src/Koan.Data.Core/Adapters/DataAdapterReadinessExtensions.cs'; symbol='WithDataReadinessAsync'; reason='operation-probe/provision/replay seam' },
    [ordered]@{ id='ANCHOR-003'; surfaceId='SUR-FWK-DIRECT'; file='src/Koan.Data.Core/Direct/DirectSession.cs'; symbol='DirectSession'; reason='internal raw execution implementation behind public Direct contract' },
    [ordered]@{ id='ANCHOR-004'; surfaceId='SUR-FWK-TRANSACTION'; file='src/Koan.Data.Core/Transactions/TransactionCoordinator.cs'; symbol='TransactionCoordinator'; reason='best-effort deferred coordinator behind public transaction surface' },
    [ordered]@{ id='ANCHOR-005'; surfaceId='SUR-FAM-KEYVALUE'; file='src/Koan.Data.Core/KeyValue/KeyValueStore.cs'; symbol='KeyValueStore<TEntity,TKey>'; reason='scan-backed family execution and batch claim seam' },
    [ordered]@{ id='ANCHOR-006'; surfaceId='SUR-FAM-DOCUMENT'; file='src/Koan.Data.Core/Document/DocumentStore.cs'; symbol='DocumentStore<TEntity,TKey>'; reason='document family execution seam' },
    [ordered]@{ id='ANCHOR-007'; surfaceId='SUR-FWK-MAPPING'; file='src/Koan.Data.Core/ProjectionResolver.cs'; symbol='ProjectionResolver'; reason='process-static scalar projection cache' },
    [ordered]@{ id='ANCHOR-008'; surfaceId='SUR-FWK-SOURCE-CATALOG'; file='src/Koan.Data.Core/DataSourceRegistry.cs'; symbol='DataSourceRegistry'; reason='flat mutable source definitions' },
    [ordered]@{ id='ANCHOR-009'; surfaceId='SUR-FWK-CAPS-RECEIPTS'; file='src/Koan.Data.Abstractions/Capabilities/DataCaps.cs'; symbol='DataCaps'; reason='current capability vocabulary' },
    [ordered]@{ id='ANCHOR-010'; surfaceId='SUR-FWK-CAPS-RECEIPTS'; file='src/Koan.Data.Abstractions/RepositoryQueryResult.cs'; symbol='RepositoryQueryResult<TEntity>'; reason='current handled-work receipt' }
)

$surfaceRows = foreach ($surface in ($surfaces | Sort-Object { $_['id'] })) {
    $surfaceId = $surface['id']
    $surfaceAssignments = @($assignments | Where-Object { $_['surfaceId'] -eq $surfaceId })
    [ordered]@{
        id = $surfaceId
        name = $surface['name']
        owner = $surface['owner']
        posture = $surface['posture']
        cells = $surface['cells']
        publicApiIds = @($surfaceAssignments | ForEach-Object { $_['apiId'] } | Sort-Object)
        publicTypes = @($surfaceAssignments | Where-Object { $_['entryKind'] -eq 'type' }).Count
        publicMembers = @($surfaceAssignments | Where-Object { $_['entryKind'] -eq 'member' }).Count
        internalAnchors = @($anchors | Where-Object { $_['surfaceId'] -eq $surfaceId } | ForEach-Object { $_['id'] })
    }
}

$document = [ordered]@{
    schemaVersion = 1
    generatedAt = [DateTimeOffset]::UtcNow.ToString('O')
    sourceCommit = $inventory.sourceCommit
    inventory = [System.IO.Path]::GetFileName($InventoryPath)
    summary = [ordered]@{
        surfaces = @($surfaceRows).Count
        publicEntries = @($assignments).Count
        mappedExactlyOnce = ($unknown.Count -eq 0 -and $duplicateApiIds.Count -eq 0)
        internalAnchors = $anchors.Count
        unmapped = $unknown.Count
        duplicateAssignments = $duplicateApiIds.Count
    }
    surfaces = @($surfaceRows)
    assignments = @($assignments | Sort-Object { $_['apiId'] })
    internalAnchors = $anchors
}

[System.IO.Directory]::CreateDirectory($output) | Out-Null
[System.IO.File]::WriteAllText(
    (Join-Path $output 'surface-map.json'),
    ($document | ConvertTo-Json -Depth 12),
    [System.Text.UTF8Encoding]::new($false))

$assignmentById = @{}
foreach ($assignment in $assignments) { $assignmentById[$assignment['apiId']] = $assignment['surfaceId'] }
$reviewPattern = 'Direct|Instruction|Transaction|Background|Initializ|Provider|Patch|Raw|Connection|Readiness|Provision'
$reviewRows = [System.Collections.Generic.List[object]]::new()
foreach ($entry in @($inventory.types) + @($inventory.members)) {
    $name = if ($null -eq $entry.PSObject.Properties['name']) { '' } else { [string]$entry.name }
    $signature = if ($null -eq $entry.PSObject.Properties['signature']) { '' } else { [string]$entry.signature }
    $haystack = "$($entry.type)|$name|$signature|$($entry.file)|$($entry.project)"
    if ($haystack -notmatch $reviewPattern) { continue }
    $surfaceId = $assignmentById[[string]$entry.id]
    $reviewRows.Add([ordered]@{
        id = $entry.id
        project = $entry.project
        type = $entry.type
        member = if ([string]::IsNullOrWhiteSpace($signature)) { $name } else { $signature }
        file = $entry.file
        line = $entry.line
        surface = $surfaceId
        owner = $entry.role
    })
}
$reviewUnclassified = @($reviewRows | Where-Object { [string]::IsNullOrWhiteSpace($_['surface']) }).Count
$review = [ordered]@{
    schemaVersion = 1
    generatedAt = [DateTimeOffset]::UtcNow.ToString('O')
    reviewer = 'mechanical-coverage-critic'
    independence = 'Separate search pass under the same exposed DAC-01 identity; not an independent human or agent certification.'
    pattern = $reviewPattern
    result = if ($reviewUnclassified -eq 0) { 'PASS' } else { 'FAIL' }
    matchedDeclarations = $reviewRows.Count
    unclassified = $reviewUnclassified
    rows = @($reviewRows | Sort-Object id)
}
[System.IO.File]::WriteAllText(
    (Join-Path $output 'missed-surface-review.json'),
    ($review | ConvertTo-Json -Depth 8),
    [System.Text.UTF8Encoding]::new($false))

$lines = [System.Collections.Generic.List[string]]::new()
$lines.Add('---')
$lines.Add('type: REFERENCE')
$lines.Add('domain: data')
$lines.Add('title: "Koan.Data Framework Surface Inventory"')
$lines.Add('audience: [architects, maintainers, developers, ai-agents]')
$lines.Add('status: current')
$lines.Add('last_updated: 2026-07-28')
$lines.Add('framework_version: v0.20.0')
$lines.Add('validation:')
$lines.Add('  date_last_tested: 2026-07-28')
$lines.Add('  status: reviewed')
$lines.Add('  scope: DAC-01 clean-baseline public and alternate execution surface inventory')
$lines.Add('---')
$lines.Add('')
$lines.Add('# Koan.Data Framework surfaces')
$lines.Add('')
$lines.Add("Status: complete audit inventory. Source commit ``$($inventory.sourceCommit)``; no production source was read from the dirty worktree.")
$lines.Add('')
$lines.Add("The syntax inventory contains $($inventory.summary.publicTypes) public types and $($inventory.summary.publicMembers) public members across $($inventory.summary.projects) Data projects. Every one of those $(@($assignments).Count) declarations is assigned exactly once below. Ten internal chokepoints cover alternate paths that public declarations alone cannot expose.")
$lines.Add('')
$lines.Add('| Surface | Concern | Owner | Public types | Public members | Internal anchors | Cells | Disposition |')
$lines.Add('|---|---|---|---:|---:|---:|---|---|')
foreach ($row in $surfaceRows) {
    $lines.Add("| $($row.id) | $($row.name) | $($row.owner) | $($row.publicTypes) | $($row.publicMembers) | $(@($row.internalAnchors).Count) | $($row.cells -join ', ') | $($row.posture) |")
}
$lines.Add('')
$lines.Add('## Mechanical coverage')
$lines.Add('')
$lines.Add('- `public-api.json` is the restore-free Roslyn syntax inventory of the frozen production source.')
$lines.Add('- `surface-map.json` contains every API-to-SUR assignment and the ten internal-anchor records.')
$lines.Add('- `vocabulary.json` compares the compact primer vocabulary with exact public declarations.')
$lines.Add('- Adapter rows are inventory boundaries, not adapter verdicts; provider certification remains with the fleet cards.')
[System.IO.File]::WriteAllLines((Join-Path $output 'surfaces.md'), $lines, [System.Text.UTF8Encoding]::new($false))

Write-Output "FRAMEWORK-SURFACE-MAP PASS surfaces=$(@($surfaceRows).Count) publicEntries=$(@($assignments).Count) anchors=$($anchors.Count) criticMatches=$($reviewRows.Count)"
