# DATA-0103: Search-engine vector connectors — shared core behind a 3-member dialect seam

**Status**: Accepted (2026-06-17)
**Date**: 2026-06-17
**Deciders**: Enterprise Architect
**Scope**: Resolves assessment card **E6** (Track E). The Elasticsearch and OpenSearch vector connectors share a single base, `SearchEngineVectorRepository<TEntity,TKey>`, in the already-existing `Koan.Data.SearchEngine` assembly, behind a 3-member `ISearchEngineDialect` seam. Both thin per-backend packages remain. `ExportAll`/`GetCount`/`IndexStats` are implemented once in the base, closing the OpenSearch capability gap.
**Supersedes**: **DATA-0097 §6** (see below).
**Related**: **DATA-0097** (vector pathway parity — `SearchEngineFilterTranslator`, the precedent shared component) · **ARCH-0084** (unified capability model — caps are honest self-report) · **DATA-0078** (vector export capabilities) · **ARCH-0079** (integration tests as canon — the VectorAdapterSurface matrix is the regression net).

---

## Context

Cartography (and the `es-os-merge` evidence card) flagged that the ElasticSearch and OpenSearch vector connectors are ~80% duplicate code. Re-derivation by diffing the two repositories confirms it empirically:

- **Normalized diff: ~79% identical** (≈300 changed of 1427 ES-side lines; per file: `VectorRepository` 235/704, Registrar 19/182, Discovery 16/129, Options 14/49, Factory 14/62; HealthContributor / Constants / Telemetry / csproj 0 changed).
- The byte-identical part is **the entire REST/transport skeleton**: `HttpClient` config + auth, the NDJSON `_bulk` envelope, `_doc` PUT/DELETE, `_delete_by_query`, the probe-then-create `EnsureIndex` flow, hit parsing, `Flush`, `IndexName` computation, id normalization.
- Only **three things genuinely differ**, and ~144 of the changed lines are not even dialect — they are a one-sided **feature gap** (ES has `ExportAll`/`GetCount`/`IndexStats`; OpenSearch does not). The genuine dialect surface is ~70–90 lines per side, confined to two JSON body builders and one token mapper.

The twin-maintenance process had already produced real bugs (all observable in `git`, not hypothetical):

1. **Dual-`elastic` provider cross-claim.** `OpenSearchVectorAdapterFactory.CanHandle` also returned `true` for provider `"elastic"`, at the **same** `ProviderPriority(20)` as the ES factory → ambiguous resolution when both packages are referenced.
2. **OpenSearch capability gap.** OS lacked `ExportAll`/`GetCount`/`IndexStats` and fell back to the throwing default-interface-method whose message tells users to switch to ElasticSearch — even though **OpenSearch supports the identical scroll + `_count` API**. Vector-migration/stats capability silently lost.
3. **Options drift.** ES carried dead `DefaultTopK`/`MaxTopK` knobs (referenced nowhere, aliased into `DefaultPageSize`); OS carried a plain `DefaultPageSize = 50`.
4. **Provenance drift.** The ES registrar emitted per-setting `sourceKey` boot-report provenance; the OS registrar was never back-ported (only its `ConnectionString` setting had it).
5. **Collision in discovery aliases.** Both discovery adapters claimed the generic alias `"search"` (ES: `elastic/es/search`; OS: `open-search/os/search`) — an ambiguous alias for two distinct backends on the same default port.
6. **The OS code literally said `(same fix as Elasticsearch)`** — manual fix-mirroring was the process.

---

## Decision

### 1. Extract the shared skeleton into `Koan.Data.SearchEngine`

`SearchEngineVectorRepository<TEntity,TKey>` (public, sealed) is the former `ElasticSearchVectorRepository` (the **superset**, with `ExportAll`/`GetCount`/`IndexStats`) renamed and parameterized by a dialect. It implements `IVectorSearchRepository<TEntity,TKey>`, `IDescribesCapabilities`, `IInstructionExecutor<TEntity>`. The constructor takes `(HttpClient, ISearchEngineVectorOptions, ISearchEngineDialect, ActivitySource, ILogger?, IServiceProvider)`. Every `ElasticSearchTelemetry.Activity` became the injected `ActivitySource`; every `"Elasticsearch"`/`"ElasticSearch"` error literal became `dialect.EngineLabel`. The project already had `SearchEngineFilterTranslator` (DATA-0097) and both connectors already referenced it — **no project-count change, no new project.**

### 2. The dialect seam is exactly three members (+ a label)

```csharp
public interface ISearchEngineDialect {
    string EngineLabel { get; }   // "Elasticsearch" | "OpenSearch" — error messages + the filter-translator engine arg
    JObject BuildSearchRequestBody(float[] query, int topK, JToken? filter, ISearchEngineVectorOptions opts);
    JObject BuildIndexBody(int dimension, string mappedSimilarity, ISearchEngineVectorOptions opts);
    string MapSimilarityToken(string metric);
}
```

- **ES**: top-level `knn {field, query_vector, k, num_candidates}` + (filter) `knn.filter` (DATA-0097 F6 pre-filter); `dense_vector {dims, index, similarity}`; `MapSimilarityToken` = identity.
- **OS**: `query.knn.<field> {vector, k}` + (filter) `filter.bool.filter`; `settings.index.knn=true` + `knn_vector {dimension, method{hnsw,lucene,space_type}}`; `MapSimilarityToken` = the `MapSpaceType` switch.

The base translates the metadata filter once (`SearchEngineFilterTranslator.TranslateWhereClause(..., dialect.EngineLabel)`), hands the result to `BuildSearchRequestBody`, and adds `timeout` afterwards — so the dialect never touches the filter translator or the timeout. **Auth is NOT part of the seam** — `ConfigureHttpClient` is identical and stays shared (see §5).

### 3. Implement `ExportAll` / `GetCount` / `IndexStats` once — closing the OS gap

These live in the base. OpenSearch supports the identical scroll (`_search?scroll`) and `_count` APIs, so the same implementation works for both engines. This **fixes** the OS gap rather than preserving it, and aligns with DATA-0097 §7's capability-flag direction. The VectorAdapterSurface TestKit gains `ExportAll`/`Stats` specs (gated by `SupportsExportAll`); the OpenSearch cell flips `SupportsExportAll => true`, so the matrix now proves OS exercises both surfaces green through the shared code.

### 4. Keep both thin packages

Reference = Intent plus per-backend `[KoanService]` orchestration metadata (different `ContainerImage`/`DefaultTag`/`Env`: `docker.elastic.co/elasticsearch:8.13.4` + `xpack.security.enabled=false` vs `opensearchproject/opensearch:2.13.0` + `DISABLE_SECURITY_PLUGIN=true`) require **two** packages. Each keeps only: the `ISearchEngineDialect` impl, `[KoanService]` metadata + provider shortCode + `CanHandle`, the discovery adapter, telemetry/constants, the options-binding path, and the registrar describe entries. The options classes extend a shared `SearchEngineVectorOptions` base; the concrete classes carry only `ConnectionString` + `Readiness`.

### 5. Fix the drift bugs in the same move

1. Removed `"elastic"` from `OpenSearchVectorAdapterFactory.CanHandle` (now only `"opensearch"`).
2. Deleted dead `DefaultTopK`/`MaxTopK` from ES options; unified `DefaultPageSize` onto the single plain `= 50` semantics in the shared base.
3. Back-ported the per-setting `sourceKey` boot-report provenance to the OS registrar.
4. Dropped the generic `"search"` discovery alias from **both** adapters (kept on neither — each retains only its unambiguous aliases). No usable generic alias is lost because there is no unambiguous owner.
5. The `ApiKey` Authorization branch in the now-shared `ConfigureHttpClient` is **kept identical**, with a code comment noting it is ES-semantics (OpenSearch uses Basic / the security plugin). It is harmless when `ApiKey` is unset (the default), and diverging the shared method would re-introduce the very drift this DDR collapses. No positive evidence was found that OS needs a different scheme on the happy path.

---

## Supersession of DATA-0097 §6

DATA-0097 §6 (`DATA-0097-vector-pathway-parity.md:138-142`) deliberately kept the two repositories separate, with the rationale:

> "the repositories stay separate (their REST/transport wiring differs)"

**That rationale is empirically false and is hereby superseded.** The REST/transport wiring is precisely the **byte-identical** part (HttpClient config, auth, NDJSON bulk envelope, `_doc` PUT/DELETE, `_delete_by_query`, probe-then-create index flow, hit parsing). Only the two JSON body builders and the one similarity-token mapper differ — exactly the three members of `ISearchEngineDialect`. DATA-0097's own S3 finding ("repos ~95% identical", `:78`) already contradicted its §6 conclusion. DATA-0097 §6's *decision to share the filter translator* stands and is extended; only its *decision to keep the repositories separate* is reversed.

---

## Consequences

- **~1.0–1.1k LOC removed**, zero project-count change.
- **Two real bugs fixed** (dual-`elastic` cross-claim; ambiguous `search` alias) and **OpenSearch gains export/stats parity**.
- The existing VectorAdapterSurface TestKit matrix (which already runs both backends through real `AddKoanDataVector()` discovery) is the regression net for the refactor (ARCH-0079).
- Future search-engine backends (e.g. a hypothetical third Lucene-family engine) need only a ~80-line `ISearchEngineDialect` + a thin package, not a fourth copy of the skeleton.

---

## Verification

- `dotnet build Koan.sln` — 0 errors.
- Both `VectorAdapterSurface` Testcontainers suites (ES `docker.elastic.co/elasticsearch:8.13.4`, OS `opensearchproject/opensearch:2.13.0`) green, including the new `ExportAll`/`Stats` specs that prove the OS gap closed.
- Grep gates: `grep -n elastic OpenSearchVectorAdapterFactory.cs` → 0; `grep -rn "DefaultTopK\|MaxTopK" src` over the ES connector → 0.
