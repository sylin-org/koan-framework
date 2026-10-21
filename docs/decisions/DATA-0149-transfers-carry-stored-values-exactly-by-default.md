---
id: DATA-0149
slug: transfers-carry-stored-values-exactly-by-default
domain: Data
status: Accepted
date: 2026-10-21
title: Copy, Move and Mirror carry stored values exactly by default; Save semantics are an opt-in
related:
  - DATA-0079
  - DATA-0105
  - DATA-0111
  - DATA-0113
  - DATA-0125
  - DATA-0143
  - DATA-0148
---

# DATA-0149: Copy, Move and Mirror carry stored values exactly by default; Save semantics are an opt-in

Amends [DATA-0079](DATA-0079-entity-transfer-helpers.md), which defines the transfer builders.

## Context

DATA-0079 built `Copy`, `Move` and `Mirror` on ordinary Entity reads and writes, and said nothing about what that does
to the rows. Every row a transfer read ran Load lifecycle, every destination write ran Upsert lifecycle and every
registered write stamp, and a `Move` ran Remove lifecycle at the origin. So:

- an archiving `Move` set every `[Timestamp(OnSave = true)]` property to the move time and then deleted the originals,
  losing the last-modified values for good, and a `Copy` to staging marked every row as fresh;
- an `AfterLoad` hook that mutates its entity had that mutation persisted to the destination;
- a `BeforeUpsert` validator could cancel a `Move` partway through on legacy rows;
- Remove hooks (notifications, application cascades) fired for every moved row;
- a bidirectional `Mirror` stamped the winner with the mirror time on the losing side, so the next run found that side
  newer and wrote it back, every run, and `MirrorConflict.Latest` compared mirror times instead of edit times. An edit
  made on the losing side during a run lost to the restamped copy on the next run.

Ledger NK-050 and NK-109, confirmed in source. DATA-0111 kept cutover off the public builders partly for this reason.

## Decision

Owner decision, 2026-10-21: **Copy, Move and Mirror transfer data exactly by default.**

1. **Exact is the default.** A transfer's own reads and writes run no Load, Upsert or Remove lifecycle and no
   registered write stamp. Each row arrives with the values it was stored with, `[Timestamp]` values included.
2. **Identity is still filled.** The identity stamp still runs, so a row that has no identity is given one.
3. **The boundary is unchanged.** Every read, write and delete is still an ordinary facade call under the caller's
   ambient context: source policy, the registered storage guards, tenancy and every isolation axis, field transforms
   (a transformed field is decrypted on read and encrypted again on write), the mutation horizon, DATA-0125's
   same-place refusal, DATA-0143's Move proof and the exact receipts all apply. A transfer reads and writes only inside
   the caller's scope, so a managed tenant field is written with the tenant it was read under, which is the tenant it
   was stored with.
4. **Save semantics are an opt-in on the builder.**

   ```csharp
   await Todo.Copy().To(partition: "staging").Fidelity(TransferFidelity.Semantic).Run(ct);
   ```

   `TransferFidelity.Semantic` restores the earlier behaviour exactly: Load lifecycle on every row read, Upsert
   lifecycle and every write stamp on every destination write, Remove lifecycle for every row a `Move` or `Mirror`
   deletes. `TransferFidelity.Exact` names the default. The verb sits beside `Batch`, `Audit` and `Conflict`.
5. **Application callbacks stay ordinary.** The `Audit` callback is application code; Entity work it does runs with
   full Save semantics, not as part of the transfer.

### Mechanism

`Run` enters an internal ambient flag (`TransferFidelityScope`) after admission and before the first read, and leaves it
when the run ends. `RepositoryFacade` reads it to leave Lifecycle out of the operation, and `StorageWritePlan` reads it
to apply the identity stamp only. Nothing else consults it. The facade, not the builder, decides, because Lifecycle and
write stamps are applied there for every path a transfer takes (bounded streams, `GetMany`, `UpsertMany`,
`DeleteMany`, the soft-delete override write).

### Cutover

DATA-0111 gave two reasons for not reusing the public transfer builders as cutover's execution owner: they ran ordinary
Entity save semantics, and they have no whole-host inventory. The first no longer holds for the default. The second
still does, and cutover keeps its own execution owner; nothing about cutover changes here.

## Consequences

- NK-109 is fixed by construction. An exact bidirectional mirror writes the winner's own timestamp, so once both sides
  hold one edit time the next run writes nothing, and a later edit on either side is newer than the mirrored copy.
- A Move's fingerprint check re-reads the origin without Load lifecycle, so a non-deterministic Load hook no longer
  makes every row look changed under the default.
- NK-184 (soft delete distorts transfers) is fixed only in part. The tombstone a `Move` writes at the origin of a
  `[SoftDelete]` Entity keeps its stored timestamps instead of being stamped with the move time. What remains: a `Copy`
  under `WithDeleted()` still writes deleted rows as visible (the managed `__deleted` value is not carried), a `Move`
  still leaves every origin row as a tombstone and reports it in `DeletedCount`, and a plain transfer still omits
  tombstones.
- Lifecycle-driven side effects (embedding on save, notifications, derived fields) do not happen for transferred rows.
  An application that relied on them asks for `TransferFidelity.Semantic`.

## Compatibility

This changes what a transfer writes, not whether it runs: no application fails at boot and no stored value is lost.
An application whose transfers relied on Save semantics (fresh OnSave times on copied rows, a validator that rejects
rows, a hook that derives a field) keeps that behaviour with `.Fidelity(TransferFidelity.Semantic)`.

## Evidence

- `EntityTransferDslSpec` (Data.Core): `Transfer_KeepsStoredTimestampsByDefault` (Copy, Move, Mirror),
  `Transfer_RunsNoLifecycleOrWriteStampsByDefault` (Copy, Move, Mirror; Load, Upsert and Remove hooks, a registered
  write stamp, an `AfterLoad` mutation), and `Mirror_Bidirectional_ConvergesAndKeepsTheWinnersTimestamp` (NK-109) fail
  with the flag disabled and pass with it. `Transfer_WithSemanticFidelity_WritesEachRowAsASave` proves the opt-in, and
  `Transfer_AuditCallbackRunsAsOrdinaryEntityWork` that the audit callback runs outside the flag.
- `TenantTransferSpec` (Tenancy, SQLite, row-scoped tenancy): an exact `Move` under one tenant reads only that tenant's
  rows, keeps the stored OnSave timestamp and the tenant, and leaves the other tenant's rows untouched. It fails with
  the flag disabled.
- The spec adapter in `EntityTransferDslSpec` now hands out copies of stored rows, as every real adapter does, so a
  Load hook cannot change the stored row in place.
