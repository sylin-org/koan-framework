# Document-adapter playbook

Authority: `docs/architecture/data-adapter-development-primer.md`, corrected against the code. This
playbook was derived from the assessed document exemplars (**Mongo** — the full document adapter,
`src/Connectors/Data/Mongo/`) and proven by **CouchDB** (`src/Connectors/Data/CouchDb/` — pure
`HttpClient`, no driver package). The data playbook's general rules (oracle hosting, truth gates,
package mechanics) apply unchanged; this file covers only what differs on the document seam.

## What the seam gives you (and what it doesn't)

- There is **no document substrate** like `Koan.Data.Relational`. The adapter implements the
  repository contracts directly against the provider client; `Koan.Data.Abstractions`/`Koan.Data.Core`
  carry the filter AST (`Filter`), `QueryDefinition`, `RepositoryQueryResult`, naming, and the
  managed-field pipeline.
- The **default (non-mapped) path serializes the whole entity** with Newtonsoft: camelCase contract
  resolver, `EntityJsonSerialization.Apply` (store-authoritative hydration —
  `ObjectCreationHandling.Replace`), `ManagedFieldJsonInjector` (framework-managed discriminators ride
  as top-level document fields, which is exactly what makes row-isolation predicates pushable on a
  document store), and the four temporal converters (DateTimeOffset → UTC DateTime, TimeSpan → ticks,
  DateOnly → `yyyy-MM-dd`, TimeOnly → `HH:mm:ss.fffffff` — copy the converter classes from
  `MongoEntityPlan`). Identity is stored in the store's reserved key (`_id`).
- Explicit maps (`MappingPlan`) are a second, optional realization; Mongo supports both. If your
  store cannot honor managed fields under an explicit map, refuse map+managed at construction like
  Mongo does (a declared map that silently loses row isolation is a leak).
- A document store lowers **collection operators natively** (`$in`/`$all`/`$size`/`$nin`), sorts and
  pages server-side, and needs no shadow columns. Declare the full scalar set + the collection set
  your selector language truly supports — probe each one; e.g. Mango matches `$all`/`$in`/`$size`/
  `$nin` (present-field) but NOT bare equality against an array element and NOT `$regex` across
  array elements, so those two stay undeclared and ride the floor.
- **Sort is where document stores cheat.** Server-side sort exists (Mongo cursors) or is
  index-gated (CouchDB `_find` refuses without a matching index; only the identity index is free).
  Declare `SortHandled` only for what the store really ordered; everything else flows to the
  framework sorter as a declared fallback. A bare-eq-on-array or sort-capability overclaim is how a
  silent in-memory fallback gets born.
- Upsert semantics are **outcome-level** obligations (insert/update/missing/conflict correct), not
  wire-level. CouchDB's MVCC means an upsert is read-rev-then-PUT — non-atomic, but the outcome is
  correct and that is what the oracle proves. Declares what is real: CouchDB `_bulk_docs` is
  per-document atomic → no `AtomicBatch` token; Mongo refuses batch transactions it hasn't proved.

## Repository surface (copy Mongo's shape)

`IDataRepository`, `IQueryRepository`, `IOptimizedDataRepository`, `IConditionalWriteRepository`,
`IInstructionExecutor`, `IDescribesCapabilities`, `IBulkUpsert<TKey>`, `IBulkDelete<TKey>`;
`IBoundedQueryRepository` + `DataCaps.Query.ProviderBoundedPaging` only with a real server-side
cursor (Mongo has one; unannounced = fail-closed, proven by the AODB suite).

- `GetMany` — one slot per input, in order, `null` for missing. Prefer the store's keyed batch read
  (CouchDB `_all_docs?include_docs=true` with `keys` preserves order and returns `null` rows).
- Query receipts: `FilterHandled` only for the AST the selector language truly lowered;
  `SortHandled`/`PaginationHandled` only for store-performed work. Counts: CouchDB Mango has no
  count — materialize empty-field rows and count them (exact, honest).
- Collection order keys (e.g. `-Sightings.LastChangedAt`): Mongo computes them in an aggregation
  pipeline; Mango cannot — return a null term and let `RelationalCollectionOrder`… (document side:
  the framework sorter owns it; do not fabricate a receipt).
- Mixed-space guard: a declared map pins its container; an ambient partition under it rejects.
- Writes: honor `ManagedFieldWriteScope.Current` as a write guard on every mutating path (Mongo
  composes it into the write filter and classifies duplicate-key failures as cross-scope writes).

## CouchDB specifics (probed 2026-08-29, couchdb:3.5, HTTP API)

- Revisions: updates and deletes require the current `_rev` (409 without). Upsert = GET current rev →
  PUT; treat the 409 race by re-reading once, never by swallowing.
- `GET /{db}/{id}` 404 = missing; `PUT /{db}` creates a database (managed lifecycle only); `GET /{db}`
  404 = absent (declared-shape validation reports it without creating).
- Mango `_find`: full-scan works with a `no_matching_index` warning (store-executed — honest
  receipt); `sort` hard-fails `no_usable_index` without a matching index — only `_id` sorts for free.
- Count = `_find` with `"fields":[]` materialized. `_all_docs` keyed batch = GetMany semantics.
- Containers: CouchDB has no collections — the adapter maps one entity container to one database
  (`{namespace prefix}_{container}`); listing = `_all_dbs` filtered by prefix; partition isolation
  resolves to a distinct database; DatabaseScoped routing selects the prefix.
- Basic auth over the admin party is disabled in 3.x by default; configure user/password via options.

## Tests

Mirror `tests/Suites/Data/Connector.Mongo/Koan.Data.Connector.Mongo.Tests`: the AODB conformance
subclass (`RoutedSourceSettings` → two routed sources; provision two databases via the HTTP API),
filter convergence, capability truth, boot provenance. Skip-with-reason the cells the store
genuinely cannot answer (document the reason in the spec file header, exactly as Firebird does).

## CouchDB dogfooding corrections (2026-08-29)

- **Bulk writes must set `_id` explicitly** — `_bulk_docs` silently assigns server uuids otherwise,
  and the divergence shows up later as wrong-id reads, not as a write error.
- **Top-level `_`-prefixed members are rejected** (`doc_validation`). Managed discriminators ride a
  legal `koan` subdocument: hoist on write, restore on read, selector path `koan.<name>`.
- **Bare equality on a collection is parser-lowered to element-match** (`Has`) — probe the WIRE, not
  your mental model: raw Mango `{"tags":"x"}` matches nothing, but the AST arrives as `Has` and
  `$all` matches. Convergence proves the composite path.
- **The pushdown guard carries a second `$like` battery** with a caller-pinned posture
  (`expectsHasContainsPushdown`): a store that cannot lower element-LIKE hosts the guard with
  `false` and is held to residual-and-recorded.
- **Fallback facts are snapshot-keyed by code+subject** — a spec asserting "the fact appears" must
  use a query shape no earlier spec ran, or the dedupe hides the entry it is looking for.
- **`$exists` compiles to present-and-non-null** when the write path stores JSON nulls.
