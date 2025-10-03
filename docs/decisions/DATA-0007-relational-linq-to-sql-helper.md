---
id: DATA-0007
slug: DATA-0007-relational-linq-to-sql-helper
domain: DATA
status: Accepted
---

# 0007: Minimal LINQ-to-SQL helper for relational adapters

 

## Context
Relational adapters benefit from LINQ predicates for ergonomics, but full LINQ providers are heavy to build and maintain. We want a pragmatic middle ground that covers common predicates while preserving safety and simplicity.

## Decision (proposed)
- Provide an optional, tiny translator that converts a constrained subset of `Expression<Func<TEntity,bool>>` into a parameterized SQL `WHERE` clause.
- Supported subset (initial):
  - Binary comparisons on scalar properties: `==`, `!=`, `<`, `<=`, `>`, `>=`.
  - Boolean `&&` / `||` and parentheses.
  - String methods: `StartsWith`, `EndsWith`, `Contains` (via `LIKE` with proper escaping and parameters).
  - Nullable checks: `x.Prop == null` / `x.Prop != null`.
- Out of scope: joins, projections, aggregates, method calls beyond the above, collection `Any` over navigations, custom functions.
- Safety: strictly parameterized output. Unsupported shapes throw `NotSupportedException` early.

## Consequences
- Relational adapters can advertise `QueryCapabilities.Linq` with better performance than in-memory filtering for common cases.
- Clear fallback path: if translation fails, adapters may either throw (strict) or fall back to in-memory filter after a coarse server-side prefilter.
- Keeps the core small; the helper lives in a relational toolkit namespace and is opt-in per provider/dialect.

## Implementation
- Namespace: `Koan.Data.Relational.Linq`.
- Dialect hooks: `ILinqSqlDialect` with `QuoteIdent`, `EscapeLike`, and `Parameter(index)`.
- Translator: `LinqWhereTranslator<TEntity>` → returns (WHERE sql, parameter values list). Unsupported nodes throw `NotSupportedException`.
- Cache: `RelationalCommandCache` for cached SELECT column lists per (entity,dialect).
- Integration: `Koan.Data.Connector.Sqlite` implements `ILinqSqlDialect` on `SqliteDialect`; repository tries pushdown and falls back to in-memory filtering on unsupported predicates.

## Implementation scenarios
1) MVP (small, high-perf, cross-DB)
- Scope: Translate simple predicates to a parameterized WHERE; no joins or projections; entity materialization only.
- Supported nodes: binary comparisons on scalars; And/Or; string StartsWith/EndsWith/Contains; null checks; collection Contains => IN.
- Performance: cache compiled translators keyed by expression shape+dialect; reuse parameter binders; avoid string concatenation in SQL (precompute patterns in parameters).
- Cross-DB: use dialect for identifier quoting and LIKE escape; compute patterns in .NET to avoid vendor-specific concatenation; leave pagination to controller or add LIMIT/TOP via dialect if Skip/Take is present.

2) Mixed pushdown
- Split predicate into translatable part P and remainder R; execute server-side WHERE P; apply R in-memory. Use index metadata to prefer pushing indexed columns.

3) Safety & correctness
- Always bind via Dapper DynamicParameters; escape LIKE wildcards with ESCAPE.
- Null semantics: map == null/!= null to IS NULL/IS NOT NULL.
- Type conversions: coerce parameters to underlying column types where possible.

4) Dialect features
- Hooks: Quote(name), LikeEscape(string), LimitOffset(sql, skip, take), ParameterPrefix.
- Collation/case-sensitivity: optional CaseInsensitive flag maps to LOWER(column) and lowercased parameter if needed (configurable per dialect).

5) Testing
- Golden tests: expression → SQL+params for each supported node/dialect.
- E2E: compare DB results vs in-memory LINQ on seeded datasets.
- Property-based tests for conjunction/disjunction associativity.

6) Fallbacks
- Unsupported shapes: throw NotSupportedException (strict) or fall back to mixed pushdown; advanced users can use StringQueryRepository or Instruction SQL.

## Alternatives considered
- linq2db (MIT): full LINQ provider with wide RDBMS support, bulk APIs, and high performance. Pros: complete, mature, active. Cons: heavier dependency; overlaps with Koan abstractions. Good candidate for an optional Koan adapter that wraps linq2db end-to-end.
- re-linq (Apache-2.0): provider-building foundation that turns LINQ expressions into an AST. Pros: purpose-built for providers; used by NHibernate/EF historically. Cons: still requires building our own translator and SQL generator.
- IQToolkit (license: see repo): toolkit for building IQueryable providers, includes SQL samples. Pros: reference implementation, educational. Cons: older, less active; integration effort similar to re-linq.
- System.Linq.Dynamic.Core (Apache-2.0): parses string-based dynamic LINQ into expressions. Pros: powerful for runtime predicates. Cons: not a LINQ-to-SQL translator; still need SQL generation; security considerations for runtime strings.
- RepoDB (Apache-2.0): hybrid ORM with expression support and robust bulk operations. Pros: practical, battle-tested; SQLite provider exists. Cons: becomes the data layer; overlaps with Koan’s repository model.
- ServiceStack OrmLite: fast typed ORM with LINQ-ish API. Pros: ergonomic. Cons: repo archived/moved; licensing/commercial concerns; not ideal as a core dependency.
- SqlKata (MIT): fluent SQL builder (no LINQ). Pros: clean, DB-agnostic, works well with Dapper. Cons: no expression parsing; could be used as the target builder for our minimal translator.

## See also

- Data Adapter Acceptance Criteria: ../support/data-adapter-acceptance-criteria.md

