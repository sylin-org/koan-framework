---
id: JOBS-0010
slug: jobs-never-starve-and-never-lose-an-update
domain: Jobs
status: Accepted
date: 2026-10-30
title: The worker never waits on its own work, and Jobs never writes over a change it did not make
related:
  - JOBS-0005
  - JOBS-0011
  - JOBS-0006
  - JOBS-0009
  - DATA-0131
---

# JOBS-0010: The worker never waits on its own work, and Jobs never writes over a change it did not make

Amends [JOBS-0005](JOBS-0005-job-orchestrator-rebuild.md) §6 (`@continuous`), §8 (the in-memory tier), §17.1
(conditional auto-save) and §19.3 (retention), [JOBS-0006](JOBS-0006-trailing-edge-coalesce-semantics.md) (what a
submission coalesces onto) and [JOBS-0009](JOBS-0009-wake-stamp-and-lease-heartbeat.md) Decision A (the one-row wake
stamp). The rest of those records stands.

> **Amended by [JOBS-0011](JOBS-0011-a-job-that-defers-itself-keeps-what-it-changed.md) (2026-11-10):** settle writes
> back what a handler changed when it returns having asked to `Reschedule` or `Backoff` too, before the deferral.

## Context

What an application meets from Jobs is short: a long job does not stop its node's heartbeat, reaper, scheduler or
sweep; a pump does not run away; a job does not undo what another writer did to its work item; a read does not load a
whole set; a duplicate submit does not run a second job; and a job is never lost. Jobs are at-least-once, so a job
running twice or being retried is the contract, not a defect.

Every write to a job row costs the application a store round trip, and the owner of a running job writes its row on
every lease renewal. A defence against a rare interleaving is paid on all of those writes, and in reads, indexes and
concepts the application has to live with. So this record names one concurrency mechanism and accepts the races it
leaves, listed below, where they need a process death or a rare interleaving and the worst outcome is a job running
twice or retried.

## Decision

1. **Every worker duty runs on its own timer.** After boot recovery the worker runs the roster heartbeat
   (`WorkerHeartbeatInterval`), the reaper (`ReaperInterval`), the scheduler (`PollInterval`), the archival sweep
   (`ArchiveInterval`) and the metrics flush (`MetricsFlushInterval`) as independent loops; a failing duty never
   pauses another. Beside them the dispatcher wakes on a submission hint, a settled execution, a moved wake stamp or at
   `PollInterval`, claims ready work up to `WorkerConcurrency` and starts each execution as a background task the
   worker owns, then returns to waiting. Executions observe the stopping token and are awaited before the node resigns.
   A duty interval longer than a timer accepts (about 49.7 days) is waited in parts of one day, so any `TimeSpan` works
   and `TimeSpan.MaxValue` means never.
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
   At settle it reads the row and saves it with only the members the handler changed on the reference it was given. A
   removal while the job ran stands. The handler that leaves its reference untouched writes nothing. An item that cannot
   be serialized as Koan stores it is saved whole from the handler's copy.
5. **A member both changed fails the attempt.** A member the handler and another writer changed to different values is
   a conflict: nothing is written, and the attempt fails with a message naming the member, so the job retries on the
   stored item under its `MaxAttempts` and settles as any failure does past them.
6. **One gate key is one row, removed once released.** `JobGateRecord.IdFor(key)` addresses the row; `ReleaseAt` is
   indexed. `IJobLedger.SetGate` reads the row and writes it whole unless its release is already as late.
   `IJobLedger.PurgeReleasedGates(now, limit)` removes at most one page of released gates, and the archival sweep pages
   it.
7. **The wake stamp is sixteen shards written blind.** A durable append writes one shard row, chosen at random, with a
   fresh random version and no read, inside the submission's transaction; probes read every shard in one bounded read
   and compare a fingerprint. This keeps JOBS-0009's invariants — a rolled-back submission moves nothing, a committed
   record implies a moved stamp — while two submitting transactions meet on one row only one time in sixteen. Known next
   step: once Koan.Data offers an after-commit hook, the bump moves off the caller's transaction.
8. **The in-memory ledger keeps settled jobs only briefly, and no read waits for a claim.** It files rows by state:
   queued rows per lane in claim order and, across lanes, the open ones in visible order; running rows with the work
   items and pool members they hold; the coalescing groups of queued jobs; each work item's jobs; the active jobs of
   each work type, counted as rows change state; and settled rows in a history of at most
   `JobsOptions.InMemoryTerminalCapacity` (4096), evicting the job that settled first. A claim walks each lane's due rows
   from its head and stops at the first claimable one. A stored row is never changed in place — every write stores a new
   copy — so a read by id, a status read by work item, a count and a query read without the claim lock. A query that
   names a work item reads that item's jobs; any other scans outside the lock, keeping only the rows up to its page.
9. **Every read is bounded.** `PurgeArchivable`, `PurgeFailed` and `TrimTerminal` take a limit and remove at most one
   page; the sweep pages each until a page falls short, and `TrimTerminal` reads only the keep-th newest row for its
   cutoff. `Stuck` and `Orphaned` return a page and the reaper pages them; the reservation coordinator reads the live
   hands' load through `RunningLoad`. A claim reads the Running rows of the work items in its candidate window and those
   holding a pool member. `JobQuery` carries `Page` and `Size` (default 100) and is read newest first by
   `FirstSubmittedAt`; a work item's status is its newest row, read alone; cancellation pages its queued and running
   jobs. `JobRecord`'s (WorkType, Status) and (WorkType, WorkId) indexes carry `FirstSubmittedAt`, and (Status,
   LastSettledAt) orders the retention pages.
10. **Coalescing works within one work item, on one unique slot.** A key names the work item itself — its identity and
    its data address — besides its type, its action and the properties `[JobIdempotent]` declares, so another item with
    the same properties always gets its own job. Every job of a key carries `JobRecord.CoalesceGroup`, an indexed
    name-based GUID of the key; `IJobLedger.FindCoalescing` finds a queued job of the key with one seek, whether it
    waits for its first run, a retry or a deferral. A new submission holds `JobRecord.CoalesceSlot`, the same GUID under
    a unique index (every other row holds a GUID of its own id; GUIDs, because SQL Server declines to index a string
    key), and `IJobLedger.AppendCoalescing` appends the record holding it, so a concurrent duplicate meets the index and
    coalesces onto the first. A duplicate whose holder was claimed in between appends without the slot; a submission never
    fails on a coalescing race. A claim gives the slot back, so a submission arriving while the job runs queues one
    trailing execution (JOBS-0006). A default job holds its work item exclusively (§17.2), so jobs of one key run one after
    another.
11. **Every write to a job row is a compare on its revision, and that is the only concurrency mechanism.**
    `JobRecord.Revision` takes a fresh value on every ledger write, and `IJobLedger.Update` — a cancellation, a
    reservation release, the reaper's reclaim, every write of a running execution — replaces the row only while it still
    carries the revision its writer read: one conditional replace where the store offers it. The reaper compares on the
    revision its sweep read and never reads again: a row changed since is left to the next sweep.
12. **A running execution writes through its claimed row.** `ClaimedRow` holds the row as the execution last wrote it.
    Its renewals, its progress and its settlement take turns, each a compare on the revision last written, so a live claim
    pays one write and no read. A miss means another writer changed the row. It is read again: while it still runs under
    this claim — this owner at this attempt, which a reclaim and the next claim always change — the change is applied
    onto the row as stored, so a cancellation mark stands and a renewal reads it, cancelling the handler wherever it runs;
    otherwise the execution abandons and writes nothing. A job asked to cancel while it ran settles `Cancelled` instead of
    returning to the queue; a completion or a failure settles as decided. Side effects follow the write.
13. **A chain's next stage is appended before its predecessor settles.** Its id derives from the predecessor's id and
    the stage (`JobRecord.SuccessorId`), so a stage that runs again addresses one row, and a process death between the
    append and the settlement costs a second run, never the chain. A work item's exclusivity holds the next stage back
    until the predecessor leaves Running. A settlement whose row carries a cancellation request cancels the successor.
14. **A cancellation is one pass.** `CancelWorkAsync` marks the work item's running jobs and signals their handlers on
    this node, then cancels its queued jobs, paged. In that order a run marked in time stops its successor itself, and a
    run that settled first has its successor queued for the second read. A write that misses reads the job again, a
    bounded number of times.
15. **A handle tells settled, timed out and unknown apart by the status it carries.** `JobHandle.Completion` returns
    `JobOutcome(Status, Error)`: a terminal status when the job settled in time (`Settled`, and `Succeeded` for
    `Completed`), the status it had reached when the wait ended first, and no status, with the reason in `Error`, when the
    ledger no longer holds the job and nobody here heard it settle — never a failure it was not. A handle reads the
    ledger and also hears a settlement its process makes (`JobCompletions`): the in-memory ledger tells it before its
    history can evict the row; the durable ledger once the write has landed outside a transaction.

## What this accepts

Each of these needs a process death or a rare interleaving, and none loses a job or stalls the store.

- **A job of a key can run twice.** Two transactions that submit one key at once each commit a job, since neither sees
  the other and a deferred write takes no slot. A job returning to the queue beside a trailing submission leaves two
  queued jobs, and the trailing one can run ahead of a deferral. Two nodes that read a work item idle at once can each
  claim one of its queued jobs; exclusivity is read, not enforced by the store. All of them are handled by idempotency.
- **A stage can run again or its successor twice.** A process death between a stage's append and its settlement runs
  the stage again, and a successor that already ran is queued again by that second append.
- **A successor can outlive its predecessor's failure.** A stage whose settlement write failed after its append, and
  whose retry then fails under `OnFailure.Abort`, leaves the successor it appended to run. A `[ParallelSafe]` type's
  successor can start before its predecessor settles.
- **A cancellation can miss one step.** A successor claimed after its predecessor settled and before the canceller
  reads the queued jobs is not stopped; cancelling again stops it.
- **A write to a work item can land inside the settle's window.** A member another writer writes between the settle's
  read and its save, and the handler did not change, is overwritten. The window is one read and one save; a write made
  any other time while the job ran stands.
- **A gate can open early.** Two writers at once can leave the earlier release, and the sweep can remove a gate
  extended between its read and its delete. Jobs then run into the resource and back off again.
- **An outcome can go unannounced.** A settlement whose answer the store lost lands without its event or metric.

## Capability limits

- **Without `ConditionalReplace`, or inside a transaction,** a job row's revision is read and compared just before an
  ordinary save: the at-least-once floor, where a write landing between that read and the save is overwritten.
- **Inside a transaction** no write can be conditional (DATA-0131). Submit writes an absent item by a deferred save
  after a read. A record takes no coalescing slot, so the commit can never fail on one; it coalesces onto a committed
  job of its key or onto one the same transaction submitted.
- **Without `InsertOnly`** (SQL Server today), submit writes an absent item by a read and a save: a creator that lands
  between them is overwritten, and where the store refuses the second save of one new identity (SQL Server), the first
  creator's item stands.
- **A unique index a store cannot build** leaves concurrent duplicates able to queue side by side; the schema validation
  reports the index as unproved. SQLite, PostgreSQL, SQL Server and MongoDB build it.
- **A handler stops cooperatively.** A cancellation marks the row and cancels the token; a handler that ignores the
  token runs to its end, and its settlement still carries the request.

## Compatibility

- **No migrations.** Stores are rebuilt.
- **Public surface.** `IJobLedger` writes rows through `Append`, `AppendMany`, `AppendCoalescing`, `ClaimNext`, `Update`
  and `TryReserve`; `Update` returns whether it wrote. `Stuck`, `Orphaned`, `PurgeArchivable`, `PurgeFailed`,
  `TrimTerminal` and `PurgeReleasedGates` take a limit; `Query` reads the page `JobQuery` names. `JobRecord` carries
  `CoalesceSlot`, `CoalesceGroup`, `SlotFor`, `Revision` and `SuccessorId`. `JobGateRecord` carries `IdFor` and an indexed
  `ReleaseAt`. `WakeStamp` exposes `ShardPrefix`. `JobsOptions` carries `InMemoryTerminalCapacity`. `JobOutcome` is a
  record of `Status` and `Error` with `Settled` and `Succeeded`.
- **Behavior.** `Jobs.Query` and `Jobs.WithStatus` return one page (100 by default), newest first. A caller saves its
  changes to a stored item before submitting. Two different work items with the same `[JobIdempotent]` properties each
  get their own job. A job asked to cancel while it runs settles `Cancelled` even when its handler returns or fails in a
  way that would requeue it.

## Evidence

On every tier unless a spec says otherwise:

- `WorkerLoopSpec.a_job_longer_than_the_death_timeout_completes_while_the_node_heartbeats_and_schedules` (in-memory, real
  worker): the heartbeat lands while the job runs, a schedule fires during it, and a peer's reaper reclaims nothing.
  `WorkerLoopSpec.a_duty_interval_longer_than_a_timer_accepts_still_runs_its_duty`: a sixty-day reaper reaps on day sixty.
- `continuous_pump_writes_one_row_per_settled_run`: ten ticks over one unsettled run write one row.
- `submit_never_overwrites_a_stored_work_item`, `a_change_made_while_the_job_runs_survives_settle`,
  `a_change_behind_a_non_public_setter_survives_settle` and `a_nested_record_the_handler_fills_is_written_back`.
- `a_concurrent_change_behind_a_non_public_setter_is_a_conflict`: nothing is written, and the retry runs on the stored
  item.
- `concurrent_duplicate_submits_coalesce`, `duplicates_submitted_while_their_job_is_claimed_append_or_coalesce_without_failing`,
  `a_deferred_idempotent_job_takes_a_duplicate_and_runs_after_its_deferral` and
  `DurableSqliteSpec.transactional_duplicates_coalesce_like_any_other` (SQLite).
- `a_reaper_reclaim_never_lands_over_a_renewal`, `the_reaper_never_overwrites_a_renewal` (durable tiers),
  `a_zombie_of_the_same_node_neither_renews_nor_settles_over_its_reclaim`, `losing_the_lease_abandons_without_settling`,
  `lost_renewal_answers_never_cost_the_settlement` (durable tiers) and `a_stored_claim_reads_back_exactly_as_written`.
- `a_cancellation_a_renewal_carried_survives_the_owners_deferral`, `a_marker_from_another_node_reaches_the_running_handler`,
  `a_cancellation_survives_a_failed_attempt` (durable tiers), `a_cancellation_that_meets_a_requeue_already_landed_still_cancels`
  (durable tiers), `a_reaped_job_asked_to_cancel_settles`, `a_submission_after_a_cancellation_runs` and
  `a_trailing_submission_after_cancelling_a_running_job_runs`.
- `cancel_at_settle_window_stops_chain`, `a_cancellation_landing_on_the_completion_stops_the_chain`,
  `a_cancellation_between_a_completion_and_its_successor_stops_the_chain`,
  `a_settle_whose_answer_is_lost_still_advances_the_chain` and
  `a_chain_step_runs_once_when_a_stalled_run_and_its_reclaim_both_finish` (durable tiers).
- `released_gates_are_purged_by_the_archival_sweep`; `WakeStampSpec.submitting_transactions_spread_their_bumps_across_shards`
  (SQLite).
- `InMemoryLedgerSpec.settled_jobs_beyond_the_terminal_capacity_are_released`,
  `claims_cost_the_same_beside_a_deep_backlog_that_is_not_due`, `reads_never_wait_for_the_claim_lock`,
  `a_handle_hears_the_outcome_of_a_job_evicted_when_it_settled` and `a_settlement_is_heard_before_the_ledger_evicts_it`.
- `retention_reads_and_removes_one_bounded_page_per_call`, `facade_query_reads_newest_first_one_page_at_a_time` and
  `DurableSqliteSpec.cancelling_inside_a_transaction_pages_through_every_queued_job` (SQLite).
