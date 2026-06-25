# ARCH-0103: AODB adapter conformance — the family-base realization contracts

**Status**: Accepted (Enterprise Architect, 2026-06-25). The fleet mandate is canon; the contracts below are ratified in shape; per-family/per-adapter implementation is phased in §7.
**Date**: 2026-06-25
**Deciders**: Enterprise Architect
**Scope**: Define the adapter-side **realization contracts** for the Access Overlay Definition Block (ARCH-0102), and mandate that **every Koan-shipped data adapter implements all three AODB modes** (Shared, Container, Database). ARCH-0102 establishes *what the framework composes and pushes down*; this decision establishes *how an adapter realizes it*, and converges the multi-generational per-adapter divergence onto **storage-model family bases** so each mode is implemented once per family, not bespoke-or-skipped per adapter.
**Related**: **ARCH-0102** (the AODB — this is its adapter-realization companion) · **ARCH-0094** (Adapter Forge / the Conformance Gate — the capability-token-co-defined-with-its-check model used here) · **ARCH-0084** (the capability model — `IDescribesCapabilities`, `DataCaps`) · **ARCH-0079** (integration tests as canon — every adapter ships a real-boot conformance spec) · **DATA-0105/0106** (managed-field stamp / read-filter contributor — the Shared-mode sources) · **[koan-design-principles]** (conformity-by-design; fail-closed; fewer-but-more-meaningful-parts) · **[contributor-pipelines-never-bespoke]**.

---

## 1. The mandate (canon)

**All Koan-shipped data adapters MUST implement all three AODB modes.** The realization bar is **native-or-emulated, no exceptions**: an adapter realizes each mode by its backend's native mechanism where one exists, and by framework-uniform emulation where it does not. There is no honest-fail-closed exemption — "the adapter doesn't do this mode" is not a shippable state. The 15 shipped data adapters in scope:

- **Relational**: Sqlite, Postgres, SqlServer
- **Document/KV**: Mongo, Couchbase, Redis, Json, InMemory
- **Vector**: Weaviate, Qdrant, Milvus, SqliteVec, InMemoryVector, ElasticSearch, OpenSearch

This supersedes the capability-gated "no capability lies" reading (which permitted an adapter to decline a mode and fail closed). Capability tokens remain — but now as a *conformance ledger* (every adapter declares all three, and the conformance kit proves each), not as an opt-out.

**No mode deferral (binding).** "This backend can't really do mode X, so defer it" is rejected for every adapter — the realization is *native-or-emulated*, and the three modes the fleet audit flagged as awkward are all feasibly realizable (and were re-confirmed in the 2026-06-25 feasibility evaluation): **InMemory Shared mode** via an object-sidecar (`(entity, managedValues)`) read by the in-memory filter evaluator; **Couchbase Database mode** via a per-source cluster provider; **vector Container mode** via class/collection-per-particle name-mangling as the universal floor, with native multi-tenancy (Weaviate tenants / Qdrant collections / Milvus partitions) as a per-adapter enrichment behind the same contract. A feasibility evaluation that recommends deferring a mode is a planning aid, not a license — the mandate holds.

**No technical debt carried.** The rebuild harvests what is reusable and rebuilds the rest; it does not preserve cruft. The audit named the debt to discard (raw-POCO serialization paths that skip managed fields; un-wired capability claims; the Mongo legacy-config migration shims; per-adapter copy-paste of health/telemetry/registration) — none of it survives into the family bases.

## 2. The debt this clears (the 2026-06-25 fleet audit)

The modes are realized **three different ways or not at all**, the classic multi-generational accretion:

1. **Two divergent factory contracts** — `IDataAdapterFactory.Create(sp, source)` vs `IVectorAdapterFactory.Create(sp)` (no `source`). The vector family **structurally cannot route**, and `VectorService` never consults `EntityContext.Source`/`DatabaseRouteRegistry` — so a Database-mode tenant axis routes the row write to the tenant DB while the embedding silently lands in the shared store (**split-brain isolation**).
2. **Three write-stamp mechanisms** — the read-side fold is generic in `RepositoryFacade`, but the Shared-mode *write*-stamp diverges: relational via a `System.Text.Json`/Newtonsoft contract resolver, Mongo via explicit BSON injection, and **Json/Couchbase/Redis/InMemory inject nothing** (they serialize the raw POCO; the managed field is lost). Those four also don't declare `RowScoped`, so today they *fail closed* — safe, but non-isolating, which the mandate forbids.
3. **Container mode declared-but-never-wired across all 7 vector adapters** (native multi-tenancy unused).
4. **Overlay naming applied only to Weaviate** — Qdrant/Milvus/ElasticSearch/OpenSearch may silently drop `__`-prefixed metadata fields (the un-propagated Phase-0 bug; a live silent-isolation-breach risk).
5. **No `ContainerScoped`/`DatabaseScoped` capability tokens and no conformance kit** — "compliant" is unmeasurable.

## 3. The framing — three layers, three contracts

The AODB already names one element kind per layer of the data path. Each becomes one realization contract with a clean *framework-computes / adapter-realizes* seam:

| AODB element | Mode | Layer | Framework provides | Adapter realizes |
|---|---|---|---|---|
| **Moniker** | Database | **connection** (factory) | the routed `source` key | a repo bound to that source's physical store |
| **Particle** | Container | **container** (table/collection/index) | the ambient name particles | a distinct physical container per particle-set |
| **FieldFilter** | Shared | **record** (per-row) | managed-field *values* + the read *filter* | persist the values as first-class **filterable** fields |

## 4. The contracts

### 4.1 Moniker → source-routed creation (one factory contract)

A **marker base** unifies the two factory interfaces at the discovery/naming/routing surface; the `Create` signatures align to `(sp, source)`. The marker lives in the foundation assembly (`Koan.Data.Abstractions`, no downstream deps); the two `Create` surfaces stay specialized because their return types (`IDataRepository` vs `IVectorSearchRepository`) live in different assemblies — a single generic interface returning both would cycle. The two `Create` methods differ *only* by their inherent return type; the contract (discovery, naming, source-routing) is one.

```csharp
// Koan.Data.Abstractions — the foundation marker (no cycle: nothing downstream is referenced).
public interface IAdapterFactory : INamingProvider
{
    bool CanHandle(string provider);
}

// Koan.Data.Abstractions
public interface IDataAdapterFactory : IAdapterFactory
{
    IDataRepository<TEntity, TKey> Create<TEntity, TKey>(IServiceProvider sp, string source = "Default")
        where TEntity : class, IEntity<TKey> where TKey : notnull;
}

// Koan.Data.Vector.Abstractions — gains the source parameter (the asymmetry fix).
public interface IVectorAdapterFactory : IAdapterFactory
{
    IVectorSearchRepository<TEntity, TKey> Create<TEntity, TKey>(IServiceProvider sp, string source = "Default")
        where TEntity : class, IEntity<TKey> where TKey : notnull;
}
```

**The routing decision is one primitive**, `RoutedSource.Resolve<TEntity>()` (`Koan.Data.Core.Routing`): an explicit `EntityContext.Source` wins, else a Database-mode `[DataAxis]` route (`DatabaseRouteRegistry`), else null. **Both** `AdapterResolver` (record) and `VectorService` (vector) resolve through it, so a Database-mode axis routes both planes to the same source — the split-brain is closed by construction. Per-source physical resolution stays uniform (`AdapterConnectionResolver.ResolveConnectionString`), reused by vector. **`ResolveFactory<TFactory>`** (a shared `[ProviderPriority]` + `CanHandle` ranking helper) replaces the duplicated ranking in `AdapterResolver`/`VectorService`.

### 4.2 Particle → container resolution

One contract — *given the ambient particles, return the physical container* — with two sanctioned realizations the family base selects:

- **Name-mangling** (the existing `StorageNameGenerator` → `INamingProvider.ResolveStorage`): the particle becomes a name segment. Works for every backend; the universal floor for Container mode.
- **Native container** (Mongo collection · Couchbase scope · Weaviate/Qdrant/Milvus native multi-tenancy / collection): the family base, when the backend declares it, routes to the native container instead of name-mangling.

The overlay-naming rule (`OverlayNamingRule`/`IOverlayNamingAware`, ARCH-0102 Phase 0) rides this contract: a backend that reserves identifier characters declares its rename once, applied at write/read/index from the one path. Container compliance for an adapter = *its containers are physically distinct per particle-set, and `__`-names survive its naming (or are renamed injectively)*.

### 4.3 FieldFilter → managed-record persistence

The read half is already uniform (filter translation). The write half is the contract: **managed fields are persisted as top-level, filterable fields, and a cross-scope write is guarded.** This converges to **one mechanism per serialization family**, not per adapter:

- **JSON-text family** (relational JSON column · Redis value · Json file): the **shared managed-field JSON injector** (lifted from relational's `ManagedFieldContractResolver`, which is already serializer-level and storage-agnostic) reads `ManagedFieldWriteScope` and injects the ambient managed values into the serialized JSON. Redis/Json route their serialization through it and gain the write-stamp; their existing in-memory read-filter then matches it (the read was always applied — it had nothing to match).
- **BSON family** (Mongo): a BSON serialization hook (Mongo already injects explicitly; formalize it as the family's injector).
- **Object-graph family** (InMemory): a sidecar — the store holds `(entity, managedValues)` so the in-memory filter evaluator reads managed values without mutating the POCO.

The **cross-scope write guard** (no tenant-A takeover of a tenant-B row) is the family base's: relational does it via the `ManagedUpsert` conflict `WHERE`; the KV/document bases do it via read-existing-managed-then-conditional-write (or a native CAS).

## 5. The convergence — storage-model family bases

The three contracts are implemented **once per family**; concrete adapters supply only backend primitives (the relational pattern, already proven):

| Family base | Adapters | Shared logic (the base) | Backend primitives (the seam) |
|---|---|---|---|
| **`RelationalStore`** (exists: `Koan.Data.Relational`) | Sqlite · Postgres · SqlServer | schema orchestrator · filter→SQL translator · managed-field JSON injector · naming · managed-upsert skeleton | `ILinqSqlDialect` · `IRelationalDdlExecutor` · `IRelationalStoreFeatures` |
| **`DocumentStore`** (new) | Mongo · Couchbase | managed-field injector (BSON/JSON) · filter→native-query translator · naming · container resolution (native collection/scope) · write guard | a `IDocumentDialect` (doc-path expr, array ops) · a doc DDL/collection executor · connect-per-source |
| **`KeyValueStore`** (new) | Redis · Json · InMemory | managed-field injector (shared JSON / object sidecar) · in-memory filter application · naming (keyspace/file/store per particle) · write guard | get/set/scan primitives · connect-per-source (or in-proc emulation) |
| **`ScopedVectorRepository`** (exists, extend) | the 7 vector adapters | already centralizes Shared (metadata-filter + overlay rename); **extend to drive Container (native MT / name) + Moniker (per-source)** | the per-adapter vector repo (write-point, search, filter-translate) |

**Net:** 15 adapters re-deriving (or skipping) 3 modes → **4 family realizations** of 3 contracts + thin backend primitives. This is the "fewer but more meaningful parts" payoff: the modes have **one** home per family, not N divergent ones.

### 5.1 The helper-module layer (above the family bases)

The family bases themselves stay thin by composing a layer of **storage-agnostic helper modules** — the cross-cutting primitives every base reuses, so a mechanism is written once for the whole fleet, not once per family:

- **`ManagedFieldJsonInjector`** — the Shared-mode write-stamp for every JSON-serializing store. Lifted from relational's `ManagedFieldContractResolver` (already serializer-level); shared by `RelationalStore` (JSON column) and `KeyValueStore` (Redis/Json values). Routing Redis/Json serialization through it persists the managed field, and their existing in-memory read-filter then matches it — the four "no write-stamp" adapters are fixed by *reuse*, not new bespoke code.
- **`FilterAstWalker`** — the shared `Filter`-AST traversal (`AllOf`/`AnyOf`/`Not`/`FieldFilter` dispatch + `FieldPathResolver` + `FilterValueConverter`); each backend dialect renders only the leaves. This is why filter sharing is *partial but real*: the walk is shared, the render (SQL / MQL / N1QL / GraphQL / REST-JSON / native-string) is per-dialect (~70 reusable LOC/adapter, honestly not the whole translator).
- **`HttpVectorStore`** — the HTTP+JSON vector lifecycle (ensure-collection / upsert / KNN-search / export / delete), generalized from the proven `SearchEngineVectorRepository` (ES + OpenSearch already share it). Absorbs the Weaviate/Qdrant/Milvus boilerplate behind an `IVectorDialect` seam.
- **Already shared, reused as-is** — `StorageNameGenerator` (Container naming), `AdapterConnectionResolver` (per-source physical resolution), `InMemoryFilterEvaluator`/`DictionaryFilterEvaluator` (the convergence oracle), `ScopedVectorRepository` (the vector Shared-mode decorator), `RoutedSource` (the one routing decision).

### 5.2 Feasibility evaluation (2026-06-25) — the validated payoff

A quantified fan-out evaluation across the four families confirmed the direction is both desirable and feasible; the current adapters carry **40–90% removable weight**:

| Family | Current ~LOC | Achievable | Per-adapter floor | Lever |
|---|---|---|---|---|
| Relational (3) | ~6,940 | base grows + ~300–700/adapter | ~250–350 (dialect+DDL+features) | the base under-absorbs today — harvest ~600 LOC of copy-paste |
| Document (2) | ~6,260 | ~1,645 (−74%) | ~320 / ~400 | new `DocumentStore` + `IDocumentDialect` |
| Key-value (3) | ~2,435 | ~1,050 (−45%) | **20–70 each** | new `KeyValueStore`; near-total collapse |
| Vector (7) | ~8,000 | ~5,500 (−31%) | HTTP trio −57…73% | `HttpVectorStore` (extend SearchEngine) |

Fleet-wide ≈ **−45% adapter code**, the debt cleared, compliance reduced to a green conformance-kit cell. **Proven pattern**: `Koan.Data.Relational` and `SearchEngineVectorRepository` already are family bases with dialect seams. **Real risks kept** (not waved away): Couchbase's 3-level `bucket.scope.collection` vs Mongo's 2-level (the container-resolution seam returns an opaque handle to absorb it); vector native-MT API divergence (name-mangling is the floor); InMemory must stay the byte-faithful convergence oracle; the new bases must match the relational base's allocation discipline (gated by byte-identity off-proofs).

## 6. Conformance — the executable definition of "compliant"

- **Capability tokens** (co-defined with their check, ARCH-0094 — a token cannot exist without its conformance module, so over-claim fails green): `DataCaps.Isolation.RowScoped` (exists) + new **`ContainerScoped`** + **`DatabaseScoped`**. Every adapter declares all three; the conformance kit proves each.
- **`AodbConformanceSpecsBase<TFactory>`** — one reusable real-`AddKoan()` spec base (the `ManagedFieldNoLeak` / `AdapterSurfaceSpecsBase<TFactory>` + `IAdapterTestFactory` pattern), run by **every** adapter surface, proving: Shared isolation (write-stamp lands + read-filter isolates + cross-scope write rejected) · Container isolation (distinct physical container per particle + `__`-name survival/rename) · Database isolation (per-source physical routing + fail-closed on unconfigured). The vector overlay-naming silent-drop is a RED cell here until fixed.

## 7. Phased implementation (TDD, break-and-rebuild authorized)

Each phase: real-`AddKoan()` conformance specs first (RED), then the rebuild, then green + a byte-identity off-proof + impl-diff review.

- **P1 — the Moniker contract.** `IAdapterFactory` marker + `IVectorAdapterFactory.Create(sp, source)` + `RoutedSource` + `ResolveFactory<TFactory>`; `AdapterResolver` and `VectorService` converge onto both. Gate: Docker-free per-source proof on InMemoryVector + SqliteVec; data-core 273 off-proof byte-identical.
- **P2 — `KeyValueStore` base.** Rebuild Redis/Json/InMemory onto it; Shared write-stamp via the shared JSON injector / sidecar + write guard; Container via keyspace/file/store-per-particle; Database via per-source connection/file/store. Gate: conformance kit green on all three (InMemory + Json Docker-free; Redis via Testcontainers).
- **P3 — `DocumentStore` base.** Rebuild Mongo + Couchbase onto it (Mongo largely conforms — formalize; Couchbase gains write-stamp + per-source). Gate: conformance kit green (Testcontainers).
- **P4 — vector.** Extend `ScopedVectorRepository` to drive Container (native MT) + Moniker (per-source); **verify+fix the overlay-naming silent-drop** on Qdrant/Milvus/ElasticSearch/OpenSearch. Gate: conformance kit green per adapter (Testcontainers; InMemoryVector/SqliteVec Docker-free).
- **P5 — the conformance ledger.** `ContainerScoped`/`DatabaseScoped` tokens + the kit wired into every adapter surface + SURFACES rows; the boot report surfaces each adapter's declared isolation capabilities.

Relational (P0, already conformant) is the reference; its specs seed `AodbConformanceSpecsBase`.

## 8. Consequences

- **Positive**: one realization per mode per family; the routing split-brain closed by construction; the four non-isolating record adapters become isolating; the latent vector overlay leaks closed; "compliant" becomes a green CI cell, not a claim. Adding a new adapter = implement one family's backend primitives, inherit all three modes.
- **Negative / risk**: a real rebuild of five document/KV adapters + the vector decorator onto new bases (mitigated by the proven relational template + per-phase conformance gates + byte-identity off-proofs). The marker-base factory is a partial structural merge (two `Create` surfaces remain) — the assembly boundary makes a single generic interface a cycle; the contract is unified where it matters (discovery, naming, source-routing).
- **Rejected**: (a) capability-gated opt-out — the mandate forbids it; (b) a single generic `IAdapterFactory<TRepo>` returning both repo types — assembly cycle; (c) the "address+document projection / dumb-store" radical convergence — relational needs materialized columns for pushdown, so it cannot be a dumb store; the family-base level is the right altitude.
