---
type: ARCHITECTURE
domain: data
title: "DAC-54 Freeze the SearchEngine Replacement Boundary"
audience: [architects, maintainers, developers, ai-agents]
status: current
last_updated: 2026-07-28
framework_version: v0.20.0
validation:
  date_last_tested: 2026-07-28
  status: passed
  scope: Elasticsearch and OpenSearch greenfield responsibility boundary
---

# DAC-54 — Freeze the SearchEngine replacement boundary

| Field | Value |
|---|---|
| Phase / kind | vector / implementation boundary |
| Depends on | DAC-51, DAC-52, DAC-53 |
| Primer scope | Source Core, Vector V-01 through V-24, G-09 |
| Production writes | forbidden in this card |
| Owner | Framework(Vector contract), Adapter(Elasticsearch), Adapter(OpenSearch) |

## Meaningful outcome

An application declares one vector-space decision and receives the same truthful Koan result from Elasticsearch or
OpenSearch. Each adapter realizes that decision through its provider's native model. Similar-looking REST endpoints do
not create a shared semantic owner.

## Exploration gate

**Task:** Decide the smallest justified production boundary before either provider is rebuilt from an empty
implementation root.

**Application intent:** Use Elasticsearch or OpenSearch through the ordinary `Vector<TEntity>` surface without
choosing native field names, mappings, score formulas, candidate controls, refresh modes, or index lifecycle switches.

**Public expression:** The provider changes; the application language does not:

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

Ordinary provider configuration contains placement, authentication, readiness, and bounded operator safety limits.
The immutable `VectorSpacePlan` owns name, dimensions, metric, model, source, and visibility. `DataSourcePlan` owns
storage lifecycle and access.

**Guarantee:** Each adapter validates the complete existing physical shape before use; Managed alone may create it;
External never creates or repairs it; ReadOnly rejects writes before provider mutation. Complete points and neutral
metadata round-trip losslessly. Awaited Session mutations are visible to subsequent reads. Search similarity is finite,
normalized to `[0,1]`, higher means closer, and reports native accuracy/candidate facts honestly. Scope, source,
partition, and logical space cannot collide. Bulk results preserve input order and expose every item outcome.
Unsupported visibility, filter, continuation, hybrid, export, or atomicity claims fail closed.

**Complete intent surface:** One provider package; `AddKoan(...)`; one source-owned vector declaration; optional exact
endpoint and credentials; standard source policy; ordinary `Vector<TEntity>` terminals. Provider clients, index
bootstrap, JSON request bodies, mapping switches, field names, refresh flags, and score conversion are not application
concepts.

**Public concepts:** `VectorSpacePlan`, `DataSourcePlan`, `VectorPoint`, `VectorScope`, `VectorSearchRequest`,
`VectorSearchResult`, `BatchResult`, and the provider's compact placement/authentication options. No SearchEngine
repository, dialect, or vector-options abstraction belongs in the public application surface.

**Docs read:**

- `docs/architecture/data-adapter-development-primer.md` — Source Core, greenfield boundary, and V-01–V-24.
- `docs/reference/ai/vector.md` — compact provider-neutral Vector language.
- Elasticsearch dense-vector, kNN, bulk, mapping, refresh, and privilege documentation for the pinned server line.
- OpenSearch vector-space, engine/method, efficient-filter, bulk, refresh, and mapping documentation for the pinned
  server line.

**Code read:**

- Framework vector plan, service, scoped repository, naming, neutral metadata, and current repository interface.
- Rebuilt InMemory, SqliteVec, and Qdrant implementations as current plan-bound examples.
- Every source file in `Koan.Data.SearchEngine`, both provider roots, and both VectorAdapterSurface projects.
- Current initiative packet placeholders, public product claims, and historical family-consolidation decisions.

**Harvested failure modes:**

- The shared factory uses the legacy source-only creation overload, so the repository never receives
  `VectorSpacePlan`.
- The shared repository implements legacy tuple operations and leaves the ratified complete-point, scoped batch,
  current search-result, clear, sync, lifecycle, and disposal contract on interface defaults.
- Options duplicate plan-owned name, dimensions, metric, and lifecycle decisions and add provider field names and
  refresh behavior to the normal surface.
- Existing shape is treated as valid after a successful index probe; dimensions, metric, model, fields, and managed
  metadata shape are not validated.
- Bulk paths return input counts even when native items fail and log provider response bodies.
- Clear/flush drops an index or issues unscoped delete-by-query without source-policy enforcement.
- IDs omit row scope, metadata filtering depends on incidental dynamic mappings, errors expose full provider bodies,
  candidate work is invented, scores are returned without Koan metric normalization, and caches are not host-bounded.
- The existing provider suites primarily exercise the retired surface and advertise capabilities that do not prove
  V-01–V-24.

**Provider deltas that remain local:**

| Concern | Elasticsearch | OpenSearch |
|---|---|---|
| vector mapping | `dense_vector`, `dims`, Elastic similarity/index options | `knn_vector`, `dimension`, engine/method/space type |
| kNN request | Elastic kNN query/top-level grammar and `num_candidates` | OpenSearch `query.knn.<field>` grammar and engine-specific controls |
| metric realization | cosine, squared-L2 score, and max-inner-product/dot semantics | `cosinesimil`, squared `l2`, and Lucene `innerproduct` score transforms |
| filtering | Elastic kNN pre-filter contract | OpenSearch engine/version-dependent efficient filtering |
| lifecycle and failures | Elastic mappings, privileges, security, version errors | OpenSearch plugin, engine, mappings, security, and version errors |

Elasticsearch computes cosine scores as `(1 + cosine) / 2` and L2 scores from squared distance. OpenSearch documents
different formulas per space and engine, including a piecewise Lucene inner-product score. Returning `_score` unchanged
would therefore make the same Koan metric mean different things.

**Reusing:** Framework plan/policy/scope/naming/metadata/outcome contracts, provider package identities, service
metadata, discovery vocabulary, pinned image identities, and black-box failure observations. These are contracts and
facts, not an implementation structure.

**Creating:** DAC-55 and DAC-56 each create a compact plan-bound route, native HTTP boundary, repository, native filter
writer, activation/health path, and executable provider fixture. The providers are implemented independently.

**Coalescence decision:** `Koan.Data.SearchEngine` is not a production superclass for the replacements. Its current
three-member dialect seam hides materially different schema, scoring, filtering, lifecycle, and failure behavior behind
one large legacy repository. DAC-55 does not consume that runtime. DAC-56 does not copy DAC-55. After both providers are
green, repeated mechanical code may be extracted only when removing either provider name leaves the abstraction
complete and neither provider loses a native guarantee. Zero shared runtime code is an acceptable final result.

**Exact implementation order:**

1. Rebuild Elasticsearch independently under DAC-55 while the old package remains only for the still-unrebuilt
   OpenSearch adapter.
2. Rebuild OpenSearch independently under DAC-56.
3. Remove the retired SearchEngine runtime and its package references when no production consumer remains.
4. Extract only repetition demonstrated by the two final implementations, then rerun both live suites if extraction
   changes production.

**Ergonomics:** Human code contains Source, Vector, Name, Dimensions, Metric, Visibility, Save, Search, Top, and Where.
An adapter author translates one immutable plan and neutral point/filter algebra into one provider. There is no dialect
inheritance tree to understand and no duplicate schema option to reconcile.

**Risks:** Elasticsearch and OpenSearch are both distributed, near-real-time stores but do not have interchangeable
vector semantics. Version drift can silently change defaults such as quantization, candidate work, or engine support.
The provider fixtures must pin versions, validate mappings, use bounded response/request sizes, and prove score
transforms rather than relying on ranking alone.

## Definition of done

- [x] Framework, provider, and possible family responsibilities have one explicit owner.
- [x] The current shared repository is classified as retirement input, not a base to repair.
- [x] Native mapping, score, filter, lifecycle, and failure deltas remain provider-owned.
- [x] DAC-55 and DAC-56 are authorized as independent empty-root rebuilds.
- [x] No provider production code changed in this gate.

## Verification result

The source inventory, current Vector contract, three rebuilt reference adapters, provider fixtures, and official native
documentation were compared. The current family package cannot express the ratified contract and its apparent code
similarity does not establish semantic sameness. The replacement boundary is therefore **prove twice, share last**.
