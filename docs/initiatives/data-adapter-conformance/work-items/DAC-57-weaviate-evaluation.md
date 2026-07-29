---
type: SPEC
domain: data
title: "DAC-57 Rebuild and Certify the Weaviate Adapter"
audience: [architects, maintainers, developers, ai-agents]
status: in-progress
last_updated: 2026-07-28
framework_version: v0.20.0
validation:
  date_last_tested: 2026-07-28
  status: behavior-green-strict-deferred
  scope: empty-root Weaviate vector adapter rebuild and live V-01 through V-24/G-09 certification
---

# DAC-57 — Rebuild and certify the Weaviate adapter

| Field | Value |
|---|---|
| Phase / kind | vector / break-and-rebuild-certification |
| Depends on | DAC-53 |
| Primer scope | Source Core, Source Integration, Vector V-01 through V-24, G-09 |
| Production writes | authorized after the exploration gate recorded below |
| Owner | Adapter(Weaviate) |

## Meaningful outcome

Weaviate realizes one declared Entity vector-space decision faithfully over a durable network service: shape is owned
before I/O, metadata remains lossless, filtering is native and bounded, Session visibility is awaited, results use
Koan similarity semantics, source policy is enforced before mutation, and no application code learns Weaviate schema,
GraphQL, vectorizer, class-name, or settling ceremony.

## Exploration gate

**Task:** Replace the Weaviate adapter from an empty implementation root, retaining only independently justified
provider facts and stable compatibility identities, then certify it against the ratified Vector and Source contracts.

**Application intent:** Store and search Entity embeddings in a durable Weaviate service through ordinary Koan Vector
semantics, with native pre-filtering, immediate awaited visibility, stable ordering, and no provider ceremony.

**Public expression:** Reference `Sylin.Koan.Data.Vector.Connector.Weaviate`, run Weaviate v1.37.6, and declare the
space once:

```csharp
services.AddKoan(koan => koan.Data
    .Source("Search")
    .Vector<Article>(space => space
        .Name("content")
        .Dimensions(1536)
        .Metric(VectorMetric.Cosine)
        .Visibility(VectorVisibility.Session)));

await Vector<Article>.Save(article.Id, embedding, new { article.Category });
var matches = await Vector<Article>.Search(embedding, query => query
    .Top(12)
    .Where(Filter.Eq("Category", "support")));
```

The ordinary configuration surface is placement and authentication only when discovery/local defaults are
insufficient:

```json
{
  "Koan": {
    "Data": {
      "Weaviate": {
        "Endpoint": "https://cluster.example.weaviate.network",
        "ApiKey": "use-a-secret-provider"
      }
    }
  }
}
```

Source-specific endpoint, `StorageLifecycle`, and `Access` use the same `Koan:Data:Sources:{name}` declaration as
every other Koan adapter. Collection name, dimensions, metric, vectorizer, auto-schema, field names, and consistency
are not duplicated as adapter options.

**Guarantee/correction:** The immutable `VectorSpacePlan` owns dimensions, metric, model, logical space, source, and
visibility before provider I/O. Managed writes create a fixed Weaviate collection with self-provided vectors, HNSW
metric, a contract marker, neutral metadata, and a constant-cardinality native-filter projection. Existing collections
are validated before use; External and read-only postures never create, repair, or delete schema. Awaited Session
mutations use provider consistency and a bounded vector-index readiness barrier. Complete point reads return ID,
original vector, and lossless metadata. Every search score is finite, normalized to `[0,1]`, and higher means closer;
Weaviate HNSW is reported approximate and candidate count remains unknown. IDs, source/partition physical names, row
scope, search, clear, and batches remain isolated. Unsupported Eventual visibility, hybrid search, continuation,
streaming export, atomic batch, multiple vectors per Entity, or filter operators without an exact native translation
fail before mutation or unbounded fallback. Wrong shape, auth failure, timeout, cancellation, disposed repositories,
and provider failures remain corrective and do not leak provider bodies or business values.

**Complete intent surface:** Package reference; `AddKoan(...)` vector declaration; Weaviate v1.37.6 runtime; optional
endpoint/API-key configuration; optional standard source policy/context; ordinary `Vector<TEntity>` operations. No
Weaviate client, class bootstrap, schema property, GraphQL query, provider DTO, or application settling loop is
required.

**Public concepts:** `VectorSpacePlan` expresses the application-owned mathematical decision; `WeaviateOptions.Endpoint`
and `ApiKey` express irreducible placement/authentication; timeout, metadata, batch, clear, and tie-expansion limits are
operator safety budgets; `DataSourcePlan` expresses lifecycle/access. No other Weaviate public concept is necessary.

**Docs read:**

- `docs/architecture/principles.md` — intent-first APIs, Entity-centric access, thin truthful adapters, and host-owned
  immutable decisions.
- `docs/architecture/data-adapter-development-primer.md` — Source Core/Integration and Vector V-01 through V-24.
- `docs/reference/ai/vector.md` — compact `Vector<TEntity>` expression and corrective capability posture.
- DAC-53, DAC-55, and DAC-56 — current network-vector acceptance language and proof shape, used as behavioral
  references rather than reusable provider execution.
- Official Weaviate v1.37 collection, self-provided-vector, HNSW distance, GraphQL filter, batch, consistency,
  asynchronous indexing, nodes, object, and release documentation — provider authority for this design.

**Code read:**

- `IVectorSearchRepository.cs`, `VectorSpacePlan.cs`, `VectorSearchRequest.cs`, `VectorScope.cs`, and
  `VectorMetadata.cs` — the provider-neutral operation, plan, query, isolation, and lossless value contracts.
- `VectorService.cs` and `ScopedVectorRepository.cs` — host-cached plan ownership and the single scope-stamping/filter
  composition boundary.
- Rebuilt Qdrant, Elasticsearch, and OpenSearch route/client/filter/repository implementations — closest current
  responsibility boundaries and acceptance behavior, not code to inherit blindly.
- Current Weaviate factory/options/client/repository/filter/module/health files — harvested failure modes: obsolete
  factory contract, first-write dimensions, duplicated metric, auto-schema property growth, serial pseudo-bulk,
  static caches, lossy class naming, mutable provider-specific metadata, provider-body leakage, Newtonsoft, and claims
  for hybrid/continuation/export that are not grounded in the current contract.
- Current Weaviate AODB/matrix/overlay tests and shared Vector conformance kit — old four-cell scaffolding, per-test
  containers, application settling sleeps, auto-schema, and obsolete capability vocabulary do not certify V-01–V-24.

**Reusing:**

- Existing framework: `VectorSpacePlan`, `VectorPoint`, `VectorScope`, `BatchResult`, neutral `VectorMetadata`, source
  policy, routed connection resolution, physical naming, capabilities, health participation, and shared conformance
  vocabulary.
- Stable provider identity: provider/alias/service metadata, official Weaviate v1.37.6 image, HTTP port, readiness
  route, self-provided vectors, and the need for GraphQL-valid collection names.
- Provider truths: collection-owned metric/vectorizer, raw distance direction, native prefilters, consistency levels,
  per-item batch failures, and optional asynchronous HNSW indexing.
- Rebuilt rather than reused: every production Weaviate execution, schema, metadata projection, filter, option, health,
  discovery, module, documentation, and test path.

**Creating new:**

| New code | Location | Justification |
|---|---|---|
| Compact constants and options | `src/Connectors/Data/Vector/Weaviate/Infrastructure/Constants.cs`, `WeaviateOptions.cs` | One owner for wire names and bounded operator budgets; no mathematical decision in options. |
| Source-aware immutable route | `src/Connectors/Data/Vector/Weaviate/Runtime/WeaviateRoute.cs` | Resolve endpoint, credential, and `DataSourcePlan` once per source. |
| REST/GraphQL boundary | `src/Connectors/Data/Vector/Weaviate/Runtime/WeaviateClient.cs` | Own HTTP, JSON, status, and safe failure mechanics without provider DTO leakage. |
| Constant-schema filter projection | `src/Connectors/Data/Vector/Weaviate/Runtime/WeaviateFilter.cs` | Encode neutral values into fixed text tokens and translate only exact native filter semantics. |
| Plan-bound repository | `src/Connectors/Data/Vector/Weaviate/Runtime/WeaviateRepository.cs` | Own shape, points, search, visibility, lifecycle, isolation, and truthful outcomes. |
| Plan-bound factory | `src/Connectors/Data/Vector/Weaviate/WeaviateVectorAdapterFactory.cs` | Validate plan/options and create one repository from immutable decisions. |
| Compact discovery/module/health | existing Weaviate discovery, initialization, configurator, and health paths | Preserve Koan activation/discovery/health conventions without semantic duplication. |
| Executable DAC-57 ledger | Weaviate VectorAdapterSurface test project | Give every V-01–V-24/G-09 cell a live proof or explicit corrective decline. |
| Instruction-first adapter docs | Weaviate `README.md` and `TECHNICAL.md` | State setup, guarantees, limits, and failure behavior without historical narrative. |

**Coalescence:** The closest responsibility pattern is the rebuilt Qdrant/Elasticsearch/OpenSearch family: immutable
route, transport boundary, native filter writer, and one plan-bound repository. Those shared laws justify placement,
not shared provider code. Weaviate collection descriptions, self-provided-vector schema, distance transforms, GraphQL
where algebra, batch receipts, readiness queue, and UUID/object lifecycle are adapter-specific. Do not create a generic
HTTP-vector superclass. Delete the complete legacy execution root, telemetry wrapper, provenance ceremony, metric/
connection-string schema controls, auto-schema behavior, Newtonsoft dependency, ZenGarden dependency, old matrix and
overlay scaffolding, and per-test containers. The target owner is `WeaviateRepository`; the factory owns plan/route
validation, the client owns transport only, and the filter writer owns exact metadata-token projection only.

**Ergonomics:** Human code reads entirely in Koan language: Source, Vector, Name, Dimensions, Metric, Visibility, Save,
Search, Top, and Where. IntelliSense exposes only placement/authentication and bounded safety budgets. An agent maps
one immutable plan and one neutral point/filter algebra without deciding provider schema or learning historical
aliases. Cognitive branches are source policy, collection missing/existing, declared metric, optional scope/filter,
and explicit decline.

**Constraints satisfied:**

- Entity-first `Vector<TEntity>` remains the public path; no repository or provider DTO leaks into application code.
- Provider HTTP/GraphQL is internal; no application HTTP endpoint is introduced.
- Stable wire identifiers live in constants; tunable safety bounds live in typed options.
- Metadata remains lossless in one neutral blob; a separate fixed-schema projection provides exact native filtering
  without one Weaviate property per user path.
- No compatibility wrapper, placeholder, commented scaffold, hidden full scan, post-filter, or application sleep is
  planned.
- Search, batches, clear, metadata, and tie expansion are bounded; streaming export is declined.
- README, TECHNICAL, initiative status, and current roadmap/ledger surfaces will move with implementation truth.

**Risks:** Live certification requires Docker/Weaviate. Weaviate schema does not expose vector dimensions, so the
Koan contract marker must record dimensions and be validated before writes. Batch object import is not assumed to be
native upsert; the adapter may use bounded ordered operations and decline a native-bulk capability rather than
fabricate it. Range predicates require an exact constant-schema representation; until proven they fail closed while
the exact equality/set/existence subset remains native. Session visibility requires a bounded readiness check when
asynchronous indexing is enabled. Stable cutoff ties require bounded expansion and corrective failure if the bound
cannot prove the cutoff.

Standing user authorization permits implementation after this gate. It does not relax provider-proof, live-test, or
strict-packet requirements.

## Execute

1. Delete the old production implementation root and create only the files named above.
2. Implement immutable collection shape, complete points, lossless metadata, fixed native filters, score normalization,
   source policy, hard isolation, truthful ordered outcomes, bounded ties, Session visibility, and corrective failures.
3. Replace legacy tests with one assembly-scoped pinned fixture and live V-01–V-24/G-09 ledger; include wrong shape,
   read-only/External, restart, cancellation, disposal, and warm path.
4. Run Weaviate, Vector regressions, full solution build, product/docs gates, and initiative consistency checks.
5. Write the strict evidence packet only through the shared packet mechanism; do not call skipped or absent evidence
   green.

## Definition of done

- [x] One empty-root implementation passes every advertised Source/Vector claim on Weaviate v1.37.6.
- [x] Every V-01–V-24/G-09 cell has an executable proof or deliberate corrective decline.
- [x] No schema decision is duplicated in Weaviate options and no legacy execution path remains.
- [x] Visibility, distance transforms, collection lifecycle, identity, filtering, and failure semantics are explicit.
- [ ] Strict Forge has a complete live evidence packet with no provider skip counted as green.

## Verification result

The empty-root adapter has one plan-bound repository, one HTTP/GraphQL boundary, one fixed-schema filter projection,
and no retained legacy execution path. The pinned live provider suite passes 28/28 cells with zero skips. V-08 proves
cosine, Euclidean, and dot-product normalization. V-13 proves every advertised equality/set/collection/existence
operator and boolean composition against the neutral oracle, while range predicates fail closed. V-17 proves ordered
per-item outcomes while also proving the adapter does not mislabel create/replace loops as native bulk.

| Command | Result |
|---|---|
| Weaviate VectorAdapterSurface suite | PASS, 28/28, zero skips, live Weaviate 1.37.6 |
| Data Core Vector filter | PASS, 24/24 |
| InMemory Vector suite | PASS, 50/50 |
| SqliteVec Vector suite | PASS, 58; five deliberate capability skips unchanged |
| `dotnet build Koan.sln --no-restore` | PASS, zero warnings and zero errors |
| package/product/docs gates | PASS; 93 packages, 43 claims, current generated outputs, public docs truth green |

Strict status remains `in-progress`: behavioral implementation is complete, while the shared versioned packet
generator remains outside this adapter rebuild. The initiative-wide validator also retains pre-existing dependency
ledger inconsistencies across earlier cards; DAC-57 now matches its roadmap dependency and does not claim that global
ledger debt as adapter evidence.

## Stop conditions

Unpinned service identity, unavailable LIVE provider, ambiguous distance/visibility semantics, external-lifecycle
mutation, scope leakage, dynamic user schema, unbounded fallback, or fabricated batch/accuracy truth blocks
certification.
