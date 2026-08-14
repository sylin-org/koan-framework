---
name: koan-data-cutover
description: Plan and execute a verified single-host promotion of the active default Entity database to an empty SQLite, MongoDB, or PostgreSQL source, including cross-provider moves, preflight correction, quarantine handling, and durable route-state preservation.
pillar: data
card: docs/guides/data/default-route-cutover.md
status: current
last_validated: 2026-08-06
---

# Koan verified Data cutover

## Trigger this skill when you see

- “move”, “migrate”, “switch”, or “promote” the application's active default database;
- SQLite to MongoDB, MongoDB to PostgreSQL, or another move within that three-provider set;
- `Data.Source("...").PromoteToDefault()`, `Plan()`, cutover receipts, or target quarantine;
- a requirement to keep ordinary `Entity<T>` calls unchanged after changing stores; or
- an operator asking how to preserve or recover the active Data route state.

Do not use this skill for one-operation routing, read replicas, partitions, or provider comparison;
use `koan-multi-provider` for `EntityContext.Source(...)` / `.Adapter(...)`. Do not present Cutover as
rolling multi-node migration, live replication, transformation, merge, or automatic rollback.

## Core principle

**A default database changes only after the complete application-owned Entity set is bounded-copied,
identity-matched, canonically verified, and durably activated.** The operator owns external writer
quiescence. Koan owns host-local mutation draining, preflight, verification, route publication,
stale-handle rejection, and quarantine after possible partial target mutation.

## Complete intent surface

Before recommending or executing a cutover, establish all of these:

1. `Sylin.Koan.Data.Cutover` and both provider connectors are referenced.
2. The target is a physically distinct named source with `Managed + ReadWrite` policy.
3. Exactly one application host will run the transition.
4. Every writer outside that host's Koan Data path is stopped or externally quiesced.
5. `Koan:Data:Cutover:WriterOwnership` is explicitly
   `HostExclusiveOrExternallyQuiesced`.
6. The operator reviews every `Plan()` blocker and Entity disposition before `Run()`.
7. `.Koan/data/active-route.json` (or `Koan:Data:Route:StatePath`) is preserved with the
   deployment.
8. A quarantined target is emptied or reprovisioned before retry.

If any item is unknown, report it as an unmet prerequisite. Never manufacture confidence from a
successful connection test alone.

## Canonical expression

<!-- validate -->
```csharp
using Koan.Data.Core;
using Koan.Data.Cutover;

CancellationToken ct = default;

var transition = Data.Source("Mongo").PromoteToDefault();
var plan = await transition.Plan(ct);

if (!plan.CanRun)
    throw new DefaultRouteTransitionRejectedException(plan);

var receipt = await transition.Run(ct);
```

`Run()` performs fresh planning inside the serialized operation and again after mutation admission
closes. Do not turn a previous successful plan into a bypass flag.

## Reference = Intent

| Reference or declaration | Meaning |
|---|---|
| `Sylin.Koan.Data.Cutover` | The application may perform the dangerous verified transition operation; `AddKoan()` discovers it automatically. |
| Current and target connector packages | Both physical routes remain available to the host during copy, verification, and later explicit access. |
| Named `Koan:Data:Sources:<Target>` | The target's adapter, connection, database, lifecycle, and access policy. |
| `WriterOwnership = HostExclusiveOrExternallyQuiesced` | The operator asserts there is no writer beyond the host-local operation horizon. It is not a distributed fence. |
| `Plan()` | Non-mutating safety classification with stable blocker corrections. |
| `Run()` | Replan, drain, copy, verify, durably activate, and return bounded audit evidence. |

## Supported envelope

- One active host.
- SQLite, MongoDB, and PostgreSQL sources, including unlike-provider moves.
- String-keyed Entity roots on the default route.
- Complete source inventory and an empty managed writable target.
- Provider-bounded traversal and exact bulk-write receipts.

Fail closed for partitions/segmentation, managed fields, stored transforms, compatibility mappings,
custom read filters, delete overrides, unexplained source containers, incomplete inventory, same
physical source/target, external/read-only targets, or a nonempty target.

## Correction table

| Observation | Correct response |
|---|---|
| `plan.CanRun == false` | Surface every blocker code, subject, reason, and correction; do not call `Run()` as an override. |
| `DefaultRouteTransitionRejectedException` | Read `.Plan`; correct the configuration or topology, then plan again. |
| `DefaultRouteTransitionException.TargetMayContainData == false` | The old route remains active and target mutation was not observed; correct the failure and replan. |
| `TargetMayContainData == true` | Treat the target as partial and quarantined; empty or reprovision it before a new verified attempt. |
| Successful receipt | Preserve the route-state file, inspect facts/health, and retain the receipt; do not rewrite `Default` configuration to mimic the pointer. |
| Request for rollback | Explain that the old database is retained but not synchronized. A later promotion must independently satisfy the empty-target contract. |
| Request for zero-downtime or multi-host cutover | Report the missing external coordinator/writer-fence architecture; do not approximate it with this package. |

## Anti-patterns to flag

| If you see | Suggest |
|---|---|
| `using (EntityContext.Source("Mongo"))` presented as changing the default | That scope routes only contained operations. Use `Data.Source("Mongo").PromoteToDefault()` for durable verified promotion. |
| Public `Entity.Copy`, transfer builders, or backup restore used for a whole-app cutover | Use Cutover; those paths do not own exhaustive application inventory, route activation, or canonical verification. |
| Changing the configured `Default` connection string under a running app | Keep source plans immutable and promote a distinct named source through the route authority. |
| Running two hosts or transition commands concurrently | Stop competing hosts/commands; this envelope has one host-local transition lease, not a cluster fence. |
| Retrying against a partially populated target | Empty or reprovision the quarantined target first. |
| Comparing source and target page positions | Verify by exact identity. Provider string collation is not a cross-provider guarantee. |
| Deleting the old database immediately after success | Retain it until the operator's recovery/decommission policy says otherwise; Cutover never deletes it. |

## Proof expected

- Focused transition specs for blockers, stale handles, failure injection, quarantine, route revisions,
  cache isolation, and cold restart.
- A real-provider round trip for every claimed provider family. Koan's graduated matrix proves
  SQLite → MongoDB → PostgreSQL → SQLite with exact identities, target-only writes, and cold
  hydration.
- Product-surface generation and a consumer compile of the terse public expression.

## See also

- [Default-route cutover how-to](../../../docs/guides/data/default-route-cutover.md)
- [Data capability](../../../docs/reference/data/index.md)
- [Cutover package README](../../../src/Koan.Data.Cutover/README.md)
- [Cutover technical contract](../../../src/Koan.Data.Cutover/TECHNICAL.md)
- [DATA-0111](../../../docs/decisions/DATA-0111-verified-default-data-cutover.md)
- [Product surface](../../../docs/reference/product-surface.md)
