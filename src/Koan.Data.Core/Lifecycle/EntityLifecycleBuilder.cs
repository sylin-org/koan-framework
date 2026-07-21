using Koan.Core.Composition;
using Microsoft.Extensions.DependencyInjection;

namespace Koan.Data.Core.Lifecycle;

/// <summary>Host-composed lifecycle declarations for one entity type.</summary>
public sealed class EntityLifecycleBuilder<TEntity, TKey>
    where TEntity : class
    where TKey : notnull
{
    internal EntityLifecycleBuilder() { }

    public EntityLifecycleBuilder<TEntity, TKey> BeforeLoad(
        Func<EntityLifecycleContext<TEntity>, ValueTask<EntityLifecycleResult>> handler)
    {
        Plan().AddBeforeLoad(handler ?? throw new ArgumentNullException(nameof(handler)));
        return this;
    }

    public EntityLifecycleBuilder<TEntity, TKey> BeforeLoad(
        Func<EntityLifecycleContext<TEntity>, EntityLifecycleResult> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return BeforeLoad(ctx => new ValueTask<EntityLifecycleResult>(handler(ctx)));
    }

    public EntityLifecycleBuilder<TEntity, TKey> AfterLoad(Func<EntityLifecycleContext<TEntity>, ValueTask> handler)
    {
        Plan().AddAfterLoad(handler ?? throw new ArgumentNullException(nameof(handler)));
        return this;
    }

    public EntityLifecycleBuilder<TEntity, TKey> AfterLoad(Action<EntityLifecycleContext<TEntity>> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return AfterLoad(ctx => { handler(ctx); return ValueTask.CompletedTask; });
    }

    public EntityLifecycleBuilder<TEntity, TKey> BeforeUpsert(
        Func<EntityLifecycleContext<TEntity>, ValueTask<EntityLifecycleResult>> handler)
    {
        Plan().AddBeforeUpsert(handler ?? throw new ArgumentNullException(nameof(handler)));
        return this;
    }

    public EntityLifecycleBuilder<TEntity, TKey> BeforeUpsert(
        Func<EntityLifecycleContext<TEntity>, EntityLifecycleResult> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return BeforeUpsert(ctx => new ValueTask<EntityLifecycleResult>(handler(ctx)));
    }

    public EntityLifecycleBuilder<TEntity, TKey> AfterUpsert(Func<EntityLifecycleContext<TEntity>, ValueTask> handler)
    {
        Plan().AddAfterUpsert(handler ?? throw new ArgumentNullException(nameof(handler)));
        return this;
    }

    public EntityLifecycleBuilder<TEntity, TKey> AfterUpsert(Action<EntityLifecycleContext<TEntity>> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return AfterUpsert(ctx => { handler(ctx); return ValueTask.CompletedTask; });
    }

    public EntityLifecycleBuilder<TEntity, TKey> BeforeRemove(
        Func<EntityLifecycleContext<TEntity>, ValueTask<EntityLifecycleResult>> handler)
    {
        Plan().AddBeforeRemove(handler ?? throw new ArgumentNullException(nameof(handler)));
        return this;
    }

    public EntityLifecycleBuilder<TEntity, TKey> BeforeRemove(
        Func<EntityLifecycleContext<TEntity>, EntityLifecycleResult> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return BeforeRemove(ctx => new ValueTask<EntityLifecycleResult>(handler(ctx)));
    }

    public EntityLifecycleBuilder<TEntity, TKey> AfterRemove(Func<EntityLifecycleContext<TEntity>, ValueTask> handler)
    {
        Plan().AddAfterRemove(handler ?? throw new ArgumentNullException(nameof(handler)));
        return this;
    }

    public EntityLifecycleBuilder<TEntity, TKey> AfterRemove(Action<EntityLifecycleContext<TEntity>> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return AfterRemove(ctx => { handler(ctx); return ValueTask.CompletedTask; });
    }

    private static EntityLifecyclePlan<TEntity, TKey> Plan()
    {
        var services = KoanCompositionScope.RequireServices($"{typeof(TEntity).Name}.Lifecycle");
        var existing = services
            .Where(d => d.ServiceType == typeof(EntityLifecyclePlan<TEntity, TKey>))
            .Select(d => d.ImplementationInstance)
            .OfType<EntityLifecyclePlan<TEntity, TKey>>()
            .LastOrDefault();
        if (existing is not null) return existing;

        var plan = new EntityLifecyclePlan<TEntity, TKey>();
        services.AddSingleton(plan);
        services.AddSingleton<IEntityLifecyclePlan>(plan);
        return plan;
    }
}
