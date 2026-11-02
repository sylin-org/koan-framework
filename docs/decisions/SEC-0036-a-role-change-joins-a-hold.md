---
id: SEC-0036
slug: a-role-change-joins-a-hold
domain: Security
status: Accepted
date: 2026-11-01
title: A role change is a hold, on a route that offers one
related:
  - SEC-0021
  - SEC-0031
  - DATA-0166
---

# SEC-0036: A role change is a hold, on a route that offers one

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
2. **A hold takes the store first; a change outside a scope takes it last.** A change holding a lock and blocked on the store
   cannot finish until the hold ends, while the hold's owner waits for that lock.
3. **A compiled bag is shared.** A read inside the hold that found a shared entry would miss its own change, and one that compiled
   into a shared entry would publish uncommitted rows.
4. **An after-event at the commit runs in the flow that committed**, whose ambient transaction has ended. A write there refused,
   since a write in a completed scope has nowhere to go.
5. **A table first used inside a hold is a wait with no way out.** Provisioning is single-flight per table
   (`DataSourceReadinessCoordinator`). A read outside the hold that gets there first provisions on its own connection and blocks on
   the hold's store, and the hold's next use of that table waits for that provisioning. It ended at the hold's limit, measured at
   19.76 seconds. The other way round, the hold's table is remembered as ready and the read fails on a table not yet committed.

## Decision

1. **A change is one outcome, wherever the route allows it.** One private step, `RoleCollection.Change`, runs every `Add`, `Remove`,
   `Define`, `Update`, `Rename`, `SetPermissions`, `Move`, `Delete`, `Overwrite`, `RemoveOverwrite` and revoke:
   - inside an open hold it joins, and commits or rolls back with everything else there;
   - outside any scope, on a route that declares `write.heldTransaction`, it is a short hold of its own;
   - on a route that does not, it runs as it did, committing as it goes, with a failing after-event reaching the caller as
     `RolePostEventException`;
   - inside a deferred scope, or a hold that has ended, it refuses.
2. **The store's queue is the only gate.** Every change on the route is a hold, holds queue in arrival order (DATA-0166 rule 6), and
   so no change decides beside a hold (1) and none holds a lock while it waits for the store (2). A hold keeps other role changes out
   until it ends however it ends, because its end frees the queue. The locks in `RoleCollection` are unchanged: they order the flows
   of one hold, and changes on a route with no holds. There is nothing else to wait for and nothing to refuse, so there is no new option
   and no new exception.
3. **A rollback, a failed commit or the limit leaves nothing.** No row, no moved position, no changed bag and no after-event. The dense
   order needs no repair: its shifts are rows, and they roll back with the rest. A deletion that stops part way leaves the role as it was.
4. **Inside a hold a bag is compiled past the shared maps** (`RoleBagCache`), as the Entity cache reads past itself (DATA-0166 rule 9). A
   read sees its own change, and nothing uncommitted reaches a shared entry. Every other flow is right to read what is committed.
5. **What assumed the write was durable waits for the commit.** Invalidation and after-events register with `AfterCommit`: every
   invalidation first, so an after-event reads compiled state that has followed the whole hold; then each after-event alone, so one
   failing costs no other. A failure is logged, because the commit stands and `Commit` reports the commit. An after-event's token is its
   own, since the change's may long since have been cancelled.
6. **`AfterCommit` work runs outside the scope, on its route** (4), for every consumer, so what follows a commit can write. The store is
   free by then, so a handler's write lands.
7. **`RoleEventRegistry.DemandMutationAllowed` is unchanged.** A before-event's write joins the hold, and a before-event's or an
   after-event's role change refuses as it always has.
8. **`Koan.Identity` makes its tables exist at boot** (5), on a route that declares `write.heldTransaction`: `Role`, `RoleMember`,
   `PermissionOverwrite`, and `AuditEvent`, which a role change writes through its audit hook. No application has to know. A store that
   refuses at boot, such as a source that may not provision, logs a warning and answers at first use as it would have. A table
   elsewhere, or in a partition, is DATA-0166's as before.

## Compatibility

- On a route that offers holds a change outside a scope gains what a hold costs and what it gives:
  - It decides and writes in one transaction, so the roles-per-person bound and the order now hold across processes as well.
  - It waits for the holds ahead of it, for at most the store's busy timeout, and then throws `TransactionException` with `NotCommitted`
    where it used to throw the driver's busy error. A cancelled wait ends at once.
  - Its before-events run inside it. So a handler that is slow keeps other writers out, and one that writes to another route, or a Direct or
    vector write, or that opens a hold of its own, refuses as anything inside a hold does. Holds are not nested.
  - A failing after-event is logged and no longer reaches the caller as `RolePostEventException`.
  - It takes its own connection. On a SQLite file, 5000 member additions in a row took 30.4 seconds, about 6 milliseconds each.
  - A deletion is bounded by the hold's limit, where it used to stop part way for a retry to finish. 5000 members took 1.5 seconds on a
    file, so the default 20 seconds covers tens of thousands, and `HoldLimit` is the correction past that.
- `RoleOptions` is unchanged, and no stored format changes.
- A change inside a caller's hold that throws part way, and that the caller catches and commits past, commits the part, since no role change
  has a savepoint of its own. Let the exception end the scope. A change that is its own hold has no such case.
- A failed commit reporting `Unknown` invalidates nothing, as the Entity cache does not retire. On SQLite a commit that fails has rolled back.
- With `Koan:Identity:HashChainAudit` on, a hold that rolls back after a role change leaves `AuditChain`'s in-process head past the row that
  rolled back, so a later verification reports a false gap, and its lock can be held by a flow waiting for the hold's store. This was read in
  the code and not reproduced, and it is open.

## Evidence

- `RoleHoldSpec` (Koan.Identity.Sqlite.Tests):
  - a rollback and a disposal leave no row, no moved position and the same bag instance, after every kind of change in one hold, and the next
    role takes the place left;
  - a commit is what other flows see, while a read inside sees its own change and both overwrites it wrote, past entries compiled before the hold;
  - a change after the hold ended refuses;
  - a commit the store refuses (`Unknown`, from a deferred foreign key) and one cancelled before it began (`NotCommitted`) leave nothing and raise
    nothing;
  - an after-event waits for the commit, runs with no ambient transaction and an uncancelled token, sees the invalidation of a later change in the
    same hold, can write, and is refused a role change, and one failing leaves the commit standing;
  - a before-event's write rolls back with the hold, its role change refuses, and a veto costs only its change;
  - a hold past its limit refuses the rest of its scope and frees another flow's change while the scope is still open;
  - a plain change beside a queue of commands lands, and so does every command; a plain change under way keeps a hold behind it;
  - a plain deletion that stops part way leaves the role whole, and a plain change cancelled in the queue ends at once and leaves nothing;
  - another flow's change decides only after the hold has ended, committed or rolled back;
  - the flows of one hold take turns, and twenty-four holds and plain changes together keep the order dense;
  - on a fresh store the four tables exist at boot, and a first bag and a first change do not meet, in either order.
- `RoleCollectionSpec` (Koan.Identity.Tests): on a route with no holds a change has no scope around it, and a failing after-event reaches the caller.
- `SqliteHeldTransactionSpec` (Connector.Sqlite): `AfterCommit` work runs outside the scope and can write.
- `RoleSqliteSpec`: a deferred scope still refuses before any write, and the bag stands.
- Each condition was measured by removing it:
  - with a plain change not a hold, another flow's role took position 2 after the hold had committed its own there, and a deletion that stopped left the
    role's permissions cleared;
  - with each invalidation registered in call order, an after-event read a stale bag of a later change in the hold;
  - with the shared map answering inside a hold, a read missed the role overwrite it wrote;
  - with the token not passed to the queue, a cancelled change waited for the hold ahead of it;
  - with no boot step, the tables were absent, and the hold's `Define` was found waiting in `DataSourceReadinessCoordinator.Run`, reached from
    `SqliteRepository.Ready`, behind the read that was provisioning.
