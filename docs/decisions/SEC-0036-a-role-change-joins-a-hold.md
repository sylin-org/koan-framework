---
id: SEC-0036
slug: a-role-change-joins-a-hold
domain: Security
status: Accepted
date: 2026-11-01
title: A role change joins a hold
related:
  - SEC-0021
  - SEC-0031
  - DATA-0166
---

# SEC-0036: A role change joins a hold

Amends [DATA-0166](DATA-0166-a-scope-can-hold-a-native-transaction-from-its-start.md) rule 5, which kept a role change out of
a hold. [SEC-0021](SEC-0021-resource-overwrites-resolve-in-discord-order.md) rule 5 stands: a deferred scope still refuses.

## Context

A command that gives a role and records why cannot be one outcome while role changes refuse inside every scope. A consumer
writes the role first, durably, and its own rows in a transaction after, so a failed commit leaves a role given and nothing
saying so. DATA-0166 kept a role change out of a hold for one stated reason: the write could run there, but its cache
invalidation and after-events would still run before the commit. A hold has a native transaction, so the write is no longer
the problem; what assumed the write was durable is.

Joining raises what `RoleCollection`'s locks did not have to answer:

1. **A lock released when a change returns does not protect a change that has not committed.** Another flow takes the role's
   lock, reads the committed state, places a role at the committed top and blocks on the store. The hold commits, and that
   write lands on a position the hold has just taken. It cannot read an uncommitted row, under WAL; it decides from a row about
   to be replaced.
2. **Keeping every lock until the end inverts their order.** A hold takes its locks in the order its body asks for them, and any
   other flow takes them in the documented order.
3. **The hold takes the store first; a change outside a scope takes the store last.** A change holding a lock and blocked on the
   store cannot finish until the hold ends, while the hold's owner waits for that lock. The hold's limit would end it after 20
   seconds, with every writer of the file stopped meanwhile.
4. **A compiled bag is shared.** A read inside the hold that found a shared entry would miss its own change, and one that compiled
   into a shared entry would publish uncommitted rows.
5. **An after-event at the commit runs in the flow that committed**, whose ambient transaction has ended. A write there refused,
   since a write in a completed scope has nowhere to go.

## Decision

1. **A role, member or overwrite change inside `EntityContext.Hold` joins it.** `Add`, `Remove`, `Define`, `Update`, `Rename`,
   `SetPermissions`, `Move`, `Delete`, `Overwrite`, `RemoveOverwrite` and a revoke all read, decide and write on the hold's
   connection. There is nothing new to learn: the same verbs, inside the same scope. Outside any scope a change behaves as it
   did. A deferred scope still refuses, and so does a hold that has committed or rolled back.
2. **A rollback, a failed commit or the limit leaves nothing.** No row, no moved position, no changed bag and no after-event. The
   dense order needs no repair: its shifts are rows, and they roll back with the rest.
3. **A hold passes one gate, alone, and stays until it ends** (`RoleChangeGate`, before every other lock). A change outside a scope
   passes with others like it. So no flow can decide from state a hold is about to replace (1), and no flow outside a hold can hold
   a lock a hold wants, so its locks are still released as each change returns and their order is unchanged (2). Reads pass no gate.
4. **`ITransactionCoordinator.WhenHoldEnds` is how the gate opens**, because `AfterCommit` runs after a commit only, and a rollback,
   a failed commit or the limit must open it too. It runs once, before any `AfterCommit` work. An abandoned hold therefore costs
   other flows' changes no more than its limit.
5. **The one futile wait refuses.** A hold's first change waits for the changes already under way, because most finish at once, and
   one that needs the store cannot (3). Past `RoleOptions.HeldChangeWait` (`Koan:Identity:Roles:HeldChangeWait`, 2 seconds) it throws
   `TimeoutException`, having written nothing, naming the correction: end the scope, which frees the store for that change, and start
   again. A short wait and a refusal are cheaper than the limit, which ends in the same failure 20 seconds later.
6. **Inside a hold a bag is compiled past the shared maps** (`RoleBagCache`), as the Entity cache reads past itself (DATA-0166 rule 9). A
   read sees its own change, and nothing uncommitted reaches a shared entry. Every other flow is right to read what is committed.
7. **What assumed the write was durable waits for the commit.** Invalidation and after-events register with `AfterCommit`: every
   invalidation first, so an after-event reads compiled state that has followed the whole hold; then each after-event alone, so one
   failing costs no other. A failure is logged and no longer reaches the caller as `RolePostEventException`, because the commit stands
   and `Commit` reports the commit. An after-event's token is its own, since the change's may long since have been cancelled.
8. **`AfterCommit` work runs outside the scope, on its route** (5), for every consumer, so what follows a commit can write. It runs after
   the gate has opened, so a handler's write cannot be waited on by a change behind it.
9. **`RoleEventRegistry.DemandMutationAllowed` is unchanged.** A before-event's write joins the hold, and a before-event's or an
   after-event's role change refuses as it always has.

## Compatibility

- A role change that refused inside a hold now joins it. Nothing else changes for code outside a hold, apart from the gate's cost on
  the way in: one semaphore turn and one counter.
- `ITransactionCoordinator` gains `WhenHoldEnds`; `RoleOptions` gains `HeldChangeWait`. No stored format changes.
- A change that throws part way, cancelled in the middle of a shift or a deletion, leaves what it wrote in the hold, since no role change
  has a savepoint of its own. Let the exception end the scope. Catching it and committing commits the part.
- A deletion is one outcome inside a hold, where outside it stops part way and a retry finishes it. The hold's limit bounds how many
  members and overwrites one hold can delete.
- A failed commit reporting `Unknown` invalidates nothing, as the Entity cache does not retire. On SQLite a commit that fails has rolled back.
- The role tables' first use belongs outside a hold, as DATA-0166 says of any table: until then another flow's bag can fail on a table
  the hold has created and not committed.
- A change outside a scope that keeps failing to reach the store, behind holds that retry at once, keeps refusing the holds' first
  changes until it reaches the store or meets the busy timeout. Holds that do not change roles are not affected. This was measured:
  six plain changes among six holds retrying after 200 to 600 milliseconds each failed at the busy timeout in turn, about 33 seconds
  apart, and no hold's change got through in 170 seconds. A hold that backs off for longer than the driver's 150 millisecond poll and
  leaves a gap goes through. The way to remove it is for a change outside a scope to take the same queue holds take on its route, which
  this record does not do.
- Across processes nothing changes for a change outside a scope, which decides before it writes. A hold's change decides inside the
  store's write lock, so it is right across processes where the other is not (SEC-0031).

## Evidence

- `RoleHoldSpec` (Koan.Identity.Sqlite.Tests): a rollback and a disposal leave no row, no moved position and the same bag instance, after
  every kind of change in one hold, and the next role takes the place left; a commit is what other flows see, while a bag read inside sees
  its own change and the overwrite it wrote; a change after the hold ended refuses; a commit the store refuses (`Unknown`, from a deferred
  foreign key) and one cancelled before it began (`NotCommitted`) leave nothing and raise nothing; an after-event waits for the commit, runs
  with no ambient transaction and an uncancelled token, sees compiled state that has followed the whole hold, can write, and is refused a
  role change, and one failing leaves the commit standing; a before-event's write rolls back with the hold, its role change refuses, and a
  veto costs only its change; a hold past its limit refuses the rest of its scope and frees another flow's change while the scope is still
  open; a change under way that needs the store costs the hold's change its short wait and then lands, and one that does not only delays it;
  another flow's change decides only after the hold has ended, committed or rolled back; the flows of one hold take turns, eight parallel
  holds never refuse each other, and a plain change beside a hold keeps the order dense over ten rounds.
- `SqliteHeldTransactionSpec` (Connector.Sqlite): `WhenHoldEnds` runs once on a commit, a disposal and the limit, at once after the end, and
  refuses on a deferred scope; `AfterCommit` work can write.
- `RoleSqliteSpec`: a deferred scope still refuses before any write, and the bag stands.
- Condition 1 was measured by removing it: with the gate released as the hold's first change returned, another flow's role took
  position 2 after the hold had committed its own role there, and the order stopped being dense.
