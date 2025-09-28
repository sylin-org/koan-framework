---
id: DATA-0074
slug: data-0074-entity-lifecycle-event-pipeline
domain: DATA
status: Accepted
date: 2025-09-28
---

# ADR DATA-0074: Entity Lifecycle Event Pipeline

Date: 2025-09-28

Status: Accepted

## Context

- Koan entities currently expose rich static CRUD helpers, but lack a unified interception surface for cross-cutting policies (moderation, audit, enrichment) at the model level.
- Teams often duplicate pre/post persistence logic inside controllers or services, leading to inconsistent behavior and harder reuse.
- Batch operations (`UpsertMany`, `DeleteMany`) and streaming adapters require predictable cancellation semantics to avoid partial writes or undetected failures.
- Some providers can supply prior entity snapshots cheaply, while others incur extra I/O; hooks need to request prior state lazily.
- Hooks must allow in-place mutation of the in-flight entity, yet protect framework-managed fields (IDs, concurrency tokens, provider metadata) from accidental tampering.

## Decision

- Introduce a lifecycle event pipeline for `Entity<TEntity, TKey>` covering `Load`, `Upsert`, and `Remove` operations with `Before*` and `After*` hooks.
- Provide fluent, static registration via a dedicated events facade (`Article.Events.Setup(...)`, `Article.Events.BeforeUpsert(...)`, etc.) backed by a thread-safe registry initialized at application startup.
- Define an `EntityEventContext<TEntity>` that exposes:
  - `Current` (mutable reference) and a lazily-loaded `Prior` snapshot via `await context.Prior.Get()`.
  - Operation metadata (operation type, batch identifiers, caller-supplied state bag).
  - Cancellation APIs (`context.Cancel(reason, code)`) returning structured `EntityEventResult` values.
  - Protected-field guardrails through ergonomically named helpers such as `context.Protect("Id")` (preferred after evaluating alternatives like `AssertUnchanged`, `RequireUnchanged`, `Freeze`), with controlled overrides for advanced scenarios.
- Offer a `Setup` stage (accessed via `Article.Events.Setup(...)`) that runs ahead of every operation, enabling shared guardrails (`ctx.ProtectAll()`, `ctx.Protect("Id")`), selective overrides (`ctx.AllowMutation("TimeStamp")`), or contextual metadata before operation-specific hooks execute.
- Allow pre-hooks to mutate `Current`; rerun entity validation (data annotations) after hook execution to ensure invariants hold.
- Support cancelable pre-hooks: cancellation halts the targeted operation and surfaces reasons to the caller; bulk operations default to partial success but honor an optional `EntityBatchOptions.RequireAtomic` flag that aborts the entire batch when set.
- Emit per-entity outcomes (`EntityOutcome`) and aggregate `BatchDisposition` (`PartialSuccess`, `Success`, `Cancelled`) for batch APIs, enabling transactional adapters to respond appropriately.
- Implement consistent ordering guarantees: hooks run in registration order; framework-provided interceptors can specify priority buckets (e.g., `Framework`, `Application`) to avoid race conditions.
- Restrict the initial lifecycle surface to `Load`, `Upsert`, and `Remove`; additional verbs (e.g., query, stream) remain outside scope until real demand emerges.

### Usage examples

**Baseline setup plus simple enrichment**

```csharp
Article.Events
  .Setup(static ctx =>
  {
    ctx.ProtectAll();
    ctx.AllowMutation("TimeStamp");
  })
  .BeforeUpsert(static ctx =>
  {
    ctx.Current.Title = Slug.Normalize(ctx.Current.Title);
    return EntityEventResult.Proceed();
  });
```

**Moderation gate that cancels the write**

```csharp
Article.Events.BeforeUpsert(static async ctx =>
{
  var verdict = await ModerationClient.CheckAsync(ctx.Current.Content, ctx.CancellationToken);
  if (verdict.IsBlocked)
  {
    return ctx.Cancel("Content pending moderation", code: "moderation-blocked");
  }

  return EntityEventResult.Proceed();
});
```

**Diff-aware audit and batch policy**

```csharp
Article.Events.BeforeUpsert(static async ctx =>
{
  var prior = await ctx.Prior.Get();
  if (prior is not null)
  {
    AuditTrail.RecordChange(prior, ctx.Current, ctx.Operation);
  }

  ctx.Operation.RequireAtomic();
  return EntityEventResult.Proceed();
});

Article.Events.AfterRemove(static ctx =>
{
  Cache.Evict(ArticleCacheKeys.ById(ctx.Current.Id));
  return EntityEventResult.Proceed();
});
```

## Consequences

- **Positive:** Centralizes entity lifecycle policies, improves DX, and makes moderation/audit patterns reusable. Enables future modules (soft delete, caching) to plug into the same surface.
- **Positive:** Lazy prior loading keeps hot paths fast while supporting advanced comparisons when needed. Guardrails reduce accidental corruption of framework-managed fields.
- **Negative:** Adds complexity to persistence calls; adapters must invoke the pipeline and respect cancellation signals, increasing implementation effort.
- **Negative:** Static registration requires clear testing utilities to avoid handler leakage between tests; documentation must stress startup-only registration.
- **Risk:** Misconfigured atomic batch settings could mask provider limitations; adapters must clearly signal unsupported modes. Additional perf validation is required to ensure hook overhead remains low.

## References

- DATA-0059 (Entity-first facade and Save semantics)
- Prior design discussion on event pipelines (2025-09-28 planning notes)
