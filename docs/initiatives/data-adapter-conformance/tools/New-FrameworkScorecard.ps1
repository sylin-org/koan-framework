[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $PrimerPath,

    [Parameter(Mandatory)]
    [string] $SurfaceMapPath,

    [Parameter(Mandatory)]
    [string] $OutputPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$primerLines = Get-Content -LiteralPath $PrimerPath
$surfaceMap = Get-Content -LiteralPath $SurfaceMapPath -Raw | ConvertFrom-Json

$requirements = [ordered]@{}
for ($index = 0; $index -lt $primerLines.Count; $index++) {
    $match = [Regex]::Match($primerLines[$index], '^- \*\*((?:[A-H]|P|V)-\d{2})\*\*\s*(.*)$')
    if (-not $match.Success) { continue }
    $id = $match.Groups[1].Value
    $parts = [System.Collections.Generic.List[string]]::new()
    $parts.Add($match.Groups[2].Value.Trim())
    for ($next = $index + 1; $next -lt $primerLines.Count; $next++) {
        $line = $primerLines[$next]
        if ($line -match '^- \*\*(?:[A-H]|P|V)-\d{2}\*\*' -or $line -match '^#{1,4} ') { break }
        if ([string]::IsNullOrWhiteSpace($line)) { break }
        $parts.Add($line.Trim())
    }
    $requirements[$id] = [ordered]@{
        line = $index + 1
        text = (($parts -join ' ') -replace '\s+', ' ').Trim()
    }
}

$expectedIds = foreach ($prefix in @('A','B','C','D','E','F','G','H','P','V')) {
    $limit = switch ($prefix) { 'A' { 9 }; 'B' { 9 }; 'C' { 6 }; 'D' { 9 }; 'E' { 15 }; 'F' { 12 }; 'G' { 9 }; 'H' { 6 }; 'P' { 6 }; 'V' { 24 } }
    foreach ($number in 1..$limit) { '{0}-{1:d2}' -f $prefix, $number }
}
if ($requirements.Count -ne 105 -or @($expectedIds | Where-Object { -not $requirements.Contains($_) }).Count -ne 0) {
    throw "Primer requirement extraction failed: expected 105, found $($requirements.Count)."
}

$targetIds = @(
    'A-06','A-08',
    'C-01','C-02','C-03','C-04','C-05','C-06',
    'D-01','D-02','D-03','D-04','D-05','D-06','D-07','D-08','D-09',
    'E-01','E-02','E-03','E-04','E-05','E-06','E-07','E-09','E-11',
    'F-01','F-02','F-03','F-04','F-05','F-06','F-07','F-08','F-09','F-10','F-11','F-12',
    'H-02','H-03','P-05','P-06',
    'V-01','V-02','V-03','V-04','V-05','V-06','V-07','V-08','V-09','V-10','V-11','V-12',
    'V-13','V-14','V-15','V-16','V-17','V-18','V-19','V-20','V-21','V-22','V-23','V-24'
)

$deferIds = @('A-01','A-03','A-05','B-01','B-02','B-06','G-03','G-07','G-08','G-09')

$groupFindings = @{
    A = 'Current route/readiness mechanics predate frozen source policy, separated route/shape state, and host-owned immutable plans.'
    B = 'The Entity/repository grammar exists, but provider outcomes and shared receipts do not yet prove the complete oracle contract.'
    C = 'No compiled StorageLifecycle/Access ceiling exists across Entity, Direct, transaction, batch, instruction, and override paths.'
    D = 'Neutral inspection descriptors, bounded container discovery, and the RecordSet substrate are absent.'
    E = 'Current projection/index metadata is not the target compiled logical-path to physical-binding mapping model.'
    F = 'No immutable application-owned registered-operation catalog exists; Direct raw commands are not a substitute.'
    G = 'Concurrency, fault, durability, and isolation guarantees are partial and lack one shared receipt/failure contract.'
    H = 'Diagnostics do not project one safe compiled decision envelope through Describe, Explain, Doctor, facts, health, and errors.'
    P = 'Warm paths contain process-static caches, reflection/JSON materialization, hidden client work, or unmeasured claims.'
    V = 'The ratified Vector contract is not yet implemented; current provider surfaces remain evidence and every V row is Target.'
}

$overrides = @{
    'A-01' = 'Koan modules and adapter factories provide ordinary reference-based availability; a clean BOOT proof remains for executable conformance.'
    'A-02' = 'ProviderCatalog has exact selection helpers, but Direct and legacy routing retain fallback branches and runtime DI enumeration outside one frozen source plan.'
    'A-03' = 'Election structures exist; proving that every unelected adapter performs zero I/O requires a boot probe after the control plane exists.'
    'A-04' = 'Projection, naming, managed-field, operation-override, polymorphism, and other mutable/static caches can cross host boundaries.'
    'A-05' = 'Host-owned disposal cannot be established statically for every provider client, keeper, cursor, and pool.'
    'A-06' = 'DataSourceRegistry has Adapter, ConnectionString, and flat Settings only; Managed + ReadWrite defaults are not represented as typed policy.'
    'A-07' = 'Relational shape APIs exist, but validation is family-local and not a definition-complete shared declared-shape contract.'
    'A-08' = 'There is no External lifecycle ceiling preventing explicit DDL and implicit native auto-create on every route.'
    'A-09' = 'WithDataReadinessAsync infers missing shape from exception text/type, attempts reflective provisioning, and replays the business operation.'
    'B-03' = 'Boolean/coarse mutation results do not express insert, update, missing, conflict, commit-known, and outcome-unknown states uniformly.'
    'B-04' = 'DeleteAll, RemoveAll, instruction clear, bulk loops, and fallback paths do not share one lifecycle/segmentation/effect receipt.'
    'B-05' = 'KeyValue deferred batch loads at Save, but missing updates/deletes can be silently skipped and family atomicity is not natively established.'
    'B-07' = 'RepositoryQueryResult reports sort/pagination/projection only; KeyValue scan-backed query can mark work handled without a provider-work receipt.'
    'B-08' = 'Unsupported operations may enter fallback, scan, or partial mutation paths instead of rejecting before work.'
    'B-09' = 'ProviderBoundedPaging exists as a token, but the claim is not uniformly tied to native bound/order/continuation receipts.'
    'C-01' = 'RepositoryFacade guards readiness after generic guards and has no typed read-only source ceiling; alternate write paths are not centrally closed.'
    'C-02' = 'EnsureReady, readiness replay, Direct, and family schema APIs have no shared External lifecycle gate.'
    'C-03' = 'The framework cannot distinguish semantic data DeleteAll from structural clear/drop under External + ReadWrite because source lifecycle policy is absent.'
    'C-04' = 'Context, Direct connection override, transactions, batches, instructions, transfers, backup, soft-delete, and provider extensions are not all bound to one compiled policy.'
    'C-05' = 'Current instructions/raw commands do not carry a mandatory frozen Read effect; string/prefix forms can be opaque.'
    'C-06' = 'Source policy is absent, and several failures include native messages or connection-adjacent diagnostics rather than a safe policy projection.'
    'D-05' = 'Direct query materializes Dictionary<string,object?>, which overwrites duplicate names and cannot preserve missing versus null or the closed neutral algebra.'
    'D-06' = 'Dictionary materialization makes ambiguous names invisible instead of preserving ordinal access and rejecting name ambiguity.'
    'D-07' = 'Direct Query<T> serializes each row to JSON and deserializes it; no reused ordinal constructor/property plan exists.'
    'D-08' = 'Direct exposes MaxRows only; RecordSet record/value/byte/duration bounds and deterministic MaterializedValueV1 accounting are absent.'
    'E-08' = 'ProjectionResolver sees top-level scalar properties only and does not produce a provider-consumed minimal physical read plan.'
    'E-10' = 'ProjectionResolver and related metadata use unbounded process-static type caches rather than bounded host-owned immutable plans.'
    'E-12' = 'Relational projection/index metadata exists, but it is not connected to the single compiled mapping plan and mutation receipt required by the primer.'
    'E-13' = 'Index metadata cannot yet prove equality between query/write scalar encoding and the provider expression used by the index.'
    'E-14' = 'No shared claim distinguishes rewrite-free expression indexes from stored projections requiring backfill.'
    'E-15' = 'A TTL capability token exists, but mapping metadata, source lifecycle policy, and executable native expiry proof are not unified.'
    'F-04' = 'Direct reflects over generic Execute methods and parameter objects at runtime; there is no frozen binding/lane/parameter/materializer plan.'
    'F-05' = 'InstructionSql names SQL operations but does not provide the immutable catalog-wide effect classification and provider-enforced read lane required here.'
    'F-11' = 'Current readiness explicitly permits business-operation replay after inferred missing shape, contradicting the no-replay rule.'
    'G-01' = 'Readiness/provisioning is not one declared-shape single-flight outcome and may race through operation-probe/replay.'
    'G-02' = 'Route readiness and shape provisioning are not represented as distinct host-scoped single-flight keys with caller-independent cancellation.'
    'G-03' = 'Pool saturation and session-state leakage require LIVE provider evidence after policy-bound acquisition exists.'
    'G-04' = 'Direct executor reflection passes default cancellation on one path, timeout/cancellation classification is not shared, and native cleanup evidence is absent.'
    'G-05' = 'TransactionCoordinator is explicitly best-effort and sequential across adapters; KeyValue atomic batch can lower to separate upsert/delete calls.'
    'G-06' = 'A conditional repository interface exists, but one shared native compare-and-set receipt and lost-race taxonomy are not established.'
    'G-07' = 'Durability is provider-specific and requires restart evidence; no static framework audit can certify it.'
    'G-08' = 'Resource/cursor/task/cache stability needs the standard soak after host ownership and bounded caches are implemented.'
    'G-09' = 'Isolation tokens exist, but each requires adversarial provider evidence and consistent source/context projection.'
    'H-01' = 'Existing Describe methods mostly expose adapter capabilities; they do not project the complete frozen route, policy, mapping, and operation plans.'
    'H-02' = 'No common pre-execution Explain surface or provider/client work plan exists.'
    'H-03' = 'No common non-mutating Doctor contract exists.'
    'H-04' = 'Boot reports, facts, health, exceptions, query results, and tests use fragmented identities and do not derive from one decision envelope.'
    'H-05' = 'Direct/readiness/transaction failures can include raw exception messages; parameter and provider redaction is not one enforced taxonomy.'
    'H-06' = 'Readiness classifies schema failures by exception message/type-name heuristics instead of exact provider codes/types and targets.'
    'P-01' = 'Warm Direct routing enumerates connection factories and resolves sources at operation time; source policy/readiness is not one precompiled plan.'
    'P-02' = 'Direct DTO projection performs JSON round trips, and several reflection caches/materializers are process-static or rebuilt outside a host plan.'
    'P-03' = 'Route-adjacent naming, mapping, type, managed-field, operation, and query caches are process-static and frequently unbounded.'
    'P-04' = 'KeyValue scan-backed query and partial RepositoryQueryResult flags can overstate provider work and conceal client sort/page/filter work.'
    'P-05' = 'No stable benchmark suite records the primer cold/warm allocation, dispatch, elapsed, and native-plan cells.'
    'P-06' = 'No current one-page responsibility map covers source policy, routing, readiness, mapping, materialization, receipts, failures, and provider translation.'
}

function Get-PrimaryOwner {
    param([string] $Id)
    $prefix = $Id.Substring(0, 1)
    if ($prefix -in @('A','C','D','F','H','P')) { return 'Framework' }
    if ($prefix -eq 'E') { return 'Framework + Family' }
    if ($prefix -eq 'B') { return 'Framework contract; Family/Adapter execution' }
    return 'Framework contract; Adapter evidence'
}

function Get-Downstream {
    param([string] $Id)
    $prefix = $Id.Substring(0, 1)
    if ($prefix -in @('B','E','G')) { return @('Family packets', 'Adapter packets') }
    if ($prefix -in @('A','C','D','F','H','P')) { return @('Every adapter packet') }
    return @()
}

$rows = foreach ($id in $expectedIds) {
    $prefix = $id.Substring(0, 1)
    $disposition = if ($id -in $targetIds) { 'Target' } else { 'Observed' }
    $verdict = if ($id -in $deferIds) { 'DEFER' } else { 'RED' }
    $finding = if ($overrides.ContainsKey($id)) { $overrides[$id] } else { $groupFindings[$prefix] }
    $linkedSurfaces = @($surfaceMap.surfaces | Where-Object { $id -in $_.cells } | Select-Object -ExpandProperty id | Sort-Object -Unique)
    if ($linkedSurfaces.Count -eq 0) { throw "No surface maps primer cell $id." }
    [ordered]@{
        id = $id
        primerLine = $requirements[$id]['line']
        requirement = $requirements[$id]['text']
        disposition = $disposition
        verdict = $verdict
        primaryOwner = Get-PrimaryOwner $id
        downstreamOwners = @(Get-Downstream $id)
        surfaceIds = $linkedSurfaces
        finding = $finding
        evidenceIds = @('EVD-DAC01-CLEAN-SOURCE','EVD-DAC01-PUBLIC-API','EVD-DAC01-SURFACE-MAP')
        remediation = if ($verdict -eq 'RED') { "Resolve through DAC-02 and the owning Framework/Family contract card before any adapter may claim $id." } else { "Execute the $id conformance cell after the owning shared contract exists; static source is insufficient for PASS." }
    }
}

$document = [ordered]@{
    schemaVersion = 1
    scope = 'framework'
    sourceCommit = $surfaceMap.sourceCommit
    primerSha256 = (Get-FileHash -Algorithm SHA256 -LiteralPath $PrimerPath).Hash.ToLowerInvariant()
    status = 'complete-audit'
    summary = [ordered]@{
        rows = @($rows).Count
        dispositions = @($rows | Group-Object { $_['disposition'] } | Sort-Object Name | ForEach-Object { [ordered]@{ name=$_.Name; count=$_.Count } })
        verdicts = @($rows | Group-Object { $_['verdict'] } | Sort-Object Name | ForEach-Object { [ordered]@{ name=$_.Name; count=$_.Count } })
        unlinked = @($rows | Where-Object { $_.surfaceIds.Count -eq 0 }).Count
    }
    rows = @($rows)
    verdict = 'RED'
    meaning = 'The current framework does not satisfy the ratified target; adapter-local remediation is forbidden.'
}

[System.IO.Directory]::CreateDirectory([System.IO.Path]::GetDirectoryName([System.IO.Path]::GetFullPath($OutputPath))) | Out-Null
[System.IO.File]::WriteAllText(
    [System.IO.Path]::GetFullPath($OutputPath),
    ($document | ConvertTo-Json -Depth 12),
    [System.Text.UTF8Encoding]::new($false))

Write-Output "FRAMEWORK-SCORECARD PASS rows=$(@($rows).Count) red=$(@($rows | Where-Object verdict -eq 'RED').Count) defer=$(@($rows | Where-Object verdict -eq 'DEFER').Count)"
