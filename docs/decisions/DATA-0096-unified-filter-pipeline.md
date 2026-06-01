---
id: DATA-0096
slug: DATA-0096-unified-filter-pipeline
domain: DATA
status: Accepted
date: 2026-05-31
supersedes: [DATA-0029, DATA-0031, DATA-0092]
supersedes-pending: [DATA-0056]
---

# DATA-0096: Unified Filter Pipeline (break-and-rebuild)

## TL;DR

Collapse every filter/query representation in the framework into **one normalized
Filter AST** that both the JSON DSL and raw LINQ lower into, negotiate it against a
**per-operator capability descriptor** each adapter declares, push down what the
adapter supports and evaluate the residual through **one bounded in-memory floor**,
and carry it together with sort/projection/paging/count in **one `QuerySpec`**.

This fixes the reported `$in`-on-`List<string>` crash, but more importantly closes a
class of silent cross-adapter correctness bugs the investigation surfaced, and removes
~5 redundant subsystems.

This is a greenfield break-and-rebuild. Back-compat is **not** a constraint; the only
preserved surface is developer-facing DX (the entity-first API and the JSON filter DSL
ergonomics: `{ Games: { $in: [...] } }`).

---

## 1. Why (verified findings)

The trigger was `Invalid cast from 'System.String' to 'List<String>'` on
`{ Games: { $in: ["ffxiv"] } }`. Root cause: `JsonFilterBuilder.BuildIn` only handles a
scalar member; for a `List<T>` member it falls to a branch that builds
`Enumerable.Contains<List<T>>(...)` and `Convert.ChangeType("ffxiv", typeof(List<string>))`
throws. `$in`/`$all` are not symmetric (one scalar-membership, one collection-contains).

A cross-adapter investigation (13 analysts + 2 adversarial verifiers) found the crash is
the visible tip of a deeper problem. Filtering on a `List<string>` field behaves three
different ways:

| Adapter | `List<string>` storage | Containment handling | State |
|---|---|---|---|
| InMemory / JSON / Redis | live CLR list / JSON / blob | LINQ-to-objects (compiled) | correct |
| Mongo | native BSON array | driver LINQ3 → server-side array match | correct **except** global GUID-string serializer rewrites GUID-shaped elements to BinData; **untested** |
| Postgres / SqlServer / Sqlite | JSON column (no projected column) | relational translator **throws** → adapter **catches** → **loads whole table** → filters in memory | **mis-paginates** (LIMIT/OFFSET applied to *unfiltered* set, then filters, then reports `PaginationHandled=true`) and is an unbounded scan |
| Couchbase | native JSON array | own translator role-swaps args → **throws**, no fallback | **500 to caller** |
| Vector family | metadata blob | not on the LINQ path | N/A |

So fixing only the parser would convert a loud crash into a *silent* relational
mis-pagination on three adapters and leave Couchbase throwing. **The capability does not
exist consistently** — that is what this ADR builds.

### Census: 7 filter representations in 2 disconnected ecosystems

1. `JsonFilterBuilder` (DSL → `Expression`)
2. `LinqWhereTranslator` (`Expression` → SQL; shared by PG/SqlServer/Sqlite; throws on static `Enumerable.Contains`)
3. `CouchbaseLinqQueryTranslator` (`Expression` → N1QL; ~80% duplicate of #2, shares zero code)
4. `TryBuildGuidFilter` (partial `Expression` → Mongo `FilterDefinition`; GUID optimization only)
5. `VectorFilter` sealed-record AST + `VectorFilterExpression` (LINQ→AST) + `VectorFilterJson` (JSON↔AST) + `VectorFilterOperator` — **the mature one**, canon under DATA-0056, already used as a *scalar* filter AST by Elasticsearch/OpenSearch (non-vector)
6. 5 vector translators (`Qdrant/Milvus/Weaviate/ElasticSearch/OpenSearch`; ES/OS byte-identical)
7. raw `?q=` string passthrough (`IStringQueryRepository`)

Plus operator-blind capabilities (`QueryCapabilities` is a 4-flag enum DATA-0092 itself
calls "advisory only"), 9 copy-pasted unbounded relational fallback `catch` blocks, and
**zero tests** anywhere on the filter path.

### The key reuse discovery

The normalized AST this ADR needs **already exists** as `VectorFilter` — an immutable
sealed-record tree (`And`/`Or`/`Not` + `Compare(path, op, value)`) with a LINQ compiler,
a JSON parser/writer, and an operator enum. It is mislabeled under `Vector.Filtering` and
is already doing double duty as a generic scalar filter AST. **We promote it, not reinvent it.**

---

## 2. Target architecture — the few meaningful parts

```
   JSON DSL ──JsonFilterParser──┐
                                ├──▶  FilterNode (the one AST)  ──▶ FilterExecutionCoordinator
   raw LINQ ──LinqFilterLifter──┘         + FieldPath (typed)        │  translate(AST, caps)
        (FilterOpaque escape hatch for un-liftable lambdas)          │     ├─ push supported → IFilterTranslator<TNative>
                                                                     │     └─ residual → InMemoryFilterEvaluator (bounded)
   QuerySpec { Filter, Sort, Projection, Page, Count, Partition } ───┘        paginate AFTER residual; emit pushdown signal
```

Eight components replace the sprawl:

1. **`Koan.Data.Filtering` (harvested from `VectorFilter`)** — the single Filter AST.
   - `FilterNode` sealed hierarchy: `FilterAnd`, `FilterOr`, `FilterNot`, `FilterNor`,
     `FilterCompare(FieldPath, FilterOperator, FilterValue)`, and `FilterOpaque(Func<…,bool>)`
     — the escape hatch for LINQ shapes the lifter can't normalize (forces in-memory eval,
     never silently dropped).
   - `FieldPath` — multi-segment, resolved against entity metadata to a **leaf CLR type**
     (drives scalar-vs-collection operator selection and value coercion).
   - Per-node case-sensitivity + null-policy flags (immutable, not threaded mutable options).

2. **Two front-ends, one binder, one coercer**
   - `JsonFilterParser` (DSL → AST): thin string→operator map; replaces `JsonFilterBuilder`'s
     expression building.
   - `LinqFilterLifter` (`Expression` → AST): harvested from `VectorFilterExpression`,
     generalized to recognize collection `Contains`, `In`, etc.; emits `FilterOpaque` for the rest.
   - Both call **one** `MemberPathResolver` (harvested from the sort path — already does
     case-insensitive binding, dotted paths, structured `InvalidFieldException`) and **one**
     value coercer (enum/Guid/DateTimeOffset/decimal/ISO, fail-loud).

3. **`IFilterTranslator<TNative>` — one contract**
   `(TNative pushed, FilterNode? residual) Translate(FilterNode, OperatorCapabilities)`.
   Each adapter implements it once. Implementations:
   - `SqlFilterTranslator` over `ISqlDialect` (harvested `ILinqSqlDialect`) → PG/SqlServer/Sqlite;
     dialects supply JSON-containment SQL (`jsonb @>`/`?|`, `OPENJSON … EXISTS`, `json_each … EXISTS`).
   - `LuceneFilterTranslator` → ES + OS (collapses the byte-identical pair).
   - `MongoFilterTranslator` (AST → `FilterDefinition`; absorbs the GUID optimization).
   - `N1qlFilterTranslator` (Couchbase; `ANY x IN field SATISFIES x = $p END`).
   - `InMemoryFilterEvaluator` (AST → compiled delegate) — **this is also the fallback floor engine and the test oracle.**

4. **`OperatorCapabilities` descriptor** — per adapter, per `(FilterOperator, FieldKind)`:
   pushable | fallback-only | unsupported. Replaces operator-blind flags (which become a
   derived summary). The thing negotiation reads.

5. **`FilterExecutionCoordinator` — one bounded fallback engine** at the orchestrator
   boundary (`Data.QueryWithCount`). Splits AST into pushable + residual per capabilities,
   pushes the pushable part, fetches that reduced set (bounded by `AbsoluteMaxRecords`),
   applies residual via `InMemoryFilterEvaluator`, **paginates after** (fixes the relational
   bug), gives Couchbase a floor instead of a 500, and emits the pushdown/fallback signal.
   Deletes all 9 ad-hoc relational `catch` blocks.

6. **`QuerySpec` — one structured carrier**
   `{ FilterNode? Filter, SortSpec[] Sort, FieldSet? Projection, Page, PageSize, Partition, CountStrategy }`.
   Replaces the untyped `object? query` param + scattered options. `RepositoryQueryResult`
   generalizes to a per-axis "handled" envelope (`FilterHandled`/`SortHandled`/
   `ProjectionHandled`/`PaginationHandled` + inline `TotalCount`), so the coordinator's
   fallback logic is one axis-generic step. Inline count removes the second round-trip.

7. **Capability self-reporting** — surface `OperatorCapabilities` through the four channels
   that already exist but carry nothing today: `/.well-known/Koan/aggregates` (per-entity
   `operators` block), boot `Describe`, response headers (`Koan-Query-Pushdown` /
   `Koan-InMemory-Filter`, mirroring the existing `Koan-InMemory-Paging`), and the MCP filter
   argument schema.

8. **Vector search converges** — retype `VectorQueryOptions.Filter` from `object?` to
   `FilterNode`; kNN stays a *search option*, not a filter node; the 5 vector translators
   become `IFilterTranslator` implementations. Metadata-filtered vector search and entity
   filtering now speak one language.

---

## 2.1 Naming (SoC / DDD)

Canonical names. Any working names used earlier in this document (e.g. `FilterNode`,
`FilterOpaque`, `QuerySpec`) are superseded by this table. Each type carries one
responsibility and a clear domain role.

| Concern (single responsibility) | Type | Role |
|---|---|---|
| Boolean predicate over an entity | `Filter` (abstract) + `FieldFilter`, `AllOf`, `AnyOf`, `Not`, `ClrFilter` | Value object (closed hierarchy) |
| Where to look | `FieldPath` (segments only; reflection-neutral) | Value object |
| What to compare against | `FilterValue` (`Scalar` / `Set` / `None`) | Value object |
| Which comparison | `FilterOperator` | Enum |
| JSON DSL → model | `JsonFilterParser` | Input adapter |
| LINQ expression → model | `LinqFilterCompiler` | Input adapter |
| Resolve a `FieldPath` to a CLR member + leaf type | `FieldPathResolver` → `ResolvedField` | Domain service (harvested from `MemberPathResolver`) |
| Coerce a JSON value to the leaf type | `FilterValueConverter` | Domain service |
| Model → provider-native query (+ residual) | `IFilterTranslator<TNative>` + `FilterTranslation<TNative>` | Anti-corruption layer |
| What a provider can push down | `FilterCapabilities` | Value object (declared per adapter) |
| Evaluate the model in memory | `InMemoryFilterEvaluator` | Domain service (the fallback floor **and** the convergence oracle) |
| Decide push vs. residual, bound the scan, paginate after | `FilterPushdownCoordinator` | Application service |
| The whole query request | `QueryDefinition` (Filter + Sort + Projection + Page + Count + Partition) | Value object (replaces `DataQueryOptions`) |

Combinators stay minimal — `$nor` lowers to `Not(AnyOf(...))`, `$between` to
`AllOf(Gte, Lte)`, and wildcard strings to `StartsWith`/`EndsWith`/`Contains` — at parse
time, so the node hierarchy and operator enum carry no redundant members.

## 3. Operator set

Leaf operators (the superset enum; DSL keyword → operator chosen by resolved leaf type):

| Kind | Operators | DSL |
|---|---|---|
| Scalar comparison | `Eq Ne Gt Gte Lt Lte` | `$eq $ne $gt $gte $lt $lte` (+ bare value = Eq) |
| Scalar set | `In Nin` | `$in`/`$nin` on a scalar field |
| String | `StartsWith EndsWith Contains Like` | `*`-wildcards (preserved) |
| Element | `Exists` | `$exists` |
| Collection | `Has HasAny HasAll HasNone Size` | `$in`→HasAny, `$all`→HasAll, `$nin`→HasNone, `$size`→Size on a collection field |
| Logical | `And Or Not Nor` | `$and $or $not $nor` |

The DSL keeps Mongo ergonomics; the AST is unambiguous because the parser knows the leaf
type (`$in` on `string` → `In`; `$in` on `List<string>` → `HasAny`). This is exactly why
typed field-binding is foundational, not cosmetic.

### Canonical null / `Nin` semantics (DECIDED — canon below)

Mongo's `$nin` matches missing fields; SQL `NOT IN` with a NULL in the set returns nothing.
If left to each translator they will diverge and "not in" returns different rows per
adapter — a direct violation of the entity-first transparency promise.

**Canon (locked):** null/absent is **not a member of any set**; therefore `Nin` and
`HasNone` **match** null/missing values (Mongo-aligned), and `In`/`HasAny` do **not**.
Rationale: entity-first developers reason about objects, where "doesn't have that value"
should be included by "not in". It is also the semantic hardest to reproduce by accident
in SQL, so making it canon forces every translator to handle null explicitly (a feature:
it prevents silent divergence). Enforced identically in every `IFilterTranslator` and the
`InMemoryFilterEvaluator`, and pinned by the conformance suite.

---

## 4. Change ledger (create / change / remove / harvest)

**Harvest / promote**
- `VectorFilter*` → `Koan.Data.Filtering` (`FilterNode` hierarchy + `FilterOperator`).
- `VectorFilterExpression` → `LinqFilterLifter` (+ collection/`In` shapes + `FilterOpaque`).
- `VectorFilterJson` → the AST's single JSON parse/write.
- `MemberPathResolver` (sort path) → shared field binder for filter + sort + projection.
- `ILinqSqlDialect` → `ISqlDialect` (extended with JSON-containment emit).
- `ProjectionResolver` / `RelationalStorageShape` → the projection axis.

**Create**
- `IFilterTranslator<TNative>` + `OperatorCapabilities` + `FilterExecutionCoordinator`
  + `InMemoryFilterEvaluator` + `QuerySpec` + the per-axis `RepositoryQueryResult` envelope.

**Change**
- `Data.QueryWithCount` → takes `QuerySpec`; routes through the coordinator (no untyped
  `object?` switch; inline count).
- Every data adapter → implement `IFilterTranslator` + declare `OperatorCapabilities`;
  delete its bespoke fallback block.
- `EntityEndpointService` / `EntityController` → build `QuerySpec`; map translation failure
  to 400/422 (not 500); fix the POST `/query` `$options` key bug (`""` → `"$options"`);
  one `EntityRequest → QuerySpec` builder shared by GET and POST.
- `VectorQueryOptions.Filter` → `FilterNode`.

**Remove**
- `JsonFilterBuilder` (expression-building), `LinqWhereTranslator`,
  `CouchbaseLinqQueryTranslator`, one of ES/OS translators, the 9 relational fallback
  `catch` blocks, the standalone `TryBuildGuidFilter` walker, the `?dir=` special case and
  the 7 `DataQueryOptions.With*` copiers (DATA-0092 leftovers).

**Supersede (docs/canon)**
- **Superseded now:** DATA-0029 (DSL), DATA-0031 (ignoreCase → AST per-node flag),
  DATA-0092 (sort → `QueryDefinition` axis). Headers updated to `status: Superseded`,
  `superseded-by: DATA-0096`.
- **Supersession pending:** DATA-0056 (vector filter AST). The unified `Filter` AST was harvested
  from it, but the vector path is schemaless (no CLR-type binding) and its provider translators are
  untested with no live store — the node-model collapse is deferred to a scoped follow-up (see §9).
  DATA-0056 remains authoritative for the vector path until then; its defensive-publication doc is
  left unchanged to avoid claiming a collapse that has not landed.

---

## 5. Test surfaces (the deliverable objective)

The pipeline has **zero** tests today; the rebuild ships its own canon.

1. **AST unit matrix** (`[Theory]`): operator × member shape (scalar `string/int/long/double/bool/enum/Guid/DateTimeOffset` + nullable; collection `List<string>`/`string[]`) × {DSL parse, LINQ lift, JSON round-trip}. Asserts the produced `FilterNode` **and** that DSL and LINQ produce the *same* node (the convergence guarantee). Runs against `InMemoryFilterEvaluator` — no Docker.
2. **Translator conformance matrix**: every `IFilterTranslator` fed each `(node, operator)` asserts a correct native shape **or** an explicit `NotSupported` — **no silent `_ => Eq`** (the current vector translators have exactly that masking default).
3. **In-memory parity oracle**: `InMemoryFilterEvaluator` is the reference; each adapter's pushdown result must equal the in-memory result for the same data + filter.
4. **Convergence suite — the cross-architecture parity proof (ARCH-0079).** The headline
   acceptance test. One canonical `(entity corpus, filter corpus)` runs against a matrix of
   **architecturally distinct** stores and asserts **identical result-id sets** for every
   filter, regardless of storage model, query surface, or pushdown-vs-fallback:
   - relational JSON-column: **Sqlite**, **Postgres**, **SqlServer**
   - document native-array: **Mongo**
   - key-value blob (full scan): **Redis**
   - file / in-process: **JSON file**, **InMemory** (the parity oracle)
   - N1QL document: **Couchbase**
   Add `Tags : List<string>` (scalar collection) to the `Widget` test entity, seed
   deterministically, and run `$in`/`$all`/`$nin`/`$size`/empty-array + scalar/string/nested
   cases through real `AddKoan()` discovery via `KoanIntegrationHost`. The invariant is
   **convergence**: the same `Filter` returns the same entities everywhere. Per-adapter
   capability gating lets honest adapters run and degraders skip-with-reason — a skip is a
   visible gap in the matrix, never a silent pass.
5. **Capability-honesty spec**: for each operator an adapter *claims* pushable, assert (a) correct rows **and** (b) `Koan-InMemory-Filter` is **not** set (it really pushed down). Kills declared-vs-actual drift — the DATA-0092 bug class.
6. **Fallback + pagination spec**: filtered **and paginated** queries return correct pages on relational adapters (the current silent bug); the in-memory floor is **bounded** (caps/refuses an unbounded scan).
7. **Negative / error contract**: unknown operator, wrong-type RHS, `$in`/`$all` non-array, `$size` non-int, `$exists` on non-nullable value type → structured 400, never 500 or silent `0`.
8. **`$nin`/null canon spec**: the chosen null semantic returns identical rows on every adapter + the evaluator.

---

## 6. Sequencing (parallelism is gated behind the frozen contract)

- **Phase A — keystone (sequential, freeze):** promote AST; operator registry + superset
  enum; `OperatorCapabilities`; `IFilterTranslator` + `InMemoryFilterEvaluator`;
  `FilterExecutionCoordinator`; `QuerySpec` + per-axis result envelope; `JsonFilterParser`
  + `LinqFilterLifter` (shared binder/coercer); endpoint error mapping + `$options` fix.
  Ship the AST unit matrix + parity oracle here.
- **Phase B — adapter fan-out (parallel, worktree per adapter):** each adapter implements
  `IFilterTranslator` + native collection containment + declares `OperatorCapabilities` +
  deletes its old fallback. InMemory/JSON/Redis = floor only. Vector family adopts the
  contract. Ship the conformance matrix + per-adapter integration + honesty specs.
- **Phase C — integration (sequential):** capability self-reporting (well-known + boot +
  headers + MCP); projection pushdown axis; finalize operators; ADR supersession + defensive
  publication update; the fallback/pagination + negative-contract specs.

---

## 7. Decisions (locked)

1. **Null / `Nin` canon** — null/absent is not a member of any set; `Nin`/`HasNone` **match**
   null/missing, `In`/`HasAny` do not (Mongo-aligned). Enforced identically in every
   translator and `InMemoryFilterEvaluator`; pinned by the convergence suite.
2. **Mongo GUID footgun** — the global `SmartStringGuidSerializer` must **not** apply to
   `List<string>` elements; collection-element serialization is carved out so string arrays
   round-trip as BSON strings. Done in Mongo Phase B; covered by the convergence suite's
   `List<string>` cases.
3. **`ClrFilter` policy** — un-liftable LINQ evaluates in memory by default (never silently
   dropped), logged and signalled via the `Koan-InMemory-Filter` header. No strict-reject mode in v1.
4. **Projection** — ship the `QueryDefinition.Projection` axis + in-memory projection floor
   in Phase C; adapters fill native `SELECT` pushdown incrementally (mirrors staged sort).
5. **`?q=` raw string path** — retained as an explicit escape hatch, gated behind a capability
   flag; never the default and never a silent match-all.

---

## 8. Implementation log (break-and-rebuild, Option B)

Strategic refinements adopted mid-implementation (fewer, more meaningful parts):

- **A — Adapter contract inversion.** The adapter is a *translator + executor*, never an
  orchestrator. `FilterPushdownCoordinator` (Koan.Data.Core) owns the split/residual/sort/
  paginate-after algorithm once; adapters declare `FilterCapabilities` and translate only the
  guaranteed-pushable filter they receive. `RepositoryQueryResult.Residual` was removed — the
  coordinator already knows the residual from the split. This is what makes the 9 copy-pasted
  relational fallback blocks *deletions with no replacement*.
- **B — `FilterCapabilities` is the query-capability truth.** The operator-blind `QueryCapabilities`
  flags enum is demoted to a derived summary; `IDataRepository` is writes-only; raw provider
  queries live behind `IRawQueryRepository`.

### Status

Landed + green (branch `feat/unified-filter-pipeline`):
- Core filtering namespace (AST, resolver, converter, evaluator, JSON + LINQ front-ends,
  capabilities, splitter, coordinator) — 31 unit specs pass.
- Contract: `QueryDefinition`, per-axis `RepositoryQueryResult`, `IQueryRepository`, `Projection`.
- Orchestrator (`Data<T,K>`/`Entity<T>`/`RepositoryFacade`) rewritten; entity-first DX preserved
  (`Query(lambda)`, `Query(string)` = DSL, new `QueryRaw` escape hatch).
- Adapters InMemory / JSON / Redis = Full floor.
- Consumers: Web endpoint (EntityController HTTP contract unchanged; `$options` key bug fixed;
  parse/unsupported → 400), CQRS decorator, soft-delete controller, GraphQL connector.
- Verified green: Direct, Vector, Vector.Abstractions, Backup, MCP.

Landed since: all 8 data adapters migrated and green — relational trio (shared `SqlFilterTranslator`,
native JSON containment), Mongo (`FilterDefinition` + GUID carve-out), Couchbase (N1QL
`ANY…SATISFIES`) — plus `CachedRepository`. Cross-architecture **convergence acceptance gate**
(19 specs: 18 filters × 5 capability profiles + paginate-after-residual) is green with zero
infrastructure. ADRs DATA-0029/0031/0092 marked Superseded.

## 9. Deferred follow-ups

1. **Vector filter node-model collapse (supersedes DATA-0056).** Repoint the 5 provider translators
   (Qdrant/Milvus/Weaviate/Elastic/OpenSearch) from `VectorFilter` nodes onto the unified `Filter`
   AST and retire `VectorFilter*` + `VectorFilterOperator`. NOT done because vector metadata
   filtering is schemaless (no CLR-type binding/coercion, distinct from entity filtering) and the
   translators are untested with no live store. Prerequisite: a no-container translator conformance
   suite (assert each translator's native output for a `Filter`-node matrix), then collapse + delete.
   The unused `VectorFilterExpression` LINQ lifter has already been deleted (dead code).
2. **Per-adapter live integration specs (ARCH-0079).** Container-backed specs that replay the
   convergence corpus against real Sqlite/Postgres/SqlServer/Mongo/Couchbase/Redis, gated by adapter
   availability — promoting the in-memory convergence proof to live stores.
3. **Projection pushdown.** The `QueryDefinition.Projection` axis + in-memory floor exist; native
   `SELECT`-column pushdown per adapter is staged for incremental fill (mirrors how sort was staged).
4. **Capability self-reporting.** Surface `FilterCapabilities` through `/.well-known/Koan/aggregates`,
   the boot report, a `Koan-InMemory-Filter` response header, and the MCP filter schema.
