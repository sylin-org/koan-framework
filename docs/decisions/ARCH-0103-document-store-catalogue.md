# ARCH-0103 Addendum — The Document-Store Family: Ideal-State Catalogue & Golden-Mongo Rebuild Spec

**Status:** Accepted (catalogue / build spec) · **Parent:** [ARCH-0103](ARCH-0103-aodb-adapter-conformance.md) §5 (P3 — the `DocumentStore` family base) · **Date:** 2026-06-25

## 1. Purpose & method

ARCH-0103 P3 builds the `DocumentStore` family base with **MongoDB as the golden reference**. Rather than incrementally patch the accreted adapter, we **rebuild it from scratch against this catalogue** — the documented ideal state of what a Mongo document-store adapter *accomplishes* — **harvesting the dense-correct pieces intact** and **leaving the structural debt behind**.

Method:
1. **Catalogue** (this doc) every responsibility the current adapter discharges: *what · how (where) · the must-preserve invariant (with provenance) · the Koan canon it should follow · base|dialect classification · how Couchbase differs*. Cataloguing Mongo's responsibilities **is** the document-family contract: it yields the `DocumentStore` base (generic rows), the `IDocumentDialect` seam (native rows), and the conformance kit (the testable rows).
2. **Characterize** (Phase 1) any harvested invariant not already pinned by the real-Mongo test suite, so the rebuild cannot silently lose it.
3. **Rebuild** (Phase 2) the *structure* clean; **harvest intact** the *encoders* (`MongoFilterTranslator`, the BSON conventions, `MongoGuidEncoding`) — they are correct, not debt; rewriting them is where knowledge is lost.
4. **Conform + review** (Phase 3); **orchestration cleanup + Couchbase** (Phase 4).

**The fixed oracle:** the existing real-`AddKoan()` Mongo suites (FilterConvergence cross-adapter oracle, `ManagedFieldNoLeak`, the TTL spec, the Web AdapterSurface CRUD/partition specs) + the byte-identity off-proofs (data-core 274, tenancy 104). The rebuild is "done" when these are green and the new conformance cells pass.

## 2. The two settled decisions

- **Base home:** `src/Koan.Data.Core/Document/` (consistent with `KeyValueStore`; the base is vendor-dependency-light — it deals only in `Filter`/`QueryDefinition`/`ManagedFieldWriteScope`/`IAdapterReadiness`/`ActivitySource`/`AdapterNaming`, all already in `Koan.Data.Core` + `Koan.Core.Adapters`). The relational base earns its own assembly because it carries SQL machinery; the document base carries none — the vendor machinery is the dialect.
- **Seam shape:** **abstract base + subclass-implements-primitives** (the `KeyValueStore` pattern), NOT a separate composed `IDocumentDialect` object. `DocumentStore<TEntity,TKey>` is abstract; `MongoDocumentStore<TEntity,TKey> : DocumentStore<TEntity,TKey>, IConditionalWriteRepository<…>, IRawQueryRepository<…>, …` implements the abstract native primitives **and adds its native-only interfaces**. The base never names a vendor container type — **each native primitive resolves its own container from the ambient context internally** (as Mongo's `GetCollection()` already does), so the base stays container-type-agnostic across Mongo's 2-level `db.collection` and Couchbase's 3-level `bucket.scope.collection`. "Dialect" = the subclass's realization of the abstract seam + its harvested helpers.

## 3. The responsibility catalogue

Legend — **Class:** `BASE` (DocumentStore, shared with Couchbase) · `DIALECT` (Mongo-native subclass) · `HELPER` (harvested-intact module) · `DROP` (debt — leave behind). **Inv?** = carries a must-preserve correctness invariant (§4).

### A. Connection, pooling & lifecycle

| Responsibility | Current mechanism (where) | Class | Inv? | Couchbase delta |
|---|---|---|---|---|
| Connect to the server; ping; hold the client | `MongoClientProvider.EnsureDatabase` (lazy connect + ping + readiness transition) | DIALECT | — | `CouchbaseClusterProvider` (cluster + bucket) |
| **One client/pool per source** (not per `(entity,source)`) | **DEBT**: `MongoAdapterFactory.Create` `new`s a provider per non-Default `Create` ⇒ N pools for N entities on one source | BASE (pattern) + DIALECT (provider) | — | identical leak shape; same fix |
| Per-source physical resolution (conn + database/index) = **Database mode** | `AdapterConnectionResolver.ResolveConnectionString` + `GetSourceSetting(…,"Database")` in the factory | BASE (the routing is `RoutedSource`; the factory just resolves placement) | — | per-source bucket/scope; same resolver |
| Dispose cached providers on teardown | absent (providers leak) | BASE (factory `IAsyncDisposable`) | — | same |

### B. Container & naming resolution (= Container mode)

| Responsibility | Current mechanism | Class | Inv? | Couchbase delta |
|---|---|---|---|---|
| Compute the physical container name from entity + **ambient partition** | `AdapterNaming.GetOrCompute<TEntity,TKey>(sp)` (per-op, never a shared field) | BASE | **I1** (partition-race-safe: resolve to a local each op) | identical |
| Naming capability (style, separators, **255-byte namespace clamp**) | `MongoAdapterFactory.GetNamingCapability` (`MaxIdentifierBytes = 255-64-1`) | DIALECT | **I2** | 3-level keyspace; own clamp |
| Resolve the native container handle for the current name | `GetCollection()` → `_collections` cache + `database.GetCollection<T>(name)` | DIALECT | **I1** | `GetCollectionContext(name)` |
| Ensure container exists (+ indexes) | `EnsureReady` / `CollectionExists` / `CreateCollectionAsync` | BASE (orchestration) + DIALECT (native calls) | — | `EnsureCollection` (+ scope + **primary index + online-wait**) |
| Schema-ready gate (don't re-ensure every op) | `_healthyCache`/`_indexCache`/`_schemaLocks` + `BuildServerKey`/`BuildCollectionKey`/`BuildIndexKey` | BASE (**collapse to ONE keyed gate**; `_healthy` ⊇ `_index`) | — | needs the same gate |

### C. Read path

| Responsibility | Current mechanism | Class | Inv? | Couchbase delta |
|---|---|---|---|---|
| Get-by-id (raw; only when unscoped — facade lowers scoped get→query) | `Find(Eq(_id,id)).FirstOrDefault` | DIALECT | — | `Collection.GetAsync(key)` + not-found→null |
| Get-many by id (order-preserving, null-padded) | `Find(In(_id,ids))` + dict re-order | DIALECT | — | parallel `GetAsync` + dict re-order |
| Query: translate filter → native, push sort + pagination, report what was handled | `Query` → `BuildFilter`/`BuildSort` → `Find/Sort/Skip/Limit`; server `CountDocuments` for total | DIALECT (orchestration shape is BASE-uniform) | **I3,I4** | N1QL `SELECT RAW doc … WHERE … ORDER BY … LIMIT/OFFSET`; **RequestPlus** scan-consistency |
| Count (Fast = estimate when unfiltered) | `EstimatedDocumentCount` / `CountDocuments` | DIALECT | — | `SELECT RAW COUNT(*)` |
| **Filter AST → native query** (the dense core) | `MongoFilterTranslator` (whole-filter, never residual; `FilterSupport.Capabilities`) | HELPER (harvest intact) | **I5,I6,I7** | `CouchbaseN1qlFilterTranslator` (own helper; **not** a shared walker — see §3 note) |
| Managed read predicate (`__koan_tenant` eq) pushdown | **no special code** — the facade ANDs `Filter.Eq("__scope",v)` into `query.Filter`; the normal translator pushes it (single-segment field, scalar value) | BASE (facade) + DIALECT (translator handles the leaf) | — | identical |
| Field-name mapping (camelCase + `_id` carve-out) | **DEBT**: `ToCamelCase` in 3 places (`MapFieldName`, `BuildSort`, translator `ToCamel`) | BASE (ONE mapper, injected into the translator) | **I8** | own mapper (`META().id` for id) |

> **§3 note — no generic `FilterAstWalker` for the document family.** ARCH-0103 §5.1 floats a shared `FilterAstWalker<TRendered>`. For documents we **reject** it: the leaf *rendering* dominates and is irreducibly native (Mongo encodes comparands through each field's own `IBsonSerializer` — DATA-0098; Couchbase emits parameterized N1QL). A generic visitor would share the ~10-line And/Or/Not skeleton at the cost of a 6-method render interface — **more parts, not fewer**. Each dialect keeps its whole translator as a harvested helper. (The AST walk *is* shared for relational, where leaves render to one SQL string; documents are the honest exception §5.1 already flags as "partial".)

### D. Write path (the AODB Shared-mode contract)

| Responsibility | Current mechanism | Class | Inv? | Couchbase delta |
|---|---|---|---|---|
| **Managed write composition**: inject = `ManagedFieldWriteScope.Effective`, guard = `.Current` | **DEBT**: duplicated inline in `Upsert` + `UpsertMany` | BASE (one `GuardAndCompose`, mirrors `KeyValueStore.GuardAndSnapshotAsync`) | **I9** | gains it (today Couchbase has **no** write-stamp) |
| Plain upsert (no scope) — byte-identical fast path | `ReplaceOne(Eq(_id),model,{IsUpsert})` | DIALECT | — | `UpsertAsync(key,model)` |
| **Conflict-aware managed upsert** (cross-scope write guard) | `ManagedUpsertOneAsync`: BsonDocument view, inject managed elements, filter `{_id, <guard eqs>}`, `IsUpsert` ⇒ foreign-owned doc fails filter → INSERT same `_id` → **E11000 → reject** | DIALECT (native mechanism) | **I10** | CAS-based: get→guard-eval→replace-under-CAS (`CasMismatch`→reject) |
| Delete by id | `DeleteOne(Eq(_id))` | DIALECT | — | `RemoveAsync(key)` + not-found→false |
| Bulk upsert (native; scope path = per-doc conflict-aware) | `BulkWrite(ReplaceOneModel[])`; under scope ⇒ loop `ManagedUpsertOneAsync` | DIALECT | **I10** | parallel `UpsertAsync` (gains the scope path) |
| Bulk delete | `DeleteMany(In(_id,ids))` | DIALECT | — | parallel `RemoveAsync` |
| Cross-scope reject diagnostic | `CrossScopeWrite(collection,id)` (generic; never names tenant) | BASE (shared message) | **I11** | same message |

### E. Serialization & encoding (harvest intact — the bug-hardened core)

| Responsibility | Current mechanism | Class | Inv? |
|---|---|---|---|
| Global conventions: camelCase elements, enum-as-string, ignore-extra-elements | `ConfigureGlobalMongoDriverSettings` `ConventionPack` | HELPER (Mongo-family init) | **I12** |
| **GUID identity** as native UUID BinData, **per-member only** (declared identity + GUID parent refs); **no global `typeof(string)` override** | `RegisterIdentitySerializers` + `SmartStringGuidSerializer` + `IdentityEncoding.GuidEncodedMembers` | HELPER | **I6** (DATA-0098) |
| **Single source of truth** for string↔Guid wire form (write & query call the same) | `MongoGuidEncoding.IsGuidEncoded/ToBinData` | HELPER | **I7** (DATA-0098: prevents the FK write-BinData/query-string drift that silently lost data) |
| **Comparable encoding**: `DateTimeOffset`→`BsonType.DateTime` (UTC instant; offset dropped), `TimeSpan`→`Int64` (ticks); registered **individually** | `ConfigureGlobalMongoDriverSettings` `TryRegister` | HELPER | **I13** (DATA-0100) |
| `List<string>`/`string[]` elements forced to BSON string (not BinData) | `StringCollectionElementConvention` | HELPER | **I14** |
| Comparand encoded through the **field's own serializer** (`AllMemberMaps`, inc. inherited) | `MongoFilterTranslator.ResolveScalarSerializer` (memoized) | HELPER | **I5** (DATA-0098/0100) |
| `$all` raw-BSON array carve-out (avoid `JObjectSerializer` claiming `typeof(object)`) | `BuildCollection` `HasAll` raw path | HELPER | **I15** |
| Discriminators disabled (no `_t`); `BsonValue` members default `BsonNull` | `NoDiscriminatorConvention` + `NullBsonValueConvention` + `IgnoreExtraElements` | HELPER | **I16** |
| `JObject` ↔ BSON for `[Flexible]`/JObject members | `JObjectSerializer` / `JObjectSerializationProvider` (claims `typeof(object)` — **scope-down candidate**, fragile but load-bearing) | HELPER (review scope) | **I15** |

### F. Schema / index / TTL

| Responsibility | Current mechanism | Class | Inv? | Couchbase delta |
|---|---|---|---|---|
| `[Index]` → index models (field names via the shared mapper) | `BuildIndexModels` (`Ascending(MapFieldName(p))`) | DIALECT | **I8** (index fields must traverse the same camelCase map as filters/sorts — JOBS-0008) | N1QL `CREATE INDEX` (+ primary index) |
| **TTL**: single-field `[Index(Ttl=true)]` → `ExpireAfter = TimeSpan.Zero` | `BuildIndexModels` | DIALECT | **I17** (DATA-0101: `expireAfterSeconds=0`; null/absent never expires) | no native per-field TTL; declares less |
| Idempotent index create (tolerate conflict codes) | `EnsureIndexes` catch `IndexOptionsConflict`/… | DIALECT | — | retry/`IF NOT EXISTS` |

### G. Batch & transactions

| Responsibility | Current mechanism | Class | Inv? | Couchbase delta |
|---|---|---|---|---|
| Batch collect (add/update/delete/mutate) + mutation-replay + `BatchResult` shaping | `MongoBatch` | BASE (skeleton; route through bulk paths so the per-doc guard runs) | **I9,I10** | `CouchbaseBatch` |
| Atomic batch (`RequireAtomic`) | session + `StartTransaction`/`BulkWrite`/`Commit`; not-supported→`NotSupportedException` | DIALECT (native extra) | **I18** (RequireAtomic on a deployment without txns must throw, not silently run non-atomic) | `Transactions.RunAsync` |

### H. Capabilities (co-defined with conformance — §7)

| Responsibility | Current | Class | Couchbase delta |
|---|---|---|---|
| Family floor: `Query.Linq`, `Query.Filter(<translator caps>)`, **`Isolation.RowScoped`** | `Describe` | BASE declares floor; DIALECT supplies the `FilterSupport` detail | identical floor |
| Native extras: `BulkUpsert`/`BulkDelete`/`AtomicBatch`/`FastRemove`/`ConditionalReplace`/`Retention.TtlIndex` | `Describe` | DIALECT adds | Couchbase: `Query.String`, `ConditionalReplace`, bulk, atomic — **no** TtlIndex/FastRemove |
| New: `Isolation.ContainerScoped` + `DatabaseScoped` (ARCH-0103 §6) | absent | BASE declares; conformance kit proves | identical |

### I. Readiness & telemetry (pure boilerplate → hoist)

| Responsibility | Current | Class | Couchbase delta |
|---|---|---|---|
| `IAdapterReadiness`/`IAdapterReadinessConfiguration` surface (10 forwarding members) | `MongoRepository` forwards to `_provider`/`_options` | BASE (forward to abstract `Readiness` provider + options) | identical block |
| Per-op gate + schema-auto-provision | `this.WithReadinessAsync<T,TEntity>(…)` (already a shared extension) | BASE (the base IS the `IAdapterReadiness` the extension inspects) | identical |
| Per-op telemetry Activity + entity tag | `using var activity = MongoTelemetry.Activity.StartActivity("mongo.X"); SetTag` ×~12 | BASE op-template (`Telemetry.StartActivity($"{Verb}.{op}")`); DIALECT supplies `ActivitySource` + verb prefix | identical |

### J. Instructions & clear

| Responsibility | Current | Class | Couchbase delta |
|---|---|---|---|
| `EnsureCreated` / `Clear` dispatch | `ExecuteAsync` switch | BASE (skeleton) + DIALECT (native ensure/clear) | same switch |
| `RemoveAll` strategy resolve (`Optimized`→`Fast`) | inline | BASE (resolve) + DIALECT (drop-recreate vs deleteMany) | Couchbase: N1QL DELETE only |
| Fast remove = drop & recreate collection + re-index | `RemoveAll` Fast path | DIALECT (native extra) | not available |

### K. AODB three-mode realization (the contract this all serves)

| Mode | Mechanism | Class |
|---|---|---|
| **Shared** (FieldFilter) | managed compose (BASE) → conflict-aware upsert (DIALECT, **I10**); read predicate folded by facade, pushed by translator | BASE+DIALECT |
| **Container** (Particle) | distinct native collection per ambient partition via `AdapterNaming` (**I1**) | BASE+DIALECT |
| **Database** (Moniker) | distinct client+database per routed source (`RoutedSource` + per-source pool) | BASE+DIALECT |

### L. Registration / boot / orchestration (peripheral — Phase 4, not the golden core)

| Responsibility | Current | Class |
|---|---|---|
| DI + static BSON config | **DEBT**: two registrars (`KoanAutoRegistrar.ConfigureMongoStaticState` + `MongoOptimizationAutoRegistrar`) with separate locks; `optimizer.Initialize(null!)` hand-invoke; `Console.WriteLine` boot noise | `KoanModule` (ARCH-0086) — ONE idempotent module: `Register` (DI) + a once-guarded Mongo-family static init + `Report` |
| Connection-string build/merge (multi-host) | **DEBT**: 4 copies (`MongoOptionsConfigurator`/`MongoDiscoveryAdapter`/`MongoOrchestrationEvaluator` ×2) | ONE `MongoConnectionString` helper |
| Boot-report connection display | **DEBT**: spins up a throwaway `MongoDiscoveryAdapter` to re-discover | reuse the resolved value |
| "Available providers" boot note | **DEBT**: bespoke assembly scan | DROP (composition lockfile already reports it) |
| ZenGarden binding | **DEBT**: two bindings for the `mongo`/`mongodb` alias | one binding, alias from `CanHandle` |
| `SimpleOptionsMonitor<T>` | per-adapter hand-roll | hoist a shared `StaticOptionsMonitor<T>` to `Koan.Core.Adapters` |

## 4. Must-preserve invariants register (the harvest)

Each is a closed bug or a correctness contract. The rebuild MUST preserve it; Phase 1 adds a characterization test for any not already pinned.

| # | Invariant | Provenance | Pinned by |
|---|---|---|---|
| **I1** | Container resolved to a **local** per op from the ambient partition (never a shared mutable field) — concurrent partitions must not cross | partition-race fix | Web AdapterSurface concurrent-partition spec |
| **I2** | Collection name clamped to the 255-byte namespace budget (reserve 64+1 for db) — overflow hashed injectively | naming | **`MongoNamingSpec`** |
| **I3** | Query reports exactly the sort/pagination it pushed (residual finished by coordinator) | unified query contract | FilterConvergence + query specs |
| **I4** | Query never falls back / never re-throws translation failures (the killed 500) | DATA-XXXX | FilterConvergence |
| **I5** | Comparand encoded through the field's **own** serializer (`AllMemberMaps`, inc. inherited base members) so write↔query never drift | DATA-0098/0100 | FilterConvergence + `MongoIdentityEncodingMatrix` |
| **I6** | GUID identity ⇒ native UUID BinData, **per declared member only**, NO global `typeof(string)` override | DATA-0098 | `MongoIdentityEncodingMatrix` + CRUD |
| **I7** | `MongoGuidEncoding` is the **single** write/query source of truth (a Guid-parseable string ⇒ BinData on both paths) — else FK predicates silently match nothing and delete-when-empty loses data | DATA-0098 (data-loss bug) | **`MongoGuidStringFilterSpec`** |
| **I8** | `[Index]`/sort/filter field names traverse the **same** camelCase+`_id` map (else indexes are uncovered → blocking in-memory sort) | JOBS-0008 | `MongoFilterWireShape` (field-name); verify index-coverage during rebuild |
| **I9** | Managed values = `Effective` for inject (isolation ∪ operation override, e.g. soft-delete `__deleted`), **`Current` only** for the conflict guard | ARCH-0101 §4 | `ManagedFieldNoLeak` + tenant soft-delete |
| **I10** | Cross-scope write rejected: a foreign-owned doc cannot be overwritten by id (E11000 path) | DATA-0105 §3b | `ManagedFieldNoLeak` |
| **I11** | Reject diagnostic is generic (names entity/id, never the tenant/axis) | ARCH-0101 | `ManagedFieldNoLeak` |
| **I12** | camelCase elements + enum-as-string + ignore-extra-elements global | conventions | CRUD specs |
| **I13** | `DateTimeOffset`→DateTime(UTC), `TimeSpan`→Int64(ticks), registered individually (one failure can't skip the rest) | DATA-0100 | DATA-0100 comparable-encoding specs |
| **I14** | `List<string>` elements stay BSON strings (not BinData) — array containment + round-trip | DATA-XXXX | `MongoFieldTransformRoundTripSpec` / identity-matrix; verify Has/HasAll during rebuild |
| **I15** | `$all` (and JObject) avoid the `typeof(object)` serializer hazard | DATA-0098 | filter collection-op specs |
| **I16** | No `_t` discriminator written; `BsonValue` members default `BsonNull` | conventions | CRUD specs |
| **I17** | TTL `[Index(Ttl)]` ⇒ `expireAfterSeconds=0`; null/absent value never expires | DATA-0101 | Mongo TTL spec |
| **I18** | `RequireAtomic` on a non-transactional deployment throws (never silently non-atomic) | batch contract | batch atomic spec |

## 5. The base/dialect split (the rebuild target)

**`DocumentStore<TEntity,TKey>`** (abstract, `Koan.Data.Core/Document/`) — implements `IDataRepository`/`IQueryRepository`/`IDescribesCapabilities`/`IInstructionExecutor`/`IAdapterReadiness`/`IAdapterReadinessConfiguration`; owns: the readiness+telemetry **op-template**, the **managed write composition** (I9) + cross-scope diagnostic (I11), the **schema-ready gate** (one keyed cache+lock), `RemoveAll` strategy resolution, the instruction + batch **skeletons**, the **capability floor** (RowScoped/Linq/Filter + ContainerScoped/DatabaseScoped). Abstract native seam (no vendor types): `EnsureContainerAsync`, `FindByIdAsync`, `FindManyAsync`, `QueryNativeAsync`, `CountNativeAsync`, `UpsertOneNativeAsync(model,inject,guard)`, `UpsertManyNativeAsync(models,inject,guard)`, `DeleteOneNativeAsync`, `DeleteManyNativeAsync`, `ClearNativeAsync(strategy)`, `DescribeBackend`, `Readiness`/`Telemetry`/`Verb`.

**`MongoDocumentStore<TEntity,TKey>`** (the golden DIALECT) — implements the seam over `IMongoCollection`; adds `IConditionalWriteRepository` (CAS), `IBulkUpsert`/`IBulkDelete`; harvests **intact** `MongoFilterTranslator`, the BSON conventions, `MongoGuidEncoding`, `BuildIndexModels`/TTL, drop-recreate fast-remove, the transaction batch. Target: a dialect of native primitives, ~250–320 LOC (from ~846), translator ~290 LOC unchanged.

## 6. The bad influence left behind (do NOT carry)

Reflection fallback for `collection.Database`; `_ = nameResolver` no-op; commented-out logs; `Console.WriteLine` from a library; the per-`(entity,source)` pool leak; the dual static registrars + `Initialize(null!)`; the 4× connection-string surgery; the re-discovery boot-report; the "available providers" scan; the dual ZenGarden bindings; the three overlapping schema caches + key-builders; the three `ToCamelCase` copies.

## 7. Conformance pulled forward (P5 → now)

Define, with Mongo as the first green cell: capability tokens **`DataCaps.Isolation.ContainerScoped`** + **`DatabaseScoped`** (co-defined with their checks — a token can't exist without its conformance, so over-claim fails green, ARCH-0094); and **`AodbConformanceSpecsBase`** proving, through real `AddKoan()`: **Shared** (`ManagedFieldNoLeak` oracle), **Container** (distinct collection per partition + `__`-name survival), **Database** (per-source physical routing + fail-closed on unconfigured). Couchbase and the other families reuse the kit.

## 8. Phasing & gates

- **P0** — this catalogue (done).
- **P1** — **satisfied by the existing oracle.** The Mongo suite already pins the harvested invariants through 18 real-`AddKoan()` specs: `MongoNamingSpec` (I2), `MongoGuidStringFilterSpec` (I7 — the DATA-0098 data-loss regression), `MongoIdentityEncodingMatrix` (I5/I6), `MongoComparableEncoding` (I13), `MongoFilterWireShape` (I8/I15), `MongoFilterConvergenceSpec` (I3/I4/I5), `MongoManagedFieldNoLeakSpec` (I9/I10/I11), `MongoFieldTransformRoundTripSpec` (I14), the TTL/batch/partition-concurrency specs (I17/I18/I1). The rebuild is protected by this fixed oracle; the only residual to confirm during the rebuild is index-coverage for I8 and `Has`/`HasAll` for I14.
- **P2** — `DocumentStore` base + `MongoDocumentStore` (translator/conventions harvested intact; per-source pool; `KoanModule`). Gate: Mongo connector + Web AdapterSurface Mongo green (Testcontainers) + data-core 274 / tenancy 104 byte-identical.
- **P3** — `ContainerScoped`/`DatabaseScoped` tokens + `AodbConformanceSpecsBase` green on Mongo + Database-routing spec; adversarial review; commit.
- **P4** — orchestration/discovery/boot-report/connection-string cleanup (`KoanModule` + one helper); then **Couchbase** folds onto the proven base (gains the write-stamp I9/I10 + per-source). **Deferred items recorded here (P2 verification, 2026-06-25):**
  - **Discovery enhancement — add a `host.docker.internal` candidate.** The runtime discovery sequence (`ServiceDiscoveryAdapterBase.BuildDiscoveryCandidates`, verified byte-identical across the P2 rebuild) probes, in priority order: `MONGO_URLS` env → Aspire (AspireAppHost) → explicit config → **in-container:** `mongodb://mongo:27017` (the compose service, `[KoanService] Host`) then `mongodb://localhost:27017` (local-fallback) / **standalone:** `mongodb://localhost:27017`. There is **no** `host.docker.internal` candidate today — so a containerized app cannot auto-reach a Mongo on the Docker *host*. Add `host.docker.internal:{EndpointPort}` as a candidate between the compose-service and `localhost` fallback (architect-approved enhancement, not a rebuild regression — it was never present).
  - **Pre-existing quirk — non-Default source with `ConnectionString="auto"`.** `AdapterConnectionResolver` returns the literal `"auto"` (or throws) for a non-Default source relying on runtime discovery; it does **not** receive the discovery-resolved Default string, so the factory's `baseOptions.ConnectionString` fallback is near-dead-code and the new per-source provider pool would key on an invalid string. Unchanged by the rebuild; fix when consolidating connection-string resolution.
