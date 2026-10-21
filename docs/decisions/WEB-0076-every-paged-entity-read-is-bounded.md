---
id: WEB-0076
slug: every-paged-entity-read-is-bounded
domain: Web
status: Accepted
date: 2026-10-21
title: Every paged entity read is bounded by one policy, on REST and MCP
related:
  - WEB-0068
  - DATA-0092
  - SEC-0010
---

# WEB-0076: Every paged entity read is bounded by one policy, on REST and MCP

## Context

`GET /` clamps `page` and `pageSize` to the controller's `PaginationPolicy`, and unpaged reads are capped by
`PaginationSafetyBounds.AbsoluteMaxRecords`. The other entity reads were not bounded (ledgers NK-025, NK-023):

- `POST /query` copied the body's `size` into the page size unclamped; `size: 0`, `page: 0` or a negative size skipped
  pagination, and `QueryCollection` received a safety limit of 0, which means none. `{"size": 2147483647}` read the
  table in one response.
- The MCP query tool copied `pageSize` unclamped; the schema's `maximum` was never enforced, because schema validation
  runs for mutations only.
- The MCP collection tool paged only under `forcePagination`, so `{page: 2, pageSize: 10}` returned the whole set,
  every page the same. Its policy came from the static `PaginationSafetyBounds.Default`, so configured bounds had no
  effect on MCP. Code-mode `page()` and `firstPage()` therefore never advanced.

## Decision

**`PaginationPolicy.Clamp` is the one rule for a page: a page below 1 is 1, a size below 1 is `DefaultSize`, a size
above `MaxSize` is `MaxSize`. The shared endpoint applies it to every paged read.**

1. `GetCollection` clamps whenever the request applies pagination, and falls back to the policy's `AbsoluteMaxRecords`
   when a caller passes no safety limit.
2. `Query` is always paged and clamped. Its policy is the new `EntityQueryRequest.Policy` (the controller passes
   `GetPaginationPolicy()`), else the configured defaults. It passes `AbsoluteMaxRecords` and refuses an exceeded read
   with `413`, like the collection.
3. The MCP edge resolves the policy from DI (`PaginationPolicy.Resolve`), as REST does. The collection tool pages when
   the call names `page`, `pageSize` or `forcePagination`, using `DefaultSize` when no size is given; without any of
   them it returns the set up to `AbsoluteMaxRecords`, and SEC-0010 renders an exceeded read as a `413` error.
4. The collection schema describes `forcePagination` and `pageSize` as they behave.

## Compatibility

- `POST /query` with `size` 0 or negative now returns `DefaultSize` rows (50 by default), and a `size` above `MaxSize`
  returns `MaxSize` rows (200 by default), instead of every row. `page: 0` is page 1.
- MCP collection calls with `page` or `pageSize` now return that page, with `X-Page` headers in `_meta.headers`. MCP
  query calls never exceed the configured maximum page size.
- Configured `Koan:Web:Pagination` bounds now apply to MCP.
- `PaginationPolicy.Clamp` and `EntityQueryRequest.Policy` are added; no signature or stored format changes.

## Evidence

- `QueryBoundsSpec` (Koan.Web.Extensions.Tests): over 230 rows, `size` 0, -5, `page` 0 return 50; `size` 100000 and
  `int.MaxValue` return 200; page 2 of 100 returns 100. The first five cells returned 230 before this change.
- `McpPagingSpec` (Koan.Mcp.Conformance.Tests), bounds `MaxPageSize = 5`, `AbsoluteMaxRecords = 8`, 12 rows: page 2
  of 3 returns rows 4 to 6; a collection page size of 100 and a query page size of 100000000 return 5; an unpaged
  collection is a `413` error; code-mode `page(1, 3)` and `page(2, 3)` are disjoint. All four failed before.
