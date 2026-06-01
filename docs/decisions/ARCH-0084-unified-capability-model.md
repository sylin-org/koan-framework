# ARCH-0084: Unified Capability Model

**Status**: Accepted (2026-06-01) — detail-mechanism fork resolved to **attach-to-token**; the Gen 1 cut (`Koan.Core.Adapters`) and the `TransactionCapabilities` runtime-state split are both approved. Implementation is gated on Facet 0 (the green ratchet) standing first.
**Date**: 2026-06-01
**Deciders**: Enterprise Architect
**Scope**: Cross-pillar capability **declaration → negotiation → self-report**. Facet 1 of the [foundation consolidation plan](../architecture/foundation-consolidation-plan.md). Lands a generic primitive in `Koan.Core.Capabilities`; migrates the data / vector / cache / storage / orchestration adapters and the `Koan.Web` negotiation sites.
**Related**: [foundation-consolidation-plan.md](../architecture/foundation-consolidation-plan.md) · supersedes the capability-declaration aspects of DATA-0002 (query-capabilities flag), DATA-0003 (write-capabilities + bulk markers), DATA-0097 / AI-0036 §9 (vector filter capabilities), MESS-0021 (messaging capabilities) · **retires** the abandoned `unified-adapter-framework` proposal's `AdapterCapabilities` (Gen 1) · sets the capability surface that Facet 2 (`KoanModule.Describe`) will host.

---

## Context

The framework so far is a **viability exercise**; v1 means removing the development scaffolding and hardening what works. Capability/self-report is the highest-leverage scaffold smell: **~40 ad-hoc capability types with no unified model** (three different types literally named `Capability`, per-pillar `*Capabilities` enums, records, and `I*Capabilities` interfaces). A stage-1 inventory of every declaration, negotiation, and report site produced the findings below; this ADR records the model they dictate.

### What the inventory found

**1. "Capability" names three unrelated concerns.** Only the first is this ADR's target:

| Concern | Examples | Verdict |
|---|---|---|
| **Infra self-report / negotiation** — what a *provider* can do | `QueryCapabilities`, `WriteCapabilities`, `VectorCapabilities` (+ `I*Capabilities` markers), `FilterCapabilities`, `VectorFilterCapabilities`, `CacheStoreCapabilities`, `CoherenceCapabilities`, `StorageProviderCapabilities`, `TransactionCapabilities`, `ExporterCapabilities` | **IN — the target** |
| **Web authorization** — what a *user* may do | `Koan.Web.Extensions/Authorization/*`: `CapabilityPolicy`, `CapabilityAuthorizer`, `RequireCapabilityAttribute`, `CapabilityActions` | **OUT — false friend** |
| **Domain vocabulary** — ZenGarden agents wishing for capabilities | `ZenGardenCapabilityWish/Surface/Requirement/…` | **OUT — false friend** |

Also OUT: `Mcp*Capability*` (an MCP-protocol advertisement document, not internal negotiation); `KoanCapabilityAttribute` (ServiceMesh RPC-method naming — deferred, possibly absorbed by Facet 2's "named capabilities a module exposes"); AI `ComputeCapability`/`ModelCapability` (a Facet 4 aspirational-pillar question, not this model's).

**2. There are two generations of the infra model; Gen 1 is abandoned scaffolding.**
- **Gen 1 — `Koan.Core.Adapters`** (`BaseKoanAdapter` + `AdapterCapabilities` fluent DSL + six fixed-category flag enums: `Health/Configuration/Security/Messaging/Orchestration/ExtendedQuery`). Its only real consumer is **one** connector (`LMStudioAdapter`); `docs/proposals/complete/unified-adapter-framework-completion.md` documents it as *"partially implemented… left incomplete… broken state."* Fixed categories and aspirational matrices (`AutoScaling`, `CircuitBreaker`, `MutualTls`) that nothing negotiates.
- **Gen 2 — `FilterCapabilities` / `VectorFilterCapabilities`** (AI-0036 / DATA-0097). Operator-set records with `CanPush`, `None`/`Full`/`Of` factories, negotiated by the filter coordinators, **fail-loud** (residual-is-error). ~16 connectors declare it; conformance specs pin it.

**3. The surface, and its blast radius.** `IQuery/IWrite/IVectorCapabilities` span 47 files: **declared** by ~10 data connectors (Postgres, SqlServer, Sqlite, Redis, Mongo, Couchbase, Json, InMemory) + 6 vector (Weaviate, Qdrant, PGVector, Milvus, OpenSearch, ElasticSearch); **negotiated** by `Koan.Data.Core`, `Koan.Data.Cqrs`, `Koan.Cache`, and `Koan.Web` (incl. `WellKnownController` — the `/.well-known` self-report); **pinned** by per-connector `*Capabilities.Spec.cs` (ARCH-0079 integration specs already exist → stage-a conformance net is in place). **Samples are insulated** — they read the *rendered* report (manifests, dashboards), never the types — so over-fit risk is low and the migration touches framework + connectors + specs, not apps.

**4. The shape the data already discovered.** Negotiation today is `enum | enum` to declare and `.HasFlag(token)` to check (e.g. `Writes.HasFlag(WriteCapabilities.FastRemove)` in every relational repo and `Entity.cs`), plus `CanPush(op, collectionField)` for filters. Crucially: **almost every `*Capabilities` "record" is just a bag of booleans** (cache 5, coherence 3, storage 4, exporter 3) — i.e. a set of independent yes/no tokens, not structured data. The **only** genuinely structured value in the entire surface is the filter operator set. And `TransactionCapabilities` is not pure capability at all — it mixes 3 capability flags with per-instance runtime state (`Adapters[]`, `TrackedOperationCount`).

### Forces

1. **The premium-DX audience is the adapter author, not the app developer** (samples never touch these types). Optimize for clean declaration + clean negotiation + free self-report.
2. **Fail-loud is canon** (DATA-0097: an un-pushable filter node is a hard error, never silent narrowing). The unified model must preserve a single, loud "not supported" path.
3. **Reference = Intent.** Tokens must live in their pillar's own abstractions assembly — referencing `Koan.Data.Vector` is what should surface the vector tokens. A single global catalog class would either centralize all tokens (breaking the rule) or be impossible to span across assemblies.
4. **One mechanism, not two.** Structured detail is needed by exactly one family (filters). The model must support detail without imposing it on the ~30 boolean tokens that need none.

---

## Decision

Ship **one** capability model. The generic primitive lives in `Koan.Core.Capabilities`; each pillar declares its own tokens in its own abstractions assembly.

### The primitive (`Koan.Core.Capabilities`)

```csharp
// A token: a stable string identity, strongly typed so authors get IntelliSense + compile-time safety.
public readonly record struct Capability(string Id);   // e.g. new("query.linq")

// The bag an adapter declares and the framework negotiates against.
public interface ICapabilities                          // builder face passed to declare-time code
{
    ICapabilities Add(Capability token);
    ICapabilities Add<TDetail>(Capability token, TDetail detail) where TDetail : notnull;
}

public sealed class CapabilitySet : ICapabilities
{
    public bool Has(Capability token);
    public void Require(Capability token);              // -> CapabilityNotSupportedException (the one fail-loud path)
    public TDetail? Detail<TDetail>(Capability token);  // structured detail, only where a token carries it
    public IReadOnlyCollection<Capability> All { get; } // free, uniform self-report
}

public sealed class CapabilityNotSupportedException : Exception;  // replaces every ad-hoc "not supported"
```

### Per-pillar token catalogs (not one global `Caps`)

Each pillar's abstractions assembly owns its tokens, so referencing the pillar surfaces them (Reference = Intent):

```csharp
// Koan.Data.Abstractions
public static class DataCaps
{
    public static class Query { public static readonly Capability String = new("query.string"), Linq = new("query.linq"),
                                       FastCount = new("query.fastCount"), OptimizedCount = new("query.optimizedCount"),
                                       Filter = new("query.filter"); }                 // <- carries FilterSupport detail
    public static class Write { public static readonly Capability BulkUpsert = new("write.bulkUpsert"), BulkDelete = new("write.bulkDelete"),
                                       AtomicBatch = new("write.atomicBatch"), FastRemove = new("write.fastRemove"); }
}
// Koan.Data.Vector.Abstractions -> VectorCaps.{Knn,Filters,Hybrid,NativeContinuation,…,DynamicCollections}
// Koan.Cache.Abstractions       -> CacheCaps.{Tags,SlidingTtl,StaleWhileRevalidate,Binary,Persistence}, CoherenceCaps.{CatchUp,AtLeastOnce,PerKeyOrder}
// Koan.Storage.Abstractions     -> StorageCaps.{SequentialRead,Seek,PresignedRead,ServerSideCopy}
// Koan.Data.Core.Transactions   -> TxCaps.{Local,Distributed,Compensation}
// Koan.Orchestration.Abstractions -> ExportCaps.{SecretsRefOnly,ReadinessProbes,TlsHints}
```

Declaration and negotiation become uniform across every pillar:

```csharp
// DECLARE  (was: public WriteCapabilities Writes => BulkUpsert | AtomicBatch;)
caps.Add(DataCaps.Write.BulkUpsert).Add(DataCaps.Write.AtomicBatch)
    .Add(DataCaps.Query.Filter, FilterSupport.Of(scalar, collection, nestedPaths: true, ignoreCase: false));

// CHECK    (was: Writes.HasFlag(WriteCapabilities.FastRemove))
if (caps.Has(DataCaps.Write.FastRemove)) …

// REQUIRE  (new single fail-loud path)
caps.Require(DataCaps.Query.Filter);

// DETAIL   (was: filterCaps.CanPush(op, collectionField))
caps.Detail<FilterSupport>(DataCaps.Query.Filter)?.CanPush(op, collectionField);
```

### The one structured detail: `FilterSupport`

`FilterCapabilities` (entity, scalar/collection split) and `VectorFilterCapabilities` (vector, single set, no split) collapse into one detail record, kept in `Koan.Data.Abstractions.Filtering` (it references `FilterOperator`):

```csharp
public sealed record FilterSupport(
    IReadOnlySet<FilterOperator> ScalarOperators,
    IReadOnlySet<FilterOperator> CollectionOperators,
    bool NestedPaths = true, bool IgnoreCase = false)
{
    public bool CanPush(FilterOperator op, bool collectionField) => (collectionField ? CollectionOperators : ScalarOperators).Contains(op);
    public static FilterSupport Uniform(bool nestedPaths, bool ignoreCase, params FilterOperator[] ops); // vector: scalar == collection
    public static FilterSupport None { get; }   // pushes nothing -> any filter node is a hard error
    public static FilterSupport Full { get; }
}
```

### Resolved fork — attach detail to the token (not a separate profile registry)

The inventory shows structured detail is needed by exactly **one** family. So detail is stored on the set keyed by its token and fetched with `Detail<T>(token)`. **Alternative considered and rejected:** a parallel typed-profile registry (`caps.Profile<FilterSupport>()`). It adds a second lookup mechanism and a second declaration verb to serve a single consumer — over-engineering against force #4. If a second structured family ever appears, attach-to-token already supports it unchanged. *(This is the one architect-level fork — flagged for explicit sign-off.)*

### Cuts and splits (the descaffolding)

- **Cut Gen 1**: delete `Koan.Core.Adapters` (`BaseKoanAdapter`, `AdapterCapabilities`, the six category enums, the Templates), migrating `LMStudioAdapter` to declare via the new model. *(Surface removal — small, one consumer — flagged for sign-off.)*
- **Delete the wrapper-record ceremony**: `RepoCaps`, `Caps`, `WriteCapsImpl`, `RepositoryCapabilities`, `RepoWriteCaps`, `DefaultQueryCapabilities` exist only to carry an enum through a marker interface; the `CapabilitySet` replaces all of them.
- **Split `TransactionCapabilities`**: model the 3 flags as `TxCaps.*`; move `Adapters[]` / `TrackedOperationCount` (live runtime state) onto the transaction-context object where they belong.
- **Retire** `QueryCapabilities`, `WriteCapabilities`, `VectorCapabilities` enums + `IQuery/IWrite/IVectorCapabilities` interfaces + `FilterCapabilities`/`VectorFilterCapabilities` records, once consumers are migrated.

### Staged migration ledger (green at every step; gated by the Facet 0 ratchet)

- **(a) Additive foundation.** Land the primitive + per-pillar catalogs + `FilterSupport`. Provide an internal enum↔token bridge so existing adapters compile unchanged. Conformance specs stay green.
- **(b) Migrate cluster-by-cluster.** data query/write connectors → vector connectors → cache (store/coherence) → storage → transaction → exporter → web negotiation (`EntityEndpointService`, `WellKnownController`) → CQRS decorator. Delete each wrapper record as its consumer moves. `LMStudio` off Gen 1.
- **(c) Delete legacy.** Remove the enums, marker interfaces, filter records, the wrapper records, and `Koan.Core.Adapters`. Reconcile the capability guides + DATA-0002/0003/0097 references in the same step.

---

## Consequences

### Positive
- **~40 capability types → ~6** (`Capability`, `CapabilitySet`, `ICapabilities`, `CapabilityNotSupportedException`, `FilterSupport`, + per-pillar token catalogs that are data, not types-per-feature). The wrapper-record category disappears entirely.
- **One declaration verb, one check verb, one fail-loud path, free self-report** — uniform across every pillar. Adapter authoring collapses to `caps.Add(...)` calls.
- **Self-report and `/.well-known` render from `caps.All` for free** — no per-pillar reporting code.
- **The boolean-bag records become honest tokens**; the one real structured case (filters) keeps its precision via `FilterSupport`.
- **Removes a documented dead-end** (Gen 1) rather than leaving two competing models in the tree.

### Negative
- **Broad mechanical migration** across ~16 connectors + framework negotiation sites. Mitigated by the enum↔token bridge (stage a) and existing conformance specs.
- **String-id tokens trade a tiny amount of compile-time exhaustiveness** (a `[Flags]` enum is a closed set) for open extensibility + free serialization. Acceptable: the strongly-typed catalogs are the authoring surface; raw strings are an escape hatch, not the path.

### Neutral
- **Three "capability" concerns stay separate by design.** Web-authz and ZenGarden keep their own vocabularies; this is deliberate, not an oversight.
- **The model is the surface Facet 2 will host** inside `KoanModule.Describe(ICapabilities)` — this ADR deliberately stops at the capability model and does not introduce the module.

---

## Notes for reviewers

1. **One fork needs your explicit call**: detail attached to the token (recommended) vs a separate typed-profile registry. The ADR proceeds on attach-to-token.
2. **Two surface removals to sign off**: cutting `Koan.Core.Adapters` (Gen 1, 1 consumer) and splitting runtime state off `TransactionCapabilities`.
3. **Sequencing**: research (this) and the design are done; **Facet 0 (the green ratchet) must be standing before stage (a) lands**, since the migration is exactly the kind of broad change the ratchet exists to gate.
4. **Out-of-scope edges to revisit later**: `KoanCapabilityAttribute` (ServiceMesh) may fold into Facet 2's module-level named capabilities; MCP's capability document is a protocol renderer that could later read from `caps.All`.
