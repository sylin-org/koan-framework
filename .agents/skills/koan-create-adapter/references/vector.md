# Vector-adapter playbook

Authority: the vector plane's own code (`Koan.Data.Vector.Abstractions`, `Koan.Data.Vector`), the
shared oracle, and the assessed HTTP exemplar **Qdrant** (`src/Connectors/Data/Vector/Qdrant/`,
proven by `Koan.Data.VectorAdapterSurface.Qdrant.Tests`). This playbook was written first from the
exemplar and corrected while building **Chroma** (`src/Connectors/Data/Vector/Chroma/`); the
corrections are at the bottom. The data playbook's general rules (package mechanics, truth gates,
AOT, staging discipline) apply unchanged; this file covers only the vector seam.

## The oracle

`tests/Suites/Data/VectorAdapterSurface/Koan.Data.VectorAdapterSurface.TestKit/`:

- `VectorAodbConformanceSpecsBase` — the suite you subclass. It proves all three AODB isolation
  modes end-to-end (Shared overlay, Container partition, Database shard) and that the decorator
  *declares* what it realizes (`Declares_realized_isolation_modes`, G-09). RowScoped is
  fail-closed-co-defined: declared ⇒ the overlay must isolate a kNN; undeclared ⇒ a scoped read must
  throw `NotSupportedException`. Container + Database are **Required** for every vector adapter —
  they are the name-fold floor, realized by naming, not by provider features.
- The **annex**: 24 framework cells V-01..V-24 registered as proof seams. The base's
  `ProveVectorAnnexCellAsync` skips loudly. Your spec overrides it with a switch that maps each
  acceptance id to a real provider proof — or **declines** an earned-but-unclaimed cell with a
  reason (Qdrant declines V-12 Eventual, V-14 Hybrid, V-15 named spaces, V-16 continuation,
  V-18 atomic batch, V-19 export; declining with reasons is conformant, silent skips are not).
- Hosting: the conformance host is a real `AddKoan()` host via
  `KoanIntegrationHost.Configure().WithSettings(...)` — NOT the record plane's fake-service
  pattern. One adapter must be registered (`IVectorAdapterFactory` exactly one — the misroute
  check). A pure vector store also needs a record store for V-22 (reference `Koan.Data.Connector.Json`
  — the Data pillar floor registers no vector adapter). Entities come from the kit
  (`VectorConformanceTenantDoc/PartitionDoc/ShardedDoc` + the shard axis); declare your own
  metric/readonly/external docs like Qdrant's `QdrantEuclideanDoc` et al.
- Source settings the host needs: `Koan:Data:Sources:<Source>:Adapter` for the conformance source,
  both shard sources, a ReadOnly source (`Access=ReadOnly`), and an External source
  (`StorageLifecycle=External`) — V-20 proves the policy matrix against them.
- `VectorAodbConformanceFixtures` / `VectorConformanceShardAmbient` are discovered by the host —
  do not re-declare them; reference the TestKit project only from the test project.
- `[assembly: CollectionBehavior(DisableTestParallelization = true)]` and
  `[assembly: Xunit.AssemblyFixture(typeof(<Your>TestFactory))]` — one container per assembly.
- A `SettleAsync` hook exists for async-index stores (Weaviate). Chroma/Qdrant are
  synchronous-after-write (`?wait=true`, sync server-side); leave the default no-op only if a real
  run proves read-your-writes — never assume it.

## What the seam gives you (and what it doesn't)

- The adapter implements `IVectorSearchRepository<TEntity,TKey>` (the ratified contract:
  `Save`/`Get`/`Delete`/`Search`/`Clear`/`Sync`/`VectorEnsureCreated` over `VectorScope`) plus
  `IDescribesCapabilities`. Legacy surface (`Upsert`, `Search(VectorQueryOptions)`,
  `GetEmbeddings`, `ExportAll`, `Flush`) has default throws — Qdrant implements only `GetEmbedding`
  (by-id embedding read) and declines the rest; inherit the defaults rather than inventing semantics.
- The **decorator does the isolation work** (`Koan.Data.Vector/ScopedVectorRepository.cs`): it
  stamps managed discriminators into metadata on write, composes the read-scope predicate into
  `request.Filter` before your adapter sees it, and fails closed when you don't announce
  `VectorCaps.Filters` but a scoped read arrives. Your obligations: honor `request.Filter`
  natively, fold `scope.Identity` into the stored id, apply `scope.Predicate` on by-id fetch and
  `Clear`, and stamp every point so the overlay fields land in the filterable index.
- **Isolation is the name-fold floor.** `VectorAdapterNaming.GetOrCompute<TEntity>(services,
  factory, plan.Source)` folds entity type + ambient partition + routed source into ONE collection
  name. Never pin a static collection name (it defeats all three discriminators). Your factory's
  `GetNamingCapability` declares charset policy — copy Qdrant's (`EntityType`, `_`, AsIs, Guid-N
  partition tokens).
- **Visibility**: declare only Session unless the provider can honor a bounded visibility barrier.
  Qdrant realizes Session with `?wait=true` and refuses Eventual at factory level
  (`plan.Visibility != Session` → `NotSupportedException`). Chroma is synchronous — same posture.
- Search is **approximate kNN**; report `VectorSearchAccuracy.Approximate`, the plan metric, and
  `CandidatesConsidered: null` unless the provider returns a real candidate count. Deterministic
  order under score ties is YOURS to provide: request extra candidates, re-rank client-side by
  (similarity desc, stable id asc) with a doubling loop bounded by `MaxSearchCandidates`
  (Qdrant's `Search` is the reference implementation).

## Storage mapping rules (HTTP stores with opaque ids)

- **Storage id**: Koan keys are strings/ints/Guids; HTTP stores may demand uuids. Qdrant maps
  Guid-like keys → themselves, everything else → a deterministic UUIDv5 of the key (and of
  `"scope:<identity>\u001f<key>"` when scope.Identity is set). Copy the scheme — including the
  fixed namespace Guid and the original key riding in the payload (`__koan_id`) so reads return the
  caller's id, never the storage uuid.
- **Metadata dual-write**: neutral metadata (`VectorMetadata.ToJson`) preserves full fidelity
  (nested objects, arrays, typed scalars via the `__koan_type` tagging) for round-trip; a flat
  **index projection** of the same metadata (top-level members, store-native scalars) makes
  filters pushable. Write both; read fidelity from the neutral blob. Reject user metadata keys
  under the reserved `__koan_` prefix before provider I/O (the materializer throws
  `InvalidOperationException` — V-06 pins it).
- Payload bookkeeping (Qdrant): `__koan_id` (original key), `__koan_scope` (scope identity),
  `__koan_norm` (cosine denormalization — Qdrant stores unit-normalized vectors), `__koan_metadata`
  (neutral blob), `__koan_index` (flat filter projection). Keep the same wire names.
- **Upsert outcomes are existence truths, not wire receipts.** HTTP upserts return nothing about
  inserted-vs-updated, and delete counts can be unreliable. Pre-fetch the ids (by-id get) before
  the mutation and derive `Inserted/Updated/Deleted/Missing` from what existed — V-03/V-04/V-17
  hold you to this.
- **EnsureShape**: GET the collection (404 = absent), validate metric (and dimension where the
  store exposes it) against the plan, create on demand under the `SchemaOrAdmin` policy effect, and
  re-validate after create. A collection that exists with the wrong shape is a hard
  `InvalidOperationException` (provision it correctly or route elsewhere) — never silently reuse.
- Read-your-writes: make every awaited mutation synchronous at the wire (`?wait=true` or a
  synchronous endpoint). If the store can't, you must implement `SettleAsync`-style visibility
  proofs or decline Session with a reason — silent eventual reads are the failure the oracle hunts.

## Filter pushdown truth (V-13)

- Translate the `Filter` AST into the store's native where-language; declare exactly the operators
  you proved against the live store, via `FilterSupport.Uniform(...)` on the
  `VectorCaps.Filters` capability. Anything the store cannot express →
  `VectorFilterUnsupportedException` **before provider I/O** (V-13's "or fails closed").
- Qdrant's proven set: Eq, Ne, Gt/Gte/Lt/Lte, In, Nin, Has, HasAny, HasAll, HasNone, Size, Exists
  (true/false), AllOf/AnyOf/Not — with nested `__koan_index.<path>` keys and no ignore-case.
- Probe absent-key semantics against the neutral evaluator
  (`Koan.Data.Abstractions.Filtering.DictionaryFilterEvaluator`) before claiming: Ne/Nin match
  absent (locked); ranges don't. A store whose native semantics diverge from the evaluator for an
  operator you cannot reconcile **declines that operator** — the convergence corpus compares your
  store against the evaluator case-by-case and any divergence is red.
- Type-hostile stores fail closed per value, not per operator: a store that rejects string ranges
  server-side (Chroma) declines range operators **on non-numeric filter values** but keeps them for
  numerics, and its spec corpus proves the numeric path.

## Similarity, metric, execution truth (V-07/V-08/V-10)

- Know the store's raw distance definition and pin it in the spec with numbers (Qdrant: cosine
  similarity ∈ [-1,1] → `(s+1)/2`; Euclid distance → `1/(1+d)`; Dot → sigmoid). Chroma: cosine
  **distance** = 1−sim; l2 = **squared** L2; ip = 1−inner-product. Normalize into [0,1],
  higher-is-closer, finite, monotonic — and use the identical mapping for `MinimumSimilarity`
  threshold pushdown (or post-filter residual if the store has no threshold, requesting enough
  candidates to make the residual honest).
- V-01 (space plan) and V-09 (space integrity): dimension/finite/zero-magnitude validation before
  provider I/O (`ArgumentException`), cross-space query requests rejected with the available space
  named (`InvalidOperationException`). V-02: empty/NaN/Infinity/zero-magnitude rejected before I/O,
  nothing persisted.

## Lifecycle, policy, failure (V-20/V-22/V-23)

- `EnsureCreated`/`Sync`/`Clear` flow through source policy: ReadOnly/External sources reject
  mutating calls with `DataSourcePolicyException` (`_route.Policy.Demand(...)` before provider I/O
  — copy Qdrant's demand sites exactly: write on save/delete/clear, schema on create).
- V-22: a cross-store `SaveWithVector` inside a transaction must report partial-commit truth — the
  framework message "does not claim cross-store transaction atomicity" is the expected throw; your
  adapter need do nothing except not lie.
- V-23: cancellation honored before I/O (`OperationCanceledException`), durability proven across a
  container restart, and a disposed repository throws `ObjectDisposedException` on use.
- V-24: warm-path budget (16 save/get/search cycles under 15s / 64MB allocations) — a per-request
  HttpClient with keep-alive passes trivially; anything channel-per-call will not.

## Provider specifics — Chroma 1.5.9 (probed 2026-08-29, chromadb/chroma:1.5.9)

- REST v2 under `/api/v2/tenants/default_tenant/databases/default_database` (the tenant/database
  prefix is REQUIRED — bare `/api/v2/collections` 400s with a CRN validation error).
- Readiness: `GET /api/v2/heartbeat`. The image has **no HEALTHCHECK** — wait on the endpoint, not
  the default Testcontainers strategy.
- Collections: `POST /collections` `{name, configuration:{hnsw:{space}}, get_or_create:false}`;
  409 "already exists" on conflict → GET and validate. `GET /collections/{name}` → 404 or the
  collection (id, `configuration_json.hnsw.space`, `dimension`). `DELETE /collections/{name}` → 200.
- **Item routes address the collection UUID only** (`/collections/{id}/upsert|get|query|delete|count`)
  — the name 400s with "Collection ID is not a valid UUIDv4". Resolve id by name and refresh on 404
  (deleted-and-recreated collections change id).
- `hnsw.dimensions` is **not a create field** (422); the dimension pins from the first upsert and is
  then visible as `dimension`. Shape validation therefore checks `space` always, `dimension` when
  non-null.
- `count` is **GET**; `upsert/get/query/delete` are POST. Upsert returns `{}` (no outcome truth —
  pre-fetch). Get drops missing ids silently (positional mapping is adapter work). `deleted` counts
  in delete responses are unreliable — derive outcomes from pre-fetch existence.
- Query: `n_results` larger than the collection is fine (returns everything). Include
  `metadatas`+`distances`; distances null otherwise. Empty collection → empty arrays, not an error.
- Filter language (`where`): operator dicts are single-key (`{"field":{"$eq":v}}`) or logical
  `$and`/`$or` (single-element allowed); **multi-key implicit AND is rejected server-side** — always
  emit explicit `$and`. Provable set: Eq/Ne/Gt/Gte/Lt/Lte/In/Nin/AllOf/AnyOf. Declined:
  Not (no `$nor`; complements diverge on absent keys), Has*/Size (arrays storable but unfilterable),
  Exists (no operator), `Eq(null)`/`$eq:null` (400), nested paths (dotted keys are literal and
  silently match nothing — fail closed instead), ignore-case. Range ops are **numeric-only
  server-side** (string ranges 400) — accept them for numeric comparison values, decline otherwise.
- Metadata values: str/int/float/bool scalars (+ arrays storable, unfilterable; nested objects 422).
  The index projection maps Guid→"D", dates→"O", TimeSpan→"c", byte[]→base64, decimal→number — the
  same conversion on filter values so pushdown matches.
- **Never write `null` metadata entries** — always `{}`: a server WAL bug around collection
  deletion leaked a deleted collection's last metadata into another collection's null-metadata
  upsert (observed live, 2026-08-29). `{}` entries are clean.
- Clear: delete-by-ids can't express "all" (empty `where` → 400); delete by `where` with the
  reserved-key always-true predicate (`__koan_id $ne ""`) combined with any scope predicate.
- Cosine distance `1−sim` ∈ [0,2] → normalize `((1−d)+1)/2`; l2 returns **squared** L2 → `1/(1+d)`;
  ip returns `1−inner-product` → sigmoid over `inner-product = 1−d`.
