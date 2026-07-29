---
type: SPEC
domain: data
title: "DAC-53 Rebuild and Certify the Qdrant Adapter"
audience: [architects, maintainers, developers, ai-agents]
status: in-progress
last_updated: 2026-07-28
framework_version: v0.20.0
validation:
  date_last_tested: 2026-07-28
  status: behavior-green-strict-deferred
  scope: empty-root Qdrant vector adapter rebuild and live V-01 through V-24/G-09 certification
---

# DAC-53 — Rebuild and certify the Qdrant adapter

| Field | Value |
|---|---|
| Phase / kind | vector / break-and-rebuild-certification |
| Depends on | DAC-51, DAC-52 |
| Primer scope | Source Core, Source Integration, Vector V-01 through V-24, G-09 |
| Production writes | authorized after the exploration gate recorded below |
| Owner | Adapter(Qdrant) |

## Meaningful outcome

Qdrant is the networked vector reference: an application declares one Entity vector-space decision and Koan realizes
that exact shape, source policy, isolation, similarity, filtering, visibility, and failure truth over a real Qdrant
service without exposing Qdrant collection or JSON ceremony.

## Exploration gate

**Task:** Replace the Qdrant adapter from an empty implementation root, retaining only independently justified
provider facts and compatibility identities, then certify it against the ratified Vector and Source contracts.

**Application intent:** Store and search Entity embeddings in a durable shared Qdrant service through ordinary Koan
Vector semantics, with native pre-filtering, immediate awaited visibility, and no provider ceremony in application code.

**Public expression:** Reference `Sylin.Koan.Data.Vector.Connector.Qdrant`, run Qdrant v1.18.3, and declare the space once:

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

The only ordinary configuration is placement and authentication when discovery/local defaults are insufficient:

```json
{
  "Koan": {
    "Data": {
      "Qdrant": {
        "Endpoint": "https://cluster.example.qdrant.io",
        "ApiKey": "use-a-secret-provider"
      }
    }
  }
}
```

Source-specific endpoint, `StorageLifecycle`, and `Access` use the same `Koan:Data:Sources:{name}` declaration as
every other Koan adapter. No collection name, dimension, metric, field-name, auto-create, wait, or quantization option
is part of the normal surface.

**Guarantee/correction:** The immutable `VectorSpacePlan` owns dimensions, metric, model, logical space, source, and
visibility before provider I/O. Qdrant collections are created only for Managed writes/ensure, and every existing
collection is validated for the declared named-vector size and metric before use. Awaited mutations use `wait=true`
and therefore realize Session visibility. Every score is finite, normalized to `[0,1]`, and higher means closer;
search reports Qdrant dense HNSW as approximate. Metadata is lossless in Koan's neutral algebra while a separate
provider-native payload projection enables bounded pre-filtering. Source/partition names, row scope, by-id operations,
clear, and batches remain isolated. Unsupported Eventual visibility, hybrid search, search continuation, streaming
export, atomic batch, or unsupported filter operators fail before mutation or unbounded fallback. Read-only writes,
External schema changes, missing External collections, wrong shape, auth failure, timeout, cancellation, and disposed
repositories fail correctively without creating, repairing, deleting, or leaking provider response bodies.

**Complete intent surface:** Package reference; `AddKoan(...)` vector declaration; Qdrant v1.18.3 runtime; optional
endpoint/API-key configuration; optional standard source policy/context; ordinary `Vector<TEntity>` operations. No
Qdrant client, collection bootstrap, schema option, provider DTO, or settling loop is required.

**Public concepts:** `VectorSpacePlan` expresses the application-owned mathematical decision; `QdrantOptions.Endpoint`
and `ApiKey` express irreducible placement/authentication; bounded timeout, metadata, batch, and candidate limits are
operator safety budgets; standard `DataSourcePlan` expresses lifecycle/access. No other Qdrant public concept is
required by the business sentence or guarantee.

**Docs read:**

- `docs/engineering/index.md` — redirects engineering work to the canonical architecture/initiative sources.
- `docs/architecture/principles.md` — requires intent-first APIs, Entity-centric access, thin truthful adapters, and
  host-owned immutable decisions.
- `docs/architecture/data-adapter-development-primer.md` — defines Source Core/Integration and Vector V-01–V-24.
- `docs/reference/ai/vector.md` — defines the compact `Vector<TEntity>` expression and corrective capability posture.
- `docs/decisions/ARCH-0103-aodb-adapter-conformance.md` — establishes source/partition/row isolation and the live
  vector conformance obligation; historical Qdrant implementation notes are evidence, not authority.
- Qdrant v1.18 official collection/search/API/release documentation — establishes named-vector shape, current
  `/points/query`, metric score direction, `wait`, payload filters, UUID/u64 IDs, and the pinned runtime identity.

**Code read:**

- `IVectorSearchRepository.cs`, `VectorSpacePlan.cs`, `VectorSearchRequest.cs`, and `VectorScope.cs` — the ratified
  provider-neutral operation, plan, query, and isolation contracts.
- `VectorService.cs` and `ScopedVectorRepository.cs` — the host-cached plan owner and the single scope-stamping/filter
  composition boundary.
- `SqliteVecAdapterFactory.cs`, `SqliteVecRoute.cs`, and `SqliteVecRepository.cs` — the closest current plan-bound
  reference for source policy, shape validation, outcomes, lifecycle, and fail-closed declines.
- Current Qdrant factory/options/repository/filter/module/health files — harvested failure modes: option/first-write
  schema ownership, removed `/points/search`, unvalidated shape, collection-dropping clear, fabricated batch counts,
  unsafe negative-ID collapse, incomplete current contract, lossy payload conversion, and non-source-aware health.
- Qdrant matrix/AODB tests and the shared conformance kit — the baseline had 65/65 skipped and no V-01–V-24 proof
  bodies, so the old green-looking project provided no current certification evidence.

**Reusing:**

- Already exists: `VectorSpacePlan`, `VectorPoint`, `VectorScope`, `BatchResult`, neutral `VectorMetadata`, source policy,
  routed connection resolution, physical naming, capability vocabulary, participation-aware health, and the shared
  V-01–V-24/G-09 kit.
- Retained provider identity: provider/alias/service metadata, Qdrant v1.18.3, REST port/health route, and the fixed
  UUIDv5 namespace. The namespace remains only because changing an unscoped external key projection silently changes
  stored identity; no old control flow is retained.
- Rebuilt rather than reused: every production Qdrant execution, schema, payload, filter, option, health, discovery,
  module, documentation, and test path.

**Creating new:**

| New code | Location | Justification |
|---|---|---|
| Compact constants and options | `src/Connectors/Data/Vector/Qdrant/Infrastructure/Constants.cs`, `QdrantOptions.cs` | One owner for stable wire names and bounded operator budgets; no schema decisions in options. |
| Source-aware immutable route | `src/Connectors/Data/Vector/Qdrant/Runtime/QdrantRoute.cs` | Resolve endpoint, credential, and `DataSourcePlan` once per source outside operation code. |
| REST boundary | `src/Connectors/Data/Vector/Qdrant/Runtime/QdrantClient.cs` | Own HTTP/JSON/status/retry mechanics once without leaking provider DTOs into the repository. |
| Plan-bound repository | `src/Connectors/Data/Vector/Qdrant/Runtime/QdrantRepository.cs` | Own Qdrant realization of the ratified vector contract and dynamic collection lifecycle. |
| Native payload filter writer | `src/Connectors/Data/Vector/Qdrant/Runtime/QdrantFilter.cs` | Translate only declared filter semantics and fail closed on everything else. |
| Plan-bound factory | `src/Connectors/Data/Vector/Qdrant/QdrantVectorAdapterFactory.cs` | Validate visibility/options and create the route/repository from one immutable plan. |
| Compact discovery/module/health | existing Qdrant discovery, initialization, configurator, and health paths | Preserve Koan activation/discovery/health conventions while removing duplicate semantic controls. |
| Executable DAC-53 ledger | Qdrant VectorAdapterSurface test project | Give every V-01–V-24 and G-09 cell a live proof or an explicit corrective decline. |
| Instruction-first adapter docs | Qdrant `README.md` and `TECHNICAL.md` | State setup, guarantees, limits, and failure behavior without historical narrative. |

**Coalescence:** Closest pattern: the rebuilt SqliteVec adapter. Its decision owner is `VectorSpacePlan`, its source
policy lifetime is repository-wide, its physical name remains operation-dynamic for ambient partitions, and its warm
path is shape-cache plus prepared provider calls. Reuse shared framework laws and source/naming mechanisms; rebuild
Qdrant-specific REST realization. Do not create the proposed generic HTTP-vector superclass: Qdrant request semantics,
score direction, shape, IDs, visibility, and payload filters have a different lifecycle from sibling providers, so
adapter specificity is the correct boundary. The one target owner is `QdrantRepository`; the factory owns only plan/
route validation and `QdrantClient` owns only transport. Delete the legacy repository, quantization object/default,
collection pin, schema/wait/field-name options, Newtonsoft dependency, old matrix/quantization scaffolding, and the
Qdrant-specific pin-warning wiring test whose footgun no longer exists.

**Ergonomics:** Human code reads entirely in Koan language: Source, Vector, Name, Dimensions, Metric, Visibility,
Save, Search, Top, Where. IntelliSense exposes only placement/authentication and bounded safety budgets on Qdrant
options. An agent can implement the adapter by mapping one immutable plan and one neutral point/filter algebra to five
Qdrant operations without learning historical aliases or choosing duplicated schema controls. Cognitive branches are
limited to source policy, collection missing/existing, declared metric, optional scope/filter, and corrective declines.

**Constraints satisfied:**

- Entity-first `Vector<TEntity>` is the public data path; no repository leaks into application code.
- No HTTP application endpoint is introduced; provider HTTP remains an internal adapter client.
- Stable wire identifiers live in project constants; tunable bounds live in typed options.
- No placeholder, compatibility wrapper, commented scaffold, or hidden in-memory query fallback is planned.
- Large unbounded export is declined; search and batches have explicit bounds.
- README, TECHNICAL, initiative card, evidence, and roadmap status will be updated with the implementation.

**Risks:** Live certification requires Docker/Qdrant; skipped provider cells cannot certify this adapter.
Qdrant normalizes cosine vectors on upload, so the payload stores only the original norm and reconstructs the caller's
vector on read rather than duplicating the full embedding. Qdrant batch mutation is not claimed atomic. Dense HNSW is
reported approximate even when a small collection happens to use an exact scan. Stable cutoff ties require bounded
candidate expansion and fail correctively if the configured bound cannot prove the cutoff.

## Execute

1. Delete the old production implementation root and create only the files named in the exploration gate.
2. Implement immutable shape creation/validation, current Qdrant Query API, complete points, native filters, score
   normalization, source policy, hard isolation, truthful bulk outcomes, bounded ties, Session visibility, and failures.
3. Replace the legacy test factory and audit-only AODB subclass with live V-01–V-24/G-09 proofs against
   `qdrant/qdrant:v1.18.3`; include restart, wrong-shape, read-only/External, cancellation, disposal, and warm-path probes.
4. Run the Qdrant suite, Vector regressions, full solution build, Forge, and initiative consistency checks.
5. Write the strict evidence packet; do not mark green for skipped LIVE cells or an absent provider packet.

## Definition of done

- [x] One empty-root implementation passes every advertised Source/Vector claim on Qdrant v1.18.3.
- [x] Every V-01–V-24/G-09 cell has an executable proof or a deliberate corrective decline.
- [x] No schema decision is duplicated in Qdrant options and no legacy execution path remains.
- [x] Visibility, score transforms, collection lifecycle, identity projection, and failure semantics are explicit.
- [ ] Strict Forge has a complete live evidence packet with no provider skip counted as green.

## Verification result

The rebuilt adapter has one plan-bound repository, one REST boundary, one native filter writer, and no retained legacy
execution path. The live pinned provider suite passes 28/28 cells with zero skips. V-08 additionally proves cosine,
Euclidean, and dot-product normalization; V-13 converges every declared filter operator and boolean composition against
the neutral evaluator and proves an undeclared operator fails closed.

| Command | Result |
|---|---|
| `dotnet test ...Qdrant.Tests.csproj --no-restore` | PASS, 28/28, zero skips, live `qdrant/qdrant:v1.18.3` |
| Data Core Vector filter | PASS, 24/24 |
| InMemory Vector suite | PASS, 50/50 |
| SqliteVec Vector suite | PASS, 58 pass; five declared capability skips unchanged |
| `dotnet build Koan.sln --no-restore` | PASS, zero warnings and zero errors |
| strict Forge, Qdrant/vector | all 28 rows PASS; overall DEFERRED only because `evidence/qdrant/conformance.json` is not generated |

Strict status remains `in-progress`, matching DAC-51 and DAC-52: behavioral implementation is complete, while the
shared versioned packet-generation control plane is intentionally outside this adapter rebuild.

## Stop conditions

Unpinned service identity, unavailable LIVE provider, ambiguous score/visibility semantics, external-lifecycle mutation,
scope leakage, unbounded fallback, or fabricated batch/accuracy truth blocks certification.
