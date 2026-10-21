---
id: WEB-0077
slug: every-delete-verb-raises-the-delete-hooks
domain: Web
status: Accepted
date: 2026-10-21
title: Every delete verb raises the model delete hooks
related:
  - WEB-0068
  - SEC-0013
  - SEC-0010
---

# WEB-0077: Every delete verb raises the model delete hooks

## Context

`IModelHook<T>.OnBeforeDelete` and `OnAfterDelete` are the documented place for per-row delete rules and their side
effects; WEB-0068 names `IModelHook` as the owner of write authorization (its `IAuthorizeHook` no longer exists). Only
`EntityEndpointService.Delete` raised them. `DeleteMany`, `DeleteByQuery` and `DeleteAll` called `Data.DeleteMany` or
`RemoveAll` directly, so `DELETE /bulk`, `DELETE /?q=`, `DELETE /all` and the MCP `delete-many`, `delete-by-query` and
`delete-all` tools deleted a row its hook refuses, and no `AfterDelete` side effect (cascade, eviction, outbox) ran for
a bulk delete (ledger NK-054).

## Decision

**A delete verb raises `BeforeDelete` and `AfterDelete` for every row it deletes, as a single delete does.**

1. When model hooks may run (any `IModelHook<T>` is registered, or the entity uses a custom `IEntityHookPipeline<T>`),
   the bulk verbs load their target rows (within the SEC-0013 bound), run `BeforeDelete` for each before deleting any,
   delete, then run `AfterDelete` for each.
2. A `BeforeDelete` short circuit refuses the whole batch with that result; nothing is deleted. SEC-0010 renders it on
   MCP as a failed call carrying its status.
3. A dry run raises `BeforeDelete` and reports the count; it raises no `AfterDelete`.
4. Without hooks the verbs keep their current paths; an unbounded `DeleteAll` keeps `RemoveAll`.

This amends WEB-0068's scope note: write authorization belongs to `IModelHook`, which now covers every delete verb.

## Compatibility

- A bulk delete that includes a row a hook refuses now fails whole instead of deleting it.
- Applications with `AfterDelete` side effects now see them for bulk deletes.
- With model hooks registered, `DeleteAll` materializes the rows it deletes instead of issuing one `RemoveAll`, and
  `DeleteMany` loads each id. `AfterDelete` runs for every loaded row; a row removed concurrently between load and delete
  is still reported to it.
- No public signature or stored format changes.

## Evidence

- `BulkDeleteHookSpec` (Koan.Web.Extensions.Tests): a locked row refused by its hook survives `DELETE /bulk`,
  `DELETE /?q=` and `DELETE /all`, and a bulk delete raises `AfterDelete` once per deleted row. All four failed before.
- `BulkDeleteHookParitySpec` (Koan.Mcp.Conformance.Tests): the same refusal on the three MCP tools, as `409` errors,
  and a dry-run `delete-many` raises `BeforeDelete` but not `AfterDelete`. Both failed before.
