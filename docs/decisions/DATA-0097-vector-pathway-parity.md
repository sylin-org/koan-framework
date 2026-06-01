---
id: DATA-0097
slug: DATA-0097-vector-pathway-parity
domain: DATA
status: Accepted
date: 2026-05-31
supersedes: [DATA-0056]
relates-to: [DATA-0096, DATA-0054, ADR-0051, ADR-0052, ADR-0053, DATA-0078, DATA-0084, DATA-0087]
---

# DATA-0097: Vector Pathway Parity (break-and-rebuild)

## Scope (read first)

This DDR targets the **vector STORAGE organ** — `Koan.Data.Vector` (`Vector<T>`/`VectorData<T>`
+ the 6 provider adapters): how it stores, kNN-searches, and **filters** pre-computed embeddings.
That layer is, structurally and correctly, a *data-adapter family* (it lives in `Data.*`, is
factory-resolved from entity attributes, capability-flagged, and parallels the LINQ/string query
surface per DATA-0054).

It is **deliberately NOT** the whole vector concern. An AI-surface understanding pass (6-slice
map + synthesis) established that embeddings live in **three decoupled layers**: (1) *production* —
`Client.Embed` emits `float[]`, never touches a store; (2) *lifecycle ownership* — `Koan.Data.AI`'s
`[Embedding]` owns existence/staleness/versioning/re-index and is the only layer that knows the
producing **model/source**; (3) *persistence* — this DDR's `Vector<T>`, which knows nothing of
model/source/staleness. Two of this plan's findings (F8 and the dropped RAG params) are therefore
**not storage-adapter bugs** — they are the **seam** between the embedding-lifecycle owner and the
storage organ, and are carved out to a joint follow-up (see §8). DATA-0097 fixes the organ; the
seam contract co-owns the data flowing through it.

## TL;DR

The vector storage organ predates DATA-0096 and still exhibits the exact bug class that work
eliminated from the entity path: **fail-silent filtering that degrades to an unfiltered full
scan**. For metadata- or tenant-scoped vector search this is a data-leak-shaped correctness
hazard, not a cosmetic gap. This DDR brings it up to par with the entity pillar's
contract: **one Filter AST**, **operator-aware capability negotiation**, **fail-loud errors**, a
**typed filter slot**, and a **convergence/conformance test net** — applying the same
break-and-rebuild freedom (this DDR, with DATA-0096, supersedes DATA-0056).

Greenfield: back-compat is not a constraint beyond the developer-facing DX (`Vector<T>.Search`,
the `[VectorEmbedding]`/`[Embedding]` attributes, the JSON filter shape).

**Koan philosophy is a hard acceptance criterion, not an afterthought** (see §2.2): the happy path
is zero-setup with sane defaults, `Reference = Intent` drives all registration, and the framework
self-reports. "Fail-loud" applies *only* at the genuine capability boundary (an explicitly
unsupported operator on a weak provider) — never as setup friction and never silently. These two
goals are reconciled, not traded off (§2.2).

---

## 1. Findings (verified)

A 6-analyst discovery + adversarial-verify pass (all findings confirmed or "partly" =
mechanism-confirmed/severity-refined) surfaced one root cause with many symptoms: the vector
path was built before the capability-negotiated, fail-loud, typed-filter contract, so it fails
**silent** at every layer.

### Correctness (the real story)

| # | Defect | Evidence | Verdict |
|---|---|---|---|
| F1 | `VectorFilterJson.TryParse` does `catch { return false; }` AND conflates "no filter" with "parse failed" (both return false). Translators map that to "no filter"; Search bodies return the whole set truncated to TopK. A malformed/unsupported/wrong-provider filter silently returns **unfiltered** data. | `VectorFilterJson.cs:11,22,24`; `MilvusVectorRepository.cs:227` | confirmed |
| F2 | Provider `TranslateCompare` switches fall through to a silent `_ => Eq` shape instead of throwing — an unsupported operator silently becomes equality. (Composite-node switches in the *same* files DO throw — inconsistent.) | `QdrantFilterTranslator.cs:97-101` vs `:51`; Milvus likewise | confirmed |
| F3 | `VectorFilterJson.ReadValue` returns `(null,false)` for JSON arrays and the caller discards the `ok` flag → `In`/`Between` from JSON are silently broken. | `VectorFilterJson.cs:96-114,53,55` | confirmed |
| F4 | PGVector is the only vector adapter with **no `*FilterTranslator`**: it `JsonConvert.SerializeObject(options.Filter)` + JSONB `@>`. Passing the typed `VectorFilter` builder serializes the record shape and matches nothing — silently. Equality-only, yet declares the same `Filters` capability. | `PGVectorRepository.cs:351-354` | partly (latent) |
| F5 | PGVector **does not compile** — orphaned `KoanAutoRegistrar` against a removed `ProvenanceModuleWriter` API (unrelated to filtering; pre-existing rot). | `PGVector/Initialization/KoanAutoRegistrar.cs:35-41` | confirmed (build) |
| F6 | ElasticSearch applies the metadata filter as a sibling top-level query rather than a kNN filter → wrong result set. | ES `*VectorRepository.cs` search body | confirmed |
| F7 | Hybrid search (`SearchText`/`Alpha`) is accepted by the universal options record but silently ignored by non-hybrid adapters (no capability gate). | `VectorQueryOptions.cs`; non-hybrid Search bodies | confirmed |
| F8 | Embedding model from `[Embedding(Model=…)]` / migrator `targetModel` is silently dropped on write paths. | `Koan.Data.AI` write paths | confirmed |

### Structural

| # | Issue | Evidence | Verdict |
|---|---|---|---|
| S1 | `VectorCapabilities.Filters` is a single operator-blind bit; never negotiated on the read path. Entity path uses operator-aware `FilterCapabilities`. | `VectorCapabilities.cs:8`; `IVectorCapabilities.cs:3-6` | confirmed |
| S2 | `VectorQueryOptions.Filter : object?` — untyped; 5/6 adapters immediately re-parse it into the typed `VectorFilter` AST; PGVector diverges. | `VectorQueryOptions.cs:10`; `Vector.cs:204`; `IVectorWorkflow.cs:34` | confirmed |
| S3 | ElasticSearch and OpenSearch translators are byte-identical except ~3 lines; repos ~95% identical. | ES/OS `*FilterTranslator.cs`, `*VectorRepository.cs` | confirmed |
| S4 | `VectorQueryResult` lacks the per-axis "what did the adapter handle" envelope of `RepositoryQueryResult`; no `Count` contract; optional ops are throwing default-interface-methods (runtime discovery) not capability flags. | `IVectorSearchRepository.cs:16-73` | confirmed |

### Testing / governance

- **Vector translators have ZERO tests** — the named prerequisite for any safe rewrite.
- PGVector sits outside the DATA-0056/0096 "5 translators" framing — an ungoverned 6th path.
- DATA-0056 is superseded by DATA-0096 (unified Filter AST) + this DDR (the vector collapse).

---

## 2. Target architecture (parity with the entity path)

The entity path's DATA-0096 contract is the template. Vector filtering is legitimately
**schemaless** (no CLR-type binding/coercion — metadata is an arbitrary blob), so we do NOT force
the entity path's *typed* front-end onto it. We DO adopt everything else: one node model, one
operator vocabulary, capability-negotiated pushdown, fail-loud parsing, and a translator+executor
shape.

```
  JSON filter / typed builder ──▶  Filter AST (the ONE unified model, DATA-0096)
                                        │   (schemaless field paths; no FieldPathResolver binding)
                                        ▼
                          VectorFilterCapabilities (per provider, operator-aware)
                                        │   split: pushable ─┬─ residual
                                        ▼                    │
                          IVectorFilterTranslator<TNative>   │  (no in-memory floor for vectors —
                          (Qdrant/Milvus/SearchEngine/…)      │   a residual is a HARD ERROR, see §4)
                                        ▼
                          provider kNN query (+ metadata filter + hybrid)
```

### The decisions

1. **One node model.** Retire `VectorFilter`/`VectorFilterAnd/Or/Not/Compare` +
   `VectorFilterOperator`; the vector path consumes the unified `Filter` AST
   (`AllOf/AnyOf/Not/FieldFilter` + `FilterOperator`). This completes the DATA-0056 collapse.
   - **Schemaless front-end stays separate:** a `VectorFilterReader` lowers the JSON metadata
     filter into `Filter` nodes **without** `FieldPathResolver` (paths are arbitrary metadata
     keys, not CLR members) and **without** `FilterValueConverter` (no leaf type to coerce to).
     This is the one honest divergence from the entity parser, and it is small.

2. **Type the slot.** `VectorQueryOptions.Filter : object?` → `Filter?`. Keep a `string`/`dict`
   convenience overload at the `Vector<T>.Search` facade that calls the reader **once, loudly**.

3. **Operator-aware capabilities.** Replace the single `VectorCapabilities.Filters` bit with a
   `VectorFilterCapabilities` value object (mirrors `FilterCapabilities`): which `FilterOperator`s
   each provider pushes, plus metric/hybrid/continuation facets. Negotiated on the read path.

4. **Fail loud, everywhere.** `VectorFilterReader` distinguishes "no filter" (null input) from
   "filter failed to parse" (throw `FilterParseException`). Translators throw `NotSupportedException`
   on operator/shape they can't render — never a silent `_ => Eq`, never silent match-all. A
   present-but-unpushable filter is an **error**, surfaced as 400 at the web layer (vectors have no
   in-memory residual floor — see §4).

5. **Translator + executor inversion.** `IVectorFilterTranslator<TNative>` declares
   `VectorFilterCapabilities` and renders the pushable filter; the repository executes the kNN +
   native filter. The coordinator splits and, finding a non-empty residual, **fails** rather than
   silently dropping it.

6. **Search-engine dedup.** Elasticsearch and OpenSearch share one `Filter`→query-DSL translator,
   `SearchEngineFilterTranslator` in the `Koan.Data.SearchEngine` assembly (the two engines speak the
   same Apache Lucene query DSL). The translation and the `VectorFilterCapabilities` constant are
   shared; the repositories stay separate (their REST/transport wiring differs), and an `engine` label
   keeps not-supported messages naming the actual adapter.

7. **Result envelope + optional-op honesty.** `VectorQueryResult` gains the per-axis "handled"
   signal (filter pushed? hybrid applied? continuation native?); optional ops (`GetEmbedding`,
   `Flush`, `ExportAll`) move from throwing default-interface-methods to declared capability flags
   so callers negotiate instead of catching at runtime.

---

## 2.2 Koan philosophy compliance (hard acceptance criteria)

Every decision in §2 is constrained by Koan's core tenets. This section is normative — a change
that violates it is not "done."

### Reference = Intent
- Adding a vector provider package (`Koan.Data.Connector.PGVector`, `…Qdrant`, …) **auto-enables**
  it via `KoanAutoRegistrar` — no manual `services.AddVector(...)`, no translator wiring. The new
  `IVectorFilterTranslator` + `VectorFilterCapabilities` are discovered reflectively the same way
  the entity `IQueryRepository` adapters are. (F5's broken `KoanAutoRegistrar` is therefore a
  *philosophy* regression, not just a compile break — fixing it restores Reference = Intent.)
- `[VectorEmbedding]` on an entity is the only intent a developer declares; provider selection,
  capability negotiation, and translator binding all follow from references + config, never code.

### Zero setup, sane defaults (the happy path just works)
- `Vector<T>.Search(queryVector)` with **no filter, no options** must work against every provider
  out of the box — default metric, default TopK, default collection naming (DATA-0087), dimensions
  inferred from the embedding. No required configuration to get a first result.
- The typed `Filter?` slot (§2.2) defaults to `null` = "no filter" = full kNN — the simplest call
  stays the simplest. The `string`/`dict` convenience overload means a developer can pass
  `{ "tenant": "acme" }` without learning the `Filter` builder.
- A brand-new provider with an empty/minimal `VectorFilterCapabilities` still serves unfiltered
  kNN perfectly — capability poverty degrades *features*, never the happy path.

### Fail-loud is reconciled with "just works" (the key tension)
The only place this DDR throws is the **genuine capability boundary**: a caller *explicitly* asked
for an operator a provider *cannot* push (§3 explains why a vector residual can't be silently
floored). That is a real, actionable developer error — surfaced once, at request time, as a clear
400 naming the operator + provider — exactly the DX of the entity path's 400-on-unsupported. It is
**not** setup friction: no filter, supported filter, and unconfigured-but-capable providers all
succeed silently. The thing we delete is the *opposite* of good DX — today's silent match-all
returns wrong/leaky data with no signal, which is the worst possible "default."

### Self-reporting infrastructure
- `VectorFilterCapabilities` is surfaced in the boot report (which operators/metric/hybrid each
  discovered provider supports) and via the well-known capability endpoint, so an operator sees
  what works without reading code — same channel the entity `FilterCapabilities` will use.
- Optional ops (`GetEmbedding`/`Flush`/`ExportAll`) become declared capability flags (§2 item 7),
  so "does this provider support export?" is introspectable, not discovered by catching an
  exception at runtime.

### Multi-provider transparency
- The same `Vector<T>.Search` + same `Filter` corpus returns the same results across providers
  (the §5.4-style convergence gate). Swapping `Qdrant` for `PGVector` is a package reference change,
  nothing else — the parity this whole DDR exists to guarantee.

## 3. Why a residual is an ERROR for vectors (not an in-memory floor)

The entity path evaluates the unpushable residual in memory because it has all candidate rows.
A vector search returns only the top-K nearest by similarity — applying a metadata predicate
*after* kNN would filter a pre-narrowed set and silently under-return (you'd miss matches that
ranked K+1 only because unfiltered neighbors crowded them out). So the vector contract is
stricter than the entity contract: **the metadata filter must be pushed down into the kNN query
or the request fails.** This is why fail-loud matters even more here, and why `VectorFilterCapabilities`
must be honest — an unsupported operator cannot degrade, it must 400.

---

## 4. Change ledger

**Harvest / promote**
- Unified `Filter` AST + `FilterOperator` (DATA-0096) become the vector node model.
- `VectorFilterJson` parse logic → `VectorFilterReader` (schemaless, fail-loud, array-aware).

**Create**
- `VectorFilterReader` (JSON metadata → `Filter`, no CLR binding).
- `VectorFilterCapabilities` (operator-aware, per provider).
- `IVectorFilterTranslator<TNative>` + a `VectorFilterCoordinator` (split + residual-is-error).
- `SearchEngineFilterTranslator` in `Koan.Data.SearchEngine` (the ES/OS shared `Filter`→query-DSL
  translation + capability constant; the repositories stay separate).
- A vector translator **conformance suite** (no container): every translator renders each
  `(Filter node × operator)` to the expected native shape OR throws `NotSupported` — no silent Eq.
- A vector **convergence suite**: a canonical metadata-filter corpus asserts each provider's
  translation matches a reference, mirroring DATA-0096's gate (container-backed specs gated by
  adapter availability; the translation-shape layer runs container-free).

**Change**
- `VectorQueryOptions.Filter` → `Filter?`; `Vector<T>.Search` + `IVectorWorkflow.Query` facade
  overloads call `VectorFilterReader` once.
- All 6 adapters: implement `IVectorFilterTranslator` + declare `VectorFilterCapabilities`; remove
  silent `_ => Eq` arms and null-as-match-all.
- PGVector: **fix the compile break (F5) first**, then give it a real `PGVectorFilterTranslator`
  (it has the richest substrate — full SQL — so it should support the most operators, not the fewest).
- ElasticSearch: fix F6 (filter must be a kNN filter, not a sibling query).
- `VectorQueryResult`: add the per-axis handled envelope.
- *(F8 moved — it is an AI-seam concern, not a storage-adapter change; see §8.)*

**Remove**
- `VectorFilter*` node types + `VectorFilterOperator` + `VectorFilterJson` (after collapse).
- The duplicated OpenSearch translator (folded into the shared `SearchEngineFilterTranslator`; the
  OpenSearch repository stays separate).

**Supersede (docs/canon)**
- DATA-0056 → Superseded by this DDR (with DATA-0096); the vector collapse landed.
- Reconcile ADR-0051 (hybrid) and ADR-0053 (continuation) with the new capability facets.

---

## 5. Test surfaces (the deliverable)

1. **Translator conformance matrix** (no container): `(operator × node-shape × provider)` →
   expected native query OR explicit `NotSupported`. Kills F2's silent-Eq and proves the closed
   operator set is exhaustively handled per provider.
2. **Reader fail-loud specs**: malformed JSON throws; null input = no filter; array RHS produces
   `In`/`Between` (F1, F3). 
3. **Capability-honesty specs**: for each operator a provider declares pushable, a real query
   pushes it (no silent match-all); for each it does NOT, the request fails loud (F4, F7).
4. **Cross-provider convergence** (ARCH-0079, container-gated): one metadata-filter corpus over a
   seeded vector set returns identical id-sets across Qdrant/Milvus/Weaviate/PGVector/ES/OS.
5. **PGVector compile + smoke** (F5): the adapter builds and round-trips an upsert→search.
6. **Hybrid + continuation** specs reconciled with ADR-0051/0053.

---

## 6. Implementation phases

**Phase 0 — Fail-loud baseline.** The fail-silent class this DDR targets is closed at the source:
- PGVector is the *reference* adapter — it compiles, is fail-loud, and has a real
  `PGVectorFilterTranslator` (F4/F5) with container-free conformance specs.
- The reader distinguishes null-input (no filter) from supplied-but-invalid (throws
  `FilterParseException`); `In`/`Between` read their array RHS; unknown operators / malformed JSON
  throw instead of silently mapping to `Eq` or vanishing (F1/F3).
- No translator carries a silent `_ => Eq` default arm: an unsupported operator throws
  `NotSupportedException` naming the operator + field (F2).

> The storage half of this pathway (the filter collapse + capabilities + coordinator — this DDR's P1)
> and the AI filter DX + W4 guard (AI-0036 P1/P2) share one filter model and one coordinator; their
> joint implementation architecture is in [AI-0036 §10](AI-0036-embedding-vector-seam.md).

**Phase 1 — Keystone (the frozen contract).** `VectorFilterReader`, `VectorFilterCapabilities`,
`IVectorFilterTranslator`, `VectorFilterCoordinator`, and the typed `VectorQueryOptions.Filter`,
proven by the container-free conformance matrix + reader specs.

**Phase 2 — Adapter implementations.** Each adapter implements the translator and declares its
`VectorFilterCapabilities`. PGVector has the richest translator (full SQL over JSONB); Elasticsearch
and OpenSearch share the `SearchEngineFilterTranslator` (including ES's kNN-filter fix, F6); Qdrant,
Milvus, and Weaviate (intentionally reduced) round out the set. Conformance + honesty specs cover each.

**Phase 3 — Integration.** The cross-provider convergence suite (container-backed), the result-envelope
+ optional-op capability flags, AI write-path provenance (F8, AI-0036), and hybrid/continuation
reconciliation. This DDR supersedes DATA-0056 and reconciles ADR-0051/0053 with the capability facets.

---

## 7. Open decisions

1. **Residual-is-error vs best-effort post-filter** (§3) — recommend hard error (correctness over
   convenience); a provider that can't push a tenant filter must not silently leak.
2. **PGVector operator scope** — it can support the full set via SQL; recommend making it the
   *reference-rich* adapter rather than equality-only.
3. **Schemaless type hints** — optionally let `[VectorEmbedding]`/metadata attributes declare
   field types so numeric range operators can be validated at parse time. Defer unless cheap.
4. **One `Filter` AST, two readers** — confirm the schemaless `VectorFilterReader` is acceptable
   as a sibling to the typed `JsonFilterParser` (it is the one justified divergence).
5. **Live-store CI** — convergence specs need containers; decide gating (adapter-available skip vs
   required lane) consistent with the entity adapters' ARCH-0079 posture.
6. **Philosophy gate (normative, §2.2)** — every phase ships against this checklist: (a) the
   no-arg `Vector<T>.Search` happy path works on the touched provider with zero config; (b) the
   provider auto-registers via `KoanAutoRegistrar` (Reference = Intent); (c) `VectorFilterCapabilities`
   appears in the boot report; (d) the only new throw is the explicit unsupported-operator boundary,
   surfaced as a clear 400 — never setup friction, never silent. A phase that regresses any of these
   is not complete.

---

## 8. The AI seam (carved out — jointly owned, NOT storage-adapter work)

An AI-surface understanding pass established that the embedding *lifecycle* (which model/source
produced a vector, staleness, versioning) is owned by `Koan.Data.AI`'s `[Embedding]`, while
`Vector<T>` is a model-agnostic store. Two findings originally listed here are seam concerns
between those two layers, not adapter bugs. They are split into a focused follow-up
(**[AI-0036 — Embedding↔Vector seam](AI-0036-embedding-vector-seam.md)**, co-owned by
`Koan.Data.AI` + `Koan.Data.Vector` + the orchestration pillars):

- **F8 — producing model/source not persisted with the vector.** `[Embedding(Model=…)]` and the
  migrator's `targetModel` are dropped on write. Without the producing model recorded alongside
  each vector, provider/model migration cannot verify or selectively re-index. Fix: thread
  model/source into the vector record's metadata at `SaveWithVector` time. *(Belongs to the
  lifecycle owner; the store just persists the extra metadata fields — DATA-0097 already types the
  metadata path, so it is seam-ready, not seam-blocking.)*
- **Retrieval-param forwarding.** `Chain.Retrieve<T>` and agent `{type}_search` accept
  `alpha`/`rerank`/`hybrid`/`filter` in their builders but forward only `topK` to
  `Vector<T>.Search` (`ChainExecutor.cs`, `EntityToolGenerator.cs`). The RAG quality knobs are
  inert. Fix: forward the full option set from the composition boundary down to `Search`. *(This
  becomes mechanical once DATA-0097 gives `Search` a typed filter + honest capabilities to forward
  *into*.)*

Sequencing: DATA-0097 (this DDR) lands the trustworthy storage substrate first; the seam follow-up
then threads model/source + RAG params across it. Neither leaks AI concerns into `Data.*` nor
leaves the seam half-built.

## 9. Where this sits in the Koan AI surface (context, not scope)

The Koan AI surface is a **full-lifecycle, entity-native AI platform**: a mature inference core
(`Client.*`) wrapped by eight bounded-context facades (`Model/Prompt/Compute/Chain/Training/
Dataset/Eval/Review`), whose differentiator is that the same `Entity<T>` is production data,
embedding source, retrieval target, training/eval data, and feedback sink — no ETL. Vectors are
**one organ** of that body — the storage-and-retrieval substrate — not a self-contained pillar.
This DDR makes that organ trustworthy and honest. Layers that sit **above** a corrected vector
substrate and are explicitly **out of scope** here (status: largely aspirational today):

- **Retrieval-quality eval** — `Eval.Metric.*` declares RecallAtK/NDCG/MRR/Faithfulness but
  `ComputeMetric` returns a `0.0` placeholder; retrieval quality cannot yet be gated.
- **Embedding-model training** — `Dataset.From<T>` only hashes the query shape (no materialization)
  and `ITrainingRuntime` adapters do not exist; `DataFormat.Triplet`/`SentenceTransformer` is unbuilt.
- **Deep RAG** — AI-0034 (`Rag.Corpus<T>`, concept graph, multi-vector, agentic retrieval) is
  Proposed; no `Koan.AI.Rag` project exists.

These are noted so the vector substrate is built to *serve* them, but they do not block parity and
are not part of this DDR.
