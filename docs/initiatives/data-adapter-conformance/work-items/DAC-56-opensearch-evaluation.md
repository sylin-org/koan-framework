---
type: SPEC
domain: data
title: "DAC-56 Rebuild and Certify the OpenSearch Adapter"
audience: [architects, maintainers, developers, ai-agents]
status: in-progress
last_updated: 2026-07-29
framework_version: v0.20.0
validation:
  date_last_tested: 2026-07-29
  status: behavior-pass-strict-deferred
  scope: OpenSearch 3.7.0 live V-01 through V-24/G-09 and SearchEngine retirement
---

# DAC-56 — Rebuild and certify the OpenSearch adapter

| Field | Value |
|---|---|
| Phase / kind | vector / break-and-rebuild-certification |
| Depends on | DAC-54, DAC-55 |
| Primer scope | Source Core, Source Integration, Vector V-01 through V-24, G-09 |
| Production writes | authorized after the exploration gate below |
| Owner | Adapter(OpenSearch); Framework owns SearchEngine retirement |

## Meaningful outcome

OpenSearch realizes one declared Koan vector space through its native k-NN model. An application receives complete
points, lossless metadata, native filtered search, normalized similarity, immediate awaited visibility, source policy,
isolation, and honest partial-batch outcomes without OpenSearch ceremony or an inherited Elasticsearch assumption.

## Exploration gate

**Task:** Delete the OpenSearch implementation and tests, build one plan-bound adapter from the ratified Koan contract
and pinned OpenSearch 3.7.0 behavior, then delete the retired SearchEngine runtime when no production consumer remains.

**Application intent:** Persist and search Entity embeddings in OpenSearch through ordinary `Vector<TEntity>`
operations, using Koan language for every mathematical, lifecycle, and isolation decision.

**Public expression:** Reference `Sylin.Koan.Data.Connector.OpenSearch`, call `AddKoan`, and declare the space once:

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
      "OpenSearch": {
        "Endpoint": "https://search.example.net:9200",
        "Username": "koan-app",
        "Password": "use-a-secret-provider"
      }
    }
  }
}
```

Standard source declarations own source-specific placement, credentials, `StorageLifecycle`, and `Access`. No index
name, prefix, vector field, metadata field, ID field, dimension, metric, engine, method, refresh, or auto-create switch
is ordinary configuration.

**Guarantee/correction:** The factory receives `VectorSpacePlan` before provider I/O. OpenSearch maps Cosine to
`cosinesimil`, Euclidean to `l2`, and unrestricted Koan DotProduct to Lucene `innerproduct`. Native scores are converted
to Koan `[0,1]` higher-is-closer meaning using the documented OpenSearch 3.7/Lucene formulas. Managed may create one
write alias and backing index; External validates only; ReadOnly rejects mutations before dispatch. Existing storage
must match contract version, dimensions, metric, model, engine, method, wire fields, and write-alias shape. Awaited
Session mutations use explicit refresh. Complete points store logical identity, vector, neutral metadata, bounded
filter projection, and scope. Scoped physical IDs cannot collide. Bulk parses every ordered item and reports
`BatchAtomicity.NotGuaranteed`. Search uses native efficient filtering, bounded `k`/tie expansion, stable identity
ordering, and no client-side scan fallback. Unsupported visibility, filters, continuation, hybrid, export, or atomicity
fail before misleading work. Provider bodies and secrets never enter public errors.

**Complete intent surface:** Provider package; `AddKoan(...)`; pinned OpenSearch 3.7.0 service; optional placement,
credentials, timeout, and bounded-work limits; standard source policy; ordinary Vector terminals. OpenSearch clients,
JSON, engine/method mappings, refresh calls, and index administration remain internal.

**Public concepts:** `VectorSpacePlan` and `DataSourcePlan` own semantic decisions. `OpenSearchOptions` owns endpoint,
API-key/basic credentials, timeout, maximum metadata/request/response sizes, batch size, and search bound. No provider
schema or algorithm option is public.

**Docs read:**

- `docs/architecture/data-adapter-development-primer.md` — greenfield boundary, Source Core, V-01–V-24, and G-09.
- `docs/architecture/principles.md` — one decision owner, compile structure once, thin hot operations, honest claims.
- `docs/reference/ai/vector.md` — compact application language and corrective capability boundary.
- DAC-54 and DAC-55 — prove-twice/share-last boundary and live failure lessons without code inheritance.
- Official OpenSearch k-NN spaces, methods/engines, query/filter, alias, bulk, refresh, and mapping documentation.

**Code read:**

- `IVectorAdapterFactory`, `IVectorSearchRepository`, `VectorSpacePlan`, `VectorSearchRequest`, `VectorScope`, neutral
  metadata, batch outcomes, naming, source policy, and participation-aware health.
- Rebuilt Qdrant and Elasticsearch adapters only for responsibility placement and black-box lessons.
- Every current OpenSearch, SearchEngine, and OpenSearch VectorAdapterSurface source file only to harvest provider
  identity, service metadata, native request shape, and failure modes.
- Solution/package/reference inventories to prove SearchEngine has no owner after OpenSearch leaves it.

**Existing pieces:**

- Already exists: immutable Vector plan, source policy/route resolution, neutral metadata/filter algebra, scope,
  capabilities, naming, result/receipt types, health participation, and the executable V/G test kit.
- Needs creation: OpenSearch route, bounded client, native filter compiler, complete repository, compact activation,
  health/discovery, and one pinned live ledger.
- Needs deletion: `OpenSearchDialect`, SearchEngine inheritance/options/telemetry path, old provider test scaffolds, then
  `src/Koan.Data.SearchEngine/**` and its solution/package references after the live replacement is green.

**Creating new:**

| Part | Location | Necessary responsibility |
|---|---|---|
| constants and options | provider root | native vocabulary and bounded operator settings |
| source route | `Runtime/OpenSearchRoute.cs` | one placement/auth/policy decision |
| REST boundary | `Runtime/OpenSearchClient.cs` | bounded HTTP, status, cancellation, and read-only transient retry |
| filter compiler | `Runtime/OpenSearchFilter.cs` | exact neutral Filter subset to native efficient filter; fail closed otherwise |
| repository | `Runtime/OpenSearchRepository.cs` | shape, complete points, receipts, k-NN/score, lifecycle, and disposal |
| activation/health/discovery | existing provider paths | availability, route observation, and redacted startup facts |
| executable ledger | OpenSearch VectorAdapterSurface project | live V-01–V-24/G-09 and provider failure proof |

**Coalescence:** The closest pattern is Elasticsearch's final four-responsibility boundary, not its code or wire model.
OpenSearch gets adapter-specific owners because its mapping, request grammar, candidates, scores, filters, and failures
have distinct provider meaning. The old OpenSearch and SearchEngine paths are `delete`; framework contracts are `keep`;
OpenSearch production is `rebuild`. Only after both providers are live-green may identical provider-free mechanics be
considered for extraction. Zero shared runtime is acceptable, and retaining the dead SearchEngine project is not.

**Exact code placement:** Production changes stay under `src/Connectors/Data/OpenSearch/**` until the replacement is
green. Tests stay under its VectorAdapterSurface project. SearchEngine deletion then touches only
`src/Koan.Data.SearchEngine/**`, `Koan.sln`, and current package/module inventories that directly expose the retired
package. Historical ADRs and assessment evidence remain dated records.

**Ergonomics:** Application code says Source, Vector, Name, Dimensions, Metric, Visibility, Save, Search, Top, and Where.
IntelliSense contains no OpenSearch engine, method, field, index, or refresh branch. An adapter author maps one immutable
plan to one native mapping and operation set; there is no dialect superclass or duplicate option model to reconcile.

**Constraints satisfied:**

- Entity-first Vector is the only ordinary application path; there are no HTTP endpoints.
- Stable wire names live in project constants and safety limits in typed options.
- System.Text.Json and bounded buffers are used; no provider response body is exposed.
- Managed creation is explicit; writes target a validated alias with `require_alias=true`.
- No placeholder, compatibility wrapper, unbounded scan/export, or simulated capability is planned.
- README, TECHNICAL, current initiative truth, package inventory, and solution membership change with behavior.

**Risks:** OpenSearch `innerproduct` scoring differs by engine; the adapter pins Lucene and must validate that choice.
Lucene HNSW treats `k` as its effective candidate control, so a truthful global candidates-considered count may be
unavailable. Efficient filters must accept the bounded neutral projection on 3.7. Restart health can answer before
shards recover. Immediate refresh favors Session latency and correctness but makes high-rate single writes more
expensive; callers should use bulk operations for throughput.

## Execute

1. Empty the OpenSearch production and provider-test roots; preserve package/version identity files only.
2. Create only the runtime parts named above and implement the plan-bound repository contract.
3. Replace tests with one pinned assembly fixture and executable V-01–V-24/G-09 proof.
4. Run the live suite and correct only behavior the provider disproves.
5. Reconcile provider docs and evidence, then delete SearchEngine if the reference inventory is empty.
6. Rerun both Elasticsearch and OpenSearch live suites, Vector regressions, solution build, docs lint, diff hygiene,
   and strict packet validation/defer truth.

## Definition of done

- [x] One empty-root implementation realizes every advertised Source/Vector claim on OpenSearch 3.7.0.
- [x] Cosine, Euclidean, and DotProduct score meaning is proven for the pinned Lucene engine.
- [x] Mapping, engine, lifecycle, scope, bulk, filtering, visibility, restart, failures, and warm path are native.
- [x] Every V-01–V-24/G-09 cell has live proof or an explicit corrective decline.
- [x] OpenSearch has no production dependency on SearchEngine or Newtonsoft.
- [x] The unowned SearchEngine runtime and solution/package surface are deleted without breaking Elasticsearch.
- [ ] Strict Forge has a complete provider packet or remains honestly deferred for the shared packet generator.

## Verification result

The empty-root adapter passed all 28 V/G scenarios against `opensearchproject/opensearch:3.7.0` in 30.199 seconds
with zero failures and zero skips. The suite proves three metrics, complete points, native filter convergence, source
policy, four isolation axes, ordered partial-batch truth, explicit Session visibility, cancellation, restart, disposal,
and bounded warm operations. Comparing the final Elasticsearch and OpenSearch implementations did not reveal a
provider-neutral extraction that reduced the responsibility count, so the unowned SearchEngine package was deleted.
Behavior passes; strict certification remains deferred only because the shared versioned packet generator is absent.

## Stop conditions

Skipped LIVE cells, ambiguous score inversion, implicit index creation, lifecycle mutation on External/ReadOnly,
unbounded provider work, fabricated bulk success, scope leakage, retained legacy execution, or an unowned SearchEngine
runtime blocks completion.
