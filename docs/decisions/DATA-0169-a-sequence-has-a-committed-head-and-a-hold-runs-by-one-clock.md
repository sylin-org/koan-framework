---
id: DATA-0169
slug: a-sequence-has-a-committed-head-and-a-hold-runs-by-one-clock
domain: Data
status: Accepted
date: 2026-11-04
title: A sequence has a committed head a reader waits on, and every hold runs by one clock
amends:
  - DATA-0167
  - DATA-0168
related:
  - DATA-0166
---

# DATA-0169: A sequence has a committed head a reader waits on, and every hold runs by one clock

## Context

DATA-0168 item 4 left a log's high-water mark to its consumer: a sequence's committed numbers have no gap, so a mark raised by a compare-and-swap
maximum after each commit is correct in any order. Its one consumer, Tangent Space, keeps that mark in memory. Each command that appends to its log
registers `AfterCommit` to raise it, and every live stream of the log waits on a pulse the raise sends. It is the same few parts any consumer that
follows a numbered log live would write again: a field, a maximum, a hook out of each commit and a wake. Kept by the consumer, the mark also learns
only of its own process's commits.

The same consumer's commands carry nineteen `DateTimeOffset? At` parameters, and pass each on to the rows they write. Nothing in the business needs them.
They exist because a test or a seed must say when each write happened, and a command has no clock of its own to ask.

**The business sentence:** a reader waits until the log moves past what it has read, and a command reads the time from the clock its write runs by.

## Decision

1. **A sequence has a committed head.** `await Event.Log.Head(ct)` answers the highest number of the column that has committed, with every number
   below it committed too. The first ask reads the store once, as `Highest` does outside a hold, and after it the head answers from memory. It never
   runs ahead of what has committed. It lags: a commit of this process reaches it before that hold answers its sender, and another process's commit
   reaches it when a wait on it ends at its bound (item 3). A head nobody waits on learns of no other process's commit.
2. **Each hold that commits raises it once.** Once a held commit has landed and the hold has ended, it raises the head of each sequence it took
   numbers from to the last number it took, from the tally it already keeps (`Take`). The raise is a compare-and-swap maximum, so the order holds
   report in does not matter (DATA-0168 item 4), and it happens before the work registered with `AfterCommit` runs and before `Send` answers: a
   sender that asks the head after its own send sees its own number. A hold that rolls back, is refused, fails, ends at its limit or reports
   `Unknown` raises nothing; if an `Unknown` commit landed after all, the next hold reads it from the store and raises past it. One hold makes one
   raise per sequence, not one per number. The head trusts what the sequence already trusts: a number a hold takes is a row it writes.
3. **A reader waits on the head for a bound it gives.** `await Event.Log.WaitPast(seen, TimeSpan.FromSeconds(25), ct)` answers the head as it stands
   when the wait ends: at once if the head has passed `seen`, as soon as a commit of this process moves it past, and otherwise at the bound. The wait
   takes its wake before it reads the head, so a commit between the two still ends it. A wait that ends at its bound reads the store once and raises
   the head to what it finds, so another process's commit reaches a waiting reader within one bound. The bound is required, positive and finite;
   the token ends the wait with `OperationCanceledException`. The wait takes no lock a writer waits on and writes nothing. There is nothing to register
   and no callback: being told of each commit is the `ICommandWatcher` DATA-0168 removed.
4. **The heads belong to the host.** They live in one internal `SequenceHeads` per host, registered beside the transaction factory, which hands it to
   every hold. They are never the static `Sequence`'s, so two hosts in one process keep two heads. An application registers and configures nothing.
5. **The head is the entity's default store's, and refuses everywhere else, with a sentence that says what to do instead.**
   - Inside a hold, `Highest` is the read: it sees the hold's own numbers, and a wait there would keep every other writer of the store waiting.
     `Head` and `WaitPast` refuse there.
   - A sequence numbers within the store and the scope it is read in (DATA-0167 item 8). `Head` and `WaitPast` refuse wherever the column would be
     read from anything but the default store: a named source or adapter, a partition, a container a storage-name particle names, and an entity that
     declares an equality field, such as a tenant's, whatever value the flow carries, since such an entity has a count per value and no one head. A hold
     that took a sequence's numbers in any of these places raises nothing for that sequence.
6. **Every hold runs by one clock.** It is the `TimeProvider` its sender named for that send, `command.Send(clock, ct)`, otherwise the host's registered
   `TimeProvider`, otherwise `TimeProvider.System`. Koan.Data.Core registers no clock of its own. A command's `Run`, and everything it calls, reads it as
   `EntityContext.Clock`. Outside a hold the accessor answers the host's, the same one dependency injection hands out, so code a command calls reads one
   accessor wherever it runs. A plain `EntityContext.Hold` runs by the host's. `Send(ct)` is unchanged. The `[Timestamp]` stamp and Koan.Identity's audit
   chain stay on the system clock.

**Why the head is not the marks DATA-0167 refused.** Item 8 refused marks in memory because numbering would then trust a second copy of the truth, kept
right by loading and reloading. Nothing numbers from the head: `Next` still reads the store at its first use in each hold. The head lags and never
leads, because it rises only to a number that has committed with every number below it, so a reader that trusts it misses nothing and reads nothing
twice. Nothing reloads it: a wait refreshes it from the store at its bound, and an `Unknown` commit leaves it behind, never ahead.

## Coalescence

- **Owner:** `Koan.Data.Core`. The hold's tally and its commit own a sequence's committed numbers, and the hold owns how a command runs. The head is
  raised from that tally inside that commit, and the clock is set on the hold the way `Send` already sets `EndedByItsOwner`.
- **Closest patterns:** `TransactionCoordinator.Take` for the numbers; the held commit's end and `RunAfterCommit` for where a commit's consequences run;
  `OAuthGrants.ForgetConsent`, which reads the host's `TimeProvider` with the system's as the fallback.
- **Absorbed from the consumer:** the high-water mark and its wake, its hook out of each commit, and the time a command is told as a parameter. The
  consumer's copies are deleted where it moves onto these.
- **Not built:** a callback for each commit; a head per partition, source or tenant; a clock overload on `EntityContext.Hold`; a clock parameter on any
  read; putting the `[Timestamp]` stamp and the audit chain on the hold's clock, which would change the stamps of every host that registers a
  `TimeProvider`, as Koan.Jobs' specs register a fake one.

## Ergonomics

`Event.Log.` lists `Highest`, `Next`, `Head` and `WaitPast`: the read inside a hold, the number, the committed head and the wait on it. A stream's loop is
one line, `seen = await Event.Log.WaitPast(seen, keepAlive, ct)`, and a page that must never run ahead of what has committed takes `await Event.Log.Head(ct)`
before it reads. `new Reply(...).` lists `Send` twice, with and without a clock. `EntityContext.Clock.GetUtcNow()` sits beside `Hold`, `Commit` and
`UncommittedScope`, and is a standard `TimeProvider`. Public concepts added: `Sequence.Head`, `Sequence.WaitPast`, `EntityContext.Clock` and the
`Send(clock, ct)` overload.

## Compatibility

- Additive. `ITransactionCoordinator`, `Send(ct)`, `Highest` and `Next` are unchanged, and nothing happens until a head is asked or a command sent.
- Internally, `TransactionCoordinator` takes the host's heads and its clock, records which sequences it numbered away from their default store, and
  raises the rest once it has committed; `TransactionCoordinatorFactory` takes `SequenceHeads`, which `AddKoanTransactions` registers once.
- DATA-0167 item 8 ("No marks in memory") and DATA-0168 item 4 (the consumer keeps its own high-water mark) change in words only: numbering reads the store
  as before, and the committed head moves from each consumer into Koan.
- A hold that numbers one sequence under two partitions or two tenants continues one count across them, because the tally is keyed by the sequence
  alone. That is unchanged here, and such a hold raises nothing.

## Evidence

- `SqliteSequenceHeadSpec` (Connector.Sqlite, 10 specs), each on its own file through a real `AddKoan()` host:
  - **The head:** the first ask answers the committed top, a send has raised it when it returns, and after the first ask it answers from memory. A
    refused command, a refusal thrown from below one, a throw and a hold disposed without a commit leave it where it was.
  - **The wait:** a commit wakes it well inside its bound; with none it answers at its bound; a cancelled one throws. A commit landed between its read of
    the head and its wait, through an internal seam, still wakes it at once.
  - **A burst of 200 concurrent sends:** every head asked, and every head a wait answered, names a row a plain read finds, with every number below it
    committed, and the burst ends with the head at its last number.
  - **Hosts and processes:** two hosts in one process, on two files, keep two heads. A commit by another process, through the two-process harness, leaves a
    head nobody waits on where it was, and reaches a waiting one at its bound.
  - **Refusals, each with its sentence:** inside a hold, a named source, an adapter, a partition, a container a particle names, and an equality field with
    no value in the flow; holds that numbered under the source and the partition raise nothing; a bound that is not positive and finite is refused.
- `SqliteCommandClockSpec` (Connector.Sqlite, 2 specs): a command reads the clock its sender named and, sent without one, the host's registered
  `TimeProvider`, a plain hold too; a host with none runs by the system's; and outside a hold the accessor answers the host's, the one DI hands out.
- **Measured by removing it:** with the wake taken after the read, the wait missed the commit landed between them and answered at its bound, 10.03 s against
  the 5 s the spec allows.
