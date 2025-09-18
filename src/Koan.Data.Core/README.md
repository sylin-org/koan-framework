# Sylin.Koan.Data.Core

Data access core for Koan: common primitives, options, and helpers used by relational/document/vector providers and apps.

- Target framework: net9.0
- License: Apache-2.0

## Capabilities

- Entity contracts and helpers for aggregate storage
- Options and conventions shared across data adapters
- Support for paging, streaming, and batching semantics (see references)

## Install (minimal setup)

```powershell
dotnet add package Sylin.Koan.Data.Core
```

## Usage - quick examples

- Prefer first-class model statics for top-level data access in your app models:
  - `Item.All(ct)`
  - `Item.Query(predicate, ct)`
  - `Item.FirstPage(pageSize, ct)` and `Item.Page(cursor, ct)`
  - `Item.QueryStream(predicate, ct)`
- For large sets, use paging or streaming; don’t materialize unbounded results.
- If a first-class static isn’t available, you can fall back to the generic facade (second-class): `Data<TEntity, TKey>.Query(...)`.

See TECHNICAL.md for contracts, options, and extension points.

## Customization

- Configuration and advanced usage are documented in [`TECHNICAL.md`](./TECHNICAL.md).

## References

- Data access patterns: `/docs/guides/data/all-query-streaming-and-pager.md`
- Decision: `/docs/decisions/DATA-0061-data-access-pagination-and-streaming.md`
- Engineering guardrails: `/docs/engineering/index.md`
- Repo: https://github.com/sylin-labs/Koan-framework
