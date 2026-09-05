---
type: REFERENCE
domain: data
title: "Entity relationships"
audience: [ai-agents, developers]
status: current
last_updated: 2026-08-27
framework_version: v1.0.0
validation:
  date_last_tested: 2026-08-27
  status: passed
  scope: cold-executed by an external agent on the SQLite path against published packages - [Parent] edge navigated as one/page/stream at 250-child scale, Required pagination clamps an oversized page with X-Page-Size, missing parent fails closed
---

# Entity relationships

Put the reference on the child with `[Parent]`, then navigate the same edge as one Entity, a bounded
set, or a stream without adding a repository layer.

## You need

| Piece | Package | Note |
|---|---|---|
| One Entity store | the application's chosen Data connector | relationship execution inherits its query limits |
| HTTP projection (optional) | `Sylin.Koan.Web` | enforce paging on child collections |
| Rarely changing lookup cache (optional) | `Sylin.Koan.Cache` | cache lookups, not unbounded working sets |

## The constraint box

> **The constraint:** `[Parent]` declares a navigable edge, not a foreign-key constraint, cascade
> policy, recursive graph loader, authorization rule, or snapshot boundary. Supply a finite page or
> stream budget, and keep parent and child inside the same tenant scope.

## Choose the read shape

| Need | Shape |
|---|---|
| One parent from a child | `child.GetParent<TParent>(ct)` |
| A bounded child collection | require paging at the controller or relationship call |
| A large sequential child workload | stream only when the selected provider proves bounded paging |
| Several possible parent types | declare each `[Parent]`; name the parent type at the call site |

## Leaves

- **Pasteable, source-verified build:** [model things that relate](../../recipes/model-things-that-relate.md)
- **Runnable exemplar:** [TaskGraph](https://github.com/sylin-org/koan-framework/blob/main/samples/fundamentals/TaskGraph/README.md)
- **Provider and relationship contract:** [Data reference](../../reference/data/index.md#relationships-stay-in-the-model)

## Count, and expand over HTTP

To answer "how many lines belong to this recipe," query the child by its foreign key — the same
governed surface, filtered. Count and existence questions are answered by the data layer's query
surface; reach into the relationship executor only when the governed query cannot express the ask.

Governed reads also expand declared edges on request: `GET /api/todos/{id}?with=user` returns the
todo with its parent inlined, gated by `Koan:Web:AllowRelationshipExpansion` (on by default; disable per app in `Koan:Web` configuration) and the relationship query policy. Write hooks run inside the same pipeline — use a `BeforeSave`-class hook to
canonicalize human units at the edge (store milliliters, accept "2 glasses", filter and compare in
canonical space), so expansions and comparisons always see the stored shape.

See also: [the entity verb map](../web/entity-api.md) and [agent surfaces](../agents.md) — the
same relationships projected to MCP tools.
