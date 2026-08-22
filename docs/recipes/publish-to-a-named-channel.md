---
type: RECIPE
recipe: publish-to-a-named-channel
title: "Publish approved records to a separate store"
domain: data
status: current
last_updated: 2026-08-19
audience: [ai-agents, developers]
framework_version: v1.0.0
validation:
  status: source-verified
  scope: snippets copied from samples/applications/DevPortal, which compiles and runs
gets_you: "A second, named store that only receives what you approved — without a second model or a copy job."
works_if: "Some records are drafts and some are published, and readers should only ever see the published ones."
costs: "A second store to configure and back up. Both can be embedded files, so this need not add a service."
ingredients:
  - "one | entity store for the working set | Sylin.Koan.Data.Connector.Sqlite, Sylin.Koan.Data.Connector.Postgres, Sylin.Koan.Data.Connector.MySql, Sylin.Koan.Data.Connector.Mongo"
  - "one | a second configured source for the channel | Sylin.Koan.Data.Connector.Sqlite, Sylin.Koan.Data.Connector.Postgres, Sylin.Koan.Data.Connector.MySql, Sylin.Koan.Data.Connector.Mongo"
---

# Publish approved records to a separate store

The model does not change. The *route* changes, scoped and visible, for exactly the operation that
publishes.

## When this is the answer

"Editors draft, readers only see approved." "Staging and live." "Push the approved subset to the
reporting database."

Reach for this when the same Entity legitimately lives in more than one place and one of those places
has a different audience. Do **not** reach for it to represent status — if drafts and published rows
are the same audience with a flag, a field is the right answer and a second store is overhead.

The questions that decide the design:

- **Is the second store a different audience, or a different status?** Audience justifies a store;
  status does not.
- **What is the identity of a published record?** Publishing twice must upsert the same identity, not
  accumulate duplicates. Decide the key before the first publish.
- **Does publishing remove?** Un-approving something usually should withdraw it, and that is a separate
  operation people forget to build.
- **Does the channel need the same provider?** No. The working store and the channel can be different
  technologies; the Entity code does not know.

## Assembly

No extra package — this is composition. Configure a second named source alongside the default, then
scope the publish operation to it:

```csharp
using (EntityContext.Source(channel.ToString()))
{
    await approved.Save(ct);
}
```

The scope is disposable and nestable, so the exceptional route is visible at the exact place it
applies and restores itself afterwards. Ordinary reads and writes elsewhere in the application are
untouched and still hit the default store.

DevPortal does this with two local SQLite files under `.koan/` — `Default` for editorial, `Preview` for
the channel — so the whole pattern runs with no container and no external service.

## Prove it

1. **Behavior** — approve two of three records, publish, and assert the channel holds exactly the two.
2. **Idempotency** — publish again and assert the same identities are upserted rather than duplicated,
   and that the working store is unchanged.
3. **Composition** — assert both sources resolved to the providers you intended. Two SQLite files look
   identical from application code, which is the risk.
4. **Correction** — make the channel unreachable and assert publishing fails visibly instead of
   silently writing to the default store.

## Boundaries

- Adding a source never moves existing data. This publishes going forward; a backfill is separate work.
- There is no transaction across the two stores. A publish can partially succeed, so make it re-runnable.
- The channel is a copy, not a mirror. Nothing keeps it in step unless you publish again.

## Interacts with

**Background work.** Publishing a large approved set belongs in a job, with the batch as its receipt.

**Tenancy.** A named source does not carry a tenant boundary by itself. Scoping to a channel and
scoping to a tenant are separate decisions, and the channel needs both.
