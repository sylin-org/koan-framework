export const meta = {
  name: 'partition-honor-investigation',
  description: 'Verify the Mongo partition-not-honored bug, determine if it is widespread across adapters, and inventory partition test coverage',
  phases: [
    { title: 'Confirm', detail: 'Adversarially confirm/refute the Mongo repo-cache partition race + hunt deterministic bugs' },
    { title: 'Breadth', detail: 'Per-adapter shared-mutable-partition-state + write-path-honors-partition analysis' },
    { title: 'Names+Coverage', detail: 'Partition-name handling end-to-end + existing test inventory' },
  ],
}

const CONTEXT = `
You are investigating a reported bug in the Koan .NET framework (repo root is the cwd):
"Partition information isn't being honored when saving to Mongo."

KEY FACTS ALREADY ESTABLISHED (verify, don't just trust):
- Partition is an ambient routing dimension: EntityContext.Current?.Partition (AsyncLocal), set via
  EntityContext.Partition("x") / EntityContext.With(partition:"x"). See src/Koan.Data.Core/EntityContext.cs.
- Storage name resolution: AdapterNaming.GetOrCompute<TEntity,TKey>(sp)
  (src/Koan.Data.Core/Configuration/AdapterNaming.cs) reads EntityContext.Current?.Partition and calls
  factory.ResolveStorage(type, partition, sp) -> StorageNameGenerator.Resolve/Generate
  (src/Koan.Data.Abstractions/Naming/StorageNameGenerator.cs), which composes baseName + PartitionSeparator + token.
  This layer appears CORRECT and is partition-aware (cache key includes partition).
- DataService is registered AddSingleton (src/Koan.Data.Core/ServiceCollectionExtensions.cs:55), and
  DataService.GetRepository caches repositories in a ConcurrentDictionary keyed by
  (EntityType, KeyType, Adapter, Source) — PARTITION IS NOT IN THE KEY (src/Koan.Data.Core/DataService.cs).
  So ONE repository instance is shared process-wide across all partitions.
- MongoRepository (src/Connectors/Data/Mongo/MongoRepository.cs) holds MUTABLE instance fields
  _collection and _collectionName, reassigned per-call in GetCollectionCore()/EnsureReady() based on the
  current partition's resolved name, with NO synchronization. GetCollectionCore does:
  desired = AdapterNaming.GetOrCompute(...); if (_collection!=null && desired==_collectionName) return _collection;
  else { _collection = db.GetCollection(desired); _collectionName = desired; return _collection; }
- The existing integration test MongoPartitionSpec (real Mongo container) PASSES but is SEQUENTIAL.
- MongoNaming.ResolveCollectionName appears to be DEAD CODE (defined, never called).
- MongoOptions has NO CollectionPrefix property though fixtures set Koan:Data:Mongo:CollectionPrefix (ignored).
- EntityContext.With has ValidatePartitionName() COMMENTED OUT (validation "deferred to adapters").

Be rigorous and evidence-based. Cite exact file:line. Do NOT modify files. Use Read/Grep/Bash(build only).
`

phase('Confirm')

const CONFIRM_SCHEMA = {
  type: 'object',
  additionalProperties: false,
  required: ['raceIsReal', 'severity', 'rootCause', 'evidenceLines', 'proposedFix', 'notes'],
  properties: {
    raceIsReal: { type: 'boolean', description: 'Is the concurrent cross-partition misrouting race real and reachable?' },
    severity: { type: 'string', enum: ['critical', 'high', 'medium', 'low', 'none'] },
    rootCause: { type: 'string' },
    evidenceLines: { type: 'array', items: { type: 'string' }, description: 'file:line citations proving the claim' },
    proposedFix: { type: 'string', description: 'Minimal correct fix (no caching of partition-specific handle on a shared repo; e.g. resolve collection per-call as a local, never mutate shared fields).' },
    notes: { type: 'string' },
  },
}

const REFUTE_SCHEMA = {
  type: 'object',
  additionalProperties: false,
  required: ['refuted', 'reasoning', 'residualRisk'],
  properties: {
    refuted: { type: 'boolean', description: 'true if you can prove the race is NOT reachable' },
    reasoning: { type: 'string' },
    residualRisk: { type: 'string', description: 'Any scenario where it still bites even if mostly mitigated' },
  },
}

const DETERMINISTIC_SCHEMA = {
  type: 'object',
  additionalProperties: false,
  required: ['findings'],
  properties: {
    findings: {
      type: 'array',
      items: {
        type: 'object',
        additionalProperties: false,
        required: ['scenario', 'deterministic', 'partitionHonored', 'evidence', 'severity'],
        properties: {
          scenario: { type: 'string', description: 'e.g. entity.Save() instance path, UpsertMany/batch, GUID partition, special-char partition, very-long partition, default-vs-partition leakage' },
          deterministic: { type: 'boolean' },
          partitionHonored: { type: 'boolean' },
          evidence: { type: 'string' },
          severity: { type: 'string', enum: ['critical', 'high', 'medium', 'low', 'none'] },
        },
      },
    },
  },
}

const [confirm, refute, deterministic] = await parallel([
  () => agent(CONTEXT + `
TASK: Confirm or deny the concurrency race. Trace the EXACT save path:
Data<T,K>.Upsert / entity.Save() -> DataService.GetRepository (singleton, partition-agnostic cache) ->
MongoRepository.Upsert -> GetCollection -> GetCollectionCore. Prove whether two concurrent async flows under
different partitions on the SAME cached repo instance can cause one flow's write to land in the other's collection.
Note the field re-read at 'return _collection'. Decide severity and propose the minimal correct fix
(the fix should make collection resolution per-call/local — never store partition-specific state on the shared repo).`,
    { label: 'confirm-race', phase: 'Confirm', schema: CONFIRM_SCHEMA }),

  () => agent(CONTEXT + `
TASK (ADVERSARIAL): Try HARD to REFUTE the race. Look for anything that would make it unreachable:
is the repo actually transient/scoped (re-check DataService lifetime AND whether GetRepository is bypassed)?
is there a lock/semaphore around GetCollection? does AsyncLocal flow make _collection writes safe? does the
MongoClient driver make IMongoCollection resolution idempotent so a wrong handle still routes right? is the
fast-path actually safe? Default to refuted=false unless you can give airtight proof. Report residual risk.`,
    { label: 'refute-race', phase: 'Confirm', schema: REFUTE_SCHEMA }),

  () => agent(CONTEXT + `
TASK: Hunt for a DETERMINISTIC (non-concurrency) partition bug on the Mongo write path. Check each scenario and
whether partition is honored: (1) entity.Save() instance extension (src/Koan.Data.Core/AggregateExtensions.cs)
vs static Data<T>.Upsert; (2) UpsertMany / Batch / CreateBatch write path; (3) GUID-shaped partition name
formatting; (4) special characters in partition names reaching a Mongo collection name (Mongo forbids '$', etc.)
— is there any sanitization? (5) very long partition names vs Mongo namespace limits (MaxIdentifierBytes is null
for Mongo); (6) default(no-partition) vs partitioned leakage; (7) the ignored CollectionPrefix; (8) dead
MongoNaming.ResolveCollectionName. For each, state if deterministic and whether partition is honored, with evidence.`,
    { label: 'mongo-deterministic', phase: 'Confirm', schema: DETERMINISTIC_SCHEMA }),
])

phase('Breadth')

const ADAPTER_SCHEMA = {
  type: 'object',
  additionalProperties: false,
  required: ['adapter', 'writePathHonorsPartition', 'sharedMutablePartitionState', 'concurrencyRaceRisk', 'evidence', 'severity'],
  properties: {
    adapter: { type: 'string' },
    writePathHonorsPartition: { type: 'boolean', description: 'Does the save/upsert path resolve storage from EntityContext.Current.Partition per-operation?' },
    sharedMutablePartitionState: { type: 'boolean', description: 'Does the repo cache a partition-specific handle (collection/table/keyspace/connection) in a mutable instance field on the partition-agnostic-cached (shared singleton) repo?' },
    concurrencyRaceRisk: { type: 'string', enum: ['yes-same-as-mongo', 'partial', 'no'] },
    evidence: { type: 'string', description: 'file:line citations' },
    severity: { type: 'string', enum: ['critical', 'high', 'medium', 'low', 'none'] },
  },
}

const ADAPTERS = [
  { key: 'postgres', path: 'src/Connectors/Data/Postgres' },
  { key: 'sqlite', path: 'src/Connectors/Data/Sqlite' },
  { key: 'sqlserver', path: 'src/Connectors/Data/SqlServer' },
  { key: 'redis', path: 'src/Connectors/Data/Redis' },
  { key: 'json', path: 'src/Connectors/Data/Json' },
  { key: 'inmemory', path: 'src/Connectors/Data/InMemory' },
  { key: 'couchbase', path: 'src/Connectors/Data/Couchbase' },
]

const breadth = await parallel(ADAPTERS.map(a => () => agent(CONTEXT + `
TASK: Analyze the '${a.key}' data adapter at ${a.path}. The shared repository instance is cached process-wide by
DataService keyed by (entity,key,adapter,source) WITHOUT partition. Determine:
1. Does its write path (Upsert/UpsertMany/Delete/etc.) resolve the physical storage target (table/collection/
   keyspace/key-prefix) from the CURRENT EntityContext.Current.Partition on EVERY operation, passed as a LOCAL?
2. Or does it cache a partition-specific handle/name in a MUTABLE instance field on the shared repo (like Mongo's
   _collection/_collectionName) — which would create the same concurrent cross-partition misrouting race?
Read the repository class + how it builds the table/collection/key per write. Cite file:line. Couchbase note:
it announces EncodePartitionInName=false (routes partition to a native scope) — check how that scope is applied per-op.`,
  { label: `adapter:${a.key}`, phase: 'Breadth', schema: ADAPTER_SCHEMA })))

phase('Names+Coverage')

const NAMES_SCHEMA = {
  type: 'object',
  additionalProperties: false,
  required: ['validatorEnforced', 'issues'],
  properties: {
    validatorEnforced: { type: 'boolean', description: 'Is partition-name validation actually enforced anywhere on the write path (PartitionNameValidator is commented out in EntityContext.With)?' },
    issues: {
      type: 'array',
      items: {
        type: 'object',
        additionalProperties: false,
        required: ['nameClass', 'adapter', 'behavior', 'risk', 'severity'],
        properties: {
          nameClass: { type: 'string', description: 'e.g. special chars ($, /, space), GUID, very long, leading digit, empty/whitespace' },
          adapter: { type: 'string' },
          behavior: { type: 'string', description: 'What actually happens: sanitized / rejected / produces illegal identifier / silently mis-routes' },
          risk: { type: 'string' },
          severity: { type: 'string', enum: ['critical', 'high', 'medium', 'low', 'none'] },
        },
      },
    },
  },
}

const COVERAGE_SCHEMA = {
  type: 'object',
  additionalProperties: false,
  required: ['tests', 'gaps'],
  properties: {
    tests: {
      type: 'array',
      items: {
        type: 'object',
        additionalProperties: false,
        required: ['name', 'kind', 'adapter', 'asserts', 'concurrent'],
        properties: {
          name: { type: 'string' },
          kind: { type: 'string', enum: ['integration-real-container', 'integration-inproc', 'unit'] },
          adapter: { type: 'string' },
          asserts: { type: 'string' },
          concurrent: { type: 'boolean' },
        },
      },
    },
    gaps: { type: 'array', items: { type: 'string' }, description: 'Missing coverage (adapters, concurrency, partition-name edge cases)' },
  },
}

const [names, coverage] = await parallel([
  () => agent(CONTEXT + `
TASK: Analyze partition-NAME handling end-to-end across the framework. Is PartitionNameValidator
(src/Koan.Data.Core — find it) ever enforced on the write path, given EntityContext.With() has its
ValidatePartitionName() call commented out? For each adapter, how is a raw partition turned into an
identifier (PartitionTokenPolicy.Format handles GUIDs; who sanitizes the rest?) — Sqlite sanitizes; does Mongo,
Postgres, Redis, Couchbase? What happens with special chars/long names/leading digits that are illegal for that
store? Identify concrete risks (illegal identifier, silent mis-route, collision).`,
    { label: 'partition-names', phase: 'Names+Coverage', schema: NAMES_SCHEMA }),

  () => agent(CONTEXT + `
TASK: Produce a PRECISE inventory of EXISTING tests that exercise partitioning and partition names. Search the
tests/ tree. For each, record kind (real-container integration / in-process integration / unit), which adapter,
exactly what it asserts, and whether it exercises CONCURRENT cross-partition access. Known ones to start from:
tests/Suites/Data/Connector.Mongo/.../Specs/Partition/MongoPartition.Spec.cs,
tests/Suites/Data/Core/.../Specs/Routing/EntityPartitionRouting.Spec.cs,
tests/Suites/Data/Core/.../Specs/Naming/AdapterResolveStorageSpec.cs. Find any others. List concrete coverage gaps.`,
    { label: 'coverage-inventory', phase: 'Names+Coverage', schema: COVERAGE_SCHEMA }),
])

return {
  mongoRace: { confirm, refute, deterministic },
  breadth: breadth.filter(Boolean),
  partitionNames: names,
  coverage,
}
