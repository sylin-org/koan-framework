---
type: SPEC
domain: data
title: "DAC-58 Rebuild and Certify the Milvus Adapter"
audience: [architects, maintainers, developers, ai-agents]
status: in-progress
last_updated: 2026-07-28
framework_version: v0.20.0
validation:
  date_last_tested: 2026-07-28
  status: behavior-green-strict-deferred
  scope: empty-root Milvus v2.6.20 rebuild; three live 28/28 V-01 through V-24/G-09 passes; regressions and repository gates green
---

# DAC-58 — Rebuild and certify the Milvus adapter

| Field | Value |
|---|---|
| Phase / kind | vector / break-and-rebuild-certification |
| Depends on | DAC-53 |
| Primer scope | Source Core, Source Integration, Vector V-01 through V-24, G-09 |
| Production writes | authorized after the exploration gate recorded below |
| Owner | Adapter(Milvus) |

## Meaningful outcome

Milvus realizes one declared Entity vector-space decision faithfully over its distributed storage/query lifecycle:
shape is owned before I/O, metadata remains lossless, filtering is native and bounded, collection load and index state
are explicit, awaited Session mutations are immediately readable, results use Koan similarity semantics, source policy
is enforced before mutation, and application code learns no Milvus schema, expression, consistency, load, or index
ceremony.

## Exploration gate

**Task:** Replace the Milvus adapter from an empty implementation root, retaining only independently justified provider
facts and stable compatibility identities, then certify it against the ratified Vector and Source contracts.

**Application intent:** Store and search Entity embeddings in a durable Milvus service through ordinary Koan Vector
semantics, with native pre-filtering, awaited visibility, stable ordering, and no provider ceremony.

**Public expression:** Reference `Sylin.Koan.Data.Vector.Connector.Milvus`, run Milvus v2.6.20, and declare the space
once:

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

Ordinary configuration is placement and authentication only when discovery/local defaults are insufficient:

```json
{
  "Koan": {
    "Data": {
      "Milvus": {
        "Endpoint": "https://milvus.example.net",
        "Database": "default",
        "Token": "use-a-secret-provider"
      }
    }
  }
}
```

Source-specific endpoint, database, credential, `StorageLifecycle`, and `Access` use the same
`Koan:Data:Sources:{name}` declaration as every other Koan adapter. Collection name, dimensions, metric, index,
field names, consistency, and auto-create are not duplicated as adapter options.

**Guarantee/correction:** The immutable `VectorSpacePlan` owns dimensions, metric, model, logical space, source, and
visibility before provider I/O. Managed writes create a fixed Milvus collection with a VARCHAR primary key, FLOAT_VECTOR,
JSON metadata, explicit HNSW index, disabled dynamic fields, and a compact contract-hash field in the fixed schema.
Existing collections are described and validated before use; External and read-only sources never create, alter, load,
release, or delete schema. The adapter uses Strong reads to realize awaited Koan Session visibility over the stateless
REST boundary; provider Session consistency is not claimed because the REST adapter has no durable client write-timestamp
state. Eventual visibility is explicitly declined. The collection is loaded only for Managed writable sources and its
load state is awaited within a bounded deadline. Complete point reads return ID, original vector, and lossless neutral
metadata. Every search score is finite, normalized to `[0,1]`, and higher means closer. Milvus HNSW is reported
approximate and candidate count remains unknown. IDs, source/partition physical names, row scope, search, clear, and
batches remain isolated. Unsupported hybrid search, continuation, streaming export, atomic batch, multiple vectors per
Entity, or filter operators without an exact native translation fail before mutation or unbounded fallback. Wrong shape,
auth failure, timeout, cancellation, disposed repositories, and provider failures remain corrective and never leak
provider bodies or business values.

**Complete intent surface:** Package reference; `AddKoan(...)` vector declaration; Milvus v2.6.20 runtime with its
official standalone dependencies; optional endpoint/database/token configuration; optional standard source
policy/context; ordinary `Vector<TEntity>` operations. No Milvus SDK, schema bootstrap, field declaration, expression,
index request, load call, consistency level, provider DTO, or application settling loop is required.

**Public concepts:** `VectorSpacePlan` expresses the application-owned mathematical decision. `MilvusOptions.Endpoint`,
`Database`, and `Token` express irreducible placement/authentication. Timeout, metadata, batch, clear, search-candidate,
response, and load/visibility limits are operator safety budgets. `DataSourcePlan` expresses lifecycle/access. No other
Milvus public concept is necessary.

**Docs read:**

- `docs/architecture/principles.md` — intent-first APIs, Entity-centric access, thin truthful adapters, and host-owned
  immutable decisions.
- `docs/architecture/data-adapter-development-primer.md` — Source Core/Integration and Vector V-01 through V-24.
- `docs/reference/ai/vector.md` — compact `Vector<TEntity>` expression and corrective capability posture.
- DAC-53, DAC-57, and the current Vector conformance kit — current network-vector proof language and executable cells,
  used as behavioral references rather than reusable provider execution.
- Official Milvus v2.6 collection/schema, metric, search, JSON filter, consistency, upsert, load/index, REST v2, standalone,
  and v2.6.20 release documentation — provider authority for this design.

**Code read:**

- `IVectorSearchRepository.cs`, `VectorSpacePlan.cs`, `VectorSearchRequest.cs`, `VectorScope.cs`, and
  `VectorMetadata.cs` — provider-neutral operation, plan, query, isolation, and lossless value contracts.
- `VectorService.cs` and `ScopedVectorRepository.cs` — host-cached plan ownership and the single scope-stamping/filter
  composition boundary.
- Rebuilt Qdrant, Elasticsearch, OpenSearch, and Weaviate route/client/filter/repository implementations — closest
  current responsibility boundaries and acceptance behavior, not code to inherit blindly.
- Current Milvus factory/options/repository/filter/module/health files — harvested failure modes: obsolete factory
  contract, first-write dimensions, duplicated metric/field/collection/consistency/schema options, repository-local
  ensured cache, dynamic quick schema, optimistic bulk/delete counts, collection drop presented as flush, provider-body
  leakage, Newtonsoft, and no explicit index/load ownership.
- Current Milvus matrix/AODB tests and shared Vector conformance kit — old four-cell scaffolding, duplicate three-service
  fixtures, application comments that normalize delayed deletes, and legacy capability vocabulary do not certify
  V-01 through V-24.

**Reusing:**

- Existing framework: `VectorSpacePlan`, `VectorPoint`, `VectorScope`, `BatchResult`, neutral `VectorMetadata`, source
  policy, routed connection resolution, physical naming, capabilities, health participation, and shared conformance
  vocabulary.
- Stable provider identity: provider/alias/service metadata, REST v2 port, official Milvus v2.6.20 image, standalone
  etcd/object-store dependencies, and readiness endpoints.
- Provider truths: collection-owned schema/metric/index, VARCHAR primary keys, FLOAT_VECTOR and JSON fields, native
  scalar/JSON prefilters, Strong read semantics, explicit load state, application-level error codes in HTTP 200 bodies,
  HNSW approximate execution, and score direction varying by metric.
- Rebuilt rather than reused: every production Milvus execution, schema, metadata/filter, option, health, discovery,
  module, documentation, and test path.

**Creating new:**

| New code | Location | Justification |
|---|---|---|
| Compact constants and options | `src/Connectors/Data/Vector/Milvus/Infrastructure/Constants.cs`, `MilvusOptions.cs` | One owner for wire names and bounded operator budgets; no mathematical decision in options. |
| Source-aware immutable route | `src/Connectors/Data/Vector/Milvus/Runtime/MilvusRoute.cs` | Resolve endpoint, database, credential, and `DataSourcePlan` once per source. |
| REST v2 boundary | `src/Connectors/Data/Vector/Milvus/Runtime/MilvusClient.cs` | Own HTTP, bounded JSON, provider status, and safe failure mechanics without provider DTO leakage. |
| Exact filter writer | `src/Connectors/Data/Vector/Milvus/Runtime/MilvusFilter.cs` | Translate the neutral filter algebra to exact native JSON-field expressions and fail closed. |
| Plan-bound repository | `src/Connectors/Data/Vector/Milvus/Runtime/MilvusRepository.cs` | Own schema/index/load, complete points, search, visibility, lifecycle, isolation, and truthful outcomes. |
| Plan-bound factory | `src/Connectors/Data/Vector/Milvus/MilvusVectorAdapterFactory.cs` | Validate plan/options and create one repository from immutable decisions. |
| Compact discovery/module/health | existing Milvus discovery, initialization, configurator, and health paths | Preserve Koan activation/discovery/health conventions without semantic duplication. |
| Executable DAC-58 ledger | Milvus VectorAdapterSurface test project | Give every V-01 through V-24/G-09 cell a live proof or explicit corrective decline. |
| Instruction-first adapter docs | Milvus `README.md` and `TECHNICAL.md` | State setup, guarantees, limits, and failure behavior without historical narrative. |

**Coalescence:** The closest responsibility pattern is the rebuilt network-vector family: immutable route, transport
boundary, native filter writer, and one plan-bound repository. Those shared laws justify placement, not shared provider
code. Milvus schema construction, index construction, load-state transitions, REST application codes, expression syntax,
and metric score direction are adapter-specific. Do not create a generic HTTP-vector superclass. Delete the complete
legacy execution root, telemetry wrapper, duplicated shape options, Newtonsoft dependency, old matrix scaffolding, and
duplicate per-test service stacks. The target owner is `MilvusRepository`; the factory owns plan/route validation, the
client owns transport only, and the filter writer owns exact native expression generation only.

**Ergonomics:** Human code reads entirely in Koan language: Source, Vector, Name, Dimensions, Metric, Visibility, Save,
Search, Top, and Where. IntelliSense exposes only placement/authentication and bounded safety budgets. An agent maps one
immutable plan and one neutral point/filter algebra without deciding provider schema or learning historical aliases.
Cognitive branches are source policy, collection missing/existing, declared metric, optional scope/filter, and explicit
decline.

**Constraints satisfied:**

- Entity-first `Vector<TEntity>` remains the public path; no repository or provider DTO leaks into application code.
- Provider HTTP is internal; no application HTTP endpoint is introduced.
- Stable wire identifiers live in constants; tunable safety bounds live in typed options.
- Metadata remains lossless in one neutral JSON field; filters use Milvus native JSON paths without dynamic schema.
- No compatibility wrapper, placeholder, commented scaffold, hidden full scan, post-filter, or application sleep is
  planned.
- Search, batches, clear, metadata, response, load, and tie expansion are bounded; streaming export is declined.
- README, TECHNICAL, initiative status, and current roadmap/ledger surfaces move with implementation truth.

**Risks:** Live certification requires Docker plus Milvus, etcd, and MinIO. The REST describe shape and index response
must be probed against v2.6.20 before validation code is frozen. Quick collection setup is insufficient because it hides
contract fields and load/index decisions. Strong read consistency is required for the stateless REST adapter's
Session guarantee and may cost latency. Batch APIs do not prove per-item failure receipts; the adapter may use bounded
ordered operations and decline `NativeBulk` rather than fabricate outcomes. Stable cutoff ties require bounded expansion
and corrective failure if the bound cannot prove the cutoff.

Standing user authorization permits implementation after this gate. It does not relax provider-proof, live-test, or
strict-packet requirements.

## Execute

1. Delete the old production implementation root and create only the files named above.
2. Implement immutable schema/index/load shape, complete points, lossless metadata, native filters, score normalization,
   source policy, hard isolation, truthful ordered outcomes, bounded ties, Session visibility, and corrective failures.
3. Replace legacy tests with one assembly-scoped pinned topology and live V-01 through V-24/G-09 ledger; include wrong
   shape, read-only/External, restart, cancellation, disposal, and warm path.
4. Run Milvus, Vector regressions, full solution build, product/docs gates, and initiative consistency checks.
5. Write the strict evidence packet only through the shared packet mechanism; do not call skipped or absent evidence
   green.

## Verification

- Milvus v2.6.20 live ledger: three 28/28 passes, zero skipped, each against a fresh pinned Milvus/etcd/MinIO topology;
  the final pass exercised the bounded one-entry readiness memo.
- Native filters: Eq, Ne, In, Nin, Has, HasAny, HasAll, HasNone, Exists, boolean composition, negation, nested paths,
  and missing/null semantics converged with the neutral oracle; unsupported operators failed closed.
- Metrics: Cosine, Euclidean, and DotProduct stayed finite, monotonic, normalized to `[0,1]`, and higher-is-closer.
- Lifecycle: fixed shape, contract field, HNSW index, load state, Strong Session visibility, managed/external/read-only policy,
  restart durability, cancellation, disposal, batches, clear, and every isolation axis passed live.
- Regressions: Data Core Vector 24/24; InMemory Vector 50/50; SqliteVec 58 passed with five deliberate existing skips.
- Repository: full `Koan.sln` build succeeded; package quality returned 93 packages, 0 repair, 10 review, 83 structurally
  ready; product surface returned 43 claims and 93 packages.
- Strict evidence remains deferred because the shared versioned packet generator is absent. No local substitute was
  created and no missing packet is counted as green.

## Definition of done

- [x] One empty-root implementation passes every advertised Source/Vector claim on Milvus v2.6.20.
- [x] Every V-01 through V-24/G-09 cell has an executable proof or deliberate corrective decline.
- [x] No schema decision is duplicated in Milvus options and no legacy execution path remains.
- [x] Visibility, score transforms, index/load lifecycle, identity, filtering, and failure semantics are explicit.
- [ ] Strict Forge has a complete live evidence packet with no provider skip counted as green.

## Stop conditions

Unpinned service identity, unavailable LIVE provider, ambiguous score/visibility semantics, external-lifecycle mutation,
scope leakage, dynamic user schema, hidden load/index mutation, unbounded fallback, or fabricated batch/accuracy truth
blocks certification.
