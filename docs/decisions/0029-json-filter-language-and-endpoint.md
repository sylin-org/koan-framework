# ADR 0029: JSON Filter Language and Query Endpoint

Date: 2025-08-17

Status: Accepted

Context
- We need a provider-agnostic, user-friendly way to express filters that can be translated to LINQ and pushed down to adapters when supported.
- Existing abstractions support string queries and LINQ predicates; LINQ is broadly supported (Mongo, Json, Sqlite translator), while string queries are provider-specific.

Decision
- Adopt a small, pragmatic Mongo-like JSON filter subset without regex, featuring wildcards (* prefix/suffix/both) and boolean composition.
- Provide a filter builder that converts JSON to `Expression<Func<TEntity,bool>>` with relaxed type coercion (e.g., ISO date strings, Guids, enums).
- Expose a POST collection endpoint (`/query`) in `EntityController` that accepts `{ filter, order, page, size, set, $options }`; compute `X-Total-Count` after filtering, then paginate.
- Also accept a querystring variant on GET: `GET /?filter={...}` (URL-encoded or single-quoted tolerated) and optional `set` parameter.
- `$options.ignoreCase` applies to all string comparisons unless overridden locally.

Consequences
- Simple, intuitive payloads map directly to LINQ, enabling pushdown to adapters and safe in-memory fallback with guardrails.
- No regex simplifies translation and avoids security pitfalls.
- Adds a small surface area to the web layer and a reusable core filter builder.

References
- Data abstractions: `ILinqQueryRepository<TEntity,TKey>`
- Sqlite translator: `Sora.Data.Relational.Linq.LinqWhereTranslator`
- ADR 0031: `$options.ignoreCase` details and provider compatibility
