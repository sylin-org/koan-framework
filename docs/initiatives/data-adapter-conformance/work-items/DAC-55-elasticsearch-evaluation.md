---
type: SPEC
domain: data
title: "DAC-55 Rebuild and Certify the Elasticsearch Adapter"
audience: [architects, maintainers, developers, ai-agents]
status: in-progress
last_updated: 2026-07-28
framework_version: v0.20.0
validation:
  date_last_tested: 2026-07-28
  status: behavior-passed-strict-deferred
  scope: Elasticsearch 9.4.3 live Vector adapter rebuild
---

# DAC-55 — Rebuild and certify the Elasticsearch adapter

| Field | Value |
|---|---|
| Phase / kind | vector / break-and-rebuild-certification |
| Depends on | DAC-54 |
| Primer scope | Source Core, Source Integration, Vector V-01 through V-24, G-09 |
| Production writes | authorized after the exploration gate below |
| Owner | Adapter(Elasticsearch) |

## Meaningful outcome

Elasticsearch realizes one declared Koan vector space through its native indexed-vector model. An application receives
complete points, lossless metadata, native pre-filtered search, normalized similarity, immediate awaited visibility,
source policy, isolation, and honest partial-batch outcomes without Elasticsearch ceremony.

## Exploration gate

**Task:** Delete the Elasticsearch implementation and tests, then build one plan-bound adapter from the ratified Koan
contract and pinned Elasticsearch 9.4.3 behavior. The retired SearchEngine runtime is not an implementation input.

**Application intent:** Persist and search Entity embeddings in Elasticsearch through ordinary `Vector<TEntity>`
operations, using Koan language for every mathematical, lifecycle, and isolation decision.

**Public expression:** Reference `Sylin.Koan.Data.Connector.ElasticSearch`, call `AddKoan`, and declare the space once:

```csharp
services.AddKoan(koan => koan.Data
    .Source("Search")
    .Vector<Article>(space => space
        .Name("content")
        .Dimensions(1536)
        .Metric(VectorMetric.Cosine)
        .Visibility(VectorVisibility.Session)));
```

```json
{
  "Koan": {
    "Data": {
      "ElasticSearch": {
        "Endpoint": "https://search.example.net:9200",
        "ApiKey": "use-a-secret-provider"
      }
    }
  }
}
```

Source-specific placement, credentials, `StorageLifecycle`, and `Access` use the standard source declaration. No index
name, prefix, vector field, metadata field, ID field, dimension, similarity, refresh, or auto-create switch is ordinary
configuration.

**Guarantee/correction:** The factory receives `VectorSpacePlan` before provider I/O. The adapter maps Cosine to
`cosine`, Euclidean to `l2_norm`, and unrestricted Koan DotProduct to `max_inner_product`; it inverts Elasticsearch's
native score formula and applies Koan normalization rather than returning `_score` blindly. Managed may create the
index; External validates only; ReadOnly rejects writes before dispatch. Existing mappings must match dimensions,
metric, model, wire fields, and version marker. Session mutations use an explicit refresh. Complete points store their
logical identity, vector, neutral metadata, filter projection, scope, space, and model. Scoped physical IDs are bounded
deterministic hashes, so equal business keys in different scopes cannot collide. Bulk parses every ordered item and
reports `BatchAtomicity.NotGuaranteed`. Search uses native kNN pre-filtering, bounded candidate expansion, stable
identity tie-breaking, and no unbounded client fallback. Provider errors expose status/category, not response bodies.

**Complete intent surface:** Provider package; `AddKoan(...)`; pinned Elasticsearch 9.4.3 service; optional placement,
credentials, timeout, and bounded work limits; standard source policy; ordinary Vector terminals. Elasticsearch clients,
JSON, mappings, refresh calls, and index administration remain internal.

**Public concepts:** `VectorSpacePlan` and `DataSourcePlan` own semantic decisions. `ElasticSearchOptions` owns endpoint,
API-key/basic credentials, timeout, maximum metadata/request/response sizes, batch size, and candidate bound. There are
no provider schema options.

**Docs read:**

- `docs/architecture/data-adapter-development-primer.md` — greenfield boundary, Source Core, V-01–V-24, G-09.
- `docs/reference/ai/vector.md` — compact Vector application language.
- DAC-54 — independent provider ownership and prove-twice/share-last boundary.
- Official Elasticsearch dense-vector, kNN query, mapping inspection, bulk, refresh, and privilege documentation.

**Code read:**

- `IVectorAdapterFactory`, `IVectorSearchRepository`, `VectorSpacePlan`, `VectorSearchRequest`, `VectorScope`, and
  neutral metadata/outcome types.
- `VectorService`, `ScopedVectorRepository`, `VectorAdapterNaming`, `DataSourcePlan`, and
  `AdapterConnectionResolver`.
- Rebuilt InMemory, SqliteVec, and Qdrant adapters for plan binding, policy, outcomes, bounded transport, and tests.
- Every old Elasticsearch and SearchEngine source and provider test file, only to identify provider identity and
  failure modes.

**Reusing:**

- Framework plan, policy, scope, naming, metadata, capabilities, participation-aware health, and conformance kit.
- Package/assembly/provider identities, `elastic` alias, discovery environment names, service metadata, REST port,
  health route, and pinned Elasticsearch 9.4.3 image.
- Official native facts: `dense_vector`, kNN pre-filter, per-shard `num_candidates`, mapping inspection, ordered partial
  bulk items, `refresh=wait_for`, score formulas, and HNSW approximation.

**Retiring:** `ElasticSearchDialect`, the SearchEngine factory inheritance, SearchEngine option inheritance, Newtonsoft,
the old telemetry wrapper, all legacy repository calls, duplicate schema/refresh/name options, and both old test
scaffolds. OpenSearch remains untouched in DAC-55 and continues to compile against SearchEngine until DAC-56.

**Creating:**

| Part | Location | Necessary responsibility |
|---|---|---|
| constants and options | provider root | stable native vocabulary and bounded operator settings |
| plan-bound route | `Runtime/ElasticSearchRoute.cs` | one source-aware placement/auth/policy decision |
| REST boundary | `Runtime/ElasticSearchClient.cs` | bounded HTTP, JSON, status, cancellation, and narrow retry ownership |
| filter writer | `Runtime/ElasticSearchFilter.cs` | exact declared Filter AST to native pre-filter query; fail closed otherwise |
| repository | `Runtime/ElasticSearchRepository.cs` | mapping, complete points, bulk receipts, search/score, lifecycle, disposal |
| factory/module/health/discovery | existing provider paths | activation, exact route, selection-aware observation, startup projection |
| executable ledger | Elasticsearch VectorAdapterSurface project | live V-01–V-24/G-09 and native failure proof |

**Exact code placement:** Only `src/Connectors/Data/ElasticSearch/**`, its VectorAdapterSurface test project, provider
docs, and DAC-55 initiative truth change in this stage. `src/Koan.Data.SearchEngine/**` and OpenSearch production are
read-only until DAC-56 removes their final relationship.

**Coalescence:** The closest implementation principles are the rebuilt Qdrant adapter's plan-bound route and bounded
REST ownership, not its native request model. Elasticsearch gets its own route/client/filter/repository and no generic
HTTP-vector superclass. Four runtime types are sufficient because transport, filter translation, route policy, and
repository semantics have distinct testable failure boundaries.

**Ergonomics:** Application code says Source, Vector, Name, Dimensions, Metric, Visibility, Save, Search, Top, and Where.
Operator configuration says endpoint, credential, timeout, and bounds. An adapter author maps one immutable plan to one
Elasticsearch mapping and five operation families; there is no dialect indirection or duplicate schema decision.

**Constraints satisfied:**

- Entity-first Vector is the only ordinary data path.
- System.Text.Json and bounded buffers are used; provider bodies and secrets do not enter public errors or logs.
- Index creation is explicit and policy-gated; document APIs cannot auto-create because writes follow validated ensure.
- Stable wire names are constants; safety limits are typed options.
- No compatibility wrapper, shadow path, in-memory search fallback, scroll export, or hybrid approximation is planned.

**Risks:** Elasticsearch 9 uses indexed-vector quantization defaults and approximate HNSW; accuracy must be reported
Approximate. Cosine rejects zero vectors. `max_inner_product` has a piecewise native score. L2 `_score` uses squared
distance. Bulk HTTP success can contain item failures. A provider timeout may leave mutation outcome unknown. Stable
cutoff ties require bounded expansion and corrective failure when the configured maximum cannot prove the boundary.

## Execute

1. Empty the Elasticsearch production and provider-test roots; retain only package/version identity files as needed.
2. Create only the runtime parts named above and implement the full plan-bound repository contract.
3. Replace the tests with one pinned assembly-scoped fixture and executable V-01–V-24/G-09 proofs.
4. Reconcile README, TECHNICAL, package description, capabilities, startup facts, and initiative status.
5. Run the provider suite, Vector regressions, solution build, Forge, docs lint, and strict packet validation.

## Result

The empty-root adapter is one plan-bound repository, one bounded REST boundary, one native filter compiler, one
source-aware route, and compact activation/health/discovery code. The production project no longer references
`Koan.Data.SearchEngine`, Newtonsoft, the dialect hierarchy, or provider-owned vector/schema options.

| Evidence | Result |
|---|---|
| Live Elasticsearch 9.4.3 ledger | 28/28 passed, zero skipped, 17 seconds |
| Filter convergence | every declared operator and boolean composition matched the neutral oracle |
| Metric truth | Cosine, Euclidean, and DotProduct normalization passed |
| Data Core Vector regression | 24/24 passed |
| InMemory Vector regression | 50/50 passed |
| SqliteVec regression | 58 passed; five deliberate capability skips unchanged |
| Provider/test build | zero warnings, zero errors |
| Solution build | zero warnings, zero errors |
| Documentation lint | zero errors; 1,472 existing warnings remain non-gating |
| Strict packet | deferred; the shared versioned packet generator is not yet available |

Live provider feedback rejected three attractive shortcuts. Case-folding logical names require an injective physical
hash suffix; Elasticsearch omits an explicit `object` type from some mapping descriptions; and `flattened` keyed values
cannot realize the declared range contract. The shipped mapping therefore uses one constant-cardinality nested
path/value projection. Explicit refresh also removed the periodic-refresh latency tax: the full ledger fell from 72 to
17 seconds while retaining awaited Session visibility.

## Definition of done

- [x] One empty-root implementation realizes every advertised Source/Vector claim on Elasticsearch 9.4.3.
- [x] Cosine, Euclidean, and DotProduct score meaning is proven, not inferred from ranking.
- [x] Mapping, lifecycle, scope, bulk partial outcomes, filtering, visibility, and failures are native and explicit.
- [x] The Elasticsearch project has no production dependency on `Koan.Data.SearchEngine` or Newtonsoft.
- [x] Every V-01–V-24/G-09 cell has live proof or an explicit corrective decline.
- [x] Strict Forge remains honestly deferred for the shared packet generator; no certificate was synthesized locally.

## Stop conditions

Unpinned provider identity, skipped LIVE cells, ambiguous score inversion, implicit index creation, lifecycle mutation on
External/ReadOnly, unbounded provider work, fabricated bulk success, scope leakage, or a retained legacy execution path
blocks completion.
