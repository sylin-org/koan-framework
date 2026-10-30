---
id: JOBS-0010
slug: jobs-never-starve-and-never-lose-an-update
domain: Jobs
status: Accepted
date: 2026-10-30
title: The worker never waits on its own work, and Jobs never writes over a change it did not make
related:
  - JOBS-0005
  - JOBS-0006
  - JOBS-0009
  - DATA-0131
---

# JOBS-0010: The worker never waits on its own work, and Jobs never writes over a change it did not make

Amends [JOBS-0005](JOBS-0005-job-orchestrator-rebuild.md) §6 (`@continuous`), §8 (the in-memory tier), §17.1
(conditional auto-save) and §19.3 (retention), [JOBS-0006](JOBS-0006-trailing-edge-coalesce-semantics.md) (what a
submission coalesces onto) and [JOBS-0009](JOBS-0009-wake-stamp-and-lease-heartbeat.md) Decision A (the one-row wake
stamp). The rest of those records stands.

## Context

Measured at `51b2c3310`.

- **The worker stopped its own duties while it ran work.** The roster heartbeat, the reaper, the scheduler, the
  archival sweep and the metrics flush ran only between drains, and a drain did not return while a claimed job was in
  flight or ready work kept arriving. A node running a job longer than `WorkerDeathTimeout` stopped heartbeating; an
  idle peer declared it dead, requeued its job and removed its roster row; the original's lease renewal then failed
  and it abandoned the job, and the new owner's drain blocked in turn. A node working a backlog never scheduled, reaped
  or archived.
- **`@continuous` ran away.** Every worker tick fired a fresh trigger whose wake hint woke the worker at once, so the
  loop spun and wrote a ledger row per tick. `[JobIdempotent]` coalesces only onto a queued row, and a short pump had
  already completed.
- **Submit and settle overwrote the work item.** Submit upserted the caller's copy whenever it differed from the
  stored row; settle upserted the copy loaded at claim plus the handler's edits, even after a job that ran for minutes.
  Either reverted whatever another writer committed in between — Tangent's sign-in submits a participant's profile job
  right after its own single writer commits.
- **Gate rows grew forever.** A gate row had a fresh id, so a check-then-insert could write two rows for one key, and
  no row was ever removed; every claim scanned `ReleaseAt` over all of them without an index.
- **Every submit updated one row inside its transaction.** The wake stamp was read and written back with the next
  version, so concurrent submitting transactions serialized on that row until commit.
- **The in-memory ledger kept everything.** Settled rows stayed for `ArchiveAfter` (seven days), and every claim
  filtered every record, built its busy set from every record and sorted the due rows under one lock; status reads,
  counts, reservation passes and queries filtered or sorted every record under the same lock.
- **Reads loaded whole sets.** An unpaged query loops pages until it holds every match: retention loaded every stale
  row and deleted them one by one, every claim loaded every Running row, the reaper loaded every Running row twice,
  and `Jobs.Query`/`WithStatus`, a work item's status and its cancellation loaded every row they matched.
- **Coalescing raced.** Every idempotent submit scanned the queued rows of its type without an index, and the check
  and the append were separate steps, so two concurrent duplicates could both miss and both append.

## Decision

1. **Every worker duty runs on its own timer.** After boot recovery the worker runs the roster heartbeat
   (`WorkerHeartbeatInterval`), the reaper (`ReaperInterval`), the scheduler (`PollInterval`), the archival sweep
   (`ArchiveInterval`) and the metrics flush (`MetricsFlushInterval`) as independent loops; a failing duty never
   pauses another. Beside them the dispatcher wakes on a submission hint, a settled execution, a moved wake stamp or at
   `PollInterval`, claims ready work up to `WorkerConcurrency` and starts each execution as a background task the
   worker owns, then returns to waiting. Executions observe the stopping token and are awaited before the node resigns.
   Because the reaper now runs beside the node's own executions, each reclaim is fenced on the owner it read, through
   the same ownership-guarded write a settlement uses. A duty interval longer than a timer accepts (about 49.7 days)
   is waited in parts of one day, so any `TimeSpan` works and `TimeSpan.MaxValue` means never.
2. **`@continuous` re-arms after its run settles.** The scheduler remembers the job each continuous action last fired
   and fires again only when that job is terminal (or gone) and at least one `PollInterval` has passed since the fire:
   one ledger row per settled run. The scheduler is ticked by its own timer, so its submissions never wake it.
3. **Submit writes a work item only when it is absent.** An atomic insert where the store declares
   `DataCaps.Write.InsertOnly`; a read followed by a save otherwise, and inside a transaction, where the save is
   deferred to the commit (DATA-0131). A stored item is never overwritten. A caller that changed its copy saves it
   before submitting.
4. **Settle writes only what the handler changed, onto the row as stored.** The orchestrator captures the item as
   Koan Data stores it when it loads it (`WorkItemImage`): its document, through the same serializer and contract
   persistence writes with, so state behind a non-public setter is included and members the contract ignores are not.
   At settle it writes back only the members the handler changed on the reference it was given, onto a fresh read of
   the row, through a conditional replace whose guard is that read (`WorkItemGuard`). Each persisted member is compared
   exactly or left out and named, nothing in between: a scalar by value; a set of scalars by its size and by holding
   each of them; a list only while it holds at most one scalar, because Koan's filter language compares neither the
   order of a longer list nor its duplicates; a nested object member by member where the store pushes nested paths.
   Each member's comparison is admitted before anything is written — one the store would not push natively, a managed
   or classified field, a string on a store whose string equality folds case, or a member the store cannot resolve is
   left out and named — so one member never costs the guard of the others. A guard miss caused by a concurrent write merges again, up to three reads; a removal while the
   job ran stands. The handler that leaves its reference untouched writes nothing, as before.
5. **A write conflict is stale work, not a failure.** A member both the handler and another writer changed to
   different values is a conflict: nothing is written, and the job runs again on the stored item after a short delay,
   through its own count (`JobRecord.Conflicts`). A conflict never consumes `MaxAttempts` and never advances a chain;
   past `JobsOptions.MaxWriteConflicts` (5) the job is dead-lettered as `DeadReason.Conflict`, with a message that
   says so.
6. **One gate key is one row, removed once released.** `JobGateRecord.IdFor(key)` addresses the row;
   `IJobLedger.SetGate` creates it with an atomic insert and extends it with a conditional replace that applies only
   while the stored release is earlier, so concurrent backoffs keep the latest release in one row. `ReleaseAt` is
   indexed. `IJobLedger.PurgeReleasedGates(now, limit)` removes at most one page of released gates, guarded so a gate
   extended meanwhile is kept, and the archival sweep pages it.
7. **The wake stamp is sixteen shards written blind.** A durable append writes one shard row, chosen at random, with a
   fresh random version and no read, inside the submission's transaction; probes read every shard in one bounded read
   and compare a fingerprint. This keeps JOBS-0009's invariants — a rolled-back submission moves nothing, a committed
   record implies a moved stamp — while two submitting transactions meet on one row only one time in sixteen. Bumping
   after commit would need a commit hook Data does not offer; relying on the wake signal and polling alone would give up
   JOBS-0009's sub-second cross-node discovery on a durable-only fleet. Known next step: once Koan.Data offers an
   after-commit hook, the bump moves off the caller's transaction.
8. **The in-memory ledger keeps settled jobs only briefly, and no read waits for a claim.** It files rows by state:
   queued rows per lane in claim order and, across lanes, the open ones in visible order; running rows with the work
   items and pool members they hold; the coalescing slots and groups of queued jobs; each work item's jobs; the active
   jobs of each work type, counted as rows change state; and settled rows in a history of at most
   `JobsOptions.InMemoryTerminalCapacity` (4096), evicting the job that settled first. A claim walks each lane's due rows
   from its head and stops at the first claimable one; a reservation pass walks the open rows from their head. A stored
   row is never changed in place — every write stores a new copy — so a read by id, a status read by work item, a count
   and a query read without the claim lock. A query that names a work item reads that item's jobs; any other is a
   predicate and scans, outside the lock, keeping only the rows up to its page.
9. **Every read is bounded.** `PurgeArchivable`, `PurgeFailed` and `TrimTerminal` take a limit and remove at most one
   page; the sweep pages each until a page falls short, and `TrimTerminal` reads only the keep-th newest row for its
   cutoff. `Stuck` and the new `Orphaned` return a page and the reaper pages them; the reservation coordinator reads the
   live hands' load through `RunningLoad`. A claim reads the Running rows of the work items in its candidate window and
   those holding a pool member. `JobQuery` carries `Page` and `Size` (default 100) and is read newest first by
   `FirstSubmittedAt`; a work item's status is its newest row, read alone; cancellation pages its queued and running
   jobs. The unused whole-set reads `Running`, `NonTerminal` and `InStage` are removed. `JobRecord`'s
   (WorkType, Status) and (WorkType, WorkId) indexes gain `FirstSubmittedAt`, and (Status, LastSettledAt) orders the
   retention pages.
10. **A coalesce key never runs twice at once, and its queued jobs are served by its next run.** Coalescing never
    crosses work items: a key names the work item itself — its identity and its data address — besides its type, its
    action and the properties `[JobIdempotent]` declares, so lookup, the slots and absorption are all per work item, and
    another item with the same properties always gets its own job. Every job of a key
    carries `JobRecord.CoalesceGroup`, an indexed name-based GUID of the key. A queued submission holds
    `JobRecord.CoalesceSlot`, the same GUID under a unique index (every other row holds a GUID of its own id; GUIDs,
    because SQL Server declines to index a string key): `IJobLedger.FindCoalescing` finds any queued job of the key with
    one seek, and `IJobLedger.AppendCoalescing` appends the record holding the slot, so a concurrent duplicate meets
    the unique index and coalesces onto the first, or appends again when the holder was claimed in between; a
    submission never fails on a coalescing race. A running job holds `JobRecord.RunSlot`, the key's GUID under a second
    unique index, taken by the claim's own write: a second job of the key cannot be claimed while one runs, and a claim
    that loses the slot to a concurrent claim leaves its job queued. Only that refusal is a lost claim: any other failure
    of a keyed claim's write surfaces as a plain claim's does. A claim reads past a job whose key runs and past a
    key with a queued job deferred beyond now — the key's work waits for its deferral rather than carrying it, because
    waiting writes nothing and so has nothing to race or lose; should a deferral land between a claim's read and its
    write, the claim carries the later visible time back to the queue instead of running. A claim whose write-back the
    store keeps refusing is never run either: it is left to lapse with its lease, logged at Warning, and the reaper
    requeues it. The claimed job then serves
    every other queued job of its key, each settled `Cancelled` with `JobRecord.ServedBy` naming it: the one place a
    job is absorbed beyond submit-time coalescing. A claim and a cancellation before running give the coalescing slot
    back, so a submission arriving while the job runs queues one trailing execution. A job that returns to the queue —
    a retry, a deferral, a shutdown requeue, a write conflict — goes through `IJobLedger.TryRequeue`, one end-of-claim
    owner write (decision 13) that gives both slots back and changes nothing else; a crashed runner's run slot is given
    back when the reaper reclaims its lapsed lease. Between a requeue and the next claim a key can hold two queued jobs, its
    requeued job and a submission that arrived while it ran; the next claim, after any deferral, serves both. JOBS-0006
    holds: one running job per key, and every submission served by a run that starts after it.
11. **Every write to a job row is a compare on its revision.** `JobRecord.Revision` takes a fresh value on every ledger
    write, and every ledger write to a row — a claim, a lease renewal, progress, a settlement, a requeue, an absorption,
    a cancellation, a reservation, the reaper's reclaim — replaces it only while it still carries the revision its writer
    read: one conditional replace where the store offers it, so a copy read before another node's write never lands
    over it. The reaper and the dead-node reclaim compare on the revision their sweep read and never read again: a row a
    renewal, a settlement or a cancellation changed since is skipped until the next sweep. A revision Jobs keeps itself;
    known next step: a version Koan.Data maintains on every store replaces it.
12. **A handle tells settled, timed out and unknown apart by type, and follows the job that serves it.**
    `JobHandle.Completion` returns `JobOutcome.Settled` with the terminal status and last error, `JobOutcome.TimedOut`
    with the status the job had reached, or `JobOutcome.Unknown`; `Succeeded` is true only for a settled `Completed`.
    A handle reads the ledger and also hears a settlement its process makes: the ledger tells the waiting handles when
    it writes a settled row — the in-memory ledger before it stores the row, and so before its history can evict it; the
    durable ledger once the write has landed outside a transaction. A submission coalesced into another job after it
    queued settles `Cancelled` with `JobRecord.ServedBy` naming that job; its waiting handles move on to the serving
    job, and its outcome is that job's settlement, with `ServedBy` naming it. A job the ledger no longer holds and nobody
    here heard settle is `Unknown`, never `Dead`.
13. **A claim token fences every owner write, and an owner writes only its own fields.** Each claim writes a fresh
    random `JobRecord.ClaimToken`, which renewals keep. Every write of the claim's owner — renewal, progress, settlement,
    requeue — applies only while the row carries that token, so an execution whose claim was taken over, on another node
    or on its own (a zombie and its node's re-claim share the owner), writes nothing; the orchestrator keys its running
    executions by token. An owner write reads the row; when the token is not there, the claim is lost — unless the row is
    the owner's own write whose answer was lost, recognised by the revision it wrote, which an end-of-claim write also
    stamps on the transition it appends. Otherwise it applies only its own fields onto the row just read — a renewal the
    lease, progress the progress fields, an end of claim its status, visible time, ownership, lease and reservation,
    settlement and expiry, reasons and last error, attempt, reschedule and conflict counts, slots, transitions and last
    progress, dropping the token — writes it as a compare on that read's revision, and reads again on a miss, a bounded
    number of times. A field another writer set, the cancellation request above all, is never overwritten, and the
    decision is made on the merged row: asked to cancel, a retry, deferral, conflict or shutdown requeue settles
    `Cancelled`, a completion settles `Completed` without advancing its chain, and a failure settles as it would. A
    success is decided on a copy of the run's row, so a failure that ends its settlement — a read that fails once the
    completion is decided — is settled from the row as the run left it, and its history never shows a completion that
    did not land. Side effects follow that decision, after the write: events are published once it lands, and a requeue
    that ended `Cancelled` sets no gate, publishes no reschedule and counts a cancellation. A cancellation lands on
    whatever state the job is in, and runs until the work item is quiet: each pass marks the item's running jobs, then
    cancels its queued and held ones, and the canceller passes again, at most five times, until a pass finds no job of
    the item that is neither settled nor marked. A mark that misses reads the job again; a job found settled by its claim
    after the miss has its successor followed and stopped. A renewal that reads the mark cancels the handler, wherever
    it runs. The reaper's reclaim drops the token too, and settles a job asked to cancel `Cancelled`.
14. **A chain is closed by order.** A run appends its successor before it settles, held for it: queued but excluded from
    every queued read on both ledgers — claims, lane seeks, reservations, coalescing, absorption, the probe — under
    `JobRecord.HeldBy` (its claim token), `JobRecord.HeldFor` (its own id) and `JobRecord.HeldOn` (the settlement it was
    appended for: `Completed` on the success path, `Failed` under `OnFailure.Continue`), with an id derived from the
    predecessor's id and that token, so one run's appends write one row and two runs never collide. Its settlement
    records `JobRecord.SettledBy` and then releases the successor — clearing `HeldBy` as a compare on the revision read,
    and moving the wake stamp as an append does — or cancels it when the settled row carries a cancellation request. A
    release whose answer was lost is recognised by the revision it wrote, and reports the release. An end of claim that
    lands deletes the held successor it does not release: one the success path appended before a failure that aborts,
    or a requeue. An end of claim whose write did not land — its claim taken over, or ended by an earlier end of the
    same claim that landed with its answer lost — leaves the successor to the reaper, which decides by what the row
    shows; the reaper's reclaim of a run deletes that run's successor. Under `OnFailure.Continue`, a failure that follows
    an end of claim which failed settles over the successor its success appended, still held on that completion. The
    reaper releases a held successor whose holder claim no longer
    exists only when its predecessor settled with the status it was held on (`HeldOn`), carries no cancellation request
    and was settled by that very claim (`SettledBy` equals `HeldBy`); otherwise it deletes it. A run that stalled and was
    reclaimed, while its re-claim settled the job, therefore never releases a second successor, and a stage recorded
    `Failed` under `OnFailure.Abort` never releases the successor its success path appended. No tombstone is written: a
    submission after a cancellation runs.

## Capability limits

- **Without `ConditionalReplace`, or inside a transaction,** a job row's revision is read and compared just before an
  ordinary save: the at-least-once floor, where a write landing between that read and the save is overwritten.
- **Inside a transaction** no write can be conditional (DATA-0131). Submit writes an absent item by a deferred save
  after a read. A record takes no coalescing slot, so the commit can never fail on one; it coalesces onto a committed
  job of its key or onto one the same transaction submitted. Two transactions that submit one key at once cannot see
  each other before they commit, so both commit a job; the first of them claimed absorbs the other, and the key runs
  once.
- **Without `InsertOnly`** (SQL Server today), submit writes an absent item by a read and a save: a creator that lands
  between them is overwritten, and where the store refuses the second save of one new identity (SQL Server), the first
  creator's item stands.
- **Without `ConditionalReplace`, under a managed or read scope (row-scoped tenancy), for an Entity variant, or when
  the stored values do not round-trip through a guard,** settle writes the merged row by an ordinary save. **A member
  the store cannot compare exactly** — a dictionary, a binary value, a classified or managed value, a collection that
  is null, a list of two or more elements or of objects, a nested object on a relational store, which pushes no nested
  paths, and a string, or a list of strings, on a store whose string equality is not declared ordinal (SQL Server, MySQL
  and Firebird declare it through `FilterSupport.OrdinalStrings`) — is left out of the guard and named; the other members are still
  compared. The in-memory Data adapter replaces conditionally like any other: its evaluator compares a `DateTime` by
  its whole value (a local time's side of an ambiguous daylight-saving hour included), a `DateTimeOffset` by its
  instant and its offset, and binary data by its bytes. SQLite, PostgreSQL, MongoDB and SQL Server store a
  `DateTimeOffset` at offset zero, so an offset-only change is not a stored change there. **An item that cannot be serialized as Koan stores it** is saved whole from the handler's copy. Each case
  is logged once per work type and reason at Information and every time at Debug, with the writes it can lose: a write
  to the members left out that lands between the settle's read and its write, or, for an item saved whole, every write
  another writer made while the job ran.
- **Gates without `InsertOnly` or `ConditionalDelete`** (SQL Server today). A gate that does not exist yet is created
  by an ordinary save and read back, and a writer that reads an earlier release than its own extends again; but two
  writers that create one gate at once can still leave the earlier of their two releases, when the earlier writer's
  save lands after the later writer read its own back. An operator then sees that gate open at the earlier time.
  Extending an existing gate is a conditional replace and keeps the later release; without `ConditionalReplace` as
  well, an extension carries the same limit. Closing the race needs an atomic insert on the store. The sweep re-reads
  each released gate just before deleting it, so an extension landing between that read and the delete is removed with
  the gate; the next backoff on that key sets it again.
- **A unique index a store cannot build** leaves concurrent duplicates able to queue side by side, and two claims of
  one key able to land at once; the schema validation reports the index as unproved. SQLite, PostgreSQL, SQL Server
  and MongoDB build both.
- **A cancellation is read, and a handler stops cooperatively.** A successor released, claimed and run to its end
  inside one window of the canceller — after its mark lost to the predecessor's completion and before its next read —
  runs. Everything the canceller's next read finds queued, held or running is stopped, and it passes until the item is
  quiet; closing that window needs a cancellation the release or the claim consults.

## Compatibility

- **No migrations.** `JobRecord` gains `CoalesceSlot` and `RunSlot` with unique indexes, `CoalesceGroup` and `HeldBy`
  with indexes, `Conflicts`, `ServedBy`, `Revision`, `ClaimToken`, `SettledBy`, `HeldFor` and `HeldOn`, and its indexes change;
  `JobTransition` gains `Revision`; gate rows are re-keyed by their gate key; wake-stamp rows are new. Stores are
  rebuilt.
- **Public surface.** `IJobLedger` gains `FindCoalescing`, `AppendCoalescing`, `TryRequeue`, `Orphaned`,
  `RunningLoad`, `PurgeReleasedGates`, `ReleaseHeld`, `DeleteHeld` and `Held`; `TryRenewLease` becomes `RenewLease`,
  fenced on a claim token and returning the row as written; `TrySettle` and `TryRequeue` take a claim token and return
  the row as written; `Progress` takes a claim token; `Update` returns whether it wrote; `Stuck`, `PurgeArchivable`, `PurgeFailed`
  and `TrimTerminal` take a limit; `Running`, `NonTerminal`, `InStage` and `FindActiveByCoalesceKey` are removed;
  `Query` reads the page `JobQuery` names. `JobQuery` gains `Page`, `Size` and `DefaultSize`. `JobRecord` gains
  `CoalesceSlot`, `CoalesceGroup`, `SlotFor`, `RunSlot`, `Revision`, `Conflicts`, `ServedBy`, `ClaimToken`, `SettledBy`,
  `HeldBy`, `HeldFor`, `HeldOn` and `SuccessorId`; `DataJobLedger` takes an optional logger. Koan.Data's `FilterSupport` gains `OrdinalStrings`, and
  `RelationalFilterSupport` gains `CaseFolding`, which SQL Server and MySQL declare; Firebird declares it false. `JobGateRecord` gains `IdFor` and an indexed `ReleaseAt`. `WakeStamp` replaces
  `SingletonId` with `ShardPrefix`. `JobsOptions` gains `InMemoryTerminalCapacity` and `MaxWriteConflicts`.
  `DeadReason` gains `Conflict`. `JobOutcome` becomes an abstract record with three cases, `Settled`, `TimedOut` and
  `Unknown`; its `Status` and `Error` move onto the cases, and `Succeeded` stays.
- **Behavior.** `Jobs.Query` and `Jobs.WithStatus` return one page (100 by default), newest first. A caller that
  relied on submit to save changes to a stored item now saves them first. A handler whose change conflicts with a
  concurrent write runs again on the stored item instead of overwriting it, without spending an attempt. A handle on
  a submission coalesced into another job reports that job's outcome. Two different work items with the same
  `[JobIdempotent]` properties no longer coalesce: each gets its own job. A job asked to cancel while it runs settles
  `Cancelled` even when its handler returns or fails in a way that would requeue it. A keyed claim whose write fails
  for any reason other than its run slot throws, as a plain claim does, and a claim whose write-back to wait for its
  key's deferral keeps failing stays `Running` until its lease lapses.

## Evidence

Each spec below fails without the change it proves and passes with it, on the tiers named, unless it says it cannot
compile against the earlier code:

- `WorkerLoopSpec.a_job_longer_than_the_death_timeout_completes_while_the_node_heartbeats_and_schedules` (in-memory,
  real worker): the heartbeat lands while the job runs, a schedule fires during it, and a peer's reaper reclaims nothing.
- `continuous_pump_writes_one_row_per_settled_run` (every tier): ten ticks over one unsettled run write one row.
- `submit_never_overwrites_a_stored_work_item` and `a_change_made_while_the_job_runs_survives_settle` (every tier);
  `a_change_behind_a_non_public_setter_survives_settle` and
  `a_concurrent_change_behind_a_non_public_setter_is_a_conflict` (every tier, failing on the first revision of this
  record, whose image held only public setters).
- `a_conflicting_change_runs_again_without_spending_an_attempt` (a stage with one attempt and `OnFailure.Continue`
  conflicts once, stays queued, does not advance its chain, then succeeds) and
  `conflicts_past_their_bound_dead_letter_as_conflict` (every tier).
- `a_tag_added_between_the_settles_read_and_its_write_survives_a_rename` (every tier): another writer adds a tag
  between the settle's read and its replace, the guard misses, and the merge keeps both changes.
- `a_list_changed_between_the_settles_read_and_its_write_is_caught_or_named` (every tier; a reorder and a change of
  which element repeats): failed on the in-memory tier and SQLite while a list was guarded by its size and elements.
- `a_member_the_store_cannot_compare_does_not_cost_the_guard_of_the_others` (every tier): failed on the in-memory
  tier, whose Data evaluator refused the whole guard over one `DateTime` member, losing another writer's change to a
  number; `ConditionalPublicationSemanticsSpec.Document_CAS_evaluator_compares_binary_by_its_bytes_and_DateTime_by_its_ticks_and_kind`
  (Data.Core) failed on that refusal.
- `a_requeue_that_loses_its_fence_leaves_the_queued_duplicate_to_run` (every tier): failed on SQLite, where the
  requeue cancelled the queued duplicate before its fence failed; the in-memory ledger already checked its fence first.
- `a_requeue_never_overwrites_a_newer_claim_of_the_same_job` (every tier; another node claims, defers and requeues the
  job right as a requeue lands) and `a_key_never_runs_twice_when_its_jobs_are_claimed_beside_a_requeue` (two nodes
  claim right as a requeue lands): failed on SQLite and SQL Server, where the requeue's second write restored a stale
  copy and two jobs of one key ran. `a_claim_that_loses_its_keys_run_slot_to_a_concurrent_claim_stays_queued` (every
  tier; two nodes claim two jobs of one key at once, and the second write meets the run slot's unique index) failed on
  the in-memory tier, SQLite and SQL Server.
- `a_case_only_change_between_the_settles_read_and_its_write_is_caught_or_named` (every tier): failed on SQL Server,
  whose guard matched "Draft" for "draft". `InMemoryBehaviors.an_offset_only_change_between_the_settles_read_and_its_write_is_caught`
  failed on the in-memory store, and
  `ConditionalPublicationSemanticsSpec.Document_CAS_evaluator_tells_apart_two_offsets_of_one_instant_and_the_two_local_times_of_an_ambiguous_hour`
  (Data.Core) on its evaluator.
- `released_gates_are_purged_by_the_archival_sweep` (every tier).
- `WakeStampSpec.submitting_transactions_spread_their_bumps_across_shards` (SQLite): thirty-two submitting
  transactions wrote one row before. SQLite is a single writer and cannot show the lock wait itself.
- `InMemoryLedgerSpec.settled_jobs_beyond_the_terminal_capacity_are_released` and
  `claims_cost_the_same_beside_a_deep_backlog_that_is_not_due` (ratio about 10 before, about 1 after).
- `concurrent_duplicate_submits_coalesce` (every tier; SQL Server only once the slot became a GUID) and
  `duplicates_submitted_while_their_job_is_claimed_append_or_coalesce_without_failing` (every tier).
- `a_deferred_idempotent_job_absorbs_a_duplicate_and_runs_after_its_deferral` and
  `a_job_deferring_absorbs_the_duplicate_submitted_while_it_ran` (every tier): a duplicate never runs ahead of a
  deferral, and one run after it serves both.
- `DurableSqliteSpec.transactional_duplicates_coalesce_like_any_other` and
  `duplicates_from_concurrent_transactions_run_once` (SQLite).
- `InMemoryLedgerSpec.a_handle_hears_the_outcome_of_a_job_evicted_when_it_settled`,
  `InMemoryLedgerSpec.a_settlement_is_heard_before_the_ledger_evicts_it` and
  `a_handle_on_a_job_absorbed_at_claim_reports_the_serving_jobs_outcome_and_names_it` (every tier, no settled history
  kept) cannot compile against the earlier outcome; a variant of the last written for it reported unknown.
- `InMemoryLedgerSpec.reads_never_wait_for_the_claim_lock` cannot compile against the earlier ledger; a variant that
  held its lock by reflection timed out on a count.
- `WorkerLoopSpec.a_duty_interval_longer_than_a_timer_accepts_still_runs_its_duty` (a sixty-day reaper reaps on day
  sixty; an archival sweep at `TimeSpan.MaxValue` faults nothing).
- `retention_reads_and_removes_one_bounded_page_per_call` and `facade_query_reads_newest_first_one_page_at_a_time`
  (every tier) cannot compile against the earlier ledger, which had no page to give;
  `cancelling_inside_a_transaction_pages_through_every_queued_job` (SQLite) guards the paged cancellation.

- Round five, ported from the reviewer's probes and failing against the round-four code on the tiers named:
  `a_cancellation_a_renewal_carried_survives_the_owners_deferral`, `a_zombie_of_the_same_node_neither_renews_nor_settles_over_its_reclaim`,
  `a_marker_from_another_node_reaches_the_running_handler` and
  `cancelling_one_item_keeps_the_submission_of_another_with_the_same_key` (in-memory, SQLite, PostgreSQL, MongoDB, SQL
  Server); `the_handles_of_two_items_with_the_same_key_report_their_own_outcomes` (the same tiers);
  `a_cancellation_that_meets_a_requeue_already_landed_still_cancels`,
  `a_cancellation_landing_on_the_completion_stops_the_chain`,
  `a_cancellation_between_a_completion_and_its_successor_stops_the_chain` and
  `a_settle_whose_answer_is_lost_still_advances_the_chain` (SQLite, PostgreSQL, MongoDB, SQL Server; the in-memory
  ledger takes each write whole, so they skip there); `a_cancellation_survives_the_owners_retry`,
  `a_reaper_reclaim_never_lands_over_a_renewal` and `a_reaped_job_asked_to_cancel_settles` (in-memory and SQLite);
  `a_cancellation_survives_a_failed_attempt` and `the_reaper_never_overwrites_a_renewal` (SQLite). The guards
  `lost_renewal_answers_never_cost_the_settlement`, `an_own_progress_write_that_never_landed_never_costs_the_settlement`,
  `a_submission_after_a_cancellation_runs`, `a_trailing_submission_after_cancelling_a_running_job_runs`,
  `a_chain_step_runs_once_when_a_stalled_run_and_its_reclaim_both_finish` and
  `a_losing_serve_key_write_back_never_leaves_the_claim_running_unexecuted` pass on both revisions;
  `a_stored_claim_reads_back_exactly_as_written` compares times to the millisecond, because MongoDB keeps no finer and
  the tick-exact form failed there on both.
- Round six, ported from the reviewer's probes or written for its decisions, each failing against the round-five code
  on SQLite, PostgreSQL, MongoDB and SQL Server and skipping on the in-memory ledger, which takes each write whole:
  `a_cancel_whose_mark_loses_to_the_completion_still_stops_the_chain`,
  `a_stage_recorded_failed_under_abort_never_releases_its_successor`,
  `a_failing_serve_key_write_back_never_runs_ahead_of_the_deferral`,
  `a_claim_write_failure_surfaces_for_keyed_and_plain_jobs_alike` and
  `a_release_whose_answer_is_lost_still_reports_the_release`; with them, on SQLite,
  `a_continuing_stage_whose_completion_answer_and_next_read_are_lost_still_advances_the_chain`, whose failure settlement
  replaced and deleted the successor of a completion that had landed. The guard
  `a_completion_whose_answer_and_next_read_are_lost_still_advances_the_chain` passes on the round-five code and failed on
  a first form of this round's change, which deleted the successor from a failure settlement that wrote nothing. The
  cancellation spec holds its successor's claim until the canceller returns: left free, that claim outran the
  canceller in three of six runs on SQLite, the window the capability limits name.

The Jobs suites pass on the in-memory tier, SQLite, PostgreSQL, MongoDB and SQL Server, with the Tenancy suite.
