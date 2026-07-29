---
type: SPEC
domain: data
title: "DAC-51 Rebuild and Certify the InMemory Vector Adapter"
audience: [architects, maintainers, developers, ai-agents]
status: implementation-complete
last_updated: 2026-07-28
framework_version: v0.20.0
validation:
  date_last_tested: 2026-07-28
  status: behavior-green-packet-pending
  scope: greenfield InMemory Vector replacement, ratified Vector execution seam, V-01 through V-24, and inherited isolation
---

# DAC-51 — Rebuild and certify the InMemory Vector adapter

| Field | Value |
|---|---|
| Phase / kind | vector / whole-adapter greenfield rebuild |
| Depends on | DAC-30 |
| Primer scope | Source Core, Vector Core, Vector Filters, Vector Bulk, Managed Vector Lifecycle, Vector Isolation |
| Production writes | InMemory Vector connector, the minimum shared Vector contract/runtime projection, focused Vector TestKit cases, docs, and DAC-51 evidence |
| Owner | Framework(Vector plan and semantic boundary); Adapter(InMemory Vector mechanics) |

## Meaningful outcome

InMemory Vector is the fast, infrastructure-free semantic oracle for exact vector behavior. It makes no durability,
external-provider, approximate-index, snapshot-continuation, hybrid-search, or provider-native inspection claim.

## Approved greenfield exploration

**Task:** Replace the InMemory Vector connector from an empty implementation root and close only the shared Vector
contract gaps required to execute the human-ratified DAC-49 language.

**Application intent:** Store and search embeddings in-process through the ordinary Koan Vector surface, with
deterministic similarity, exact result truth, immediate session visibility, and no hidden durability or cross-process
claims.

**Public expression:** Reference `Sylin.Koan.Data.Vector` and
`Sylin.Koan.Data.Vector.Connector.InMemory`, call `AddKoan`, declare one immutable source-owned space, and use
`Vector<TEntity>` terminals:

```csharp
builder.Services.AddKoan(koan =>
{
    koan.Data.Source("Semantic").Vector<Document>(space => space
        .Name("documents")
        .Dimensions(1536)
        .Metric(VectorMetric.Cosine)
        .Visibility(VectorVisibility.Session));
});

await Vector<Document>.Save(document.Id, embedding, new { document.Category }, ct);
VectorPoint<string>? stored = await Vector<Document>.Get(document.Id, ct);
VectorSearchResult<string> related = await Vector<Document>.Search(
    embedding,
    query => query.Top(12).Where(filter).AtLeast(.82),
    ct);
bool removed = await Vector<Document>.Delete(document.Id, ct);
```

The runtime prerequisite is one Koan host. Storage is host-owned and disappears at disposal. `EntityContext.Source`
or a compiled Database axis overrides the declared source; otherwise one unambiguous Entity declaration supplies it.

**Guarantee/correction:** A successful save atomically replaces one complete point and is immediately visible to
subsequent get/search calls in the same source. Search returns unique results ordered by descending finite normalized
similarity and stable identity, reports `Exact`, and never fabricates a total or continuation. Empty, non-finite,
wrong-dimension, unknown-space, read-only mutation, external lifecycle mutation, capacity overflow, and unsupported
query clauses reject before mutation with a corrective exception. A missing or ambiguous space declaration rejects
before adapter creation.

**Complete intent surface:** Package references, one `AddKoan` call, one source/space declaration, optional source or
partition context, and the `Save`, `Get`, `Delete`, `Search`, `Clear`, `Sync`, and batch terminals above are complete.
There is no provider registration call, connection string, index-management API, retry loop, or disposal ceremony.

**Public concepts:** `Source` chooses policy/routing; `Vector<TEntity>` selects the Entity facet; `Name`, `Dimensions`,
`Metric`, and `Visibility` are physical-semantic guarantees that cannot be inferred safely; `VectorQuery` expresses
only `Top`, `Where`, `Space`, `AtLeast`, `Text`, `SemanticWeight`, and `After`; `VectorPoint`, `VectorMatch`, and
`VectorSearchExecution` expose portable value and execution truth. InMemory earns `Top`, `Where`, `Space` when it
matches the one declared space, and `AtLeast`; it declines `Text`, `SemanticWeight`, `After`, Eventual visibility,
multi-space, atomic batch, and export.

**Docs read:**

- `docs/engineering/index.md` redirects contributor law to current owners; relevant as a compatibility pointer only.
- `docs/architecture/principles.md` requires intent-first APIs, one semantic owner, immutable plans, thin adapters,
  truthful capabilities, and explainable corrective failures.
- `docs/toc.yml`, root `README.md`, and `samples/CATALOG.md` establish the current documentation front door and the
  Entity-centered local-first posture; none grants Vector-specific semantics.
- `docs/architecture/data-adapter-development-primer.md` is the sole normative Source/Vector contract and owns V-01
  through V-24.
- DAC-49 ratifies the exact Vector language; DAC-50 projects it into Forge/TestKit and forbids a second catalog.
- `docs/reference/ai/vector.md` is user-facing but still describes the pre-annex result/query shape and must be
  reconciled after executable behavior changes.

**Code read:**

- `Vector.cs` and `VectorData.cs` expose the legacy static facade, process/flow host lookup, dictionary metadata,
  fabricated count returns, `Flush`, and positional query surface; they are a compatibility inventory, not authority.
- `IVectorSearchRepository`, `VectorQueryOptions`, and `VectorQueryResult` expose the pre-annex provider contract and
  lack complete point retrieval, positional get-many, immutable space plans, source policy, and execution truth.
- `VectorService` owns provider election and host disposal but caches only entity/key/source and does not bind the
  ratified vector-space declaration or source plan.
- `ScopedVectorRepository` is the closest framework semantic boundary for segmentation and guards, but its own comment
  records unclosed by-id/admin isolation gaps; it must be absorbed into one current Vector execution boundary.
- The retired InMemory repository stores caller arrays/metadata by reference, ignores cancellation and dimensions,
  emits negative raw cosine scores, lacks stable tie ordering, treats invalid continuations as offset zero, enumerates
  a non-snapshot concurrent dictionary, and overclaims hybrid, continuation, export, and dynamic lifecycle behavior.
- The current InMemory suite passes 34 legacy cases and skips all 24 V-cells. That 34/24 baseline is frozen as
  black-box evidence, not certification.

**Provider facts:** `TensorPrimitives.CosineSimilarity` requires non-empty equal-length spans and returns `NaN` for
non-finite elements; exact results may vary slightly by architecture. `ConcurrentDictionary` enumeration is safe but
not a moment-in-time snapshot, so it cannot underpin a snapshot continuation or deterministic concurrent oracle.
The replacement therefore validates before dispatch and ranks a point-in-time immutable snapshot.

**Reusing:** `DataSourcePlan.Demand` for first-boundary policy, `RoutedSource` for source axes, `DataObject`/`DataArray`
for neutral metadata, `Filter` plus the shared dictionary evaluator for the exact filter oracle, `VectorCaps` and the
one conformance manifest for claims, `TensorPrimitives` for SIMD numeric kernels, and ordinary Koan module/provider
election. Existing InMemory adapter implementation code is not reused.

**Creating new:**

| New code | Location | Justification |
|---|---|---|
| Vector metric, visibility, point, result, execution, request, batch, and immutable space-plan contracts | `src/Koan.Data.Vector.Abstractions/` | provider-neutral meanings consumed by every later Vector adapter |
| `VectorSpaceBuilder<TEntity>` and host-owned declaration catalog | `src/Koan.Data.Vector/Composition/` | compile the application decision once in the Vector pillar |
| `VectorQuery` and metadata materializer | `src/Koan.Data.Vector/Querying/` and `Runtime/` | one compact query grammar and one neutral metadata owner |
| plan-bound source-policy execution boundary | `src/Koan.Data.Vector/Runtime/` | one unavoidable Framework gate before provider creation/I/O |
| typed memory limits | `src/Connectors/Data/Vector/InMemory/InMemoryVectorOptions.cs` | bound spaces, points, dimensions, and metadata shape explicitly |
| immutable route and bounded exact store | `src/Connectors/Data/Vector/InMemory/Runtime/` | adapter-owned in-process mechanics and hot-path state |
| executable DAC-49 cells | InMemory Vector test project and shared TestKit proof hooks only where required | replace loud skips with real adapter/framework evidence without a second catalog |

**Coalescence:** Closest pattern is `VectorService` plus `ScopedVectorRepository`; current decision ownership is split
between static facades, service, decorator, schema registry, and adapter. Disposition is `REBUILD` for the adapter,
`ABSORB` for plan/policy/metadata/query shaping into one Vector-pillar boundary, and `RETIRE` for `Flush`, fabricated
totals/counts, dictionary get-many, raw scores, offset continuations, and adapter-local semantic validation. The Vector
pillar is the target owner because these meanings cross providers; Data Core is too broad and InMemory is too narrow.
The adapter retains only exact bounded storage and metric mechanics. Legacy public overloads may temporarily forward
to the one new execution path while downstream vector consumers are converted; no legacy provider logic, secondary
store, fallback result shape, or alternate InMemory control flow remains.

**Ergonomics:** The application reads as one source decision and four ordinary verbs. IntelliSense exposes only
semantic clauses, not vector-store clients or index dialects. The query builder avoids positional optional arguments;
the result says `Similarity`, `Items`, and `Execution` without provider score vocabulary. Unsupported capabilities are
visible as corrections, not null conventions or silent degradation.

**Constraints satisfied:**

- Entity remains the model center; Vector is its explicit similarity facet.
- No HTTP endpoint is added.
- Stable identifiers live in project constants; tunables live in typed options.
- Structure is compiled once; warm paths perform no provider election, reflection, policy recomputation, or metadata
  shape rebuild.
- InMemory declines streaming/export; exact search is explicitly bounded by configured points and `Top`.
- README, TECHNICAL, reference docs, claims, and the DAC card change with executable behavior.

**Risks:** Changing the shared Vector contract affects every later provider and the AI reflection consumers. Keep one
forwarding facade during the fleet transition, prove it reaches the same execution boundary, and remove it when the
last provider migrates. Exact brute-force search is O(points × dimensions); capacity is therefore a semantic bound,
not a tuning suggestion. Neutral POCO metadata compilation must be host-scoped and bounded. `Clear` is data mutation,
not lifecycle mutation; External+ReadWrite may clear while ReadOnly may not. InMemory cannot prove restart durability,
provider faults, network timeout, or provider-native inspection and must decline those rows.

## Greenfield boundary

Remove every current InMemory Vector implementation body before authoring the replacement. Retain package identity,
provider name/aliases/priority, module activation, and System.Numerics.Tensors only because those are ratified external
contracts or measured provider facts. The retired type graph, stores, continuation, hybrid scorer, instruction switch,
and test-factory capability claims are forbidden implementation inputs.

## Verification

- Build Vector Abstractions, Vector, InMemory Vector, and the InMemory Vector TestKit cell with zero warnings.
- Execute every applicable V-cell and every claimed Source Core/isolation/filter/bulk cell without skips.
- Prove unsupported clauses, policy postures, lifecycle, durability, continuation, hybrid, export, and atomic batch
  decline before mutation/provider work.
- Run concurrency, host-isolation/disposal, cancellation, capacity, deterministic-oracle, mutation, and warm-path probes.
- Run strict Forge/packet validation and the full solution build; record any later-provider runtime regressions as
  explicit deferred consumer conversions rather than weakening the current contract.

### Result

- `dotnet build Koan.sln --no-restore`: **green**, zero warnings.
- InMemory Vector surface: **50 passed, 0 failed, 0 skipped**.
- Forge `vector/InMemory`: **green** for V-01 through V-24 and inherited G-09 cases.
- Data AI: **87 passed, 0 failed, 0 skipped**.
- Tenancy: **87 passed, 0 failed, 0 skipped**, including non-equality vector filtering.
- Data Core: **468 passed**; four unrelated host-ownership tests are blocked by Windows Event Log permissions in the
  execution environment. The focused Vector slice is **20 passed, 0 failed, 0 skipped**.
- The versioned strict evidence packet remains intentionally unsynthesized. Behavioral implementation did not grow a
  second evidence model merely to turn generated pending stubs green; packet generation remains a conformance
  control-plane task.

## Definition of done

- [x] The retired InMemory implementation is absent and one new storage/execution path remains.
- [x] The ratified public Vector expression compiles and executes through one immutable plan.
- [x] Every claimed Source/Vector cell has executable evidence with no certification skips.
- [x] Every unclaimed capability rejects correctively and claims/docs/runtime agree.
- [x] InMemory is designated the exact ephemeral semantic oracle, not a durable/native-provider gold reference.
- [x] Full solution build and scoped diff/retirement checks are green.
- [ ] Strict Forge packet validation is green.

## Stop conditions

A second semantic catalog, unbounded store/cache, process-global mutable adapter state, raw provider score, mutable
caller-owned payload, non-snapshot continuation, unbounded fallback, policy check after provider creation, or need to
preserve legacy InMemory control flow stops work.
