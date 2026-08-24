---
type: REFERENCE
domain: data
title: "Recoverable Entity deletion"
audience: [ai-agents, developers]
status: current
last_updated: 2026-08-23
framework_version: v1.0.0
validation:
  status: not-yet-tested
  scope: docs/capabilities/data/recoverable-deletion.md
---

# Recoverable Entity deletion

Let ordinary Entity removal hide a row while retaining explicit, type-scoped restore and physical
purge operations.

## You need

| Piece | Package | Note |
|---|---|---|
| Recoverable deletion semantics | `Sylin.Koan.Data.SoftDelete` | opt in on the Entity type |
| An Entity store | one Data connector | the provider must preserve Koan's managed deletion field |
| Recycle-bin HTTP surface (optional) | application-owned controller | authorize restore and purge explicitly |

## The constraint box

> **The constraint:** Soft deletion is a persistence semantic, not authorization, an audit log,
> retention policy, legal hold, or automatic recycle-bin API. Tenant and request filters still
> apply inside `WithDeleted()`; opening the recycle bin never bypasses them.

## Choose the operation deliberately

| User intent | Entity behavior |
|---|---|
| Remove from ordinary use | normal `Remove` hides the Entity |
| Inspect deleted rows of one type | enter that type's `WithDeleted()` scope |
| Put it back | load it in the scope, then restore it |
| Erase it physically | use the explicit hard-delete operation under product policy |

## Leaves

- **Build context:** [store and expose](../../recipes/store-and-expose.md)
- **Package contract:** exact attributes, scopes, restore, and purge mechanics:
  [SoftDelete README](https://github.com/sylin-org/koan-framework/blob/main/src/Koan.Data.SoftDelete/README.md)

If the product needs retention, audit, legal hold, or approval, model that policy separately rather
than treating a hidden row as the whole feature.
