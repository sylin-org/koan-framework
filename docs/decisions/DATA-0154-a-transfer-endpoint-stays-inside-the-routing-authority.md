---
id: DATA-0154
slug: a-transfer-endpoint-stays-inside-the-routing-authority
domain: Data
status: Accepted
date: 2026-10-21
title: A transfer endpoint cannot leave its Database-mode route or address the retired default by accident
related:
  - ARCH-0101
  - ARCH-0102
  - DATA-0079
  - DATA-0111
  - DATA-0125
  - DATA-0153
---

# DATA-0154: A transfer endpoint cannot leave its Database-mode route or address the retired default by accident

Amends [DATA-0079](DATA-0079-entity-transfer-helpers.md), which defines `From()` and `To()`.

## Context

An explicit source always wins routing (ARCH-0103 §4.1), and two routing authorities stand behind the sources a
transfer endpoint can name:

- **A Database-mode axis** routes each operation of an Entity to the source the ambient scope names, usually the
  tenant's database (ARCH-0102 §3). A named source outranks it and carries no tenant boundary, so under tenant A
  `Todo.Copy().To(source: "Shared").Run()` wrote A's rows where every tenant reads them, and
  `From(source: <B's database>)` read B's rows. Nothing refused or reported it (NK-107).
- **The default-route authority** moves the active default during a verified cutover (DATA-0111). An explicit
  `"Default"` deliberately keeps addressing the configured, now retired, store. A scheduled
  `Todo.Copy().From(source: "Default").To(source: "Archive")` archived a frozen snapshot, and
  `Todo.Move().From(source: "Default").To()` upserted retired rows over the active store, reverting every edit made
  since, then deleted them (NK-112).

## Decision

At admission, after both endpoints are resolved and before anything is read, a transfer checks every endpoint whose
route comes from an explicit source, whether named in `From()`/`To()` or inherited from an ambient
`EntityContext.Source`:

1. **After a cutover, `"Default"` is refused.** When an endpoint names `Default` (any letter case) and the active
   default is another route, the transfer throws. The correction: leave the source unset to use the active default;
   to reach the retired store on purpose, configure it under its own source name and name that. Ordinary
   `EntityContext.Source("Default")` access keeps its documented meaning; only transfers refuse it, because a transfer
   writes or deletes in bulk on the strength of the name.
2. **Under a Database-mode axis, a source other than the routed one needs `AcrossDatabaseRoutes()`.** When the
   Entity's axis routes the current scope to a source and an endpoint names a different one, the transfer throws unless
   the builder opted in:

   ```csharp
   await Todo.Copy().To(source: "Shared").AcrossDatabaseRoutes().Run(ct);
   ```

   A run that crosses records the `koan.data.transfer.route.crossed` runtime fact. The fact names the Entity, never a
   source key, which can itself identify a tenant.
3. An endpoint that names no source follows the authority (the routed database, the active default), as before.

## Consequences

- A transfer naming `Default` after a cutover, or naming another tenant's or a shared source under a Database-mode axis,
  fails before it does any work, where it used to run. No application fails at boot and no data is lost; the
  correction is in the message.
- `AcrossDatabaseRoutes()` is a new builder verb beside `Batch`, `Audit`, `Fidelity` and `Conflict`.

## Evidence

- `MultiDatabaseRoutingSpec` (Data.Axes.Integration, two SQLite shards): the cross-route `Copy` to the default and from
  the other shard's source throw without the opt-in, an unnamed-source `Copy` stays in the shard, and the opt-in runs
  and records the fact. Fails before this change.
- `DefaultRouteTransitionSpec.After_a_cutover_a_transfer_naming_Default_is_refused_and_the_active_store_is_untouched`
  (Data.Cutover, SQLite): a `Move` from and a `Copy` to `Default` after a cutover are refused and the post-cutover edit
  survives. Fails before this change.
