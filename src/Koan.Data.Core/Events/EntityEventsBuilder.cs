using System;
using System.Threading.Tasks;

namespace Koan.Data.Core.Events;

/// <summary>
/// Fluent configuration surface for entity lifecycle events.
/// </summary>
public sealed class EntityEventsBuilder<TEntity, TKey>
    where TEntity : class
    where TKey : notnull
{
    internal EntityEventsBuilder()
    {
    }

    public EntityEventsBuilder<TEntity, TKey> Setup(Func<EntityEventContext<TEntity>, ValueTask> handler)
    {
        EntityEventRegistry<TEntity, TKey>.AddSetup(handler ?? throw new ArgumentNullException(nameof(handler)));
        return this;
    }

    public EntityEventsBuilder<TEntity, TKey> Setup(Action<EntityEventContext<TEntity>> handler)
    {
        if (handler == null) throw new ArgumentNullException(nameof(handler));
        return Setup(ctx =>
        {
            handler(ctx);
            return ValueTask.CompletedTask;
        });
    }

    public EntityEventsBuilder<TEntity, TKey> BeforeLoad(Func<EntityEventContext<TEntity>, ValueTask<EntityEventResult>> handler)
    {
        EntityEventRegistry<TEntity, TKey>.AddBeforeLoad(handler ?? throw new ArgumentNullException(nameof(handler)));
        return this;
    }

    public EntityEventsBuilder<TEntity, TKey> BeforeLoad(Func<EntityEventContext<TEntity>, EntityEventResult> handler)
    {
        if (handler == null) throw new ArgumentNullException(nameof(handler));
        return BeforeLoad(ctx => new ValueTask<EntityEventResult>(handler(ctx)));
    }

    public EntityEventsBuilder<TEntity, TKey> AfterLoad(Func<EntityEventContext<TEntity>, ValueTask> handler)
    {
        EntityEventRegistry<TEntity, TKey>.AddAfterLoad(handler ?? throw new ArgumentNullException(nameof(handler)));
        return this;
    }

    public EntityEventsBuilder<TEntity, TKey> AfterLoad(Action<EntityEventContext<TEntity>> handler)
    {
        if (handler == null) throw new ArgumentNullException(nameof(handler));
        return AfterLoad(ctx =>
        {
            handler(ctx);
            return ValueTask.CompletedTask;
        });
    }

    public EntityEventsBuilder<TEntity, TKey> BeforeUpsert(Func<EntityEventContext<TEntity>, ValueTask<EntityEventResult>> handler)
    {
        EntityEventRegistry<TEntity, TKey>.AddBeforeUpsert(handler ?? throw new ArgumentNullException(nameof(handler)));
        return this;
    }

    public EntityEventsBuilder<TEntity, TKey> BeforeUpsert(Func<EntityEventContext<TEntity>, EntityEventResult> handler)
    {
        if (handler == null) throw new ArgumentNullException(nameof(handler));
        return BeforeUpsert(ctx => new ValueTask<EntityEventResult>(handler(ctx)));
    }

    public EntityEventsBuilder<TEntity, TKey> AfterUpsert(Func<EntityEventContext<TEntity>, ValueTask> handler)
    {
        EntityEventRegistry<TEntity, TKey>.AddAfterUpsert(handler ?? throw new ArgumentNullException(nameof(handler)));
        return this;
    }

    public EntityEventsBuilder<TEntity, TKey> AfterUpsert(Action<EntityEventContext<TEntity>> handler)
    {
        if (handler == null) throw new ArgumentNullException(nameof(handler));
        return AfterUpsert(ctx =>
        {
            handler(ctx);
            return ValueTask.CompletedTask;
        });
    }

    public EntityEventsBuilder<TEntity, TKey> BeforeRemove(Func<EntityEventContext<TEntity>, ValueTask<EntityEventResult>> handler)
    {
        EntityEventRegistry<TEntity, TKey>.AddBeforeRemove(handler ?? throw new ArgumentNullException(nameof(handler)));
        return this;
    }

    public EntityEventsBuilder<TEntity, TKey> BeforeRemove(Func<EntityEventContext<TEntity>, EntityEventResult> handler)
    {
        if (handler == null) throw new ArgumentNullException(nameof(handler));
        return BeforeRemove(ctx => new ValueTask<EntityEventResult>(handler(ctx)));
    }

    public EntityEventsBuilder<TEntity, TKey> AfterRemove(Func<EntityEventContext<TEntity>, ValueTask> handler)
    {
        EntityEventRegistry<TEntity, TKey>.AddAfterRemove(handler ?? throw new ArgumentNullException(nameof(handler)));
        return this;
    }

    public EntityEventsBuilder<TEntity, TKey> AfterRemove(Action<EntityEventContext<TEntity>> handler)
    {
        if (handler == null) throw new ArgumentNullException(nameof(handler));
        return AfterRemove(ctx =>
        {
            handler(ctx);
            return ValueTask.CompletedTask;
        });
    }

    internal void Reset() => EntityEventRegistry<TEntity, TKey>.Reset();
}
