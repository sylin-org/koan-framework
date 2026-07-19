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

These are semantic tiers, not CLR implementation names. They describe the running host without
claiming provider-fleet certification.

## Authoring and persistence

`IKoanJob<T>` constrains `T` to `Entity<T>`. The static `Execute` handler receives the mutable work
item and a read-only orchestration snapshot. The coordinator persists the work item before enqueue and
after handler mutation. The ledger stores orchestration state separately as `JobRecord` entities.

Use:

- `item.Job.Submit/Status/Cancel` for one work item;
- `items.Submit` or `Entity.QueryStream(...).Submit` for pointwise source acceptance;
- `Entity.Jobs.Trigger/Query/WithStatus/Cancel` for the type-wide control plane;
- `ctx.Progress` for durable progress;
- one of `ContinueWith`, `StopChain`, `Reschedule`, or `Backoff` to alter the normal settle result.

Calling more than one control signal in a handler fails immediately.

Scalar and source submission converge on one coordinator acceptance operation: resolve policy and
coalescing, persist the work Entity, append the ledger record, then emit a bounded wake hint. A source
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

`JobWakeCoordinator` emits one internal, bounded Communication signal after
a non-transactional submit. The process-local provider is automatic; directly referencing a
Communication connector transparently changes its reach. A dropped or duplicated signal costs at
most one poll interval. Claims, leases, retries, and reclaim behavior remain ledger-owned.

Jobs does not own a transport provider or application-visible message contract. Connector election,
health, wire encoding, and local/network delivery belong exclusively to Communication. The internal
signal carries no job or ambient business context; the claimed ledger record remains the durable,
context-bearing truth.

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
- runtime facts report ledger selection, the wake provider, Communication's framework-signal election,
  the Jobs segmentation realization, and a guarantee statement that names host-trusted restoration,
  shared control-plane ledger, Data-owned state isolation, at-least-once execution, and context-free wake;
- `/health/ready` reports bounded aggregate queue facts in Development and aggregate status in production;
- `JobRecord` queries provide per-work-item transitions, progress, and failure text;
- optional metrics preserve aggregate outcomes beyond ledger retention.

Health inspection is intentionally bounded and does not scan every lane. `QueueAgeWarning` opts into a
degraded signal; the underlying age and depth facts are always returned.

If the ledger becomes unavailable, readiness becomes unhealthy. The worker logs the first failed
iteration at Error, paces retries at `PollInterval`, keeps repeated failures at Debug, and reports one
Information transition when it recovers. Health is the persistent operator signal; repeated Error lines
are not.

## Unsupported claims

Current focused evidence covers the core/in-process suite and SQLite-backed durable behavior. It does
not certify every database adapter, clock-skew envelope, multi-region topology, every Communication connector,
upgrade path, or exactly-once external effect. See the V1 capability ledger before making broader
support claims.

Streaming bounds application memory, not ledger size or lifecycle cost. Very large or unbounded
sources should model a cursor/window/conveyor as the job rather than minting one job per row.
