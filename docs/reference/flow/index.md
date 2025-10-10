---
type: REF
domain: flow
title: "Flow Pillar Reference"
audience: [developers, architects, ai-agents]
status: current
last_updated: 2025-10-09
framework_version: v0.6.3
validation:
  date_last_tested: 2025-10-09
  status: verified
  scope: docs/reference/flow/index.md
---

# Flow Pillar Reference

## Contract

- Inputs: Koan app bootstrapped with `builder.Services.AddKoan()`, entity models, and optional messaging/AI adapters.
- Outputs: Deterministic orchestration via lifecycle hooks, streaming pipelines, and background jobs.
- Error modes: Non-idempotent handlers, retries without backoff, unbounded streams without cancellation, capability mismatches.
- Success criteria: Hooks run once; pipelines scale with backpressure; diagnostics expose stage depth and failures.

### Edge Cases

- Idempotency: Handlers must tolerate retries.
- Backpressure: Limit concurrency/batch sizes for long pipelines.
- Cancellation: Always pass CancellationToken to streaming operations.
- Ordering: Prefer `AllStream`/`QueryStream` with stable ordering on large sets.
- Capabilities: Gate AI/vector usage with provider capability checks.

---

## Core Concepts

- Lifecycle hooks: Before/After Upsert/Delete for policy and enrichment.
- Pipelines: Compose transforms over streams for long-running jobs.
- Background work: Use hosted services for durable processing; avoid per-request orchestration.
- Observability: Emit metrics for stage depth, throughput, retries, and error classes.

### Minimal Hook

```csharp
Product.Events
  .BeforeUpsert(ctx =>
  {
      if (ctx.Current.Price < 0) return ctx.Cancel("Price must be non-negative.");
      return ctx.Proceed();
  })
  .AfterUpsert(ctx => ctx.Proceed());
```

### Streaming Pipeline

```csharp
await foreach (var item in Product.AllStream(ct))
{
    // Enrich and persist in batches to avoid OOM
}
```

### Background Service Skeleton

```csharp
public sealed class EnrichmentWorker : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        await foreach (var item in Product.AllStream(ct))
        {
            // process
        }
    }
}
```

## Diagnostics & Health

- Log hook cancellations with reason codes.
- Track backlog depth, throughput, retry counts, and dead-letter items.
- See Support Troubleshooting Hub for backlog checks.

## Semantic Pipelines

End-to-end enrichment pipelines that stream entities, apply transforms (AI or business rules), and persist results without custom orchestration.

## Pipeline quick start

1. Start from an entity stream (`AllStream`/`QueryStream`).
2. Compose operations with the pipeline DSL (transform, branch, save, notify).
3. Execute with a cancellation token and backpressure-aware batch sizes.

## Flow entities & stages

Stages represent units of work (intake, enriched, failed). Flow records capture progress and errors for observability and retries.

## Adapters & ingestion

Ingest from storage or messaging adapters and push through Flow using unified entity-first APIs.

## Core operations

Map, filter, for-each, save, and notify are the building blocks for pipelines and background jobs.

## Branching & error capture

Use `.Branch(...)` with success/failure paths; record errors and persist outcomes to enable replay.

## Events & messaging

Publish events from lifecycle hooks or pipeline steps to coordinate downstream services.

## Interceptors & lifecycle

Intercept pipeline envelopes for validation and policy; hook Before/After Upsert/Delete for invariants.

## Monitoring & diagnostics

Emit metrics for stage depth, throughput, retries, latency, and error classes; expose health contributors.

## Error handling & retries

Apply retry envelopes for transient failures; dead-letter unrecoverable items and surface actionable diagnostics.

## Related

- Guides: Semantic Pipelines, Performance Optimization
- ADRs: FLOW-0101..0106 (bindings, identity map, toolkit)
- Data: Streaming APIs (`AllStream`, `QueryStream`)