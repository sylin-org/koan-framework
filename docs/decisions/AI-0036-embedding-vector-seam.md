---
id: AI-0036
slug: AI-0036-embedding-vector-seam
domain: AI
status: Accepted
date: 2026-05-31
fulfills: [DATA-0097]
relates-to: [DATA-0097, DATA-0054, DATA-0084, DATA-0087]
co-owners: [Koan.Data.AI, Koan.Data.Vector, Koan.AI.Orchestration, Koan.AI.Agents, Koan.Rag]
---

# AI-0036: The Embedding↔Vector Seam (provenance + retrieval forwarding)

## Scope (read first)

DATA-0097 hardened the vector **storage organ**: one Filter AST, fail-loud translation, real
metadata filtering. This DDR fixes the **two seams above that organ** that DATA-0097 §8
deliberately carved out, because they are *not* storage-adapter bugs — they are the contract
between the embedding-**lifecycle** owner (`Koan.Data.AI`, the only layer that knows the producing
model/source) and the model-agnostic **store** (`Koan.Data.Vector`), plus the **orchestration**
layers that read through it (`Koan.AI.Orchestration`, `Koan.AI.Agents`, `Koan.Rag`).

It is **NOT** a re-litigation of the storage organ, and it does not move any AI concern *into*
`Data.*`. The store stays model-agnostic; this DDR only decides **what metadata flows through it on
write** and **what options flow through it on read** — the data on the seam, owned by the layers on
either side.

This is the same disease DATA-0097 named — *a real capability at a lower layer, silently narrowed
at the seam above, with no fail-loud at the genuine boundary* — observed **one layer up**, on both
the write and read sides of the AI surface.

## TL;DR

This DDR defines the two contracts on the seam between the embedding lifecycle (`Koan.Data.AI`) and
the model-agnostic store (`Koan.Data.Vector`), plus the orchestration layers that read through it:

- **Write side — provenance is metadata the lifecycle owner stamps.** Every write path
  (`EmbeddingWorker`, the synchronous embed hook, the `EmbeddingMigrator`) routes through a single
  `VectorProvenance` helper that stamps the producing `model`/`source`/`provider`/`version` into the
  vector record under a reserved `__embedding.*` namespace. Provenance is co-located with the vector
  and queryable like any other metadata, so a model mismatch is catchable — the W4 guard throws at the
  write that would otherwise create a **mixed-space index** (vectors from different models are not
  comparable). *(Previously all three paths passed `null`, and the producing model survived only in
  out-of-band sidecars — making a mixed-space index undetectable at query, migration, or audit time.)*

- **Read side — one `VectorRetrieveOptions` the entry points compose.** `Chain.Retrieve<T>`, the agent
  `{type}_search`/`{type}_query` tools, and the RAG pipeline each build and forward the one typed
  options record (`text`/`alpha`/`topK`/`filter`/`rerank`) instead of hand-marshalling positional
  args, so the DATA-0097 filter capability is expressible through all three — a tenant- or
  category-scoped retrieve included. *(Previously each path forwarded a different subset and all three
  dropped `filter`; `Chain.Retrieve` even advertised `alpha`/`rerank` and silently ignored them.)*

## 1. Context — the gaps this seam closed

Before the seam was defined, the embedding lifecycle computed model/source on the write side and RAG
intent on the read side, but both were lost crossing the `Vector<T>` boundary. The two sides:

### Write side — provenance was dropped at three `SaveWithVector(…, null, …)` sites

| # | Site | What it knows | What it persists |
|---|------|---------------|------------------|
| **W1** | [EmbeddingWorker.cs:271](../../src/Koan.Data.AI/Workers/EmbeddingWorker.cs#L271) | `metadata.Model ?? job.Model`, `metadata.Source` — used at 254-268 for cost + telemetry | `null` metadata |
| **W2** | [Koan.Data.AI/Initialization/KoanAutoRegistrar.cs:475](../../src/Koan.Data.AI/Initialization/KoanAutoRegistrar.cs#L475) | synchronous embed hook; same lifecycle metadata in scope | `null` metadata |
| **W3** | [EmbeddingMigrator.cs:250](../../src/Koan.Data.AI/Migration/EmbeddingMigrator.cs#L250) | `targetModel`/`targetSource`/`targetProvider` — the migration *is* a model change; written to `EmbeddingState.Model` at 264 | `null` metadata |

The provenance survived only in **out-of-band sidecars** (`EmbeddingState<T>` keyed by entity id;
ephemeral telemetry). Neither was co-located with the vector, neither was reachable from the vector
query path, and neither let the store or a migration audit answer "which model is this vector?"

- **W4 — no provenance read-back / guard.** Because nothing was written, nothing could check it: there
  was no "this index contains vectors from models {A, B}" diagnostic and no fail-loud when a query
  embedding's model disagreed with the stored vectors' model. Mixed-space indexes returned
  plausible-but-wrong neighbours **silently**.

### Read side — `Vector<T>.Search` capability was narrowed differently at each entry point

`Search(float[] vector, string text, double? alpha, int? topK, object filter, string, string, ct)`.

| # | Entry point | Forwards | Drops | Failure mode |
|---|-------------|----------|-------|--------------|
| **R1** | `Chain.Retrieve<T>` — [ChainExecutor.cs:196](../../src/Koan.AI.Orchestration/ChainExecutor.cs#L196), [:480](../../src/Koan.AI.Orchestration/ChainExecutor.cs#L480) | `topK` | `alpha`, `rerank` (**both advertised** at [ChainBuilder.cs:51](../../src/Koan.AI.Orchestration/ChainBuilder.cs#L51)), `text`, `filter` | **silent** — DX accepts knobs it ignores |
| **R2** | agent `{type}_search` — [EntityToolGenerator.cs:389](../../src/Koan.AI.Agents/EntityToolGenerator.cs#L389) | `text`, `topK` | `alpha`, `filter` | **missing capability** — no slot exposed |
| **R3** | RAG pillar — [RagRetrievalPipeline.cs:65](../../src/Koan.Rag/Retrieval/RagRetrievalPipeline.cs#L65) | `text`, `alpha`, `topK` | `filter` | **missing capability** — no filter param |

- **R4 — no shared contract.** Three forwarders, three different subsets, **`filter` lost by all
  three**. The store's read surface (fail-loud and filter-capable) had no single typed options object
  the orchestration layers composed; each hand-marshalled positional reflection args
  (`[embedding, …, null, null, null, ct]`), which is exactly how knobs got silently dropped.

## 2. Target architecture

Two small, symmetric contracts on the seam — **write provenance in, read options through** — and
the store stays model-agnostic (it persists/honours both without interpreting AI semantics).

### 2.1 Write: provenance is metadata the lifecycle owner stamps

The lifecycle owner (`Koan.Data.AI`) already computes `(model, source, provider)` at every write.
Stamp it into the vector record's metadata at `SaveWithVector` time under a small reserved
namespace so it round-trips and is queryable like any other metadata (DATA-0097 already types the
metadata path — **seam-ready, not seam-blocking**):

```
__embedding.model     e.g. "text-embedding-3-large"
__embedding.source    e.g. "openai-prod"
__embedding.provider  e.g. "openai"
__embedding.version   the [Embedding] schema version (already tracked in EmbeddingState)
```

Single choke point: a `VectorProvenance` helper builds this dictionary from the resolved
`EmbeddingMetadata`/`job`/migrator target, and all three write sites (W1/W2/W3) route through it
instead of passing `null`. The store does not parse these keys; they are ordinary filterable
metadata. **W4** then becomes cheap: a model-mismatch guard and a "models present in index"
diagnostic read this back; the guard is **fail-loud only at the genuine boundary** (query model ≠
indexed model for the same logical field) and otherwise silent.

### 2.2 Read: one `VectorRetrieveOptions` the three entry points compose

Introduce a single typed options record that maps 1:1 onto `Search`'s real surface, and have all
three entry points build and forward *it* instead of hand-marshalling positional args:

```
VectorRetrieveOptions { string? Text; double? Alpha; int? TopK; Filter? Filter; bool Rerank; }
```

- `Chain.Retrieve<T>` gains a `filter:` parameter and forwards the **full** record (closing R1's
  silent `alpha`/`rerank` drop — they are already on `ChainStep`, the executor just never reads
  them).
- the agent `{type}_search` tool schema gains `alpha` and `filter` slots, parsed into the record.
- the RAG pipeline forwards `Filter` (closing R3).

The DSL filter that flows here is the **DATA-0097 `Filter` AST / its JSON form** — same fail-loud
parser, same operator-aware capabilities. No new filter dialect.

### 2.3 Koan philosophy compliance (normative)

- **Zero-setup / sane defaults.** Provenance is stamped automatically by the lifecycle owner — the
  developer does nothing; `[Embedding]` already declares the model. `VectorRetrieveOptions` defaults
  to today's behaviour (no filter, default alpha), so existing `Retrieve<T>(q)` calls are unchanged.
- **Reference = Intent.** No new toggles. Referencing `Koan.Data.AI` already means "manage embedding
  lifecycle"; recording *which model* produced the vector is part of that intent, not an opt-in.
- **Self-reporting.** The "models present in this index" diagnostic surfaces in the vector boot/health
  report, making a mixed-space index visible instead of silent.
- **Fail-loud only at the genuine boundary.** The only new throw is the model-mismatch guard (W4) —
  a real correctness boundary (incomparable vector spaces). Dropped-knob fixes are *corrections of a
  fail-silent bug*, not new errors.

## 3. Rationale — why stamp/forward rather than leave the sidecars

- Provenance in a sidecar (`EmbeddingState`) answers "when did entity X last embed" but **cannot**
  answer "is this index single-model" or "did this query use the same model as the index" — those
  need the model **on the vector**, reachable from the query path. The migrator (W3) is proof: it
  rewrites vectors into a new model and the store cannot tell new from old.
- Silently ignoring an **advertised** knob (R1's `alpha`/`rerank`) is strictly worse than not
  offering it: the user reasonably believes hybrid/rerank is active. This is the DATA-0096/0097
  fail-silent principle applied to a DX surface.
- A shared `VectorRetrieveOptions` removes the positional-reflection marshalling that *is* the
  drop mechanism; it's fewer, more meaningful moving parts (consistent with the DATA-0096 brief).

## 4. Change ledger

**Write (provenance):**
- `Koan.Data.Vector` (or `Koan.Data.AI` if kept lifecycle-side): `VectorProvenance.Build(metadata, job?)` → `IReadOnlyDictionary<string,object>`; reserved `__embedding.*` key constants.
- W1 `EmbeddingWorker.cs:271`, W2 `KoanAutoRegistrar.cs:475`, W3 `EmbeddingMigrator.cs:250` — replace `null` with provenance dict (merged with any caller metadata).
- W4: model-mismatch guard + "models in index" diagnostic on the vector read/health path.

**Read (forwarding):**
- New `VectorRetrieveOptions` record (lives with `Vector<T>` so all three pillars reference one type).
- R1 `Chain.Retrieve<T>` — **P0:** forward the already-stored `step.Alpha` (now `double?`) + `step.Rerank` via `ExecuteRetrieve`/`InvokeVectorSearch`. **P1:** add a `filter:` param feeding `VectorRetrieveOptions`.
- R2 `EntityToolGenerator` — **P1:** extend tool schema (`alpha`, `filter`); parse + forward.
- R3 `RagRetrievalPipeline` (+ `SearchChunks`, streaming path) — **P1:** add a `Filter` slot to `RagQueryOptions` (none exists today) and forward it. *(RAG already forwards `text`/`alpha`/`topK`; filter is the only gap, and it is a slot addition, not a forward.)*

## 5. Test surfaces

- **Provenance round-trip:** write via worker/hook/migrator → read vector metadata → `__embedding.model` matches the declared/target model (per write path).
- **Mixed-space guard (W4):** index with model A, query embedding tagged model B → fail-loud; same-model → silent pass.
- **Migrator (W3):** migrate to `targetModel` → migrated vectors carry the **new** model, distinguishable from un-migrated.
- **Forwarding (R1/R2/R3):** a filtered retrieve through each entry point actually scopes results (not match-all); `Chain.Retrieve(alpha:…, rerank:true)` demonstrably changes ordering vs default (proves no silent drop).
- **Back-compat:** `Retrieve<T>(q)` / existing agent search / existing RAG call sites behave identically with defaults.

## 6. Capabilities

- **P0 — no silent drops.** Provenance is threaded through all three write sites via the single
  `VectorProvenance` helper — no write passes `null` — and `Chain.Retrieve<T>` forwards
  `step.Alpha`/`step.Rerank`. `Alpha` is `double?` (null = pure-vector; set = hybrid with
  `text = query`, matching `Search`'s own `double? alpha` and the RAG pillar's hybrid call); `Rerank`
  is honoured by the rerank pass. Default `Retrieve<T>(q)` is unchanged — nothing the layer already
  knows or advertises is dropped.
- **P1 — the typed seam.** `VectorRetrieveOptions` is the one read-options record; `Chain.Retrieve<T>`,
  `RagQueryOptions`, and the agent `{type}_search`/`{type}_query` schemas each expose a `filter` slot
  and compose it instead of hand-marshalling positional args. The DSL filter is the unified `Filter`
  AST / its JSON form — the single filter dialect, no new vocabulary.
- **P2 — provenance as a guard.** The W4 mixed-space guard and the "models in index" self-report read
  the stamped provenance back; the guard throws at the write boundary (§9.1) and the health report
  makes a multi-model index visible.

## 7. Decisions

1. **Provenance key namespace → reserved `__embedding.*` metadata keys.** Keeps the store
   model-agnostic, needs no per-adapter schema change, and is queryable as ordinary filterable
   metadata. The store never parses the keys; it persists them. Key *constants* live in
   `Koan.Data.Vector` (the lower layer, so the lifecycle owner and the read-back guard both reference
   one definition); the *builder* lives with the lifecycle owner.
2. **W4 severity → throw when knowable, warn for by-design multi-model.** Fail-loud only at the
   genuine boundary: a write that would introduce a second model into a single-model index throws
   (incomparable spaces = wrong neighbours). An index that is legitimately multi-model by design is
   tolerated with a warn + a "models in index" health report. The throw is realized at write time
   (§9.1 D3), which is strictly stronger than a read-time query check.
3. **`VectorRetrieveOptions` lives in `Koan.Data.Vector`.** It mirrors `Search`'s own surface and
   avoids an AI dependency edge into the store; all pillars reference the one type.

## 8. Relationship to the AI surface

Per the AI-surface understanding pass: embeddings live in three decoupled layers — *production*
(`Client.Embed`), *lifecycle* (`[Embedding]` in `Koan.Data.AI`), *persistence* (`Vector<T>`). This
DDR is the **contract between lifecycle and persistence** (write provenance) and between
**orchestration and persistence** (read options). It does not expand the storage organ's
responsibilities; it ensures the layers that own model/source and RAG intent actually deliver them
across the seam DATA-0097 left seam-ready.

## 9. Implementation architecture

This is the joint design of record for DATA-0097's typed vector `Filter` (collapse + capabilities)
**and** AI-0036's filter DX (P1) and W4 guard (P2), together, because they share one filter model and
one coordinator. The vector path uses the one unified `Filter` AST — the former vector-only AST was
*promoted into* it (`Filter.cs`), so the vector path and the entity path share a single origin. One
`VectorFilterCoordinator` at `VectorData<T>.Search` enforces residual-is-error below every facade
overload and above all six repositories.

### 9.1 Decisions

- **D1 — filter DX: metadata-key == entity-property is now CANON; lambda is the primary idiom.**
  `Chain.Retrieve<Doc>(q, filter: d => d.Year > 2020)` compiles via `LinqFilterCompiler` exactly like
  `Entity<T>.Query(predicate)`. To make it sound *by construction* (S5.Recs proves keys are
  hand-named today), the **embedding write path auto-stamps filterable entity properties as metadata
  under their CLR property names** (sane default: scalar/string properties; large embedded-text
  excluded). A `Filter?` overload remains for advanced/explicit-key callers. A lambda over a property
  the write path did not stamp **fails loud** naming the missing key (never silent non-match).
- **D2 — tenancy is a PARTITION, not a filter (no AND-composition).** Verified: `Vector<T>.WithPartition`
  → `EntityContext.Partition` (DATA-0077), and the vector **read** path resolves storage via
  `VectorAdapterNaming.GetOrCompute` → `factory.ResolveStorage(type, EntityContext.Current?.Partition, sp)`,
  so a search runs against the partition-suffixed index. A host scopes the partition (`WithPartition(tenantId)`)
  ambiently *before* invoking agent/Chain/RAG retrieval; an LLM-authored **filter is intra-partition**
  and structurally cannot address another tenant's index (partition is storage routing, not a filter
  node). The agent `{type}_search` filter slot therefore ships in P1 **without** AND-composition
  machinery. Guidance (docs): multi-tenant deployments isolate by partition; co-mingling tenants in
  one partition and relying on a filter is a Koan anti-pattern. *(The entity-web `QueryFilterComposer.AndAll`
  remains the right tool for hook-contributed predicates within a partition, but is not a tenancy boundary.)*
- **D3 — W4 guards at the write boundary, backed by a model registry.** A durable per-collection
  model registry (`VectorModelRegistry<TEntity>`, keyed per `(entity, partition)`, O(1), never stale)
  records the producing model on each `SaveWithVector`. `GuardWrite` runs immediately before each
  write (`EmbeddingWorker`, `KoanAutoRegistrar`) and throws `VectorModelMismatchException` when a write
  would introduce a second model into a single-model index — fired at the genuine boundary (the write
  that would corrupt the index), and only against the durable registry it just read, never on stale
  data. An index that is legitimately multi-model is tolerated with a WARN; `Evaluate`/`Inspect`
  surface "models in index" as warn-only health, and `EmbeddingMigrator.Reset` clears the registry for
  a by-design model transition. Guarding at write time is strictly stronger than a read-time check —
  a guarded single-model index can never mismatch a same-model query — and avoids the layering problem
  that `Koan.Data.Vector` cannot resolve the query model.

### 9.2 Filter semantics & capability contract

1. **The convergence oracle must be built, not assumed.** `InMemoryFilterEvaluator` is type-bound and
   `InMemoryVectorRepository.Search` ignores `options.Filter`. P1a adds a **schemaless
   `DictionaryFilterEvaluator`** (reads `FieldPath.Leaf` from `IReadOnlyDictionary<string,object>`,
   porting the locked null/Nin/HasNone semantics verbatim) and wires `InMemoryVectorRepository.Search`
   to apply it. This is the reference id-set for the conformance matrix.
2. **Null semantics are part of the capability contract, not just the operator set.** PGVector `Ne`
   emits `(IS NULL OR <> @p)`; Qdrant/Milvus bare `must_not`/`!=` exclude missing rows differently →
   same `Nin` returns different id-sets. Each translator must either emit the **null-inclusive** form
   for `Ne/Nin/HasNone` or **declare the operator absent** (hard-error). Conformance rows seed a
   MISSING key and assert every adapter's id-set equals the oracle.
3. **`IgnoreCase` stays a capability dimension.** `VectorFilterCapabilities` keeps `IgnoreCase`; the
   schemaless splitter applies the entity gate `(!f.IgnoreCase || caps.IgnoreCase)` so an unsupported
   case-fold becomes a loud `VectorFilterUnsupportedException`, never a silent case-sensitive query.
4. **No lossy wildcard/operator coercion.** The reader lowers only leading/trailing `*` →
   `StartsWith/EndsWith/Contains`. An **interior/multi-segment** wildcard (`*a*b*`) or raw `Like` with
   no exact unified target throws `FilterParseException` at read — never a silent narrow/widen.
5. **Filter errors propagate, never become empty results.** `ChainExecutor` does not swallow filter
   failures: `VectorFilterUnsupportedException` / `FilterParseException` / `InvalidFilterFieldException`
   / `NotSupportedException` surface as a chain error rather than a silently-cleared result set.
6. **Coercion contract.** The schemaless reader normalizes obvious numeric/bool literals so all six
   providers see the same CLR-typed scalar; cross-provider coercion cases are required rows in the
   conformance matrix, not discovered-on-failure.

The unknown-metadata-key contract is part of this: the metadata path is schemaless (no static field
list), so a missing key is oracle-consistent — `false` for `Eq`, `true` for `Nin`/`HasNone` per the
null semantics above. A shared `Koan.Data.Vector.TestKit` hosts the InMemory adapter + oracle so the
six connector test projects and the conformance project reference one definition.

### 9.3 Components

The pieces below make up the as-built architecture. They carry their original P-labels because code
comments reference them as anchors; the labels are descriptive groupings, not a delivery sequence.

- **P1a — filter foundation.** `VectorFilterCapabilities` (unified `FilterOperator` + `IgnoreCase` +
  `NestedPaths`; `None`/`Full`); the schemaless `VectorFilterReader` (JSON → unified `Filter`, `Filter`
  passthrough, lowers leading/trailing wildcards, normalizes scalars, fails loud);
  `IVectorFilterTranslator<TNative>` (`Translate(Filter)`, `metadataField` via ctor);
  `VectorFilterCoordinator` (split + residual-is-error); `VectorFilterUnsupportedException`;
  `FilterSplitter`'s schemaless overload; `DictionaryFilterEvaluator` wired into
  `InMemoryVectorRepository.Search`; the shared `Koan.Data.Vector.TestKit`; and the container-free
  conformance matrix (operator × node × provider-capability vs the oracle id-set).
- **P1b — typed filter on the store + per-adapter translation.** `VectorQueryOptions.Filter` is
  `Filter?`; the `Vector`/`VectorData` facade routes string/dict through the reader once and
  `VectorData.Search` invokes the coordinator. Each adapter declares its `VectorFilterCapabilities`,
  renders `FilterValue.Set`/`Scalar`, and self-reports its operator set in `Describe()`: PGVector (the
  reference, full operator set over JSONB), Qdrant, Milvus, Weaviate (intentionally reduced — no `In`),
  and Elasticsearch + OpenSearch (which share `SearchEngineFilterTranslator`; see §9.4).
- **P1-AI — filter DX slots.** The embedding write path auto-stamps CLR-named filterable metadata
  (which makes the lambda sound by construction); `VectorRetrieveOptions` lives in `Koan.Data.Vector`;
  `Chain.Retrieve<T>` takes `Expression<Func<T,bool>>? filter` (+ `Filter?` on `ChainStep`) and
  `ChainExecutor` builds and forwards `VectorRetrieveOptions` (no positional array) without swallowing
  filter errors; `RagQueryOptions.Filter` + pipeline forwarding + `Ask`/`Search` convenience overloads;
  the agent `{type}_search`/`{type}_query` tools expose a JSON-DSL `filter` (parsed via the schemaless
  reader) + `alpha`. No-filter calls behave exactly as before at all three entry points.
- **P2 — W4 mixed-space guard.** `VectorModelGuard.GuardWrite`, backed by the durable
  `VectorModelRegistry<TEntity>` (per `(entity, partition)`), runs before each `SaveWithVector` and
  throws `VectorModelMismatchException` when a write would introduce a second model into a single-model
  index; an already-multi-model index is tolerated with a WARN, and `EmbeddingMigrator.Reset` clears
  the registry for a by-design transition. `Evaluate`/`Inspect` surface "models in index" as warn-only
  health. (Design rationale in §9.1 D3.)

### 9.4 The Elasticsearch / OpenSearch shared base

Elasticsearch and OpenSearch are built on the same Apache Lucene query DSL, so a single
`SearchEngineFilterTranslator` in the `Koan.Data.SearchEngine` assembly is their one source of truth
for `Filter` → query-DSL translation, and it owns the one `VectorFilterCapabilities` constant both
adapters expose. The assembly is named for the engine *category* (the same convention as
`Koan.Data.Relational`), so the dependency reads clearly without Lucene knowledge; it mirrors the
Relational precedent of a shared library referenced by a connector family. An `engine` label
parameterizes only the not-supported exception messages, so a failure still names the actual adapter
("Elasticsearch"/"OpenSearch"). String exact-match and wildcard target the `.keyword` sub-field;
numeric range and exists target the bare field; Lucene's null-inclusive `bool/must_not` gives
`Ne`/`Nin`/`HasNone` their oracle-matching semantics.

### 9.5 Verification

The container-free conformance matrix checks every adapter's pushdown against the
`DictionaryFilterEvaluator` oracle: MISSING-key null-semantics rows, coercion rows,
`Not(Eq)`/`Not(Ne)`/nested-`Not` rows, unsupported-operator → `VectorFilterUnsupportedException`, and
`ClrFilter` → hard error; plus reader convergence with `JsonFilterParser`, coordinator
residual-is-error, AI back-compat, and W4 warn/health. Per ARCH-0079, each of the six adapters also
ships an integration spec through real `AddKoan()` discovery (`KoanIntegrationHost`): an operator
outside the declared set throws, an operator in the set returns the same id-set as the oracle over a
seeded corpus against a live container, and a second-model write trips the W4 guard.
