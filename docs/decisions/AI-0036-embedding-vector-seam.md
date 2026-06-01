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

Post-DATA-0097 the store can filter trustworthily and records whatever metadata you give it. But:

- **Write side (provenance):** every one of the **three** code paths that persists a vector
  (`EmbeddingWorker`, the synchronous embed hook, the `EmbeddingMigrator`) calls
  `SaveWithVector(entity, embedding, **null**, ct)`. The producing model/source is *computed* —
  threaded into telemetry, cost estimation, and the sidecar `EmbeddingState.Model` — and then
  **dropped at the store boundary**. The migrator, whose entire purpose is to change the producing
  model, stamps `null` onto the migrated vector. Result: stored vectors carry **no provenance of
  which model produced them**. A model change silently creates a **mixed-space index** (vectors
  from different models are not comparable) with no way to detect it at query, migration, or audit
  time. This is a silent correctness hazard of the same shape DATA-0097 just closed.

- **Read side (retrieval forwarding):** `Vector<T>.Search` exposes `vector, text, alpha, topK,
  filter, …`. Three independent entry points read through it, each losing a *different* subset, and
  **all three drop `filter`** — the exact capability DATA-0097 made trustworthy:
  - `Chain.Retrieve<T>(query, topK, alpha, rerank)` **advertises** `alpha`/`rerank`, stores them on
    the step, then forwards **only `topK`** — `alpha`/`rerank` are **silently ignored**. The DX lies.
  - the agent `{type}_search` tool exposes only `text`+`top_k` — no `alpha`/`filter` slot exists.
  - the RAG pillar forwards `text`+`alpha` (hybrid ✓) but still **cannot pass a `filter`**.

  There is no single retrieval-options contract; each path re-implements forwarding ad hoc and each
  loses something. A tenant- or category-scoped RAG retrieve is **inexpressible** through any of the
  three, despite the store now supporting it.

## 1. Findings (grounded)

### Write side — provenance dropped at three `SaveWithVector(…, null, …)` sites

| # | Site | What it knows | What it persists |
|---|------|---------------|------------------|
| **W1** | [EmbeddingWorker.cs:271](../../src/Koan.Data.AI/Workers/EmbeddingWorker.cs#L271) | `metadata.Model ?? job.Model`, `metadata.Source` — used at 254-268 for cost + telemetry | `null` metadata |
| **W2** | [Koan.Data.AI/Initialization/KoanAutoRegistrar.cs:475](../../src/Koan.Data.AI/Initialization/KoanAutoRegistrar.cs#L475) | synchronous embed hook; same lifecycle metadata in scope | `null` metadata |
| **W3** | [EmbeddingMigrator.cs:250](../../src/Koan.Data.AI/Migration/EmbeddingMigrator.cs#L250) | `targetModel`/`targetSource`/`targetProvider` — the migration *is* a model change; written to `EmbeddingState.Model` at 264 | `null` metadata |

The provenance survives only in **out-of-band sidecars** (`EmbeddingState<T>` keyed by entity id;
ephemeral telemetry). Neither is co-located with the vector, neither is reachable from the vector
query path, and neither lets the store or a migration audit answer "which model is this vector?"

- **W4 — no provenance read-back / guard.** Because nothing is written, nothing checks it: there is
  no "this index contains vectors from models {A, B}" diagnostic and no fail-loud when a query
  embedding's model disagrees with the stored vectors' model. Mixed-space indexes return
  plausible-but-wrong neighbours **silently**.

### Read side — `Vector<T>.Search` capability narrowed differently at each entry point

`Search(float[] vector, string text, double? alpha, int? topK, object filter, string, string, ct)`.

| # | Entry point | Forwards | Drops | Failure mode |
|---|-------------|----------|-------|--------------|
| **R1** | `Chain.Retrieve<T>` — [ChainExecutor.cs:196](../../src/Koan.AI.Orchestration/ChainExecutor.cs#L196), [:480](../../src/Koan.AI.Orchestration/ChainExecutor.cs#L480) | `topK` | `alpha`, `rerank` (**both advertised** at [ChainBuilder.cs:51](../../src/Koan.AI.Orchestration/ChainBuilder.cs#L51)), `text`, `filter` | **silent** — DX accepts knobs it ignores |
| **R2** | agent `{type}_search` — [EntityToolGenerator.cs:389](../../src/Koan.AI.Agents/EntityToolGenerator.cs#L389) | `text`, `topK` | `alpha`, `filter` | **missing capability** — no slot exposed |
| **R3** | RAG pillar — [RagRetrievalPipeline.cs:65](../../src/Koan.Rag/Retrieval/RagRetrievalPipeline.cs#L65) | `text`, `alpha`, `topK` | `filter` | **missing capability** — no filter param |

- **R4 — no shared contract.** Three forwarders, three different subsets, **`filter` lost by all
  three**. The store's read surface (now fail-loud and filter-capable) has no single typed
  options object the orchestration layers compose; each hand-marshals positional reflection args
  (`[embedding, …, null, null, null, ct]`), which is exactly how knobs get silently dropped.

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

## 6. Phased plan

- **P0 — stop the silent drops (S, no decisions needed).** ✅ **SHIPPED** (commit `a0efbdee`; the
  build-unblock it depended on is `c934c9e2`). These are pure corrections of a
  fail-silent bug, mirroring DATA-0097 P0 — only data the layer already *knows* or *advertises*:
  - thread provenance through W1/W2/W3 (the `null` → dict change + the single `VectorProvenance` helper);
  - have R1 forward `step.Alpha`/`step.Rerank` (**already on `ChainStep`**). `Alpha` becomes `double?`
    (null = pure-vector = today's effective behaviour; set = hybrid with `text = query`, matching
    `Search`'s own `double? alpha` shape and the RAG pillar's hybrid call). `Rerank` is honoured inline
    by the existing rerank pass. Default `Retrieve<T>(q)` is unchanged.
  - Net: nothing the layers already know/advertise is dropped anymore.
  - *Not in P0:* forwarding a **filter** from any read path — RAG/agent/Chain have **no filter slot
    today**, so that is a slot *addition* (a typed-options decision), moved to P1.
- **P1 — the typed seam (M).** `VectorRetrieveOptions`; add a `filter:` slot to `Chain.Retrieve<T>`,
  `RagQueryOptions`, and the agent `{type}_search` schema; all compose the one record. The DSL filter
  is the DATA-0097 `Filter` AST / its JSON form — the single filter dialect, no new vocabulary.
- **P2 — provenance as a guard, not just data (S/M).** W4 model-mismatch guard + "models in index"
  self-report; depends on P1's filter to express the read-back cleanly.

## 7. Decisions (ratified 2026-05-31)

1. **Provenance key namespace → reserved `__embedding.*` metadata keys.** Keeps the store
   model-agnostic, needs no per-adapter schema change, is queryable as ordinary filterable metadata,
   and is seam-ready today. The store never parses the keys; it persists them. Key *constants* live
   in `Koan.Data.Vector` (the lower layer, so the lifecycle owner and the future read-back guard both
   reference one definition); the *builder* lives with the lifecycle owner.
2. **W4 severity → throw when knowable, warn for by-design multi-model.** Fail-loud only at the
   genuine boundary: when the query path can know the index model and they differ, throw
   (incomparable spaces = wrong neighbours). When the index is legitimately multi-model by design,
   warn + surface "models in index" in the health report.
3. **`VectorRetrieveOptions` lives in `Koan.Data.Vector`.** It mirrors `Search`'s own surface and
   avoids an AI dependency edge into the store; all pillars reference the one type.

## 8. Relationship to the AI surface

Per the AI-surface understanding pass: embeddings live in three decoupled layers — *production*
(`Client.Embed`), *lifecycle* (`[Embedding]` in `Koan.Data.AI`), *persistence* (`Vector<T>`). This
DDR is the **contract between lifecycle and persistence** (write provenance) and between
**orchestration and persistence** (read options). It does not expand the storage organ's
responsibilities; it ensures the layers that own model/source and RAG intent actually deliver them
across the seam DATA-0097 left seam-ready.

## 10. Hardened P1/P2 plan-of-record (post adversarial review, 2026-06-01)

A 10-agent discovery + 3-lens adversarial workflow (run `wyzuoe56p`) verified the spine against
source and found six revision-worthy holes. This section is the **executable plan of record** for
DATA-0097 P1 (typed vector Filter + collapse + capabilities) **and** AI-0036 P1 (filter DX) + P2
(W4 guard), jointly, because they share one filter model and one coordinator. The spine is
verified-correct: the unified `Filter` AST was *promoted from* the former vector-only AST
(`Filter.cs:13`), so the vector path rejoins its origin; one `VectorFilterCoordinator` at
`VectorData<T>.Search` enforces residual-is-error below every facade overload and above all 6 repos.

### 10.1 Ratified decisions (this session)

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
- **D3 — W4 ships WARN-only first.** Initial P2: WARN + "models in index" health report (the safe
  asymmetry — never a false-positive block). The hard THROW is added only once a **write-time
  per-collection model registry** (O(1), never stale, accumulated on each `SaveWithVector`) backs it,
  so the throw cannot fire on stale/false data.

### 10.2 Corrections folded in (advisor-level, from the review)

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
4. **No lossy wildcard/operator coercion.** Reader lowers only leading/trailing `*` →
   `StartsWith/EndsWith/Contains`. An **interior/multi-segment** wildcard (`*a*b*`) or raw `Like` with
   no exact unified target throws `FilterParseException` at read — never a silent narrow/widen. Publish
   the legacy→unified operator map.
5. **`ChainExecutor` must not swallow filter errors.** The `catch(Exception)=>results.Clear()`
   (`ChainExecutor.cs:520`) is narrowed so `VectorFilterUnsupportedException` / `FilterParseException` /
   `InvalidFilterFieldException` / `NotSupportedException` **propagate** (surface as a chain error),
   instead of becoming silent empty retrieval. A spec asserts an unsupported Chain filter throws.
6. **Coercion contract decided now:** the schemaless reader **normalizes obvious numeric/bool literals**
   so all 6 providers see the same CLR-typed scalar; cross-provider coercion cases are REQUIRED rows in
   the conformance matrix (not discovered-on-failure).

Plus mechanical corrections: the keystone slot-flip (`VectorQueryOptions.cs:10` `object?`→`Filter?`)
is **one atomic commit** with `Vector.cs` + `VectorData.cs` + `VectorWorkflowRegistry.cs` (all pass
into that slot); the `Vector<T>.Search(...,object? filter,...)` **reflection target signature is frozen
byte-for-byte** until `ChainExecutor`/`EntityToolGenerator` migrate off positional reflection (then
removed); the `object?` facade short-circuits `if (input is Filter f) return f;` (legacy `ParseOrThrow`
passthrough); `FilterSplitter` gains a concrete `Split(Filter, VectorFilterCapabilities)` overload
(no `Type`, never calls `FieldPathResolver`, pinned by a spec); the **unknown-metadata-key** contract
is documented (schemaless ⇒ no static field list; missing key ⇒ oracle-consistent: false for `Eq`,
true for `Nin/HasNone` per locked null semantics); Weaviate's real silent-widen sources are the
TryParse-at-entry and composite drop-empties arms (not the unreachable default arm); a shared
`Koan.Data.Vector.TestKit` hosts the InMemory adapter + oracle so the 6 connector test projects and the
conformance project reference one definition; agent `{type}_query` gains the same JSON-DSL filter slot
for surface symmetry; the 3 connector READMEs drop the deleted `VectorFilterExpression` examples.

### 10.3 Staged ledger (build green at every step; compiler is the migration checklist)

> **Status (2026-06-01):** **P1a ✅ shipped** (`25e1e2e1`), **P1b ✅ shipped** (keystone flip
> `6ee9171b`; PGVector `5f2fe980`; Qdrant+Milvus `7d33245e`; Weaviate+ES+OS `947dc051`), **P1c ✅
> shipped** (`8f20c655` — legacy `VectorFilter*`/`VectorFilterJson` deleted, both samples migrated,
> the DATA-0056 node-model collapse finally done). The vector storage organ now shares the single
> unified `Filter` AST with the entity path, fail-loud and operator-aware. **Remaining:** the
> live-store *per-provider convergence* specs (quarantined, need real stores), **P1-AI** (filter DX
> slots — depends on a pre-existing `Koan.Rag` DATA-0096 break being fixed first), and **P2** (W4).

- **P1a — additive foundation (no breaking change, no decision blocks it).** `VectorFilterCapabilities`
  (unified `FilterOperator` + `IgnoreCase` + `NestedPaths`; `None`/`Full`); schemaless `VectorFilterReader`
  (JSON→unified `Filter`, `Filter` passthrough, lower wild/between, normalize scalars, fail-loud);
  `IVectorFilterTranslator<TNative>` (`Translate(Filter)`, `metadataField` via ctor); `VectorFilterCoordinator`
  (split + residual-is-error); `VectorFilterUnsupportedException`; `FilterSplitter` schemaless overload;
  `DictionaryFilterEvaluator` + wire `InMemoryVectorRepository.Search`; shared `Koan.Data.Vector.TestKit`;
  the container-free **conformance matrix** (operator × node × provider-capability vs oracle id-set) +
  reader/coordinator specs. **Gate: lands before any fan-out.**
- **P1b — keystone flip + adapter migration.** Atomic unit: `VectorQueryOptions.Filter` `object?`→`Filter?`
  + `Vector.cs`/`VectorData.cs`/`VectorWorkflowRegistry.cs` (facade routes string/dict through the reader
  once; `VectorData.Search` invokes the coordinator). Then one file at a time: PGVector (reference) →
  Qdrant → Milvus → Weaviate (reduced caps, no `In`; close composite drops) → shared Lucene base for
  ES+OS (one capability constant; ES F6 `knn.filter` fix) → the 2 samples (S5.Recs/S7.Meridian). Each
  adapter declares `VectorFilterCapabilities`, drops `TryParse`-at-entry, renders `FilterValue.Set/Scalar`,
  and self-reports its operator set in `Describe()`.
- **P1c — delete legacy.** Remove `Vector/Filtering/VectorFilter*` + `VectorFilterOperator` +
  `VectorFilterJson` once the last consumer compiles against `Filter?`; rewrite the 17 legacy specs.
- **P1-AI — filter DX slots (independent per entry point, each its own green commit).** Write-path
  **auto-stamp** of CLR-named filterable metadata (makes the lambda sound); `VectorRetrieveOptions`
  (`Koan.Data.Vector`); `Chain.Retrieve<T>` gains `Expression<Func<T,bool>>? filter` (+ `Filter?` on
  `ChainStep`) and `ChainExecutor` builds/forwards `VectorRetrieveOptions` (kills the positional array)
  and stops swallowing filter errors; `RagQueryOptions.Filter` + pipeline forwarding + `Ask/Search`
  convenience overloads; agent `{type}_search` + `{type}_query` gain a JSON-DSL `filter` (parsed via the
  schemaless reader for `_search`) + `alpha`; a **PHIL-1 back-compat spec lands first** (no-filter ==
  today at all three entry points).
- **P2 — W4 guard (WARN-only).** `VectorModelGuard` reads the model-set, **warns** + health-reports on
  mismatch (silent same-model pass); "models present" in the vector boot/health report. THROW deferred
  to a follow-up that adds the write-time model registry.

### 10.4 Test surfaces

Container-free (must pass before fan-out): conformance matrix vs the `DictionaryFilterEvaluator`
oracle (incl. MISSING-key null-semantics rows, coercion rows, `Not(Eq)`/`Not(Ne)`/nested-`Not` rows,
unsupported-operator → `VectorFilterUnsupportedException`, `ClrFilter` → hard error); reader convergence
with `JsonFilterParser` on shared JSON; coordinator residual-is-error; PHIL-1 AI back-compat; W4
warn/health. **Seeded + quarantined** (ARCH-0079, one per provider, real `AddKoan()` via
`KoanIntegrationHost`): each operator NOT in the declared set throws; each operator IN the set returns
the SAME id-set as the oracle over a seeded corpus; ES F6 `knn.filter` parity vs OS; W4 live mismatch.
