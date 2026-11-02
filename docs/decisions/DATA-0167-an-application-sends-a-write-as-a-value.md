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
2. **Sending is the command's own verb, and a command runs in its sender's context.** `command.Send(ct)` answers with the outcome.
   `command.TrySend()` never waits: it answers null, and the command never runs, when the queue is full or has ended. Both resolve the
   queue ambiently (ARCH-0135). Each captures what a deferred scope captures for an operation (`CapturedCarriers`, NK-047), so the command's
   partition, source and tenant are its sender's, and it never runs somewhere its sender is not. Its hold takes the route that context
   names, by the rule every hold has, so a source that cannot hold refuses with `NotDispatched`, and one that can is served.
3. **One queue, as a posture.** Holds already serialize writers, in other processes too, so correctness does not rest on the queue, and a
   command run beside another process's writer stays correct. What the queue gives is the rest: a bound of 1024 that makes a sender wait for a
   place, arrival order, announcements in commit order, and a stop at a command boundary that answers everything still queued. It is not a
   router. A write the command routes elsewhere refuses when it is issued (DATA-0166 rule 5), and the command fails.
4. **Six outcomes, once.** `Done` with its result, `Absent`, `Gated` with why, `Invalid` naming the field, `Conflict`, and `Failed`
   with its cause and what landed. A surface maps them without knowing the command. `Failed.Landed` is the framework's own
   `DataCommitOutcome`, so a failure reads the same here as on `TransactionException`:
   - `NotDispatched`: the command never ran. It was stopped in the queue, its sender gave up before it reached the queue, or its hold could
     not begin, behind another writer or on a route that cannot hold.
   - `NotCommitted`: it ran, and nothing landed.
   - `Unknown`: the native commit itself failed, or work following it outlasted the limit (rule 9).

   Only the queue can say `Failed`: its constructor is internal, because a command cannot truthfully report what its commit will do. A
   command that fails throws.
5. **A refusal can be thrown.** An entity method that refuses has no `Outcome<TResult>` to return, because it does not know the
   command's result type, so `Refusal.Absent()`, `Refusal.Gated(why)`, `Refusal.Invalid(field, reason)` and `Refusal.Conflict(reason)`
   become those outcomes, and any other throw is `Failed`.
6. **A cancelled wait is not an outcome.** The token passed to `Send` bounds the sender and never the command. Once a command is queued,
   cancelling throws `OperationCanceledException`, and the command still runs. Cancelled while waiting for a place in a full queue, or
   already cancelled, the command never reaches the queue, and the sender gets `Failed` with `NotDispatched`. So an exception always
   means it still runs, and an outcome always says what happened.
7. **A sequence is a read of the store inside the hold.** `Sequence.Of<Event>(e => e.Seq)` names a numbered column, declared once, and
   refuses one without `[Index(Unique = true)]`. `await Event.Log.Next(ct)` reads the highest stored value at its first use in a hold and counts
   on from there in that hold. There is nothing to load at start, nothing to reload after an unknown commit, and no registration, and a second
   process needs nothing: holds take turns, and each one reads what the last committed. A rollback removes the rows, and so the numbers, so a
   committed sequence has no gap. It belongs to the hold, not the queue, so a plain `EntityContext.Hold` can use it, and outside a hold it
   refuses, because a number read outside the transaction it is written in is what causes a gap or a duplicate.
   - **The read is one row.** A page takes the identity as a tiebreaker, for a total order across pages, and past the column's own key that kept
     SQLite from answering out of the index: the top row took 10 to 14 ms over 20,000 rows. `Data<T,K>.Top`, internal, reads it with no tiebreaker,
     in 0.09 ms. Every other paged read keeps its total order.
   - **No marks in memory.** A numbered command costs about what an unnumbered one does (6.64 ms against 7.62 ms in the last run, within noise),
     because the commit is what costs, and marks would add a second copy of the truth to keep right.
   - **A scope that hides rows refuses.** A soft-deleted top row is hidden from the read and still holds its number in the unique index, so the
     next number would collide (measured: `UniqueValueConflictException`). A sequence over an entity a non-equality read scope covers refuses, and
     says so. A tenant is an equality scope with a unique index of its own, so it numbers within the tenant.
8. **The announcement is an `AfterCommit`.** `CommandQueue.Committed` tells subscribers in this process which command committed and the
   last number it took from each sequence. It registers with the coordinator's `AfterCommit` when the command has returned `Done`, so it
   runs after the commit, after the cache and role invalidations that registered earlier, and before the sender is answered. The queue awaits each
   commit, so announcements are in commit order. Each subscriber is called on its own, so one that throws is logged and costs no other. A command
   that wrote nothing is not announced: the coordinator records whether the hold admitted a write (`AdmitToHold`), since `TrackedOperationCount`
   stays zero in a hold.
9. **The queue never waits on itself, and never waits past the limit.** Three rules keep a sender answered:
   - **The loop's flow cannot send.** A `Send` or a `TrySend` from a command, or from work following its commit, refuses, since awaiting it would wait
     for the queue that is running it. A `Committed` subscriber's own task does not inherit that flow, so it can send.
   - **A command is waited for as long as its hold can last.** Its token ends at `HoldLimit`, and the queue stops waiting then, even for one blocking its
     thread. The hold has rolled back and refuses whatever the command still tries to write, so a command that never returns costs its own answer,
     `Failed` with `NotCommitted`, and the queue moves on.
   - **Work following a commit gets the same bound.** The commit stands, and the sender is told `Unknown`, because the queue no longer knows how that
     work ended.
10. **The queue ends a command's hold.** While `Run` runs, `Commit` and `Rollback` refuse, through `EntityContext` and the coordinator alike, so an
    outcome cannot disagree with what landed. A send from inside any other open scope refuses too, since that scope keeps the command's turn from it.
11. **The queue answers at any point in a host's life.** It is a standard hosted service whose loop starts with the first send, so it depends on no
    orchestrator: a startup service seeding with no token, and a host with background services turned off, are both answered. It stops with the host.
12. **A route with no holds is refused at start, when the application has commands.** `ICommand` is discoverable, so `KoanDataCoreModule.Start` can ask
    whether the application declares any, and if so, whether the default route can hold, with nothing held. If it cannot, the queue ends with the same
    `NotSupportedException` a hold gives, naming the route, the capability and the correction. It logs it, reports it through its health, and answers every
    send with it as `Failed`. It does not refuse the boot, so a host that only reads goes on. An application with no commands is left alone on any route.
13. **A queue that has ended says so.** Its health contribution reports unhealthy with the cause, and is critical. After rule 9 only a stop or
    rule 12 can end it.

## Coalescence

- **Closest pattern:** `IHostedService` for lifecycle, `RoleCollection.Change` for one step that owns a hold, `CapturedCarriers` for a context
  that outlives its flow, and `IHandleEntityEvent` for a discoverable marker. A consumer's writer is what this lifts.
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

- Additive. Every host gains one hosted service that does nothing until a command is sent.
- `TransactionCoordinator`, internally, records whether a hold admitted a write, tallies the hold's sequences and refuses an end from inside a
  command, and its route check is callable without beginning a hold. `ITransactionCoordinator` is unchanged. `Data<T,K>` gains an internal `Top`.
- A command owns the hold while it runs: every other writer of the store waits, so it does no network or other slow work. Each command takes a
  connection, as a role change does (SEC-0036).
- A command that outlasts `HoldLimit` runs on unobserved until it returns, with nowhere to write. It holds nothing of the store.
- DATA-0166's first-use note applies to a command: a table first used inside a hold can make another flow wait, so an entity a command writes
  belongs at boot on a route that holds, as `Koan.Identity` does for its own.
- The queue never retries: a commit reporting `Unknown` is reported, and not run again.

## Evidence

- `SqliteCommandQueueSpec` (Connector.Sqlite), each on its own file through a real `AddKoan()` host:
  - **Two real processes:** the test assembly started again, sending 25 numbering commands while this process sends until the other has finished,
    with one command in three here refused after taking a number. The numbers run 1 to the total with no gap, and the processes handed over to each
    other 14, 18 and 20 times in three runs, so they did interleave.
  - **The run:** many senders run once each, in arrival order, on one unbroken sequence, announced in commit order. Two entity types commit whole and
    nothing shows outside until they do. A throw, each outcome returned and each refusal thrown leave nothing and give their numbers back. The same id
    twice answers with what is there, unannounced, and another body is a conflict. A command reads its own writes, and two sequences in one command
    count apart, are announced together and go back together.
  - **The queue:** past the bound a try is refused and a sender waits; a wait for a place cancelled never runs; a cancelled wait once queued throws and
    the command still runs; a stop ends the running command, answers what is queued and every later send, and health then reports it.
  - **Rules 8 to 11:** a subscriber that throws first costs the next nothing, and a subscriber's own task can send. A send awaited from work following a
    commit, and a try awaited from a command, refuse, and the queue goes on. A command still running at the limit, by await or by blocking its thread, is
    answered `NotCommitted`, what it wrote is gone, a late write is refused, and the next command runs with its number. Work after a commit that never ends
    is answered `Unknown` with the row present. `Commit` and `Rollback` from a command refuse by both paths and leave nothing. A command lands in its
    sender's partition with its carried value, and the next does not inherit them. A startup service that sends with no token, and a send with background
    services off, are answered.
  - **The commit:** a deferred foreign key the store refuses at `COMMIT` reports `Unknown` and announces nothing; a hold behind another connection's lock
    reports `NotDispatched`; in both the queue goes on and the next number is right with nothing reloaded. A refused first command on a fresh file leaves
    the next a table to write to.
- `CommandRouteSpec` (Koan.Tests.Data.Core): on JSON and InMemory health reports the refusal before any send, and a send is told `NotDispatched` with the
  correction. A source its sender names on JSON is refused the same way, and the queue goes on. It sits here because the SQLite specs' assembly has no other
  provider to route to.
- `CommandQueueWithoutCommandsSpec` (Connector.InMemory): an assembly that declares no command stays healthy on a route shown to refuse a hold.
- `CommandTenantSpec` (Koan.Tenancy.Tests): a tenant's command writes that tenant's rows, invisible to another, and numbers within the tenant.
- `SequenceReadScopeSpec` (Koan.Data.Axes.Integration.Tests): a sequence over an entity with a hiding read scope refuses, past a hidden top row.
- `RoleHoldSpec` (Koan.Identity.Sqlite.Tests): a command's role change and entity write roll back together and leave the same compiled bag, and commit
  together. It sits there because referencing `Koan.Identity` from the SQLite specs would change every host they boot.
- **Measured by removing them:**
  - without the discovery gate, `CommandQueueWithoutCommandsSpec` finds the queue unhealthy;
  - without the read-scope refusal, the command fails on `UniqueValueConflictException`;
  - with `FirstPage` in place of `Top`, 100 reads took 1.05 s against a bound of a quarter of 100 commands, 0.22 s.
