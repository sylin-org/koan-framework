---
type: REF
domain: data
title: "Entity Lifecycle Events"
audience: [developers, architects, ai-agents]
last_updated: 2025-02-19
framework_version: "v0.2.18+"
status: current
validation: 2025-02-19
---

# Entity Lifecycle Events

## Contract
- **Inputs**: Entities deriving from `Entity<TEntity>` or `Entity<TEntity, TKey>` with lifecycle delegates configured via `TEntity.Events`.
- **Outputs**: Mutated entity instances, cancellation signals, or enriched load results propagated to the calling data facade.
- **Error Modes**: Guard violations throw `InvalidOperationException`; handler exceptions bubble; cancellations return `EntityEventResult.Cancel(reason, code)` to callers.
- **Success Criteria**: Hooks run in deterministic order (Setup → Before → Data → After), respect protection rules, and support batch atomicity across providers.
- **Dependencies**: Requires Koan.Data.Core v0.2.18+; handlers can resolve services through ambient DI when needed.

## Edge Cases to Account For
- New entities do not have a prior snapshot — guard code should check `ctx.Prior.HasValue` before dereferencing.
- Batched deletes may stream across multiple providers; call `ctx.Operation.RequireAtomic()` when you must abort the whole batch on the first failure.
- Multiple handlers can register for the same stage; order is FIFO registration — avoid assumptions about other modules.
- `ProtectAll` blocks revision or identifier updates unless explicitly whitelisted via `AllowMutation`.
- Cancellation should include user-facing reasons; empty strings degrade diagnostics.

## Registering Hooks

Lifecycle hooks are configured through the static `Events` builder on each entity type. Register them during application boot (for example, inside a module initializer or registrar) so they run once per process.

```csharp
public static class ArticleLifecycle
{
    static ArticleLifecycle()
    {
        Article.Events
            .Setup(ctx =>
            {
                ctx.ProtectAll();
                ctx.AllowMutation(nameof(Article.UpdatedAt));
            })
            .BeforeUpsert(async ctx =>
            {
                if (!ctx.Current.IsPublished)
                {
                    return ctx.Proceed();
                }

                var prior = await ctx.Prior.GetAsync(ctx.CancellationToken);
                if (prior is not null && !prior.IsApproved)
                {
                    return ctx.Cancel("Published articles require approval.", code: "article.approval");
                }

                return ctx.Proceed();
            })
            .AfterUpsert(ctx =>
            {
                ctx.Items["audit"] = new ArticleAudit
                {
                    ArticleId = ctx.Current.Id,
                    Message = "Article lifecycle updated"
                };
            });
    }
}
```

### Execution Order
1. **Setup handlers** run once per entity instance before any stage — ideal for protection rules, default values, or shared state preparation.
2. **Before* handlers** can replace the current entity (`ctx.Current = …`), cancel execution, or request atomic batching.
3. The **data operation** executes via the configured provider.
4. **After* handlers** observe the committed value and can emit side effects (logging, queuing) with the assurance that persistence succeeded.

## Mutation Guarding

Protection rules prevent unexpected changes to immutable fields such as identifiers and revision tokens. Use them in setup handlers.

```csharp
Order.Events.Setup(ctx =>
{
    ctx.ProtectAll();
    ctx.AllowMutation(nameof(Order.Status));
    ctx.AllowMutation(nameof(Order.LastSyncedAt));
});
```

Any mutation outside the allowlist throws an `InvalidOperationException` before the provider executes, making drift visible during tests and previews.

## Prior Snapshots

Access the prior value lazily through `ctx.Prior`. Koan loads the original record only when requested, keeping fast paths light.

```csharp
InventoryItem.Events.BeforeUpsert(async ctx =>
{
    var prior = await ctx.Prior.GetAsync(ctx.CancellationToken);
    if (prior is null)
    {
        return ctx.Proceed();
    }

    if (ctx.Current.Quantity < 0)
    {
        return ctx.Cancel("Quantity cannot be negative.", "inventory.negative");
    }

    if (ctx.Current.Quantity - prior.Quantity > 500)
    {
        return ctx.Cancel("Large inventory jumps require approval.", "inventory.jump");
    }

    return ctx.Proceed();
});
```

## Operation State and Atomic Batches

`ctx.Operation` exposes flags shared within the current lifecycle scope.

```csharp
Shipment.Events.BeforeRemove(ctx =>
{
    ctx.Operation.RequireAtomic();

    if (ctx.Current.ManifestLocked)
    {
        return ctx.Cancel("Manifest locked; cancel shipment first.", "shipment.manifest_locked");
    }

    return ctx.Proceed();
});
```

For multi-entity operations (`UpsertMany`, `Remove(IEnumerable<TKey>)`, queries) marking the operation atomic ensures Koan aborts the entire batch when any handler requests cancellation.

## Coordinating Handlers with Items

The `Items` bag is a shared dictionary that flows across handlers. Use it for lightweight cross-stage communication.

```csharp
Payment.Events
    .Setup(ctx => ctx.Items["now"] = DateTimeOffset.UtcNow)
    .AfterUpsert(ctx =>
    {
        if (ctx.Items.TryGetValue("now", out var createdAt) && createdAt is DateTimeOffset timestamp)
        {
            ctx.Current.ProcessedAt ??= timestamp;
        }
    });
```

## Load Enrichment

Load hooks can project additional data into the entity before it reaches callers. This is ideal for read-mostly enrichment like badges or derived flags.

```csharp
Profile.Events.AfterLoad(async ctx =>
{
    var stats = await ProfileStats.Get(ctx.Current.Id, ctx.CancellationToken);
    ctx.Current.Badges = stats?.Badges ?? [];
});
```

Because `AfterLoad` runs after provider retrieval, enrichment does not affect persistence.

## Testing and Resetting

When running unit tests, call `TEntity.Events.Reset()` in `TestInitialize`/`[SetUp]` to remove previously registered handlers and ensure isolation.

```csharp
[TestInitialize]
public void ResetHooks() => TestEntity.Events.Reset();
```

Avoid registering hooks lazily during tests — production code should configure pipelines at startup, and tests should exercise the same code paths.
