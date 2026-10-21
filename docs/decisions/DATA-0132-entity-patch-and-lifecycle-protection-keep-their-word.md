---
id: DATA-0132
slug: entity-patch-and-lifecycle-protection-keep-their-word
domain: Data
status: Accepted
date: 2026-10-21
title: Entity patch honours its null policy, and lifecycle protection guards handlers, not write stamps
related:
  - DATA-0074
  - WEB-0035
  - DATA-0131
---

# DATA-0132: Entity patch honours its null policy, and lifecycle protection guards handlers, not write stamps

## Context

**Patch (ledger NK-045).** `Entity.Patch(id, partial, PartialJsonNullPolicy?)` and `PatchMerge(id, delta,
MergePatchNullPolicy?)` built RFC 6902 operations, emitting `replace null` (Patch) or `remove` (PatchMerge) for every
null whatever the policy, and `PatchOpsExecutor` passed the options only to a method that ignored them. `Ignore` and
`Reject` had no effect, and a merge null aimed at a value-type member threw a serializer error instead of applying
`SetDefault`. The policy-honouring applicators (`PartialJsonApplicator`, `MergePatchApplicator`) existed and were used
only by Koan.Web. Measured before this change: `Patch(id, new { Notes = null }, Ignore)` persisted `Notes = null`;
`PatchMerge(id, new { Priority = (int?)null })` threw `JsonSerializationException`.

## Decision

1. **The Entity patch statics use the same applicators as Web.** The partial or delta becomes a JSON document, the
   applicator merges it into a working copy of the stored row under the requested null policy (`SetNull` and
   `SetDefault` stay the defaults), and the copy is saved. A refused member (`Reject`, identity or family shape)
   throws before any write. The live stored instance is never edited.
2. **Last-writer-wins is stated.** Read and save are two operations, so concurrent patches to different members can
   lose one update. The XML docs say so and point to `ReplaceIf` for a guarded write. Inside
   `EntityContext.Transaction` a patch refuses (DATA-0131).
3. `Data<T,K>.Patch(PatchPayload)` keeps its RFC 6902 surface unchanged.

## Compatibility

- `Ignore` now leaves members unchanged, `Reject` throws `InvalidOperationException` before any write, and a merge null
  on a value type sets its default instead of throwing.
- Nested objects merge as the Web patch merges them (members not mentioned survive).
- No public signature or stored format changes.

## Evidence

`EntityPatchNullPolicySpec` (Koan.Tests.Data.Core, InMemory): partial-JSON `Ignore` keeps the value, `Reject` throws
and writes nothing, the default sets null; merge-patch on an `int` member sets 0 by default and `Reject` throws and
writes nothing. Both specs failed before this change.
