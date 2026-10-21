using Koan.Core.Capabilities;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Capabilities;

namespace Koan.Data.Core.Polymorphism;

/// <summary>
/// Exact-variant CRUD view over one root semantic repository. It never selects or constructs a physical adapter.
/// </summary>
internal sealed class EntityVariantRepository<TRoot, TVariant, TKey> :
    IDataRepository<TVariant, TKey>,
    IInsertOnlyRepository<TVariant, TKey>,
    IQueryRepository<TVariant, TKey>,
    IDescribesCapabilities
    where TRoot : class, IEntity<TKey>
    where TVariant : class, TRoot, IEntity<TKey>
    where TKey : notnull
{
    private readonly Func<IDataRepository<TRoot, TKey>> _root;

    public EntityVariantRepository(Func<IDataRepository<TRoot, TKey>> root)
        => _root = root ?? throw new ArgumentNullException(nameof(root));

    public Task EnsureReady(CancellationToken ct = default) => _root().EnsureReady(ct);

    public async Task<TVariant?> Get(TKey id, CancellationToken ct = default)
    {
        var root = _root();
        using var _ = EntityMaterializationScope.Enter(typeof(TRoot), typeof(TVariant));
        return Convert(await root.Get(id, ct).ConfigureAwait(false));
    }

    public async Task<IReadOnlyList<TVariant?>> GetMany(
        IEnumerable<TKey> ids,
        CancellationToken ct = default)
    {
        var root = _root();
        using var _ = EntityMaterializationScope.Enter(typeof(TRoot), typeof(TVariant));
        var values = await root.GetMany(ids, ct).ConfigureAwait(false);
        return values.Select(Convert).ToArray();
    }

    public async Task<MutationResult<TVariant, TKey>> Insert(TVariant model, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(model);
        EntityTypeCatalog.Register(typeof(TVariant));
        if (_root() is not IInsertOnlyRepository<TRoot, TKey> inserts)
            throw new NotSupportedException($"The root repository for {typeof(TVariant).Name} does not support atomic insertion.");
        var result = await inserts.Insert(model, ct).ConfigureAwait(false);
        return new(result.Key, result.Outcome, Convert(result.Entity), result.CommitOutcome);
    }

    public async Task<TVariant> Upsert(TVariant model, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(model);
        EntityTypeCatalog.Register(typeof(TVariant));
        var value = await _root().Upsert(model, ct).ConfigureAwait(false);
        return Convert(value)
            ?? throw new InvalidDataException(
                $"Root repository '{typeof(TRoot).FullName}' returned null after saving '{typeof(TVariant).FullName}'.");
    }

    // NK-015: a variant view shares the root's identity space, so a delete proves membership first: an id that
    // resolves to another variant (or to nothing visible) is not this view's row, exactly as Get refuses it. The
    // check-then-delete residual is the one every keyed facade delete carries (DATA-0124).
    public async Task<bool> Delete(TKey id, CancellationToken ct = default)
    {
        var root = _root();
        return (await Members(root, [id], ct).ConfigureAwait(false)).Count == 1 &&
               await root.Delete(id, ct).ConfigureAwait(false);
    }

    public Task<int> UpsertMany(IEnumerable<TVariant> models, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(models);
        EntityTypeCatalog.Register(typeof(TVariant));
        return _root().UpsertMany(models.Cast<TRoot>(), ct);
    }

    public async Task<int> DeleteMany(IEnumerable<TKey> ids, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ids);
        var root = _root();
        var requested = ids as IReadOnlyList<TKey> ?? ids.ToArray();
        var members = await Members(root, requested, ct).ConfigureAwait(false);
        return members.Count == 0
            ? 0
            : await root.DeleteMany(requested.Where(members.Contains).ToArray(), ct).ConfigureAwait(false);
    }

    /// <summary>The requested ids whose visible root row materializes as this variant.</summary>
    private static async Task<HashSet<TKey>> Members(
        IDataRepository<TRoot, TKey> root,
        IReadOnlyList<TKey> ids,
        CancellationToken ct)
    {
        var members = new HashSet<TKey>();
        if (ids.Count == 0) return members;
        IReadOnlyList<TRoot?> rows;
        using (EntityMaterializationScope.Enter(typeof(TRoot), typeof(TVariant)))
            rows = await root.GetMany(ids.Distinct().ToArray(), ct).ConfigureAwait(false);
        foreach (var row in rows)
            if (row is TVariant variant) members.Add(variant.Id);
        return members;
    }

    public Task<int> DeleteAll(CancellationToken ct = default)
        => throw SetOperation(nameof(DeleteAll));

    public Task<long> RemoveAll(RemoveStrategy strategy, CancellationToken ct = default)
        => throw SetOperation(nameof(RemoveAll));

    public Task<RepositoryQueryResult<TVariant>> Query(
        QueryDefinition query,
        CancellationToken ct = default)
        => throw SetOperation(nameof(Query));

    public Task<CountResult> Count(
        QueryDefinition query,
        CancellationToken ct = default)
        => throw SetOperation(nameof(Count));

    public IBatchSet<TVariant, TKey> CreateBatch()
    {
        var root = _root();
        return new VariantBatch(root, root.CreateBatch());
    }

    public void Describe(ICapabilities caps)
    {
        var root = _root();
        if (root is IDescribesCapabilities)
        {
            var rootCapabilities = DataCaps.Describe(root, typeof(TRoot).Name);
            foreach (var token in rootCapabilities.All)
            {
                if (token == DataCaps.Write.ConditionalReplace || token == DataCaps.Write.ConditionalDelete) continue;
                if (rootCapabilities.Detail<object>(token) is { } detail) caps.Add(token, detail);
                else caps.Add(token);
            }
        }
    }

    private static TVariant? Convert(TRoot? value)
    {
        if (value is null)
        {
            return default;
        }

        if (value is TVariant variant)
        {
            return variant;
        }

        throw new InvalidDataException(
            $"Entity '{value.Id}' belongs to runtime type '{value.GetType().FullName}', not requested variant " +
            $"'{typeof(TVariant).FullName}'.");
    }

    private static NotSupportedException SetOperation(string operation)
        => new(
            $"'{operation}' is a set operation and cannot target variant '{typeof(TVariant).Name}' independently. " +
            $"Use the Entity root '{typeof(TRoot).Name}'.");

    /// <summary>
    /// Queues the builder's operations and replays them into the root batch at Save, so a delete is dispatched only
    /// for a member of this variant (NK-015). Another variant's id reports <see cref="BatchItemOutcome.Missing"/> in
    /// its builder position, the outcome a missing id reports (DATA-0124).
    /// </summary>
    private sealed class VariantBatch(IDataRepository<TRoot, TKey> rootRepository, IBatchSet<TRoot, TKey> root)
        : IBatchSet<TVariant, TKey>
    {
        private readonly List<Action<IBatchSet<TRoot, TKey>>> _operations = [];
        private readonly Dictionary<int, TKey> _deletes = [];

        public BatchExecutionCapabilities ExecutionCapabilities => root.ExecutionCapabilities;

        public IBatchSet<TVariant, TKey> Add(TVariant entity)
            => Queue(batch => batch.Add(entity));

        public IBatchSet<TVariant, TKey> Update(TVariant entity)
            => Queue(batch => batch.Update(entity));

        public IBatchSet<TVariant, TKey> Update(TKey id, Action<TVariant> mutate)
            => Queue(batch => batch.Update(id, entity =>
            {
                if (entity is not TVariant variant)
                {
                    throw new InvalidDataException(
                        $"Batch update '{id}' resolved '{entity.GetType().FullName}', not '{typeof(TVariant).FullName}'.");
                }

                mutate(variant);
            }));

        public IBatchSet<TVariant, TKey> Delete(TKey id)
        {
            _deletes[_operations.Count] = id;
            _operations.Add(batch => batch.Delete(id));
            return this;
        }

        public IBatchSet<TVariant, TKey> Clear()
            => throw SetOperation("Batch.Clear");

        public async Task<BatchResult> Save(
            BatchOptions? options = null,
            CancellationToken ct = default)
        {
            var members = await Members(rootRepository, _deletes.Values.ToArray(), ct).ConfigureAwait(false);
            var native = new int[_operations.Count];
            var dispatched = 0;
            for (var index = 0; index < _operations.Count; index++)
            {
                if (_deletes.TryGetValue(index, out var id) && !members.Contains(id))
                {
                    native[index] = -1;
                    continue;
                }

                _operations[index](root);
                native[index] = dispatched++;
            }

            var result = await root.Save(options, ct).ConfigureAwait(false);
            if (dispatched == _operations.Count || !result.HasCompleteItemOutcomes) return result;
            var byNative = result.Items.ToDictionary(static item => item.Index);
            var items = new BatchItemResult[_operations.Count];
            for (var index = 0; index < _operations.Count; index++)
                items[index] = native[index] < 0
                    ? new BatchItemResult(index, BatchOperation.Delete, BatchItemOutcome.Missing)
                    : byNative[native[index]] with { Index = index };
            return result with { Items = items };
        }

        private VariantBatch Queue(Action<IBatchSet<TRoot, TKey>> operation)
        {
            _operations.Add(operation);
            return this;
        }
    }
}
