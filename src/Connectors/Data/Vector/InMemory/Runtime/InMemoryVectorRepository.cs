using System.Collections;
using System.Globalization;
using System.Numerics.Tensors;
using System.Text;
using Koan.Core.Capabilities;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Filtering;
using Koan.Data.Vector.Abstractions;
using Koan.Data.Vector.Abstractions.Capabilities;
using Koan.Data.Vector.Naming;

namespace Koan.Data.Vector.Connector.InMemory;

internal sealed class InMemoryVectorRepository<TEntity, TKey> :
    IVectorSearchRepository<TEntity, TKey>,
    IDescribesCapabilities
    where TEntity : class, IEntity<TKey>
    where TKey : notnull
{
    private readonly IServiceProvider _services;
    private readonly InMemoryVectorAdapterFactory _factory;
    private readonly VectorSpacePlan _plan;
    private readonly InMemoryVectorStoreCatalog _stores;
    private readonly InMemoryVectorOptions _options;

    public InMemoryVectorRepository(
        IServiceProvider services,
        InMemoryVectorAdapterFactory factory,
        VectorSpacePlan plan,
        InMemoryVectorStoreCatalog stores,
        InMemoryVectorOptions options)
    {
        _services = services;
        _factory = factory;
        _plan = plan;
        _stores = stores;
        _options = options;
    }

    public void Describe(ICapabilities capabilities) => capabilities
        .Add(VectorCaps.Knn)
        .Add(VectorCaps.Filters, FilterSupport.Full)
        .Add(VectorCaps.BulkUpsert)
        .Add(VectorCaps.BulkDelete)
        .Add(VectorCaps.ScoreNormalization)
        .Add(VectorCaps.DynamicCollections);

    public Task Save(VectorPoint<TKey> point, VectorScope scope, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        Validate(point);
        Store().Save(point, scope.Identity);
        return Task.CompletedTask;
    }

    public Task<BatchResult<TKey>> Save(
        IReadOnlyList<VectorPoint<TKey>> points,
        VectorScope scope,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(points);
        for (var index = 0; index < points.Count; index++)
        {
            ct.ThrowIfCancellationRequested();
            Validate(points[index]);
        }
        return Task.FromResult(Store().Save(points, scope.Identity, ct));
    }

    public Task<VectorPoint<TKey>?> Get(TKey id, VectorScope scope, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(Store().Get(id, scope));
    }

    public Task<IReadOnlyList<VectorPoint<TKey>?>> Get(
        IReadOnlyList<TKey> ids,
        VectorScope scope,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ids);
        var result = new VectorPoint<TKey>?[ids.Count];
        var store = Store();
        for (var index = 0; index < ids.Count; index++)
        {
            ct.ThrowIfCancellationRequested();
            result[index] = store.Get(ids[index], scope);
        }
        return Task.FromResult<IReadOnlyList<VectorPoint<TKey>?>>(result);
    }

    public Task<bool> Delete(TKey id, VectorScope scope, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(Store().Delete(id, scope));
    }

    public Task<BatchResult<TKey>> Delete(
        IReadOnlyList<TKey> ids,
        VectorScope scope,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ids);
        return Task.FromResult(Store().Delete(ids, scope, ct));
    }

    public Task<VectorSearchResult<TKey>> Search(
        VectorSearchRequest request,
        VectorScope scope,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateEmbedding(request.Embedding.Span, nameof(request.Embedding));
        if (request.Top <= 0) throw new ArgumentOutOfRangeException(nameof(request.Top));
        if (!string.Equals(request.Space, _plan.Name, StringComparison.Ordinal))
            throw new InvalidOperationException(
                $"Vector request targets space '{request.Space}', but this repository is bound to '{_plan.Name}'.");
        if (request.Text is not null || request.SemanticWeight is not null)
            throw new NotSupportedException("InMemory Vector does not simulate lexical or hybrid search.");
        if (request.Continuation is not null)
            throw new NotSupportedException("InMemory Vector does not claim a snapshot continuation contract.");

        var snapshot = Store().Snapshot(scope.Identity);
        var predicate = request.Filter is null ? null : DictionaryFilterEvaluator.Compile(request.Filter);
        var ranked = new List<Ranked>(snapshot.Length);
        var considered = 0;
        for (var index = 0; index < snapshot.Length; index++)
        {
            if ((index & 63) == 0) ct.ThrowIfCancellationRequested();
            var point = snapshot[index];
            if (predicate is not null && !predicate(point.MetadataBag)) continue;
            considered++;
            var similarity = Similarity(request.Embedding.Span, point.Embedding);
            if (request.MinimumSimilarity is not null && similarity < request.MinimumSimilarity.Value) continue;
            ranked.Add(new Ranked(point.Id, similarity, point.Metadata));
        }

        ranked.Sort(static (left, right) =>
        {
            var similarity = right.Similarity.CompareTo(left.Similarity);
            return similarity != 0
                ? similarity
                : StringComparer.Ordinal.Compare(StableId(left.Id), StableId(right.Id));
        });
        var count = Math.Min(request.Top, ranked.Count);
        var items = new VectorMatch<TKey>[count];
        for (var index = 0; index < count; index++)
        {
            var item = ranked[index];
            items[index] = new VectorMatch<TKey>(item.Id, item.Similarity, VectorMetadata.Clone(item.Metadata));
        }
        return Task.FromResult(new VectorSearchResult<TKey>(
            Array.AsReadOnly(items),
            null,
            new VectorSearchExecution(_plan.Metric, VectorSearchAccuracy.Exact, considered)));
    }

    public Task Clear(VectorScope scope, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        Store().Clear(scope, ct);
        return Task.CompletedTask;
    }

    public Task Sync(VectorScope scope, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }

    public Task VectorEnsureCreated(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        _ = Store();
        return Task.CompletedTask;
    }

    private InMemoryVectorStore<TKey> Store()
    {
        var container = VectorAdapterNaming.GetOrCompute<TEntity>(_services, _factory, _plan.Source);
        var route = string.Concat(container, "\u001f", _plan.Name);
        return _stores.GetOrAdd<TKey>(route, _options.MaxPointsPerSpace);
    }

    private void Validate(VectorPoint<TKey> point)
    {
        ArgumentNullException.ThrowIfNull(point);
        ValidateEmbedding(point.Embedding.Span, nameof(point.Embedding));
        var bytes = MetadataBytes(point.Metadata);
        if (bytes > _options.MaxMetadataBytesPerPoint)
            throw new InvalidOperationException(
                $"Vector metadata uses approximately {bytes} bytes; InMemory is bounded to {_options.MaxMetadataBytesPerPoint} bytes per point. " +
                "Reduce metadata or increase Koan:Data:Vector:InMemory:MaxMetadataBytesPerPoint.");
    }

    private void ValidateEmbedding(ReadOnlySpan<float> embedding, string parameter)
    {
        if (embedding.Length != _plan.Dimensions)
            throw new ArgumentException(
                $"Vector embedding has {embedding.Length} dimensions; space '{_plan.Name}' requires {_plan.Dimensions}.",
                parameter);
        for (var index = 0; index < embedding.Length; index++)
            if (!float.IsFinite(embedding[index]))
                throw new ArgumentException($"Vector embedding contains a non-finite value at index {index}.", parameter);
    }

    private double Similarity(ReadOnlySpan<float> query, ReadOnlySpan<float> candidate) => _plan.Metric switch
    {
        VectorMetric.Cosine => NormalizeCosine(TensorPrimitives.CosineSimilarity(query, candidate)),
        VectorMetric.Euclidean => NormalizeDistance(query, candidate),
        VectorMetric.DotProduct => NormalizeDot(TensorPrimitives.Dot(query, candidate)),
        _ => throw new InvalidOperationException($"Unsupported Vector metric '{_plan.Metric}'.")
    };

    private static double NormalizeCosine(float cosine) =>
        float.IsNaN(cosine) ? 0.5 : Math.Clamp((cosine + 1d) / 2d, 0d, 1d);

    private static double NormalizeDistance(ReadOnlySpan<float> left, ReadOnlySpan<float> right)
    {
        double sum = 0;
        for (var index = 0; index < left.Length; index++)
        {
            var delta = (double)left[index] - right[index];
            sum += delta * delta;
        }
        return 1d / (1d + Math.Sqrt(sum));
    }

    private static double NormalizeDot(float dot)
    {
        if (dot >= 0)
        {
            var exp = Math.Exp(-dot);
            return 1d / (1d + exp);
        }
        var negativeExp = Math.Exp(dot);
        return negativeExp / (1d + negativeExp);
    }

    private static int MetadataBytes(object? value) => value switch
    {
        null => 0,
        string text => Encoding.UTF8.GetByteCount(text),
        byte[] bytes => bytes.Length,
        DataObject data => data.Properties.Sum(static property =>
            Encoding.UTF8.GetByteCount(property.Name) + MetadataBytes(property.Value)),
        DataArray array => array.Items.Sum(MetadataBytes),
        _ => 16
    };

    private static string StableId(TKey id) => id switch
    {
        string text => text,
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture) ?? string.Empty,
        _ => id.ToString() ?? string.Empty
    };

    private sealed record Ranked(TKey Id, double Similarity, DataObject? Metadata);
}

internal sealed class InMemoryVectorStore<TKey> where TKey : notnull
{
    private readonly object _gate = new();
    private readonly Dictionary<(string Scope, TKey Id), StoredPoint> _points = new();
    private readonly int _capacity;

    public InMemoryVectorStore(int capacity) => _capacity = capacity;

    public MutationOutcome Save(VectorPoint<TKey> point, string scope)
    {
        var stored = StoredPoint.From(point);
        lock (_gate)
        {
            var key = (scope, point.Id);
            var exists = _points.ContainsKey(key);
            if (!exists && _points.Count >= _capacity) throw Capacity();
            _points[key] = stored;
            return exists ? MutationOutcome.Updated : MutationOutcome.Inserted;
        }
    }

    public BatchResult<TKey> Save(IReadOnlyList<VectorPoint<TKey>> points, string scope, CancellationToken ct)
    {
        lock (_gate)
        {
            var newKeys = points.Select(point => (scope, point.Id)).Distinct().Count(key => !_points.ContainsKey(key));
            if (_points.Count + newKeys > _capacity) throw Capacity();
            var items = new BatchItemResult<TKey>[points.Count];
            for (var index = 0; index < points.Count; index++)
            {
                ct.ThrowIfCancellationRequested();
                var point = points[index];
                var key = (scope, point.Id);
                var exists = _points.ContainsKey(key);
                _points[key] = StoredPoint.From(point);
                items[index] = new BatchItemResult<TKey>(
                    index,
                    point.Id,
                    exists ? MutationOutcome.Updated : MutationOutcome.Inserted);
            }
            return new BatchResult<TKey>(items, BatchAtomicity.NotGuaranteed);
        }
    }

    public VectorPoint<TKey>? Get(TKey id, VectorScope scope)
    {
        lock (_gate)
            return _points.TryGetValue((scope.Identity, id), out var point) && Matches(point, scope.Predicate)
                ? point.ToPoint()
                : null;
    }

    public bool Delete(TKey id, VectorScope scope)
    {
        lock (_gate)
        {
            var key = (scope.Identity, id);
            if (!_points.TryGetValue(key, out var point) || !Matches(point, scope.Predicate)) return false;
            return _points.Remove(key);
        }
    }

    public BatchResult<TKey> Delete(IReadOnlyList<TKey> ids, VectorScope scope, CancellationToken ct)
    {
        lock (_gate)
        {
            var items = new BatchItemResult<TKey>[ids.Count];
            for (var index = 0; index < ids.Count; index++)
            {
                ct.ThrowIfCancellationRequested();
                var id = ids[index];
                var key = (scope.Identity, id);
                var deleted = _points.TryGetValue(key, out var point) &&
                              Matches(point, scope.Predicate) &&
                              _points.Remove(key);
                items[index] = new BatchItemResult<TKey>(
                    index,
                    id,
                    deleted ? MutationOutcome.Deleted : MutationOutcome.Missing);
            }
            return new BatchResult<TKey>(items, BatchAtomicity.NotGuaranteed);
        }
    }

    public StoredPoint[] Snapshot(string scope)
    {
        lock (_gate)
            return _points
                .Where(entry => string.Equals(entry.Key.Scope, scope, StringComparison.Ordinal))
                .Select(static entry => entry.Value)
                .ToArray();
    }

    public void Clear(VectorScope scope, CancellationToken ct)
    {
        lock (_gate)
        {
            var keys = _points
                .Where(entry => string.Equals(entry.Key.Scope, scope.Identity, StringComparison.Ordinal) &&
                                Matches(entry.Value, scope.Predicate))
                .Select(static entry => entry.Key)
                .ToArray();
            foreach (var key in keys)
            {
                ct.ThrowIfCancellationRequested();
                _points.Remove(key);
            }
        }
    }

    private static bool Matches(StoredPoint point, Filter? predicate) =>
        predicate is null || DictionaryFilterEvaluator.Compile(predicate)(point.MetadataBag);

    private InvalidOperationException Capacity() => new(
        $"InMemory Vector space reached its configured limit of {_capacity} points. " +
        "Delete points, use another source/partition, or increase Koan:Data:Vector:InMemory:MaxPointsPerSpace.");

    internal sealed record StoredPoint(
        TKey Id,
        float[] Embedding,
        DataObject? Metadata,
        IReadOnlyDictionary<string, object?> MetadataBag)
    {
        public static StoredPoint From(VectorPoint<TKey> point) => new(
            point.Id,
            point.Embedding.ToArray(),
            VectorMetadata.Clone(point.Metadata),
            ToBag(point.Metadata));

        public VectorPoint<TKey> ToPoint() => new(
            Id,
            new ReadOnlyMemory<float>(Embedding.ToArray()),
            VectorMetadata.Clone(Metadata));

        private static IReadOnlyDictionary<string, object?> ToBag(DataObject? data)
        {
            if (data is null) return EmptyBag;
            var result = new Dictionary<string, object?>(data.Properties.Count, StringComparer.Ordinal);
            foreach (var property in data.Properties)
                if (!result.TryAdd(property.Name, BagValue(property.Value)))
                    throw new InvalidOperationException(
                        $"Vector metadata property '{property.Name}' is duplicated. Metadata filter names must be unambiguous.");
            return result;
        }

        private static object? BagValue(object? value) => value switch
        {
            DataObject data => ToBag(data),
            DataArray array => array.Items.Select(BagValue).ToArray(),
            byte[] bytes => bytes.ToArray(),
            _ => value
        };

        private static readonly IReadOnlyDictionary<string, object?> EmptyBag =
            new Dictionary<string, object?>(StringComparer.Ordinal);
    }
}
