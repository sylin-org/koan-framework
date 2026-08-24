---
type: REFERENCE
domain: data
title: "Entity relationships"
audience: [ai-agents, developers]
status: current
last_updated: 2026-08-23
framework_version: v1.0.0
validation:
  status: not-yet-tested
  scope: docs/capabilities/data/relationships.md
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
