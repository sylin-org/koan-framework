---
id: DATA-0159
slug: lexical-search-over-declared-entity-text
domain: Data
status: Accepted
date: 2026-10-29
title: An entity's declared text is indexed lexically by the store that holds it
related:
  - ARCH-0084
  - ARCH-0135
  - DATA-0119
  - DATA-0158
  - DX-0043
---

# DATA-0159: An entity's declared text is indexed lexically by the store that holds it

Amends [DATA-0119](DATA-0119-adapters-express-grammar-the-framework-owns-guarantees.md): the relational schema
orchestrator plans one more structure, the lexical index, beside the keyed indexes. Extends the accessor inventory of
[ARCH-0135](ARCH-0135-capability-accessors-follow-one-anatomy.md) with `.Lexical`.

## Context

Measured at `7ba505e49` by a consumer that needs search across the titles and bodies of its records, bounded, ranked,
with excerpts:

- No type or file in `src/` named full-text or lexical search. `Sylin.Koan.Data.Vector` is semantic search over
  embeddings, and the capability map keeps search stores apart from entity persistence. Nothing of either was reachable
  through `Entity<T>` for words.
- The bundled SQLite (`SQLitePCLRaw` `e_sqlite3` 3.53.3) is compiled with FTS5; PostgreSQL has `tsvector` and GIN.
  Neither was used.
- An application today keeps a normalized text column and filters it with `Contains`: a table scan, no ranking, no
  excerpt, and a second column the application has to keep in step itself.

## Decision

1. **The declaration is on the entity.** `[Lexical]` (`Koan.Data.Abstractions.Annotations`) marks each string property
   whose words should be found; the declared properties of one entity form one index, searched together. One language
   per entity, named on any declared property (`Language`, a PostgreSQL text search configuration name such as
   `english` or `simple`, validated as a lower-case identifier) or `english` when none names one. `LexicalMetadata`
   compiles it once per type into a `LexicalSpec`; the mapping plan carries it as `MappingPlan.Lexical`
   (`MappingLexicalPlan`), resolved onto the same compiled bindings reads and writes use.
   It stands beside `IndexSpec` rather than inside it: a lexical index is not a key. It has no uniqueness, no order, no
   expression a planner matches and no tenant-scoped form, and every consumer of `IndexMetadata` — the relational
   orchestrator, Mongo's and Couchbase's index builders, Redis's TTL lookup — would have had to learn to skip it.
2. **The capability is a token and a facet.** `DataCaps.Query.Lexical` (`query.lexical`) says the provider keeps the
   index in step with every write of the row inside the same write unit and answers a bounded, ranked term search.
   The repository that declares it implements `ILexicalSearchRepository<TEntity,TKey>`: `Search(LexicalQuery, scope)`,
   `Rebuild()`, `Status()`. `LexicalQuery` is bounded by construction — a page from 1, a size between 1 and 1,000, a
   page that starts no deeper than 10,000 matches (`MaximumOffset`; amended 2026-10-29: a store ranks every match and
   discards the ones before the page, so a page past that point is a scan nobody reads, and a page whose offset would
   exceed the bound or overflow is refused when the query is built, by `LexicalQuery` and by `LexicalSearchQuery.Page`,
   naming the bound and the latest page the size allows), a snippet window between 1 and 64 words — and its terms are
   the letter-or-digit runs of the text, so no store ever parses query grammar a caller typed. Every term is optional;
   a row holding more of them ranks higher.
3. **The accessor follows ARCH-0135.** `Post.Lexical` is a C# 14 static extension member on every string-keyed
   `Entity<T>`, delivering the stateless `LexicalStatics<T>`: `Search(text, s => s.Top(20).Snippet())` answers
   `LexicalMatch<string>` (identity, score, snippet) best first, `Find(...)` loads the entities in rank order through the
   ordinary read path, `Rebuild()` indexes rows that existed before the declaration, `Status()` reports what the store
   holds. Verbs are short in the facade and qualified on the flat static the facade routes to:
   `Data<TEntity,TKey>.LexicalSearch`, `LexicalRebuild`, `LexicalStatus`. A declaration that names neither `Top` nor
   `Page` is refused. Scores are the store's own (SQLite's negated BM25, PostgreSQL's cover density) and compare within
   one answer only.
4. **A route that cannot keep the index refuses the first write.** The Data facade computes once per repository whether
   the entity declares `[Lexical]` and the route's provider declares `query.lexical`; when it does not, the first
   operation that is not a read is refused with a `NotSupportedException` naming the entity, the route, the provider,
   the token and the corrections (route to SQLite or PostgreSQL, or remove the declaration), before anything is held.
   A search on such a route is refused the same way. The relational orchestrator records the declaration as an
   unproved claim on a store without the feature and builds nothing; the refusal has one owner.
5. **The schema orchestrator plans it.** `IRelationalStoreFeatures.SupportsLexicalIndex` says whether a store keeps
   one; `RelationalSchemaPlan.Lexical` carries the `RelationalLexicalDefinition` (name `LX_<table>`, the parts in the
   dialect's terms, the language); `EnsureCreated` asks `IRelationalDdlExecutor.CreateLexicalIndex` under the same DDL
   consent as a keyed index, and validation reports a missing index as unverified, never corrective: reads and writes
   through the mapping work without it, and the search verb says so itself. On a route whose DDL policy did not build
   the index (`Validate`, `NoDdl`), `Search` and `Rebuild` ask the executor's `HasLexicalIndex` once per table before
   any query and refuse with a `NotSupportedException` naming the entity, the route, the missing structures and the
   corrections (allow DDL on the route, or create the structures); `Status` answers `Exists = false`.
6. **SQLite serves it from FTS5.** The executor creates a view that reads the declared values out of the row
   (`<table>__koan_lexical_source`), an FTS5 external-content table over it (`<table>__koan_lexical`, tokenizer
   `porter unicode61` for `english`, `unicode61` otherwise) and three `AFTER INSERT`/`UPDATE`/`DELETE` triggers that
   maintain it. External content keeps one copy of the text, the row's. The triggers run inside the row's statement,
   so the entry lands in the same write unit as the row — a single `Save`, `UpsertMany`, a batch, or the ambient
   atomic commit of DATA-0158 — and a write that fails or rolls back takes its entry with it. A set of objects that
   no longer matches the declaration is replaced and left empty until `Rebuild` (`INSERT INTO fts(fts) VALUES
   ('rebuild')`) fills it. `Status` compares the FTS document count with the row count.
7. **PostgreSQL serves it from `tsvector`.** The executor adds one stored generated column `__koan_lexical`
   (`to_tsvector('<language>'::regconfig, coalesce(<field>, '') || ' ' || ...)`) and a GIN index `LX_<table>`,
   fitted to 63 bytes as every index name is (DATA-0146). The server computes the column on every write and for every
   row the table already holds, so the index is current as soon as it exists; `Rebuild` has nothing to re-derive and
   answers the row count. The column's recipe is recorded as its comment, so a changed declaration replaces the column
   instead of silently serving the old one. The search runs `websearch_to_tsquery` over the OR-joined terms, ranks by
   `ts_rank_cd` and cuts snippets with `ts_headline`. CockroachDB, which shares the runtime, keeps the feature off.
8. **Facts and readiness.** The composition facts carry, per declaring entity, `data:lexical:<entity>` with its
   fields, language and the provider its route elects, and the entity's `Lexical` trait; the `data:lexical` health
   contributor reports per entity whether the routed store's index is current
   (`<provider>:current|stale:<indexed>/<rows>`, or `<provider>:absent`), `Degraded` while a rebuild is owed. A probe
   never runs an unbounded read: currency is measured by `Status`, which counts the container, so the contributor
   measures each entity at most once per `DataRuntimeOptions.LexicalReadinessInterval` (default five minutes) and
   answers the last measurement in between, with the interval and the time of the measurement in the report.
   `Entity.Lexical.Status()` called by an application is always exact.
9. **The capability is a facet of the data pillar, not a package.** The contracts live in `Koan.Data.Abstractions`,
   the declaration, accessor and refusal in `Koan.Data.Core`, the planning in `Koan.Data.Relational`, and the index in
   the connectors that already hold the rows. A package of its own would have carried only the accessor, and every
   connector would have referenced it anyway, which makes Reference = Intent say nothing; nothing new is operated.

## Compatibility

- Additive on every surface. An entity without `[Lexical]` pays one memoized type lookup per repository and nothing
  else; its schema is unchanged.
- A PostgreSQL table of a declaring entity gains a `__koan_lexical` column and an `LX_` index; a SQLite database
  gains a view, a virtual table with its shadow tables, and three triggers per declaring table, which `Inspect()` lists
  beside the application's own containers.
- `RelationalSchemaPlan`'s constructor gains an optional parameter; `IRelationalStoreFeatures` and
  `IRelationalDdlExecutor` gain default members; `NpgsqlFeatures.Declare`/`Describe` gain a defaulted `lexical`
  parameter, `NpgsqlStoreFeatures` a defaulted constructor argument, `NpgsqlRepositoryOptions` a `SupportsLexicalIndex`
  option that only the PostgreSQL factory sets.
- SQLite keys the index by the row's rowid. A `VACUUM` of a table whose primary key is text may renumber rows; after
  one, run `Rebuild`. Koan never issues one.
- A deleted row on SQLite now runs a trigger, so `DELETE FROM` of a declaring table is a row-by-row delete rather than
  the truncate optimization.

## Evidence

- `SqliteLexicalSearchSpec` (Connector.Sqlite): a saved row is found by a term from each declared field and not by an
  undeclared one; a two-term query ranks the row holding both terms first with a higher score; a removed row leaves
  and an edited row is found by its new text only; an unbounded declaration and a page over the ceiling are refused,
  and three pages over seven rows neither overlap nor skip; a six-word snippet carries the marked term; rows written
  before the declaration are counted as not current, are found only after `Rebuild`, which answers their count; the
  `Status` verb and the `data:lexical` readiness report the provider, the fields, the language and currency, and two
  probes in quick succession measure once; under a `Validate` DDL policy the row lands, `Search` and `Rebuild` refuse
  naming `LX_<table>`, the objects and the policy correction, and `Get` still answers the row; a second row that fails
  inside an Atomic scope rolls the first row's index entry back with the row; 100,000 rows built in
  8,251 ms, a two-term first page of 20 answered in 70 ms cold and 56 ms warm, SQLite 3.53.3 with FTS5 on NTFS.
- `PostgresLexicalSearchSpec` (Connector.Postgres, container): the same seven outcomes where the store differs —
  the GIN index exists under `LX_`, rows that existed before the declaration are current at once and `Rebuild` answers
  their count, and under a `Validate` DDL policy the row lands while `Search` and `Rebuild` refuse naming the column
  and the index.
- `LexicalPageBoundSpec` (Koan.Tests.Data.Core): a page whose offset would overflow and one past the maximum offset
  are refused by `LexicalQuery` and `LexicalSearchQuery.Page` naming the bound; the deepest allowed page builds and
  runs on SQLite, answering nothing past the only match.
- `LexicalDeclarationSpec` (Koan.Tests.Data.Core): the JSON and InMemory routes refuse the first save of a declaring
  entity naming `query.lexical`, the adapter and the corrections, holding nothing, and refuse the search; the
  composition facts carry `data:lexical:memo` with `provider:sqlite`, `fields:Title,Body`, `language:english` and the
  entity's `Lexical` trait.
