---
id: DATA-0167
slug: an-application-sends-a-write-as-a-value
domain: Data
status: Accepted
date: 2026-11-01
title: An application sends a write as a value, and it runs in a hold of its own, in the sender's flow
related:
  - DATA-0158
  - DATA-0166
  - SEC-0036
  - ARCH-0135
amended_by:
  - ARCH-0142
  - DATA-0168
  - ARCH-0143
  - DATA-0169
---

# DATA-0167: An application sends a write as a value, and it runs in a hold of its own, in the sender's flow

> **Amended by [ARCH-0142](ARCH-0142-the-realignment-finished.md) (2026-11-02): an entity on a default route that holds has its table at start,** so the first-use notes under Compatibility apply only to a partition's table and an entity naming its own source.

> **Amended by [DATA-0168](DATA-0168-a-read-starts-no-write.md) (2026-11-03): `TrySend` and `ICommandWatcher` are gone.** Items 7 and 9, their lines under
> Ergonomics, Compatibility and Evidence, and the announcement in commit order no longer hold. A write is sent with `Send` by whoever asks for it, and work that
> follows a commit registers `AfterCommit`.

> **Amended by [ARCH-0143](ARCH-0143-one-refusal-shape.md) (2026-11-03): a plain string no longer converts to an invalid reason.** Item 3's implicit conversion no longer holds: an invalid
> reason is `Reason.Invalid(text)`, and equality is the record's.

> **Amended by [DATA-0169](DATA-0169-a-sequence-has-a-committed-head-and-a-hold-runs-by-one-clock.md) (2026-11-04): a sequence has a committed head, and every hold runs by one clock.** Item 8's
> "No marks in memory" still holds for numbering, which reads the store at its first use in each hold. Beside it Koan keeps a head per host that each held
> commit raises: nothing numbers from it, it lags what has committed and never leads it, and a wait refreshes it from the store.

## Context

A write that decides from what it reads needs three things at once: to read inside the transaction it writes in, to leave
nothing when it refuses or throws, and an answer that says what happened. [DATA-0166](DATA-0166-a-scope-can-hold-a-native-transaction-from-its-start.md)
gives the transaction and [SEC-0036](SEC-0036-a-role-change-joins-a-hold.md) lets a role change join it, but each consumer still
writes the same lines around them: an answer for every path out, a commit announcement and sequence numbers.

A consumer's writer written before DATA-0166 also needed a queue, because correctness rested on it. A hold ended that: the store's turn
serializes writers in a process, and the store's lock serializes them across processes. Lifted as it was, the queue was tried and measured.
Its costs were almost all the cost of moving a command onto another flow: losing the sender's context, a loop a command could stall or wait on,
and a send that could not run until something else started the loop. Those took more code to answer than the queue itself took.

**The business sentence:** declare a write as a value, send it, and be told what happened.

```csharp
public sealed record Reply(string TopicId, string Body) : ICommand<Post>
{
    public static readonly Reason NoTopic = Reason.Absent("No Topic has this Id.");
    public static readonly Reason Locked = Reason.Gated("locked");
    public const string NeedsBody = "A Post's body holds at least one visible character.";

    public async Task<Outcome<Post>> Run(CancellationToken ct)
    {
        if (await Post.Get(TopicId, ct) is not { } topic) return new Outcome<Post>.Refused(NoTopic);
        if (topic.Locked) return new Outcome<Post>.Refused(Locked);
        if (string.IsNullOrWhiteSpace(Body)) throw new Refusal(NeedsBody);
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

1. **A command is a value that runs in a hold of its own, in its sender's flow.** `ICommand<TResult>` has one method, `Run`. `Send` opens
   `EntityContext.Hold`, runs it, and commits only `Done`: every other outcome, and any throw, rolls back. Reads, saves and role changes inside it
   are the ordinary Entity verbs, joined by the ambient hold, so it sees its own writes. It carries the ids it creates; replaying one is its own
   business, and nothing here remembers a command. Its partition, source and tenant are its sender's by construction, and a command that never
   returns hangs its own sender and nobody else: `HoldLimit` ends its hold, and it finds nothing to write into.
2. **The store's turn is the line.** There is no queue. On SQLite, holds of one database wait on an asynchronous turn for at most the busy timeout,
   and only the head waits on the file (DATA-0166 rule 6).
   - **Fairness:** waiters are served in arrival order. A send reaches the turn synchronously, in call order, and a released turn passes to the longest
     waiter with no barging. That order is `SemaphoreSlim`'s behaviour, not its documented contract, so it is pinned by a spec. Role changes and
     plain holds stand in the same line.
   - **The bound:** a sender waits at most the busy timeout and is then told `NotDispatched`. A burst larger than the timeout's worth of commands sheds
     its tail rather than piling up: at about 105 commands a second and the default 30 seconds, 4000 at once answered 3184 `Done` and 816
     `NotDispatched`, with no gap in the numbers. A longer `BusyTimeout` is the correction.
3. **Three outcomes, and a refusal's kind lives on its reason.** `Done` with its result, `Refused` with a reason, and `Failed` with its cause and what landed. A `Reason`
   declares its kind once, where it is defined: `Reason.Absent(...)`, `Reason.Gated(...)`, `Reason.Conflict(...)`, and a plain string converts implicitly to an invalid
   reason. A constant has one answer, so the kind belongs to it and not to the many places that refuse with it, and a call site reads the same whichever it is. A surface
   maps a kind once. `Failed.Landed` is the framework's own `DataCommitOutcome`, so a failure reads the same here as on `TransactionException`:
   - `NotDispatched`: the command never ran, because its hold could not begin. The sender gave up, the wait ran out, the route cannot hold, or a scope
     is open around the send.
   - `NotCommitted`: it ran, and nothing landed.
   - `Unknown`: the native commit itself failed.

   Only `Send` can say `Failed`: its constructor is internal, because a command cannot truthfully report what its commit will do. A command that
   fails throws.
4. **A refusal can be thrown.** An entity method that refuses has no `Outcome<TResult>` to return, because it does not know the command's result type, so
   `throw new Refusal(reason)` becomes `Refused` with that reason, and any other throw is `Failed`.
5. **The sender's token means what a token means.** It ends the wait and the command, and the sender is told what happened, in the outcome, with no second
   channel: cancelled in the line, `NotDispatched`, and it never ran; cancelled while it runs, `NotCommitted`, and nothing landed. Nothing cuts a commit
   short: a command that reached it commits or fails on its own terms.
6. **A send that cannot run is told so.** An open scope around a send, a route that cannot hold, a source that cannot hold and a host that has gone all surface through
   the hold's own refusal, as `NotDispatched`. Nothing depends on a service having started, or on a token. Work following a commit runs after the turn has passed on,
   so it can send and await the answer.
7. **A try never waits.** `command.TrySend()` starts the command on a task of its own, in this context, and returns. It answers null, and the command never runs,
   when 1024 tries are waiting for their hold already, so no stream of them piles up behind a busy store. A try holds its slot only while it waits: until its hold begins, or
   until it is told it never ran. So a slot lasts at most the store's busy timeout, however long the command then runs, and a command that never returns does not use up the
   bound. The count is process-wide, so a store that is stuck can fill it for another, for at most that timeout. It is for work a later attempt makes good.
8. **A sequence is a read of the store inside the hold.** `Sequence.Of<Event>(e => e.Seq)` names a numbered column, declared once, and refuses one without
   `[Index(Unique = true)]`. `await Event.Log.Next(ct)` reads the highest stored value at its first use in a hold and counts on from there in that hold. There is
   nothing to load at start, nothing to reload after an unknown commit, and no registration, and a second process needs nothing: holds take turns, and each one
   reads what the last committed. A rollback removes the rows, and so the numbers, so a committed sequence has no gap. It belongs to the hold, so a plain
   `EntityContext.Hold` can use it, and outside a hold it refuses, because a number read outside the transaction it is written in is what causes a gap or a
   duplicate.
   - **The read is one row.** A page takes the identity as a tiebreaker, for a total order across pages, and past the column's own key that kept SQLite from
     answering out of the index: the top row took 10 to 14 ms over 20,000 rows. `Data<T,K>.Top`, internal, reads it with no tiebreaker, in about 0.1 ms. Every
     other paged read keeps its total order.
   - **No marks in memory.** A numbered command costs about what an unnumbered one does, because the commit is what costs, and marks would add a second copy of
     the truth to keep right.
   - **A scope that hides rows refuses.** A soft-deleted top row is hidden from the read and still holds its number in the unique index, so the next number would
     collide (measured: `UniqueValueConflictException`). A sequence over an entity a non-equality read scope covers refuses, and says so. A tenant is an equality
     scope with a unique index of its own, so it numbers within the tenant.
9. **`ICommandWatcher` is told each commit that wrote, in commit order.** It is registered in DI and told in this process, with the command and the last number it
   took from each sequence. Order has to be true: consumers keep state machines and drop-oldest channels, which an out-of-order announcement would leave wrong. So
   it runs after the native commit and before the store's turn passes on (`BeforeItsTurnEnds`), and not through `AfterCommit`, which runs once the turn has gone.
   - **The turn passes on regardless.** The commit has landed, so whatever an announcement does is logged and changes neither the answer nor the turn, a watcher that cannot
     even be resolved included. One watcher that throws costs no other.
   - **A watcher runs in the sender's context, with the hold no longer ambient,** since it has committed. So calling an async method that sends, or starting a task that does,
     simply takes its turn behind this command. A watcher still returns at once, since the store's turn waits for it.
   - **A command that wrote nothing is not announced:** the coordinator records whether the hold admitted a write (`AdmitToHold`), since `TrackedOperationCount` stays zero
     in a hold.
10. **A command does not end its own hold.** While `Run` runs, `Commit` and `Rollback` refuse, through `EntityContext` and the coordinator alike, so an outcome cannot
    disagree with what landed.

## Coalescence

- **Closest pattern:** `RoleCollection.Change` for one step that owns a hold.
- **Owner:** `Koan.Data.Core`, because the run depends only on `EntityContext.Hold` and `Koan.Core`. Web and MCP surfaces project commands and are a later
  decision; `Koan.Identity` joins through SEC-0036 with no reference either way.
- **Absorbed from a consumer's writer:** the contract, outcomes, sequences, the refusal and the announcement. **Deleted:** the queue and its loop, the step before the
  transaction, loaded and reloaded marks and their registration, the per-command state passed to `Run`, and the classification of what a failed commit left, which
  the coordinator reports.
- **Word:** "command" is the word a coding model and a reader bring to a write sent as a value, and is free as a type: `ServiceCommand` and its subtypes control a
  background service through `HandleCommand`, in another namespace and pillar. `IWrite<TResult>` was the alternative; it avoids that overlap, but reads as a verb and
  says less about being sent.

## Ergonomics

One interface and one verb are the common path, and `Sequence`, `Reason`, `Refusal` and `ICommandWatcher` are there only for what uses them. `new Reply(...).` lists `Send` and
`TrySend` in IntelliSense. An application reads `Outcome<T>` by pattern, which is three cases, and a surface maps a reason's kind once.

## Compatibility

- Additive. A host gains no service: nothing happens until a command is sent.
- `TransactionCoordinator`, internally, records whether a hold admitted a write, tallies the hold's sequences, refuses an end from inside a command and runs a step
  before its turn passes on. `ITransactionCoordinator` is unchanged. `Data<T,K>` gains an internal `Top`.
- A route with no holds is refused at the first send and not at start, since nothing runs at start to ask, and refusing at boot would stop hosts that only read.
- A command holds the store while it runs: every other writer waits, so it does no network or other slow work. Each command takes a connection, as a role change does
  (SEC-0036).
- A command that never returns hangs its sender until the sender's token, and a sender with no token has chosen that. Its hold ends at `HoldLimit`, whatever it does.
- A host's stop does not answer senders itself: a command ends with its caller, and a host that ends aborts its open holds (DATA-0166 rule 7).
- A stop can wait for a hold, for at most `HoldLimit`, if it overtakes the host's first lexical measurement. `StartupProbeService` must await its contributor before services
  are disposed; measuring provisions the entity's tables, which needs the write lock; and the driver's busy wait cannot observe a token. Holds are aborted only at disposal,
  which comes after the stop. It needs an open hold and an unfinished first measurement at once.
- A try is not awaited by a stopping host. It ends as an abandoned hold does, and a later attempt makes it good.
- DATA-0166's first-use note applies to a command: a table first used inside a hold can make another flow wait, so an entity a command writes belongs at boot on a route that
  holds, as `Koan.Identity` does for its own.
- `Send` never retries: a commit reporting `Unknown` is reported, and not run again.

## Evidence

- `SqliteCommandSpec` (Connector.Sqlite, 37 specs), each on its own file through a real `AddKoan()` host:
  - **Two real processes:** the test assembly started again, sending 25 numbering commands while this process sends until the other has finished, with one command in three
    here refused after taking a number. The numbers run 1 to the total with no gap, and the processes handed over to each other 14 times in the run recorded here, so they
    did interleave.
  - **Contention in one process:** 500 concurrent sends are all answered, with no gap in the numbers and announced strictly in commit order. Senders' own orders are kept.
    A send behind a turn that outlasts the busy timeout is told it never ran. Past the bound a try is refused, a send waits, and a wait cancelled in the line never runs.
  - **The run:** two entity types commit whole and nothing shows outside until they do. A throw, each outcome returned and each refusal thrown leave nothing and give their
    numbers back. The same id twice answers with what is there, unannounced, and another body is a conflict. A command reads its own writes, and two sequences in one command
    count apart, are announced together and go back together.
  - **The flow:** a command lands in its sender's partition with its carried value, and the next does not inherit them. A send and an awaited try inside a command or a scope are
    told they never ran. Work following a commit can send and await it. A startup service sending with no token, and a send with background services off, are answered. A
    command cancelled while it runs is told nothing landed.
  - **The bounds:** a command that never returns, by await or by blocking its thread, hangs only its sender while the next command runs with its number; when let go it is told
    nothing landed, and its late write is refused. Work after a commit that never ends hangs only its sender. A host that ends rolls back the command that is running, at once
    when the first measurement has been taken, and within the hold's limit when it has not.
  - **Watchers:** one that throws costs the next nothing, one that cannot be resolved costs nothing and the next send goes on, a commit is announced before its sender is
    answered, and a task a watcher starts, or an async method it calls, sends and takes its turn behind this command.
  - **Reasons:** every kind, returned and thrown, leaves nothing and reaches the sender as it was declared; a literal and an interpolated string are invalid reasons.
  - **The try's slot:** after tries that were answered and one whose command never returns, exactly the whole bound is free.
  - **The commit:** `Commit` and `Rollback` from a command refuse by both paths and leave nothing. A deferred foreign key the store refuses at `COMMIT` reports `Unknown` and
    announces nothing; a hold behind another connection's lock reports `NotDispatched`; in both the next number is right with nothing reloaded. A refused first command on a
    fresh file leaves the next a table to write to.
- `CommandRouteSpec` (Koan.Tests.Data.Core): on JSON and InMemory a send is told `NotDispatched` with the correction, and so is one under a source on JSON, while the next is
  served. It sits here because the SQLite specs' assembly has no other provider to route to.
- `CommandTenantSpec` (Koan.Tenancy.Tests): a tenant's command writes that tenant's rows, invisible to another, and numbers within the tenant.
- `SequenceReadScopeSpec` (Koan.Data.Axes.Integration.Tests): a sequence over an entity with a hiding read scope refuses, past a hidden top row.
- `RoleHoldSpec` (Koan.Identity.Sqlite.Tests): a command's role change and entity write roll back together and leave the same compiled bag, and commit together. It sits there
  because referencing `Koan.Identity` from the SQLite specs would change every host they boot.
- **Measured against the queue this replaced**, the same source on one machine, one file per run: 300 commands in sequence and bursts of 500 and 2000 took 9.3 to 10.3 ms per
  command on both, about 105 a second, so the queue bought no throughput. The two part at the bound: a burst of 4000 took the queue 38.29 s with all 4000 done, and here 30.23 s
  with 3184 done and 816 told `NotDispatched`, with no gap.
- **Measured by removing them:** with watchers run after the turn has passed on, both order specs failed in three runs out of three, since their watcher takes longer than a commit
  now and then; with an announcement's failure not caught, the next send was refused; with a try's slot kept while its command runs the bound had 1023 free, and with slots never
  given back 1018; with watchers run in the hold's flow, a send they start was told it never ran. Against the queue's form of this code: without the read-scope refusal the command
  fails on `UniqueValueConflictException`; with `FirstPage` in place of `Top`, 100 reads took 1.05 s against a bound of a quarter of 100 commands, 0.22 s.
- **A stop behind a hold,** ten runs of each: with the first measurement taken, 2 to 4 ms every time; with it racing, about 15 ms or, in three of the ten, about 2050 ms, which was
  the 2-second limit of that spec. `StartupProbeService.StopAsync` was timed at about 20 s against the default limit, and `data:lexical` was the one contributor found blocked.
