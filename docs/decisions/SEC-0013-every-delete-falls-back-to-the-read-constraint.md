---
id: SEC-0013
slug: every-delete-falls-back-to-the-read-constraint
domain: Security
status: Accepted
date: 2026-10-21
title: Every delete verb falls back to the Read constraint
related:
  - SEC-0004
  - DATA-0124
---

# SEC-0013: Every delete verb falls back to the Read constraint

## Context

An `EntityAccess<T>` realization narrows rows per action through `Constrain`. `DeleteAll` bounded itself by the Delete
constraint, else the Read constraint, "so an author who scopes reads but forgets Delete cannot accidentally truncate".
`Delete`, `DeleteMany` and `DeleteByQuery` used the Delete constraint only, and a missing one passed every row. A
realization that constrains only `AccessAction.Read`, the mistake `DeleteAll` anticipates, let any caller who passed
the remove gate delete other owners' rows by id, by id list or by query, over REST and MCP (ledger NK-026). The per-row
manifest's `remove` verb read the Delete constraint alone as well.

## Decision

**One bound decides which rows a principal may delete: the Delete constraint, else the Read constraint.**
`EntityEndpointService.DeleteBound` computes it; `Delete`, `DeleteMany`, `DeleteByQuery`, `DeleteAll` and the per-row
projection's `remove` verb all use it. A row outside the bound is a `404` for `Delete`, skipped by `DeleteMany`, and
excluded from `DeleteByQuery` and `DeleteAll`, as for an explicit Delete constraint.

Update and Patch keep their own constraint; a Read-only realization still lets an update reach a row the caller cannot
read. That is recorded as a follow-up, not changed here.

## Compatibility

- An application whose realization constrains Read but not Delete loses the ability to delete rows outside its read
  scope through the entity endpoints. Deleting such rows needs an explicit Delete constraint (`_ => q` for Delete
  restores the old reach).
- No public signature or stored format changes.

## Evidence

- `ReadConstraintDeleteSpec` (Koan.Web.Extensions.Tests), over a realization that constrains Read only: deleting
  another owner's row by id is `404`; a bulk delete with both owners' ids deletes one; a delete-by-query matching both
  owners' rows leaves the other owner's row. All three failed before this change.
