---
type: REFERENCE
domain: jobs
title: "Background work and communication"
audience: [developers, operators, architects, ai-agents]
status: current
last_updated: 2026-07-22
framework_version: v0.20.0
validation:
  date_last_tested: 2026-07-22
  status: verified
  scope: jobs, Entity events, snapshot transport, and connector selection
---

# Background work and communication

Use this pillar when something must happen after the current call, on a schedule, after a retry, or
outside the current process. Choose the business intent first; queues and transports are implementation
details contributed by referenced packages.

## Choose the right intent

| Need | Application expression | Owner |
|---|---|---|
| Run retryable or scheduled work | `job.Job.Submit()` | [Jobs](../jobs/index.md) |
| State that a typed business occurrence happened | `entity.Events.Raise<TEvent>()` | [Communication](../communication/index.md) |
| Distribute the Entity snapshot currently held | `entity.Transport.Send()` | [Communication](../communication/index.md) |
| Change physical reach | reference a qualifying connector | connector package README |

Do not turn every background action into an event, every event into a job, or every delivery into an
application-owned queue. Jobs own execution state and retry policy. Entity events own a named business
occurrence. Transport owns delivery of the current Entity snapshot.

## Guarantee and correction

The local runtime is the zero-infrastructure floor. Referenced connectors can add durable or remote
reach without changing the application grammar, but only after their capabilities and configuration
qualify them for the requested intent. Unsupported durability, framework-signal, ordering, or
settlement requirements reject or remain explicitly local; Koan does not silently claim a stronger
delivery guarantee.

Jobs are at-least-once, so handlers must be idempotent. Communication acceptance is distinct from
transport settlement. Inspect the job ledger, delivery receipt, readiness, and runtime facts rather
than inferring completion from method return alone.

## Operate the result

- Use `/health/ready` for participating connectors and durable job dependencies.
- Read startup and runtime facts for the elected job persistence and Communication route.
- Keep external brokers and databases in the application's existing deployment topology.
- Treat connector-specific ordering, redelivery, acknowledgement, and outage behavior as part of the
  connector contract.

## Continue

- [Jobs](../jobs/index.md) — model, submit, schedule, retry, and inspect work.
- [Communication](../communication/index.md) — raise occurrences and send Entity snapshots.
- [Troubleshooting](../../support/troubleshooting.md) — correct composition and dependency failures.
