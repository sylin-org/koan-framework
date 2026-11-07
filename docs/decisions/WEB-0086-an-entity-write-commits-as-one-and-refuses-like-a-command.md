---
id: WEB-0086
slug: an-entity-write-commits-as-one-and-refuses-like-a-command
domain: Web
status: Accepted
date: 2026-11-06
title: An entity write commits as one and refuses like a command
amends:
  - ARCH-0092
  - ARCH-0143
related:
  - DATA-0166
  - SEC-0036
  - ARCH-0145
  - ARCH-0146
---

# WEB-0086: An entity write commits as one and refuses like a command

## Context

`EntityController<T>`'s writes already run every seam that can refuse or extend a write: the model hooks around each save, the entity
lifecycle inside it, and record access. What they did not have was one outcome. A hook that appended a log row, stamped a counter or
gave a role wrote it durably beside the row, so a refusal after the save left the hook's rows, or the row, behind. A `Refusal` a hook
or a lifecycle handler threw, and a lifecycle `Cancel`, reached ASP.NET as an unhandled exception and answered 500. A seam denial
answered a bare 401 or 403 with no body, an insert that met another row's id answered 404 whatever the reason, and a hook that stopped
a write with the stored row, as a replay does, answered that row without the emit the first write had gone through. A command had all
of this through `Send`: one hold, and the one refusal mapping.

## Decision

1. **A write runs from its prior read to its last after-hook as one.** Every mutating path of the shared endpoint (`Upsert`,
   `UpsertMany`, `Patch`, `Delete`, `DeleteMany`, `DeleteByQuery`, `DeleteAll`), on REST and on the MCP entity tools alike:
   - inside a scope already open it joins, and the scope's owner commits it, rolls it back and hears whatever refuses;
   - otherwise, where the entity's provider declares `write.heldTransaction`, it is a hold of its own on the route the entity is
     stored on, wherever the ambient context points: `Data<T>.Hold`, which `RoleCollection`'s changes (SEC-0036) and the audit
     chain's appends take too;
   - elsewhere it runs as it does without one.
2. **The hold opens at the prior read.** That is after the gate, field admission and `OnBeforePatch`, so an application judges costly
   input with nothing held. It commits after `OnAfterSave`, `OnAfterPatch` or `OnAfterDelete`, and before the emit hooks, which compose
   the answer reading only. It rolls back on a hook's stop of 400 or more, a thrown `Refusal`, an `EntityLifecycleCancelledException`,
   any other throw, and a rehearsal (`dry_run`). A role or overwrite change, an audit-chain append or a `Sequence` number a hook takes
   joins it, and so does any write of a hook on the hold's route.
3. **The prior row is on the hook context.** `HookContext<T>.Prior` is the row as it stood where the hold opened, or null for a create,
   set before `OnBeforeSave` or `OnBeforeDelete` of every single write. It is the same item the MCP state delta projects from, so the
   delta no longer waits for an opt-in, and the opt-in key is gone.
4. **A refusal answers as a command's does.** A thrown `Refusal` answers through `Answers.Refused`, keeping its kind, word and field.
   A lifecycle cancellation is mapped once, in `Answers`: a 409 conflict whose word is the handler's code, or `conflict` where it names
   none, and whose detail is its reason. A seam denial answers through `Answers.Denied`: a refusal with its reason, otherwise the one
   sentence of its kind. A hold that cannot begin, because another writer kept the store for the whole of its busy timeout, never ran,
   so the write answers as a command that never ran does, through `Answers.NeverRan`: `503` with `Retry-After`, nothing written and
   safe to send again, on REST and through a route tool, whose `meta` carries `retryAfter` and `landed: false`; an entity tool carries
   the same problem, and the header among the endpoint's headers.
5. **A create whose id another write took first answers 409 `id-taken`** wherever the caller may read that row, as a create that lost a
   race to a visible row does, whether or not they may change it. Only a row their record access hides, through its Read constraint,
   stays the existence-hiding 404, as a read of it answers. The constraint is judged as a read judges it: a row predicate on the row,
   a counterpart by the store, through the Data facade. Field access and row projection shape what a read shows, never whether the
   row exists for the caller, so they change nothing here.
6. **A hook that stops a write with a row answers it as written.** The emit composes that row as it would a saved one, so a replay
   answers exactly as its first write did, and what the hold did stands.
7. **An entity tool carries the problem.** `ResponseTranslator` hands a refusal the endpoint answered as a `Problem` to the edge, which
   puts it in `meta.problem` and `structuredContent` with its own status, as a route tool's refusal is (ARCH-0143 §4 named an entity
   tool's `errorCode` as not yet carried over). The tool's `errorCode` keeps its vocabulary.

## Consequences

- A write that holds keeps every other writer of its store waiting until it ends, so a hook inside it stays short, and work beyond the
  store (a fetch, a mail) belongs before the hold, in the override or `OnBeforePatch`, or after it, in an emit or the hold's
  `AfterCommit`.
- A hook's write to another route refuses inside a hold, as every write there does (DATA-0166): an entity stored on its own source
  or adapter holds there, and a hook that writes an entity stored elsewhere answers 500 where it wrote before. Such a write belongs
  in the hold's `AfterCommit` or an emit hook, or the hook's entity on the same route.
- Every single upsert reads its prior row, including a plain one with no constraint.
- A lifecycle `AfterUpsert` inside an entity write runs before the hold commits (the lifecycle reference says so).

## Compatibility

- An application that mapped `EntityLifecycleCancelledException` or `Refusal` to its own answer for entity writes no longer sees them
  reach its filter there; an action of its own still does.
- A denied entity write or read answers a `Problem` body where it answered none.
- A rehearsal on a holding provider rolls back what its hooks wrote.

## Evidence

- `BusyStoreSpec` (Koan.Mcp.Verbs.Tests, real Kestrel and SQLite with a one-second busy timeout): behind a command holding the store, a
  declared create and change answer `503` with `Retry-After: 1` on REST and on `tools/call`, the tool's `meta` saying `landed: false`;
  nothing of them is written, and the create sent again once the store is free answers `201`.

## Ergonomics

New public concepts: `HookContext<T>.Prior`, and `Data<T>.Hold`, a hold on the route an entity is stored on, or none where that
route cannot hold. Removed: `EntityMutationProbe.WantsDeltaKey`. Nothing is declared to get a write's hold: the provider's capability
decides, as it does for a role change.
