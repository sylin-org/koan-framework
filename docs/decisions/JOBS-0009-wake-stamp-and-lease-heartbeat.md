# JOBS-0009: Wake Stamp and Lease Heartbeat

> **Status: Accepted 2026-08-25** · Extends JOBS-0005 §6 (two trigger models) with two completions:
> framework-owned cross-node wake carriage that requires no adapter code, and running jobs that hold
> their lease alive instead of being reclaimable mid-flight.

## 1. Problem

Two gaps survive the JOBS-0005 rebuild when an application scales past one instance:

1. **Cross-node dispatch latency is coupled to infrastructure breadth.** Peers discover new work at
   `PollInterval` unless the host also references a Communication connector for wake signals. The
   obvious fix — store-native push adapters (`LISTEN/NOTIFY`, change streams) — was designed,
   built, and then removed: it put driver-specific implementations into every connector and added
   an election seam to solve a problem the framework can own end-to-end with less.
2. **A lease is stamped once and never renewed.** `LeaseUntil = claim time + JobsOptions.LeaseDuration`
   (default 1 min). A handler that legitimately runs longer than its lease is reclaimed by the reaper
   *while still running*, producing a second concurrent execution of the same attempt — beyond the
   documented at-least-once floor, which speaks about crashes, not about slow-but-alive handlers.

## 2. Forces / principles

- **The ledger decides; every hint merely hurries** (JOBS-0005 §6). No change here may move
  correctness onto a lossy channel.
- **Less but more meaningful parts.** Carriage owned by one pillar beats an interface implemented
  everywhere. If a mechanism can be expressed in the framework's existing vocabulary — Entities,
  ambient transactions, options — it must not become an adapter surface.
- **Reference = Intent.** Cross-node wake must arrive with the durable Data reference alone;
  a broker stays an optional accelerator, never a requirement.
- **Fail open to poll.** Every failure path ends in "the worker will discover work at its next pass."

## 3. Decision A — the WakeStamp sentinel

One fixed row per durable store: `WakeStamp : Entity<WakeStamp>, IAmbientExempt` at id
`koan-jobs-wake`, carrying `Version` + `UpdatedAt`.

- **Bump on durable append.** `DataJobLedger.Append/AppendMany` bump the stamp after writing records,
  inside the same ambient transaction — a rolled-back submission never moves it (outbox-safe), and
  bulk submissions cost exactly one extra write per batch.
- **Cheap probe.** Workers with a durable ledger read the single indexed row every
  `JobsOptions.WakeProbeInterval` (default 250 ms) — negligible next to the full claim scan, which
  now runs only when the stamp moved or when `PollInterval` elapsed since the last full pass
  (the fallback that keeps polling the complete correctness mechanism).
- **In-memory ledgers skip everything**: a volatile tier never crosses nodes, so no bump, no probe.
- **Failed hint writes and reads are swallowed by design** — a missed bump costs one
  `PollInterval`, never work, and can never fail a submission.

## 4. Decision B — lease heartbeat

- `IJobLedger.TryRenewLease(jobId, owner, leaseUntil, now, ct) → bool`: guarded renewal — the row
  moves its `LeaseUntil` forward only while `Status == Running && Owner == owner`. All three tiers
  implement it: in-memory under its lock; durable via the same capability-graded CAS triad as claims
  (`ConditionalReplaceAsync` where available, optimistic verify otherwise); routing probes durable
  then volatile like `Get`.
- The orchestrator runs **one renewal loop per execution**: `PeriodicTimer` on the injected clock,
  interval `max(100ms, LeaseDuration / 3)` (derived — deliberately not a new knob).
- **Lost lease ⇒ abandon, never settle.** If renewal discovers another owner, the loop cancels the
  execution token so the handler stops at its next checkpoint; the catch site recognizes this case
  and writes NOTHING — the row already belongs to another claimant, and any settle would clobber it.
- **Containment rule:** the renewal task swallows its own cancellation and disposal; transient store
  errors are retried next tick; a renewal exception never reaches the handler's catch sites (a stray
  OCE there means shutdown-requeue, which is not the renewal's meaning).

## 5. What was deliberately rejected

Store-native push adapters behind an `IStoreSignalChannel` capability were implemented and then
removed in favor of the stamp. The rejection is recorded so it stays decided:

- Push mechanics are irreducibly driver-specific (`LISTEN/NOTIFY` vs `$changeStream` vs nothing),
  so the design necessarily spread an interface across Abstractions plus per-connector adapters,
  module registrations, and an election helper — more parts for a latency win the stamp delivers
  within one cheap-probe interval.
- Centralizing those drivers anywhere shared would force Npgsql/MongoDB.Driver onto every host.
- Instant dispatch remains available the way it always was: reference a Communication connector and
  the existing signal lane crosses nodes. That is the ladder's second rung, unchanged.

## 6. Invariants preserved

- At-least-once floor unchanged; heartbeat only removes the *spurious* reclaim of alive handlers.
- Poll remains the completeness mechanism for wake, forever; the stamp only shortens discovery.
- Settle remains single-writer; renewal touches `LeaseUntil` under an ownership guard only.
- The stamp participates in the submission transaction, so "record exists ⇒ stamp moved" holds.
- In-memory and durable tiers keep converging behaviorally (ARCH-0079): both renew, both refuse
  foreign renewals.

## 7. Consequences

**Positive** — sub-second cross-node discovery on every durable store with zero adapter code and
zero new interfaces; long-running handlers no longer race the reaper; lost-lease handling stops a
real clobber class; the whole wake mechanism lives in one pillar and reads as one entity.

**Negative / cost** — worst-case peer latency is `WakeProbeInterval` rather than broker-instant
(a Communication connector remains the instant path); one tiny write per append batch; one probe
read per node per interval; the probe must be guarded against hot-looping a broken store (failed
reads fall through to the slow cadence without changing the baseline).

**Risks + telemetry** — probe load scales O(nodes × 4/s) against one indexed row (trivial);
stamp-row retention is moot (single fixed id, overwritten in place).

## 8. Test surface

- Durable submit bumps the stamp once per batch; rolled-back transactions move nothing (outbox).
- In-memory hosts leave no stamp and never probe.
- Probe detects a foreign bump and triggers the drain path ahead of `PollInterval`.
- Renewal keeps a handler alive past the original lease (in-memory tier, fake clock).
- Steal-the-row mid-run: renewal refuses, execution cancels, no ledger write clobbers the new owner.
- Lapsed-lease reclaim still works when no heartbeat runs (dead node).
- Durable-tier renewal honors the ownership guard (SQLite CAS path).

## 9. Implementation status

Shipped with this ADR: the stamp, probe/fallback worker pacing, heartbeat on all tiers, specs above.
Fleet roster, reservation-based assignment, jar topology, SKIP LOCKED strategies, and lifecycle
event projections are pinned separately in the post-cycle register (PMC-055…060).
