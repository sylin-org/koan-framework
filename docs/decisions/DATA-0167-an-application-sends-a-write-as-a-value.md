---
id: DATA-0167
slug: an-application-sends-a-write-as-a-value
domain: Data
status: Accepted
date: 2026-11-01
title: An application sends a write as a value, and one queue runs each in a hold of its own
related:
  - DATA-0158
  - DATA-0166
  - SEC-0036
  - ARCH-0135
---

# DATA-0167: An application sends a write as a value, and one queue runs each in a hold of its own

## Context

A write that decides from what it reads needs three things at once: to read inside the transaction it writes in, to leave
nothing when it refuses or throws, and an answer that says what happened. [DATA-0166](DATA-0166-a-scope-can-hold-a-native-transaction-from-its-start.md)
gives the transaction and [SEC-0036](SEC-0036-a-role-change-joins-a-hold.md) lets a role change join it, but each consumer still
writes the same three hundred lines around them: a queue, a loop, an answer for every path out, a commit announcement, sequence
numbers, and a shutdown that answers what is queued. A consumer's writer written before DATA-0166 was also forced into shapes a hold
makes unnecessary: a step before the transaction for writes that could not join it, a high-water mark loaded at start and reloaded
after an unknown commit, and a queue that correctness rested on.

**The business sentence:** declare a write as a value, send it, and be told what happened.

```csharp
public sealed record Reply(string TopicId, string Body) : ICommand<Post>
{
    public async Task<Outcome<Post>> Run(CancellationToken ct)
    {
        if (await Post.Get(TopicId, ct) is not { } topic) return new Outcome<Post>.Absent();
        if (topic.Locked) return new Outcome<Post>.Gated("locked");
        var post = await new Post { ParentId = topic.Id, Body = Body }.Save(ct);
        await new Event { Seq = await Event.Log.Next(ct), PostId = post.Id }.Save(ct);
        return new Outcome<Post>.Done(post);
    }
}

var outcome = await new Reply(topicId, "hi").Send(ct);
```

The complete surface of that expression is a reference to `Koan.Data.Core` and a route that declares `write.heldTransaction`. There is
no registration, option or decoration.

## Decision

1. **A command is a value that runs in a hold of its own.** `ICommand<TResult>` has one method, `Run`. The queue opens
   `EntityContext.Hold` for it, runs it, and commits only `Done`: every other outcome, and any throw, rolls back. Reads, saves and
   role changes inside it are the ordinary Entity verbs, joined by the ambient hold, so it sees its own writes. It carries the ids it
   creates; replaying one is its own business, and nothing here remembers a command.
2. **Sending is the command's own verb.** `command.Send(ct)` answers with the outcome. `command.TrySend()` never waits: it answers
   null, and the command never runs, when the queue is full or has ended. Both resolve the queue ambiently (ARCH-0135).
3. **One queue, for the default route, as a posture.** Holds already serialize writers, in other processes too, so correctness does not
   rest on the queue, and a command run beside another process's writer stays correct. What the queue gives is the rest: a bound of
   `CommandQueue.QueueBound` that makes a sender wait for a place, arrival order, announcements in commit order, and a stop at a
   command boundary that answers everything still queued. It is not a router. A write the command routes elsewhere refuses when it is
   issued (DATA-0166 rule 5), and the command fails.
4. **Six outcomes, once.** `Done` with its result, `Absent`, `Gated` with why, `Invalid` naming the field, `Conflict`, and `Failed`
   with its cause and what landed. A surface maps them without knowing the command. `Failed.Landed` is the framework's own
   `DataCommitOutcome`, so a failure reads the same here as on `TransactionException`:
   - `NotDispatched`: the command never ran. It was stopped in the queue, its sender gave up before it reached the queue, or its
     route cannot hold.
   - `NotCommitted`: it ran, or its hold could not begin behind another writer, and nothing landed.
   - `Unknown`: the native commit itself failed.
5. **A refusal can be thrown.** An entity method that refuses has no `Outcome<TResult>` to return, because it does not know the
   command's result type, so `Refusal.Absent()`, `Refusal.Gated(why)`, `Refusal.Invalid(field, reason)` and `Refusal.Conflict(reason)`
   become those outcomes, and any other throw is `Failed`.
6. **A cancelled wait is not an outcome.** The token passed to `Send` bounds the sender and never the command. Once a command is queued,
   cancelling throws `OperationCanceledException`, and the command still runs. Cancelled while waiting for a place in a full queue, or
   already cancelled, the command never reaches the queue, and the sender gets `Failed` with `NotDispatched`. So an exception always
   means it still runs, and an outcome always says what happened.
7. **A sequence is a read of the store inside the hold.** `Sequence.Of<Event>(e => e.Seq)` names a numbered column, declared once, and
   refuses one without `[Index(Unique = true)]`. `await Event.Log.Next(ct)` reads the highest stored value at its first use in a hold, one
   row through `FirstPage`, and counts on from there in that hold. There is nothing to load at start, nothing to reload after an unknown
   commit, and no registration, and a second process needs nothing: holds take turns, and each one reads what the last committed. A
   rollback removes the rows, and so the numbers, so a committed sequence has no gap. It belongs to the hold, not the queue, so a plain
   `EntityContext.Hold` can use it, and outside a hold it refuses, because a number read outside the transaction it is written in is what
   causes a gap or a duplicate. Marks kept in memory were measured and not kept: a numbered command costs 8.01 ms against 7.77 ms for an
   unnumbered one over 20,000 rows, because the commit is what costs, and they would add a second copy of the truth to keep right.
8. **The announcement is an `AfterCommit`.** `CommandQueue.Committed` tells subscribers in this process which command committed and the
   last number it took from each sequence. It registers with the coordinator's `AfterCommit` when the command has returned `Done`, so it
   runs after the commit, after the cache and role invalidations that registered earlier, and before the sender is answered, and a
   subscriber's throw is logged by the coordinator and changes nothing. The queue awaits each commit, so announcements are in commit order.
   A command that wrote nothing is not announced: the coordinator records whether the hold admitted a write (`AdmitToHold`), since
   `TrackedOperationCount` stays zero in a hold.
9. **A route with no holds is refused at start, when the application has commands.** `ICommand` is discoverable, so the queue knows at
   start whether the application declares any, and if so, whether the route can hold. If it cannot, the queue ends with the same
   `NotSupportedException` a hold gives, naming the route, the capability and the correction. It logs it, reports it through `Check`, and
   answers every send with it as `Failed`. It does not refuse the boot, so a host that only reads goes on. An application with no commands is
   left alone on any route.
10. **A sender inside a transaction refuses.** A command sent from inside an open scope, a command's own included, would wait for a turn
    that scope keeps from it, so `Send` throws `InvalidOperationException` before queueing. `TrySend` does not wait, and stays allowed.
11. **A page ordered by a key that cannot repeat takes no tiebreaker.** `FilterPushdownCoordinator` appends the identity to every paged
    sort so that a page is a window onto a total order, and a key under a unique index, of a non-nullable value type, is a total order
    already. The tiebreaker past it kept SQLite from answering out of that index: the top row of a numbered column took 13 ms over 20,000
    rows, where the same read out of the index takes 0.07 ms. This holds for every paged read, not just a sequence's.
12. **A queue that has ended says so.** `Check` reports unhealthy with the cause, and `IsCritical` is true. Only the queue's check changes:
    `KoanBackgroundServiceBase.Check` still reports healthy for every other service, and the orchestrator already reports a faulted one
    (`KoanBackgroundServiceOrchestrator.Check`), so widening the base would change the others and still miss a queue ended by a stop.

## Coalescence

- **Closest pattern:** `KoanBackgroundServiceBase` for lifecycle, `RoleCollection.Change` for one step that owns a hold, and
  `IHandleEntityEvent` for a discoverable marker. A consumer's writer is what this lifts.
- **Owner:** `Koan.Data.Core`, because the run depends only on `EntityContext.Hold` and `Koan.Core`. Web and MCP surfaces project commands
  and are a later decision; `Koan.Identity` joins through SEC-0036 with no reference either way.
- **Kept:** the queue, its bound, the answer on every path, the stop. **Absorbed:** outcomes, sequences and the refusal.
  **Deleted against a consumer's writer:** the step before the transaction, loaded and reloaded marks and their registration, the
  announcement's own failure handling, the per-command state passed to `Run`, and the classification of what a failed commit left,
  which the coordinator reports.
- **Word:** "command" is the word a coding model and a reader bring to a write sent as a value, and is free as a type: `ServiceCommand` and
  its subtypes control a background service through `HandleCommand`, in another namespace and pillar. `IWrite<TResult>` was the alternative;
  it avoids that overlap, but reads as a verb and says less about being sent.

## Ergonomics

One interface and one verb are the common path, and `Sequence` and `Refusal` are there only for what uses them. `new Reply(...).` lists
`Send` and `TrySend` in IntelliSense. A cognitive branch remains: an application reads `Outcome<T>` by pattern, which is six cases once
per surface.

## Compatibility

- Additive. Every host gains one idle background service, `CommandQueue`, which holds nothing until a command is sent.
- `TransactionCoordinator`, internally, records whether a hold admitted a write and tallies the hold's sequences, and its route check is
  callable without beginning a hold. `ITransactionCoordinator` is unchanged.
- A paged read ordered by a key under a unique index no longer sends the identity to the store as a second key (rule 11). That rule relies on
  the index being enforced: a store that does not enforce one (JSON, InMemory) can hold a repeated value, and a page over it then has the ties it
  had before the identity was appended for them.
- One queue, for the default route. An ambient `EntityContext.Source` around `Send` does not reach the command, which runs on the queue's
  flow; a command that names a source inside `Run` and writes there refuses unless it names the same store.
- A command owns the hold while it runs: every other writer of the store waits, so it does no network or other slow work, and past
  `HoldLimit` it fails with `NotCommitted`. Each command takes a connection, as a role change does (SEC-0036).
- DATA-0166's first-use note applies to a command: a table first used inside a hold can make another flow wait, so an entity a command writes
  belongs at boot on a route that holds, as `Koan.Identity` does for its own.
- A host with background services turned off (`Koan:BackgroundServices:Enabled` false, or the queue named as disabled) has nothing to run a
  command, so `Send` waits for its token.
- Outcomes rest on the commit, not on a replay: a commit reporting `Unknown` is never retried by the queue.

## Evidence

- `SqliteCommandQueueSpec` (Connector.Sqlite, 17 specs), each on its own file through a real `AddKoan()` host:
  - **Two real processes:** the test assembly started again, sending 25 numbering commands while this process sends until the other has
    finished, with one command in three here refused after taking a number. The numbers run 1 to the total with no gap, and the two
    processes handed over to each other 14 and 18 times in two runs, so they did interleave.
  - **The run:** many senders run once each, in arrival order, on one unbroken sequence, announced in commit order. Two entity types commit
    whole and nothing shows outside until they do. A throw, each outcome returned and each refusal thrown leave nothing and give their numbers
    back. The same id twice answers with what is there, unannounced, and another body is a conflict. A command reads its own writes.
  - **The queue:** past the bound a try is refused and a sender waits; a wait for a place cancelled never runs; a cancelled wait once queued
    throws and the command still runs; a stop ends the running command, answers what is queued and every later send, and `Check` then reports
    unhealthy. A subscriber that throws changes nothing, and a commit is announced before its sender is answered. A send inside a scope refuses.
  - **The commit:** a deferred foreign key the store refuses at `COMMIT` reports `Unknown` and announces nothing; a hold behind another
    connection's lock reports `NotCommitted`; in both the queue goes on and the next number is right with nothing reloaded. A refused first
    command on a fresh file leaves the next a table to write to.
- `CommandRouteSpec` (Koan.Tests.Data.Core): on JSON and InMemory the queue ends at start, `Check` reports it, and a send is told
  `NotDispatched` with the correction. It sits here because the SQLite specs' assembly has no other provider to route to.
- `CommandQueueWithoutCommandsSpec` (Connector.InMemory): an assembly that declares no command, on a route that cannot hold, keeps a healthy queue.
- `PageOrderOwnershipSpec` (Koan.Tests.Data.Core): a non-nullable key under a unique index takes no tiebreaker, and a nullable or merely
  indexed key does.
- `RoleHoldSpec` (Koan.Identity.Sqlite.Tests): a command's role change and entity write roll back together and leave the same compiled bag,
  and commit together. It sits there because referencing `Koan.Identity` from the SQLite specs would change every host they boot.
- **Measured** by `A_number_read_from_the_store_costs_a_small_part_of_a_command`, which prints the read at 13 ms over 20,000 rows before rule 11
  and 0.073 ms after, and 8.01 ms against 7.77 ms per command after.
