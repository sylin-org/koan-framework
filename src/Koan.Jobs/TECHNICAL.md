# Koan Jobs Technical Contract

## Composition

`KoanJobsModule` is discovered from the referenced assembly. It calls `AddKoanJobs()`, which registers
one `IJobCoordinator`, worker, scheduler, ledger, transport, and health contributor per host.

Ledger election is capability-driven:

- only in-memory/JSON data factories: `InMemoryJobLedger`;
- any durable data factory: `RoutingJobLedger`, which routes `JobPersistenceMode.InMemory` work to an
  in-memory ledger and `Auto`/`DataStore` work to `DataJobLedger`;
- custom registrations may replace the default interfaces before the host is built.

`JobsCompositionContributor` projects this decision into the shared composition model:

| Subject | Selection | Reason |
|---|---|---|
| `jobs:ledger` | `in-memory` | `no-durable-data-adapter` |
| `jobs:ledger` | `durable-data` | `durable-data-adapter` |
| `jobs:transport` | `in-process` | `default-transport` |
| `jobs:transport` | `custom` | `registered-transport` |

These are semantic tiers, not CLR implementation names. They describe the running host without
claiming provider-fleet certification.

## Authoring and persistence

`IKoanJob<T>` constrains `T` to `Entity<T>`. The static `Execute` handler receives the mutable work
item and a read-only orchestration snapshot. The coordinator persists the work item before enqueue and
after handler mutation. The ledger stores orchestration state separately as `JobRecord` entities.

Use:

- `item.Job.Submit/Status/Cancel` for one work item;
- `Entity.Jobs.Trigger/Query/WithStatus/Cancel` for the type-wide subsystem;
- `ctx.Progress` for durable progress;
- one of `ContinueWith`, `StopChain`, `Reschedule`, or `Backoff` to alter the normal settle result.

Calling more than one control signal in a handler fails immediately.

## Delivery and recovery

The ledger is the queue. Wake transports reduce latency but never carry correctness. A dropped wake
costs at most one poll interval. Claims, leases, retries, and reclaim behavior remain ledger-owned.

## Logical-flow context

- `JobCoordinator` captures the host's `KoanContextCarrierRegistry` exactly once before the first
  await. Batch items share that submission snapshot, and coalescing folds the opaque bag so work from
  distinct context axes cannot collapse together accidentally.
- `JobOrchestrator` restores with `ContextIngressTrust.HostTrusted` before loading the work item or
  invoking its handler. This states that the durable ledger is inside the application's administrative
  trust boundary; it does not claim that opaque syntax is tamper detection.
- A missing value suppresses that registered axis rather than inheriting the worker flow. Unknown axes,
  malformed values, unsupported versions, or insufficient trust settle as
  `DeadReason.CarrierRestoreFailed` before application code.
- Jobs owns capture timing and durable settlement. Each module-owned `IKoanContextCarrier` owns the
  meaning and versioned encoding of its axis; Jobs never names tenant, subject, or another axis.

The contract is at-least-once. A process may stop after an external effect but before settlement, so
handlers must make external effects idempotent or use a business-specific deduplication/outbox boundary.
Koan does not imply cross-provider transactions.

## Inspection

- startup provenance reports the number of discovered job types;
- runtime facts report ledger and transport elections;
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
not certify every database adapter, clock-skew envelope, multi-region topology, messaging transport,
upgrade path, or exactly-once external effect. See the V1 capability ledger before making broader
support claims.
