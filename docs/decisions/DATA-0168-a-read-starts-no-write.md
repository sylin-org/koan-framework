---
id: DATA-0168
slug: a-read-starts-no-write
domain: Data
status: Accepted
date: 2026-11-03
title: A read starts no write, and a command is sent by whoever asks for it
amends:
  - DATA-0167
  - ARCH-0142
related:
  - DATA-0166
  - BUILD-0073
amended_by:
  - DATA-0169
---

# DATA-0168: A read starts no write, and a command is sent by whoever asks for it

> **Amended by [DATA-0169](DATA-0169-a-sequence-has-a-committed-head-and-a-hold-runs-by-one-clock.md) (2026-11-04): the high-water mark moves into Koan.** Item 4's consumer need no longer keep
> its own: each held commit raises `Sequence.Head` by a compare-and-swap maximum, and a reader waits on it with `Sequence.WaitPast` for a bound it gives.
> Item 2 stands: nothing tells code of each commit, and the wait has nothing to register.

## Context

DATA-0167 gave a command two verbs beside `Send`, and ARCH-0142 a third. Their one consumer, Tangent Space, used them as follows. No Koan module or sample
calls or implements any of them.

- **`TrySend` and `TriesWaiting` (DATA-0167 item 7, ARCH-0142 item 4).** Every call started a write from a read: the first GET of the door, a GET of the
  notifications, and any request arriving on a new host name, a health check included. The bound of 1024 was there only because reads are a trigger with no bound.
  It covered the whole process, so unrelated stores shared it, and each drop was patched elsewhere: by a later opening, by a retry on every commit, and by tests
  polling `TriesWaiting` until it read zero. A GET that writes also makes the hint Koan.Mcp derives from a GET route, read-only and idempotent, untrue.
- **`ICommandWatcher` (DATA-0167 item 9).** It was `AfterCommit` registered for every command, and moved inside the store's turn so that announcements came in
  commit order. That placement needed four guards: the sender's `ExecutionContext` captured so that a watcher's send did not inherit the hold, a throw caught per
  watcher, a watcher that could not be resolved caught as well, and a contract that a watcher returns at once because the turn waits for it. The consumer used it to
  carry a log's high-water mark and to keep gates in memory by command type. A `Sequence` has no gap in what commits, so the high-water mark is enough and the order
  of the announcements is not needed.

**The business sentence:** whoever asks for a write sends it, and is told what happened.

## Decision

1. **A write is sent by whoever asks for it, with `Send`, in their own flow. A read starts none.** `TrySend`, `TriesWaiting`, the bound and `Run`'s `began`
   callback are gone, and nothing replaces them. `_ = command.Send()` is not a replacement either. While the turn is free, `Send` runs the whole command on the
   caller's thread: the hold begins synchronously, the SQLite driver's async methods complete synchronously, and its busy retry can put the thread to sleep. A send
   that nobody awaits still runs in the flow that started it.
2. **`ICommandWatcher` is gone, with everything that existed only to run it inside the turn:** `CommandExtensions.Announce`, the sender's captured context,
   `TransactionCoordinator.BeforeItsTurnEnds` and its catch, `Wrote` and `Sequences`. The turn passes on once the native commit lands.
3. **Work that must follow a command's commit registers it on the scope.** `EntityContext.UncommittedScope.AfterCommit`, from inside the command, runs only if
   it commits. It runs after the turn has passed on and before `Send` answers, so it can send and await the answer. A fact that other code acts on is an Entity
   event, which waits for the commit (ARCH-0142 item 5).
4. **Koan no longer promises announcements in commit order.** A consumer that needs order reads it from the store. A sequence's committed numbers have no gap,
   so a high-water mark raised by a compare-and-swap maximum after each commit is correct, whatever order the commits are reported in.
5. **`Sequence.Next` is unchanged.** A hold still tallies the numbers it takes (`Take`), and a rollback still gives them back.

## Coalescence

- **Owner:** `Koan.Data.Core`, which owned all three verbs.
- **Closest pattern:** `AfterCommit`, which Cache, Communication and Identity already use for work that must wait for a commit.
- **Deleted:** `CommandExtensions.TrySend`, `TryBound`, `_trying`, `TriesWaiting`, `Announce` and `Run`'s `began`; `ICommandWatcher`;
  `TransactionCoordinator.BeforeItsTurnEnds`, `Wrote` and `Sequences`; the six specs that covered only them.

## Ergonomics

`new Reply(...).` lists `Send` and nothing else. There is one way to write, and one way to follow a commit. Public concepts removed: `CommandExtensions.TrySend`,
`CommandExtensions.TriesWaiting` and `ICommandWatcher`.

## Compatibility

- This is a breaking removal of public surface, which the 1.x train allows while the framework is unannounced (BUILD-0073).
- A caller of `TrySend` sends the command where the write is asked for: a POST, another command, or work after a commit. A test that polled `TriesWaiting`
  awaits the sends it started.
- An `ICommandWatcher` becomes an `AfterCommit` registered by the command or by the code it calls, or an Entity event. Neither runs inside the store's turn, so
  neither reports in commit order.
- `Outcome<T>`, `Reason`, `Refusal`, `Sequence` and `ITransactionCoordinator` are unchanged.

## Evidence

- `SqliteCommandSpec` (Connector.Sqlite, 27 specs). These specs are deleted: four on watchers (a throw, an unresolvable watcher, a task it starts, an async
  method it calls), a try's slot, and a try awaited inside a command. These specs keep only what they assert about the store: commands from several senders, a
  burst of 500, every refusal, the same id twice, a commit the store refuses, two sequences in one command, and a send that waits for its turn while a cancelled
  wait never runs. The numbers in each still run from 1 with no gap.
- Koan's `src` and samples build with the three verbs removed. Nothing there used them.
