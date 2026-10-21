---
id: AI-0040
slug: code-mode-sdk-reports-what-the-endpoint-did
domain: AI
status: Accepted
date: 2026-10-21
title: The code-mode SDK reports what the endpoint did
related:
  - AI-0014
  - SEC-0010
  - WEB-0076
---

# AI-0040: The code-mode SDK reports what the endpoint did

## Context

The code-mode SDK (`SDK.Entities.<Entity>`) runs each call through the same endpoint as a tool call, then turned the
result into a JavaScript value. `delete` discarded the result and returned `1`, even when the row did not exist, the
call was denied or it failed. `deleteMany` read a `count` property the endpoint never returns (it returns
`{ deleted }`), so it returned `1` on any success and `0` on any failure, without throwing. A denied `getById` became
`{}` and a denied collection `{ items: [], count: 0 }`. AI-0014 and the generated TypeScript declarations promise
`collection()` returns `{ items, page, pageSize, totalCount }`; it returned `{ items, count }` (ledger NK-024). A
multi-step agent script could proceed on a deletion that never happened.

## Decision

**An SDK call returns what the endpoint did, and throws when the endpoint refused.**

1. Every SDK call throws a JavaScript error `"<code>: <message>"` when the tool result failed; with SEC-0010 this
   covers denials (`unauthenticated`, `forbidden`), hook and validation refusals, and execution errors.
2. `delete(id)` returns `1` when the row was deleted and `0` when no row with that id is visible to the caller
   (`not_found`); `getById(id)` returns `null` for the same case.
3. `deleteMany(ids)` returns the endpoint's `deleted` count.
4. `collection`, `page` and `firstPage` return `{ items, count, page, pageSize, totalCount }`: the page served (from
   `X-Page`, `X-Page-Size`, `X-Total-Count`), or page 1 of the whole set when the read was unpaged.
5. A success with no payload is `null`, not `{}`.

This amends AI-0014's description of the SDK's return values.

## Compatibility

- Scripts that relied on `delete` returning `1` for a missing row get `0`; scripts that called a denied verb get an
  error instead of a success value.
- `deleteMany` returns the real count.
- Collection results gain `page`, `pageSize` and `totalCount`; `items` and `count` are unchanged.
- No public signature changes; the SDK proxy is internal.

## Evidence

- `CodeModeSdkHonestySpec` (Koan.Mcp.Conformance.Tests), over the anonymous STDIO sandbox: a denied delete throws
  `unauthenticated` and the row remains; deleting a missing id returns 0; `deleteMany` of three existing ids and one
  missing returns 3; page 2 of size 2 over four rows reports `count 2, page 2, pageSize 2, totalCount 4`. These four
  failed before this change. Denied reads throwing was already true after SEC-0010 and is kept as a guard.
