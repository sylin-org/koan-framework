---
type: REFERENCE
domain: jobs
title: "Background jobs"
audience: [ai-agents, developers]
status: current
last_updated: 2026-08-23
framework_version: v1.0.0
validation:
  date_last_tested: 2026-08-23
  status: passed
  scope: docs/capabilities/work/background-jobs.md - route table cold-verified twice against the run-work-in-background leaf, including restart-survival
---

# Background jobs

Return a receipt now, then let an Entity own retryable or scheduled work whose progress and failure
remain inspectable.

## You need

| Piece | Package | Note |
|---|---|---|
| Job Entity, execution, ledger, retries, and schedules | `Sylin.Koan.Jobs` | implement `IKoanJob<T>` and submit through `.Job` |
| Deterministic execution in tests (optional) | `Sylin.Koan.Jobs.Testing` | drives the production engine without waiting on wall time |
| Durable restart survival | an eligible durable Data connector | the in-memory floor alone cannot survive process loss |

Verified against: `Sylin.Koan.Jobs` 1.0.5 or newer, durable ledger via `Sylin.Koan.Data.Connector.Sqlite` 1.0.10 or newer (patch releases compatible).

## The constraint box

> **The constraint:** Jobs are at-least-once. The handler must be idempotent or detect duplicates;
> scheduling is not exactly-once execution, and staged input must remain available until success so
> a retry or restarted host can read it again.

## Choose the work shape

| Need | Entity expression |
|---|---|
| One retryable action | one `Entity<T>, IKoanJob<T>` with static `Execute` |
| Several named actions on one receipt | `[JobAction]` declarations and `ctx.Action` |
| A scheduled singleton | type-level `.Jobs.Trigger(...)` and schedule policy |
| Several ordered stages | a declared Job chain, only when each stage has a meaningful checkpoint |

## Leaves

- **Build and durability proof:** [run work in background](../../recipes/run-work-in-background.md)
- **Full Entity-shaped contract:** [Jobs guide](../../guides/jobs-howto.md)
- **Runtime contract:** [Jobs reference](../../reference/jobs/index.md)

Tenant context is captured at submission and restored before execution when Tenancy is active. Prove
that boundary whenever a Job reads tenant-owned Data, Storage, or vectors.

With a durable ledger, submissions bump a framework-owned wake-stamp row in the same transaction and
peers discover work at short-probe cadence — no extra infrastructure; a Communication connector makes
wake instant (JOBS-0009). Long-running handlers renew their lease automatically; a handler that loses
its lease abandons without settling rather than racing the new claimant.
