# Koan Jobs Technical Contract

## Composition

`KoanJobsModule` is discovered from the referenced assembly. It calls `AddKoanJobs()`, which registers
one `IJobCoordinator`, worker, scheduler, ledger, wake coordinator, and health contributor per host.

Ledger election is capability-driven:

- only in-memory/JSON data factories: `InMemoryJobLedger`;
- any durable data factory: `RoutingJobLedger`, which routes `JobPersistenceMode.InMemory` work to an
  in-memory ledger and `Auto`/`DataStore` work to `DataJobLedger`;
- custom registrations may replace the default interfaces before the host is built.

`[JobPersistence(DataStore)]` is a required guarantee. If any discovered work type declares it while the host has
no durable Data adapter, ledger composition throws one corrective `InvalidOperationException` naming the affected
types. `Auto` remains the explicit capability-graded default; `InMemory` remains an explicit volatile override.
Built-in ledger, registry, scheduler, selector, and orchestrator implementations are internal host mechanics.

The retained module's `JobsCompositionFacts` projector publishes this decision into the shared composition model:

| Subject | Selection | Reason |
|---|---|---|
| `jobs:ledger` | `in-memory` | `no-durable-data-adapter` |
| `jobs:ledger` | `durable-data` | `durable-data-adapter` |
| `jobs:wake` | elected Communication provider | `ledger-backed-latency-hint` |
| `jobs:dispatch` | `pull-cas` | `default-dispatch-mode` |
| `jobs:dispatch` | `reservation-roster` | `reservation-opt-in` |

These are semantic tiers, not CLR implementation names. They describe the running host without
claiming provider-fleet certification. Cross-node discovery with only a durable Data reference rides
the framework-owned `WakeStamp` sentinel (JOBS-0009): a submission writes one of its sixteen shard rows,
chosen at random and without a read, inside the submission transaction (JOBS-0010), and durable-tier
workers read every shard in one bounded read each `WakeProbeInterval`; the full claim scan still runs at
most one `PollInterval` apart.

Dispatch-mode composition has one corrective boundary (PMC-056): `JobsOptions.DispatchMode.Reservation`
with `JobsOptions.Mode.Inline` throws at orchestrator construction — an inline host executes on the
caller and owns no fleet roster, so reservation cannot be honored and is never silently degraded to pull.

## Authoring and persistence

`IKoanJob<T>` constrains `T` to `Entity<T>`. The static `Execute` handler receives the mutable work
item and a read-only orchestration snapshot. The ledger stores orchestration state separately as
`JobRecord` entities.

Neither submit nor settle overwrites a write another writer made to the work item (JOBS-0010):

- **Submit** writes the work item only when its identity is absent — an atomic insert where the store
  declares `DataCaps.Write.InsertOnly`, a read followed by a save otherwise and inside a transaction,
  where the save is deferred to the commit. A stored item stands as it is.
- **Settle** captures the item as Koan Data stores it — its document, through the same serializer and
  contract persistence writes with, so state behind a non-public setter is included and members the contract
  ignores are not. When the handler returns, only the members it changed on the reference it was given are
  written, onto the row as stored at settle, through a conditional replace whose guard is that row as the
  settle just read it. Each persisted member is compared exactly or left out and named, nothing in between:
  a scalar by value; a set of scalars by its size and by holding each of them; a list only while it holds at
  most one scalar, because Koan's filter language compares neither the order of a longer list nor its
  duplicates; a nested object member by member where the store pushes nested paths. Each member's comparison is
  admitted before anything is written, so one member the store refuses never costs the guard of the others. A
  string, or a list of strings, on a store whose string equality folds case (`FilterSupport.OrdinalStrings` false:
  SQL Server, MySQL) is named rather than compared.
  A member both the handler and another writer changed to different values is a
  conflict: nothing is written, and the job runs again on the stored item after a short delay. A conflict is
  valid work made against a stale item, not a failure: it never consumes an attempt and never advances a
  chain, and past `JobsOptions.MaxWriteConflicts` (5) the job is dead-lettered as `DeadReason.Conflict`. A
  guard miss caused by a concurrent write merges again, up to three reads. A removal while the job ran stands.
- **Capability limits.** A member the store cannot compare exactly — a dictionary, a binary value, a
  classified or managed value, a collection that is null, a list of two or more elements or of objects, a
  nested object on a relational store, which pushes no nested paths — is left out of the guard and named.
  Where the store or the entity's scope cannot replace conditionally (no `ConditionalReplace`, a managed or
  read scope such as row-scoped tenancy, an Entity variant), or the stored values do not round-trip through a
  guard, the merged row is written by an ordinary save. An item that cannot be serialized as Koan stores it is
  written whole from the handler's copy. Each case is logged once per work type and reason at Information and
  every time at Debug, with the writes it can lose: a write to the members left out that lands between the
  settle's read and its write, or, for an item written whole, every write another writer made while the job
  ran.

The snapshot's `State.GateKey` is the gate key captured with THIS execution's claim: the declared or
runtime-resolved gate for ordinary jobs, the pool-elected member for pooled jobs, and null when the
job carries no gate. It is immutable for the rest of this execution, and it is the claim-time
projection: not the work item's current routing and not a later `Backoff` key override. The ledger
record's own gate key is a separate, persisted value that deferral may reconfigure; successors
inherit the record's key at chain time. A handler therefore compares business routing with the
claimed gate without querying its own ledger entry.

### Work address and scheduling placement

The coordinator captures an immutable `EntityContext` before the first asynchronous boundary, together
with the opaque context carrier. Deferred source enumeration and each accepted work item restore that
snapshot. `JobRecord.WorkSource`, `WorkAdapter` and `WorkPartition` persist only the logical data address;
transactions and cache controls are never serialized. An absent address field means the host default
address. Source and adapter selectors are case-normalized; partition spelling is preserved.

The built-in durable ledger clears source, adapter and partition at its I/O boundary, retaining the
submission transaction coordinator. Work persistence, ledger append and wake stamp therefore retain
the existing provider transaction guarantee. Worker execution restores the work address without a
submission transaction. Successors inherit the original record address. Invalid addresses are rejected
before handler execution through the existing context-failure settlement.

Ledger, gates, wake stamp, roster and metrics reside in the default host data store. `IAmbientExempt`
governs managed ambient fields only and does not alter physical routing for other Entity types. Jobs
never discovers or moves ledger records that live outside that store; a deployment that placed records
elsewhere owns reconciling them.

Per-work status/cancel and exclusivity include the full work address. Coalescing folds the address
through `AmbientAxisComposer`, so only submissions that share a work address can coalesce.
`JobQuery` address fields use null as a wildcard and empty string as the default address; records
without an address read as the default address. Explicit gates retain their declared shared resource
identity.

Use:

- `item.Job.Submit/Status/Cancel` for one work item;
- `items.Submit` or `Entity.QueryStream(...).Submit` for pointwise source acceptance;
- `Entity.Jobs.Trigger/Query/WithStatus/Cancel` for the type-wide control plane;
- `ctx.Progress` for durable progress;
- one of `ContinueWith`, `StopChain`, `Reschedule`, or `Backoff` to alter the normal settle result.

Calling more than one control signal in a handler fails immediately.

Scalar and source submission converge on one coordinator acceptance operation: resolve policy and
coalescing, write a new work Entity, append the ledger record, then emit a bounded wake hint.

Coalescing keeps a coalesce key from running twice at once and serves its queued jobs with its next run
(JOBS-0006, JOBS-0010). It never crosses work items: a key names the work item itself — its identity and data
address — besides its type, action and declared properties, so another item with the same properties gets its own
job. Every job of a key carries `JobRecord.CoalesceGroup`, an indexed GUID of the key. A
queued submission holds `JobRecord.CoalesceSlot`, a uniquely indexed GUID of the key (every other row holds a
GUID of its own id): a submission coalesces onto any queued job of its key, found with one seek; a sequential
duplicate writes nothing, and a concurrent duplicate's append meets the unique slot and coalesces onto its
holder — or, when the holder was claimed in between, appends again, so a submission never fails on a coalescing
race. A running job holds `JobRecord.RunSlot`, the key's GUID under a second unique index, taken by the claim's
own write, so a second job of the key cannot be claimed while one runs; only that refusal makes a keyed claim a lost
one, and any other failure of its write surfaces as a plain claim's does. A claim reads past a key that runs and
past a key with a queued job deferred beyond now — the key's work waits for the deferral. A deferral that lands
between a claim's read and its write sends the job back to the queue; when the store keeps refusing that write-back,
the claim is never run: it is left to lapse with its lease and the reaper requeues it. The claimed job
serves every other queued job of its key, settling each Cancelled as coalesced into it. A claim and a
cancellation before running give the coalescing slot back, so a submission arriving while the job runs queues
one trailing execution. A job that returns to the queue — a retry, a deferral, a shutdown requeue, a write
conflict — is one end-of-claim owner write that gives both slots back, so a requeue that loses its claim changes
nothing, and nothing else is written. Every write to a job row is a compare on `JobRecord.Revision`, the revision its
writer read, so a stale copy never lands where the store replaces conditionally.

Each claim writes a fresh `JobRecord.ClaimToken`, and every write of the claim's owner — renewal, progress,
settlement, requeue — applies only while the row carries it, so a zombie execution, on another node or its own, writes
nothing. An owner write reads the row and applies only its own fields onto it, as a compare on that read's revision,
reading again on a miss; a field another writer set, the cancellation request above all, stands. The decision is made
on that merged row: asked to cancel, a requeue settles Cancelled and a completion settles Completed without advancing
its chain, and events, gates and metrics follow the decision after the write. A success is decided on a copy of the
run's row, so a failure that ends its settlement is settled from the row as the run left it. A write whose answer was
lost is recognised by the revision it wrote, which an end-of-claim write stamps on its transition. A cancellation lands
on whatever state the job is in and runs until the work item is quiet: each pass marks the running jobs, then cancels
the queued and held ones, and passes repeat, bounded, until one finds nothing neither settled nor marked; a job found
settled after its mark missed has its successor followed and stopped. A renewal that reads the mark cancels the
handler on its node. The reaper's reclaim compares on the revision its sweep read, never
reading again, drops the token, and settles a job asked to cancel Cancelled.

A run appends its chain successor before it settles, held for it (`JobRecord.HeldBy`, its claim token;
`JobRecord.HeldFor`, its id; `JobRecord.HeldOn`, the settlement it was appended for) under an id derived from both, so
the successor is never claimed, reserved, coalesced onto or absorbed until the run's settlement releases it — or
cancels it, when the settled row carries a cancellation request. A release whose answer was lost is recognised by its
revision. An end of claim that lands deletes the held successor it does not release, as after a failure that aborts;
an end of claim whose write did not land — its claim taken over, or ended by an earlier end of the same claim whose
answer was lost — leaves it to the reaper, and the reaper's reclaim of a run deletes that run's successor. The reaper releases a held successor whose holder claim is gone only when the predecessor settled with
the status it was held on, carries no cancellation request and was settled by that claim (`SettledBy`); otherwise it
deletes it. Inside a transaction the append is deferred and cannot be conditional (DATA-0131),
so the record takes no slot and the commit can never fail on one: it coalesces onto a committed job or onto
one the same transaction submitted. Two transactions that submit one key at once both commit a job; the
first of them claimed serves the other. A source
captures logical context once at the terminal and restores it around deferred enumeration and every
item save. Items are accepted sequentially, preserving source order, multiplicity, one-pass behavior,
and bounded producer memory. Long-running sources wake at bounded intervals; inline mode drains after
each new record.

`JobSubmission` retains counters only. It distinguishes newly submitted records from explicit
idempotency coalesces, reports whether the source ended naturally, and exposes ambient-transaction
enlistment through `PendingCommit`. `JobSubmissionException` and
`JobSubmissionCanceledException` carry that same confirmed prefix. Submission does not promise
collection atomicity, retain per-item handles, or count a provider call that throws as confirmed—even
though a provider-specific side effect at that failing boundary can be intrinsically unknowable.

## Delivery and recovery

The ledger is the queue. A provider-declared Data conditional replace is the durable atomic claim primitive;
an adapter without it retains the documented optimistic at-least-once fallback. There is no user-selected claim
algorithm, clock-skew election window, or claim-ticket store.

`JobWakeCoordinator` emits one internal, bounded Communication signal after a non-transactional
submit. The process-local provider is automatic; directly referencing a Communication connector
transparently changes its reach. Independently of signals, a durable submission writes one shard of
the `WakeStamp` inside the same transaction and durable-tier workers read the shards at
`WakeProbeInterval` (JOBS-0009, JOBS-0010), so peers on any store discover work well under `PollInterval`. A dropped or duplicated
hint costs at most one poll pass. Claims, leases, retries, and reclaim behavior remain ledger-owned.

A claimed job renews its lease on a derived cadence (`LeaseDuration`/3, 100 ms floor) through a
guarded `TryRenewLease` that only extends rows it still owns (JOBS-0009). Failing renewal means
another claimant won: execution cancels and settles nothing, so no write can clobber the new owner.
The renewal loop swallows its own cancellation; transient store errors retry next tick with the
reaper as the bound.

### Worker duties (JOBS-0010)

After boot recovery the worker runs every duty on its own timer: the roster heartbeat at
`WorkerHeartbeatInterval`, the reaper at `ReaperInterval`, the scheduler at `PollInterval`, the archival sweep
at `ArchiveInterval` and, when enabled, the metrics flush at `MetricsFlushInterval`. A non-positive interval
runs at the poll cadence; an interval longer than a timer accepts (about 49.7 days) is waited in parts of one
day, so any `TimeSpan` works and `TimeSpan.MaxValue` means never. Beside them the dispatcher wakes on a submission hint, on a settled execution, on a
moved wake stamp or at `PollInterval`, claims ready work up to `WorkerConcurrency` and starts each execution
as a background task the worker owns; it never waits for one to finish. On stop every execution observes the
stopping token, is re-queued for reclaim, and is awaited before the node resigns from the roster.

The reaper runs beside those executions, so each reclaim is fenced on the owner it read: a row its owner
settled in the meantime keeps that settlement.

`@continuous` re-arms only after the run it last fired has settled and at least one `PollInterval` after that
fire, so a continuous pump writes one ledger row per settled run.

### Bounded reads (JOBS-0010)

No ledger read loads a whole set. Retention removes one page at a time (`PurgeArchivable`, `PurgeFailed`,
`TrimTerminal` and `PurgeReleasedGates` each take a limit) and the sweep pages until a page falls short; the
reaper pages `Stuck` and `Orphaned`; a claim reads the Running rows of the work items in its candidate window
and those holding a pool member; `JobQuery` carries `Page` and `Size` (100 by default) and reads newest first;
a work item's status is its newest row, read alone; cancellation pages a work item's queued and running jobs.

One gate key is one `JobGateRecord` row, addressed by `JobGateRecord.IdFor`, created by an atomic insert and
extended by a conditional replace that applies only while the stored release is earlier. `ReleaseAt` is
indexed, and the archival sweep removes released gates through a conditional delete that keeps a gate
extended meanwhile. Two capability limits apply to SQL Server today, which has neither an atomic insert nor a
conditional delete. A gate that does not exist yet is created by an ordinary save and read back, and a writer
that reads an earlier release than its own extends again; but two writers that create one gate at once can
still leave the earlier of their two releases, when the earlier writer's save lands after the later writer read
its own back, and the gate then opens at that earlier time. And the sweep re-reads each released gate just
before deleting it, so an extension landing between that read and the delete is removed with the gate.

The in-memory ledger files rows by state — queued rows per lane in claim order and, across lanes, the open
ones in visible order; running rows with the work items and pool members they hold; coalescing slots and
groups; each work item's jobs; the active jobs of each work type, counted as rows change state; and settled
rows in a history of at most `JobsOptions.InMemoryTerminalCapacity` (4096), evicting the job that settled
first. A claim walks each lane's due rows from its head. Every write takes one lock, the claim lock, and stores
a new copy of the row rather than changing it in place, so a read by id, a status read by work item, a count
and a query read without that lock and never wait for a claim; a query that names no work item scans outside
the lock, keeping only the rows up to its page.

`JobHandle.Completion` returns one of three cases, told apart by type: `JobOutcome.Settled` with the terminal
status and last error, `JobOutcome.TimedOut` with the status the job had reached, or `JobOutcome.Unknown`. A
handle reads the ledger and also hears a settlement its process makes: the ledger tells the waiting handles
when it writes a settled row — the in-memory ledger before it stores the row, so before its history can evict
it; the durable ledger once the write has landed outside a transaction. A submission coalesced into another
job after it queued settles `Cancelled` with `JobRecord.ServedBy` naming that job; a handle on it follows the
serving job and reports that job's settlement, with `ServedBy` naming it. A handle that asks after the ledger
has forgotten a job nobody here heard settle reports `Unknown`, never a failure.

### Dispatch modality (PMC-056)

Pull/CAS is the only default. `DispatchMode.Reservation` layers an active coordinator over the same
ledger, strictly opt-in:

- Assignment state is ledger-verifiable dispatch metadata on the row itself — `JobRecord.ReservedFor`
  / `ReservedUntil` while Queued. No side queues, no routing tables, no membership primitive of its
  own; assignment writes no transition and raises no lifecycle event.
- Stamp: a guarded narrow write (`IJobLedger.TryReserve`) applying only while the row is Queued and
  unreserved — capability-graded exactly like `TrySettle`. Claim eligibility converges across tiers:
  unreserved / reserved-for-me / lapsed; stamping at claim consumes the cookie.
- Coordinator duty runs on the senior live roster member (oldest alive `StartedAt`, id tie-break) as a
  derived fact of the shared roster — no election protocol exists to split or fail. It self-throttles
  to `WorkerHeartbeatInterval`, releases reservations whose hand is confirmed dead or whose stamp
  lapsed (re-verifying the stored row immediately before each write), then assigns oldest-due work to
  the least-loaded live hand bounded by hands × `WorkerConcurrency` and one scan batch per pass.
- Coordinator loss self-heals: seniority migrates at the death timeout, lapsed stamps re-open rows,
  and pull behavior returns automatically for never-reserved work. In Pull mode the entire machinery
  is inert.

Jobs does not own a transport provider or application-visible message contract. Connector election,
health, wire encoding, and local/network delivery belong exclusively to Communication. The wake
stamp is an ordinary Jobs-owned Entity — carriage needs no adapter surface. Hints carry no job or
ambient business context; the claimed ledger record remains the durable, context-bearing truth.

## Logical-flow context

- `JobsContextPlan` wraps Core's memoized `SegmentationContextPlan`. `JobCoordinator` binds every hard
  obligation and captures exactly once before persistence or the first await. Source items share that
  submission snapshot, and coalescing folds the opaque bag so work from distinct context axes cannot
  collapse together accidentally. Missing required context rejects before work or ledger persistence.
- `JobOrchestrator` restores with `ContextIngressTrust.HostTrusted`, requires every applicable axis,
  and re-binds segmentation before loading the work item or
  invoking its handler. This states that the durable ledger is inside the application's administrative
  trust boundary; it does not claim that opaque syntax is tamper detection.
- A missing value suppresses that registered axis rather than inheriting the worker flow. Unknown axes,
  malformed values, unsupported versions, or insufficient trust settle as
  `DeadReason.CarrierRestoreFailed` before application code.
- Jobs owns capture timing and durable settlement. Each module-owned `IKoanContextCarrier` owns the
  meaning and versioned encoding of its axis; Jobs never names tenant, subject, or another axis.
- The Jobs realization receipt covers submit, coalesce identity, load, execute, settle, retry, and
  chain propagation. The ledger stays host-scoped/shared, Data remains responsible for work-item state
  isolation, and the context-free wake signal never becomes a tenant-routing authority.

The contract is at-least-once. A process may stop after an external effect but before settlement, so
handlers must make external effects idempotent or use a business-specific deduplication/outbox boundary.
Koan does not imply cross-provider transactions.

## Inspection

- startup provenance reports the number of discovered job types;
- runtime facts report ledger selection, the dispatch regime, the wake provider, Communication's
  framework-signal election, the Jobs segmentation realization, and a guarantee statement that names
  host-trusted restoration, shared control-plane ledger, Data-owned state isolation, at-least-once
  execution, and context-free wake;
- `/health/ready` reports bounded aggregate queue facts in Development and aggregate status in production;
- `JobRecord` queries provide per-work-item transitions, progress, and failure text;
- optional `JobMetrics.Summary(...)` reads an internal node-sharded rollup that preserves aggregate
  outcomes beyond ledger retention without exposing the framework persistence row as an application Entity.

Health inspection is intentionally bounded and does not scan every lane. `QueueAgeWarning` opts into a
degraded signal; the underlying age and depth facts are always returned.

If the ledger becomes unavailable, readiness becomes unhealthy. Each worker duty logs its first
failure at Error, retries on its own interval (the dispatcher at `PollInterval`), keeps repeated failures at
Debug, and reports one Information transition when it recovers. Health is the persistent operator signal;
repeated Error lines are not.

## Unsupported claims

Current focused evidence covers the core/in-process suite and SQLite-backed durable behavior. It does
not certify every database adapter, clock-skew envelope, multi-region topology, every Communication connector,
upgrade path, or exactly-once external effect. See the V1 capability ledger before making broader
support claims.

Streaming bounds application memory, not ledger size or lifecycle cost. Very large or unbounded
sources should model a cursor/window/conveyor as the job rather than minting one job per row.
