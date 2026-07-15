namespace Koan.Data.Core.Lifecycle;

internal interface IEntityLifecyclePlan
{
    Type EntityType { get; }
    IReadOnlyDictionary<string, int> HandlerCounts { get; }
    void Freeze();
}

/// <summary>
/// One host-owned declaration and execution plan for an entity type. Registration is mutable only
/// while the host is being composed; the first inspection or operation freezes immutable arrays.
/// </summary>
internal sealed class EntityLifecyclePlan<TEntity, TKey> : IEntityLifecyclePlan
    where TEntity : class
    where TKey : notnull
{
    private readonly object _gate = new();
    private List<Func<EntityLifecycleContext<TEntity>, ValueTask<EntityLifecycleResult>>> _beforeLoad = [];
    private List<Func<EntityLifecycleContext<TEntity>, ValueTask>> _afterLoad = [];
    private List<Func<EntityLifecycleContext<TEntity>, ValueTask<EntityLifecycleResult>>> _beforeUpsert = [];
    private List<Func<EntityLifecycleContext<TEntity>, ValueTask>> _afterUpsert = [];
    private List<Func<EntityLifecycleContext<TEntity>, ValueTask<EntityLifecycleResult>>> _beforeRemove = [];
    private List<Func<EntityLifecycleContext<TEntity>, ValueTask>> _afterRemove = [];
    private bool _frozen;

    public Type EntityType => typeof(TEntity);
    public bool HasLoad { get { Freeze(); return _beforeLoad.Count != 0 || _afterLoad.Count != 0; } }
    public bool HasUpsert { get { Freeze(); return _beforeUpsert.Count != 0 || _afterUpsert.Count != 0; } }
    public bool HasRemove { get { Freeze(); return _beforeRemove.Count != 0 || _afterRemove.Count != 0; } }

    public IReadOnlyDictionary<string, int> HandlerCounts
    {
        get
        {
            Freeze();
            return new Dictionary<string, int>(StringComparer.Ordinal)
            {
                ["before-load"] = _beforeLoad.Count,
                ["after-load"] = _afterLoad.Count,
                ["before-upsert"] = _beforeUpsert.Count,
                ["after-upsert"] = _afterUpsert.Count,
                ["before-remove"] = _beforeRemove.Count,
                ["after-remove"] = _afterRemove.Count,
            };
        }
    }

    public void Freeze()
    {
        if (_frozen) return;
        lock (_gate)
        {
            if (_frozen) return;
            _beforeLoad = [.. _beforeLoad];
            _afterLoad = [.. _afterLoad];
            _beforeUpsert = [.. _beforeUpsert];
            _afterUpsert = [.. _afterUpsert];
            _beforeRemove = [.. _beforeRemove];
            _afterRemove = [.. _afterRemove];
            _frozen = true;
        }
    }

    internal void AddBeforeLoad(Func<EntityLifecycleContext<TEntity>, ValueTask<EntityLifecycleResult>> handler) => Add(_beforeLoad, handler);
    internal void AddAfterLoad(Func<EntityLifecycleContext<TEntity>, ValueTask> handler) => Add(_afterLoad, handler);
    internal void AddBeforeUpsert(Func<EntityLifecycleContext<TEntity>, ValueTask<EntityLifecycleResult>> handler) => Add(_beforeUpsert, handler);
    internal void AddAfterUpsert(Func<EntityLifecycleContext<TEntity>, ValueTask> handler) => Add(_afterUpsert, handler);
    internal void AddBeforeRemove(Func<EntityLifecycleContext<TEntity>, ValueTask<EntityLifecycleResult>> handler) => Add(_beforeRemove, handler);
    internal void AddAfterRemove(Func<EntityLifecycleContext<TEntity>, ValueTask> handler) => Add(_afterRemove, handler);

    private void Add<THandler>(List<THandler> handlers, THandler handler)
    {
        lock (_gate)
        {
            if (_frozen)
                throw new InvalidOperationException(
                    $"{typeof(TEntity).Name}.Lifecycle is already active and can no longer be changed. " +
                    "Declare lifecycle behavior while the host is being composed.");
            if (!handlers.Contains(handler)) handlers.Add(handler);
        }
    }

    internal async ValueTask<TEntity> ApplyLoad(TEntity entity, CancellationToken ct)
    {
        Freeze();
        if (!HasLoad) return entity;
        var context = new EntityLifecycleContext<TEntity>(
            entity, EntityLifecycleOperation.Load, EntityLifecyclePrior<TEntity>.Empty, ct);
        await RunBefore(_beforeLoad, context).ConfigureAwait(false);
        await RunAfter(_afterLoad, context).ConfigureAwait(false);
        return context.Current;
    }

    internal async ValueTask<EntityLifecycleContext<TEntity>> BeginUpsert(
        TEntity entity,
        Func<CancellationToken, ValueTask<TEntity?>> prior,
        CancellationToken ct)
    {
        Freeze();
        var context = new EntityLifecycleContext<TEntity>(
            entity, EntityLifecycleOperation.Upsert, new EntityLifecyclePrior<TEntity>(prior), ct);
        await RunBefore(_beforeUpsert, context).ConfigureAwait(false);
        return context;
    }

    internal async ValueTask CompleteUpsert(EntityLifecycleContext<TEntity> context, TEntity persisted)
    {
        context.UpdateCurrent(persisted);
        await RunAfter(_afterUpsert, context).ConfigureAwait(false);
    }

    internal async ValueTask<EntityLifecycleContext<TEntity>> BeginRemove(TEntity entity, CancellationToken ct)
    {
        Freeze();
        var prior = new EntityLifecyclePrior<TEntity>(_ => new ValueTask<TEntity?>(entity));
        var context = new EntityLifecycleContext<TEntity>(entity, EntityLifecycleOperation.Remove, prior, ct);
        await RunBefore(_beforeRemove, context).ConfigureAwait(false);
        return context;
    }

    internal ValueTask CompleteRemove(EntityLifecycleContext<TEntity> context) => RunAfter(_afterRemove, context);

    private static async ValueTask RunBefore(
        IReadOnlyList<Func<EntityLifecycleContext<TEntity>, ValueTask<EntityLifecycleResult>>> handlers,
        EntityLifecycleContext<TEntity> context)
    {
        foreach (var handler in handlers)
        {
            var result = await handler(context).ConfigureAwait(false);
            context.ValidateProtection();
            if (result.IsCancelled)
                throw new EntityLifecycleCancelledException(context.Operation, result.Reason!, result.Code);
        }
    }

    private static async ValueTask RunAfter(
        IReadOnlyList<Func<EntityLifecycleContext<TEntity>, ValueTask>> handlers,
        EntityLifecycleContext<TEntity> context)
    {
        foreach (var handler in handlers)
        {
            await handler(context).ConfigureAwait(false);
            context.ValidateProtection();
        }
    }
}
