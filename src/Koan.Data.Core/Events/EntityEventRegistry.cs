using System;
using System.Threading.Tasks;

namespace Koan.Data.Core.Events;

internal static class EntityEventRegistry<TEntity, TKey>
    where TEntity : class
    where TKey : notnull
{
    private static readonly object Gate = new();

    private static Func<EntityEventContext<TEntity>, ValueTask>[] _setup = Array.Empty<Func<EntityEventContext<TEntity>, ValueTask>>();
    private static Func<EntityEventContext<TEntity>, ValueTask<EntityEventResult>>[] _beforeLoad = Array.Empty<Func<EntityEventContext<TEntity>, ValueTask<EntityEventResult>>>();
    private static Func<EntityEventContext<TEntity>, ValueTask>[] _afterLoad = Array.Empty<Func<EntityEventContext<TEntity>, ValueTask>>();
    private static Func<EntityEventContext<TEntity>, ValueTask<EntityEventResult>>[] _beforeUpsert = Array.Empty<Func<EntityEventContext<TEntity>, ValueTask<EntityEventResult>>>();
    private static Func<EntityEventContext<TEntity>, ValueTask>[] _afterUpsert = Array.Empty<Func<EntityEventContext<TEntity>, ValueTask>>();
    private static Func<EntityEventContext<TEntity>, ValueTask<EntityEventResult>>[] _beforeRemove = Array.Empty<Func<EntityEventContext<TEntity>, ValueTask<EntityEventResult>>>();
    private static Func<EntityEventContext<TEntity>, ValueTask>[] _afterRemove = Array.Empty<Func<EntityEventContext<TEntity>, ValueTask>>();

    public static EntityEventsBuilder<TEntity, TKey> Builder { get; } = new();

    public static bool HasLoadPipeline => _setup.Length > 0 || _beforeLoad.Length > 0 || _afterLoad.Length > 0;

    public static bool HasUpsertPipeline => _setup.Length > 0 || _beforeUpsert.Length > 0 || _afterUpsert.Length > 0;

    public static bool HasRemovePipeline => _setup.Length > 0 || _beforeRemove.Length > 0 || _afterRemove.Length > 0;

    public static ReadOnlyMemory<Func<EntityEventContext<TEntity>, ValueTask>> SetupHandlers => _setup;

    public static ReadOnlyMemory<Func<EntityEventContext<TEntity>, ValueTask<EntityEventResult>>> BeforeLoadHandlers => _beforeLoad;

    public static ReadOnlyMemory<Func<EntityEventContext<TEntity>, ValueTask>> AfterLoadHandlers => _afterLoad;

    public static ReadOnlyMemory<Func<EntityEventContext<TEntity>, ValueTask<EntityEventResult>>> BeforeUpsertHandlers => _beforeUpsert;

    public static ReadOnlyMemory<Func<EntityEventContext<TEntity>, ValueTask>> AfterUpsertHandlers => _afterUpsert;

    public static ReadOnlyMemory<Func<EntityEventContext<TEntity>, ValueTask<EntityEventResult>>> BeforeRemoveHandlers => _beforeRemove;

    public static ReadOnlyMemory<Func<EntityEventContext<TEntity>, ValueTask>> AfterRemoveHandlers => _afterRemove;

    internal static void AddSetup(Func<EntityEventContext<TEntity>, ValueTask> handler) => Add(ref _setup, handler);

    internal static void AddBeforeLoad(Func<EntityEventContext<TEntity>, ValueTask<EntityEventResult>> handler) => Add(ref _beforeLoad, handler);

    internal static void AddAfterLoad(Func<EntityEventContext<TEntity>, ValueTask> handler) => Add(ref _afterLoad, handler);

    internal static void AddBeforeUpsert(Func<EntityEventContext<TEntity>, ValueTask<EntityEventResult>> handler) => Add(ref _beforeUpsert, handler);

    internal static void AddAfterUpsert(Func<EntityEventContext<TEntity>, ValueTask> handler) => Add(ref _afterUpsert, handler);

    internal static void AddBeforeRemove(Func<EntityEventContext<TEntity>, ValueTask<EntityEventResult>> handler) => Add(ref _beforeRemove, handler);

    internal static void AddAfterRemove(Func<EntityEventContext<TEntity>, ValueTask> handler) => Add(ref _afterRemove, handler);

    internal static void Reset()
    {
        lock (Gate)
        {
            _setup = Array.Empty<Func<EntityEventContext<TEntity>, ValueTask>>();
            _beforeLoad = Array.Empty<Func<EntityEventContext<TEntity>, ValueTask<EntityEventResult>>>();
            _afterLoad = Array.Empty<Func<EntityEventContext<TEntity>, ValueTask>>();
            _beforeUpsert = Array.Empty<Func<EntityEventContext<TEntity>, ValueTask<EntityEventResult>>>();
            _afterUpsert = Array.Empty<Func<EntityEventContext<TEntity>, ValueTask>>();
            _beforeRemove = Array.Empty<Func<EntityEventContext<TEntity>, ValueTask<EntityEventResult>>>();
            _afterRemove = Array.Empty<Func<EntityEventContext<TEntity>, ValueTask>>();
        }
    }

    private static void Add<THandler>(ref THandler[] target, THandler handler)
    {
        if (handler is null) throw new ArgumentNullException(nameof(handler));

        lock (Gate)
        {
            var length = target.Length;
            var copy = new THandler[length + 1];
            if (length > 0)
            {
                Array.Copy(target, copy, length);
            }

            copy[length] = handler;
            target = copy;
        }
    }
}
