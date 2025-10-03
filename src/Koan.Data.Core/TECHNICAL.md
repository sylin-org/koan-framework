---
uid: reference.modules.Koan.data.core
title: Koan.Data.Core - Technical Reference
description: Contracts, options, design and operations for the Koan data core.
since: 0.2.x
packages: [Sylin.Koan.Data.Core]
source: src/Koan.Data.Core/
---

## Contract

- Inputs/Outputs
  - Core interfaces and helpers for aggregates (IEntity<TKey>, conventions, helpers)
  - Generic facades as second-class helpers: `Data<TEntity, TKey>`
- Options (selected)
  - Paging defaults and limits (via typed Options)
  - Naming and constants policy (see ADR ARCH-0040)
- Error modes
  - Validation and adapter errors surface as exceptions; adapters may wrap provider errors
- Success criteria
  - Consistent paging/streaming semantics across adapters; predictable options binding

## Key types and surfaces

- Primary abstractions: `IEntity<TKey>`, helpers and extensions for data operations
- Extension points: adapter/provider implementations consume these primitives

## Configuration

- Prefer typed Options for tunables; avoid magic values
- Centralize constants per project (see ARCH-0040)
- Paging and streaming follow DATA-0061

## Usage guidance

- In application models, expose first-class statics:
  - `Item.All(ct)`, `Item.Query(...)`, `Item.FirstPage(...)`, `Item.Page(...)`, `Item.QueryStream(...)`
- Reserve `Data<TEntity, TKey>` for cases where no first-class static exists
- For large sets, prefer paging/streaming; avoid unbounded `All()`

## Pager and EntityCursor

- Stable iteration order is by `Id ASC` across adapters.
- `EntityCursor` captures position and optional includeTotal; treat it as an opaque resume token.
- `Pager` consumes a cursor and yields pages with `Items`, `HasMore/End`, and a continuation `Cursor`.
- Typical flow: `FirstPage(size)` → render/process → `Page(cursor)` until `HasMore=false`.
- For long-running jobs and resumability, use `EntityCursor` directly with `Pager`.

## Streaming semantics

- `AllStream/QueryStream` iterate lazily in batches; honor `batchSize` and `CancellationToken`.
- Backpressure: awaiting inside the loop throttles production naturally.
- Error handling: throw on first error; callers should checkpoint between batches if needed.

## Edge cases and limits

- Large result sets → require paging/streaming
- Concurrency and batches → follow transactional batch semantics (see DATA-0007)
- Adapter capabilities vary; use capability flags when present

## Observability and security

- Integrate with platform logging/tracing; propagate operation context
- Data exposure is adapter-specific; enforce auth/permission at the Web or service layer

## Design and composition

- Data.Core provides shared building blocks used by relational/document/vector adapters
- Composes with Web controllers via application models exposing first-class statics

## Deployment and topology

- No standalone runtime; shipped as a library used by apps and adapters

## Performance guidance

- Prefer paging/streaming over materializing large sets
- Batch operations according to adapter guarantees

## Compatibility and migrations

- Target frameworks: net9.0
- Works with: Koan.Data.Connector.Sqlite, SqlServer, Postgres, Redis, Mongo, Vector providers

## References

- ADR ARCH-0040 - config and constants naming: `/docs/decisions/ARCH-0040-config-and-constants-naming.md`
- ADR DATA-0061 - paging/streaming semantics: `/docs/decisions/DATA-0061-data-access-pagination-and-streaming.md`
- Engineering guardrails: `/docs/engineering/index.md`

