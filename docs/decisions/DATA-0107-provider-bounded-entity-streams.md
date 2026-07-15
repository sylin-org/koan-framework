---
id: DATA-0107
slug: DATA-0107-provider-bounded-entity-streams
domain: DATA
status: Accepted
date: 2026-07-15
---

# DATA-0107: Provider-bounded Entity streams

## Context

`Entity<T>.AllStream` and `QueryStream` exposed `IAsyncEnumerable<T>`, but loaded the complete query
before the first yield and ignored `batchSize`. The call shape suggested backpressure and bounded
work that the implementation did not provide. Earlier proposed cursor and Pager designs were never
implemented, while sorted-stream guidance accepted full materialization as a fallback.

Koan needs one small, honest primitive beneath the Entity surface. Physical providers vary, so an
adapter must earn the claim rather than Data.Core assuming that every numbered page is provider
bounded.

## Decision

Keep `IAsyncEnumerable<TEntity>` as the only public stream substrate. Do not add a public cursor,
Pager, continuation token, provider stream, or Flow type.

Data.Core owns one stream coordinator. For each enumeration it:

1. captures the routed provider, source, partition, and registered logical-context carriers;
2. requires `DataCaps.Query.ProviderBoundedPaging` before provider I/O;
3. validates the caller's semantic order, then appends the actual Entity identifier as a separate
   provider-stable page tie-breaker;
4. requests one numbered candidate page at a time with no total-count strategy;
5. validates that pagination and the complete order were handled and that the returned candidate
   count does not exceed `batchSize`;
6. yields that page before requesting another, so cancellation, disposal, and consumer pace stop
   later work; and
7. rejects a numbered page before provider I/O when its zero-based offset cannot be represented by
   the current `Int32` `Skip`/`OFFSET` contract.

A residual predicate may run pointwise over each bounded candidate page. Empty residual output does
not terminate candidate traversal.

Every user-requested stream sort component must be a single-member, top-level path. The initial
cross-adapter semantic floor is deliberately small and exact: non-nullable `bool`, `byte`, `sbyte`,
`short`, `ushort`, and `int`. Nullable values, enums, strings/chars, `uint`, 64-bit integers,
floating-point/decimal values, temporal values, `Guid`, binary, nested, complex, collection sorts, and
an explicit Entity-identifier sort reject before provider I/O instead of materializing or returning a
provider-specific interpretation.

The Entity identifier is a separate rule. After validating the caller order, Koan appends the exact
identifier only when it is the usual string key because the shared six-adapter corpus proves that
key as an opaque, provider-stable tie-breaker. That makes numbered pages total; it is not a CLR or
cross-provider collation promise, which is why a caller cannot request Entity-identifier ordering.
Every custom identifier shape rejects before provider I/O. A future type is admitted only
after the six-adapter semantic matrix proves its full value domain, null order where applicable, and
storage encoding.

Identifier detection is member-exact: a business property named `id` does not suppress an `Id`
key tie-breaker. This coordinator rule does not make CLR models declaring both `Id` and `id` portable
storage models. Relational naming and document-serializer conventions can collide for members that
differ only by case; those models are outside this decision and the qualified-adapter matrix.

`batchSize` bounds the Koan-visible candidate page. It does not claim a bound for opaque driver
buffers. Numbered offset paging does not provide snapshot consistency, mutation-safe traversal,
resumability, or cross-process continuation. A requested page must satisfy
`(pageNumber - 1) * pageSize <= Int32.MaxValue`; a stream refuses the next page before crossing that
provider-offset boundary.

## Capability qualification

The following adapters have passed the shared realization cell:

| Behavior | Adapters |
|---|---|
| Provider-bounded Entity streams | SQLite, PostgreSQL, SQL Server, CockroachDB, MongoDB, Couchbase |
| Corrective rejection before query/yield | InMemory, JSON, Redis |

An adapter capability declaration is necessary but not sufficient. Every returned page must report
that it applied pagination and the total order. False execution metadata becomes a typed
`QueryStreamRejectedException`; Koan never silently returns to complete-result materialization.

## Inspectability

The first selected or rejected execution records a redacted `koan.data.stream.execution` runtime fact
for the Entity. Composition reports only capabilities known at startup; it does not pretend that a
lazy per-Entity repository has already been selected.

## Consequences

- Existing Entity/Data stream signatures remain source-compatible, with natural cancellation-token
  overloads added.
- Supported streams are consumer-paced and avoid implicit count work.
- `Page` remains an explicitly materialized numbered-page API. It is not a cursor or resumable stream.
- Consumers such as Data Backup inherit the capability boundary. Unsupported adapters fail before
  export rather than hiding a full-source scan.
- Data Backup's source enumeration is page-bounded, but its current ZIP archive is still accumulated
  in one `MemoryStream`; this decision does not certify total backup memory.

## Supersession

This decision supersedes the unimplemented cursor/Pager and stream sections of DATA-0061 and its
duplicate ADR-0050. It also supersedes DATA-0093 section 2: a sort inside the proved semantic floor
remains provider-bounded; unsupported sorts reject rather than triggering full-result materialization.

## Evidence

- Core coordinator contract, rejection, runtime-fact, cancellation/disposal, residual, overclaim,
  exact-identifier, offset-overflow, exact scalar-floor, and context-stability specifications.
- Shared adapter realization/fail-closed conformance across all nine adapters listed above. With a
  page size of two, each qualified adapter orders type-specific boundary values for non-nullable
  `bool`, `byte`, `sbyte`, `short`, `ushort`, and `int`; the same cell separately proves default
  string Entity-identifier ordering and uniqueness across page boundaries.
- SQLite provider metadata/count specification.
- Real Data Backup acceptance through SQLite and local storage: pages `2/2/1`, no count, caller
  cancellation during page 2 prevents its completion and archive publication, and early rejection
  for InMemory/JSON.
