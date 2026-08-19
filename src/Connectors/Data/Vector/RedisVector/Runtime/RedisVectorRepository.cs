using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.ComponentModel;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Koan.Core.Capabilities;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Filtering;
using Koan.Data.Abstractions.Sources;
using Koan.Data.Vector.Abstractions;
using Koan.Data.Vector.Abstractions.Capabilities;
using Koan.Data.Vector.Naming;
using StackExchange.Redis;

namespace Koan.Data.Vector.Connector.RedisVector;

internal sealed class RedisVectorRepository<TEntity, TKey> :
    IVectorSearchRepository<TEntity, TKey>,
    IDescribesCapabilities,
    IDisposable,
    IAsyncDisposable
    where TEntity : class, IEntity<TKey>
    where TKey : notnull
{
    private readonly IServiceProvider _services;
    private readonly RedisVectorVectorAdapterFactory _factory;
    private readonly VectorSpacePlan _plan;
    private readonly RedisVectorRoute _route;
    private readonly RedisVectorOptions _options;
    private readonly ConcurrentDictionary<string, NativeShape> _shapes = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _shapeGates = new(StringComparer.Ordinal);
    private int _disposed;

    internal RedisVectorRepository(
        IServiceProvider services,
        RedisVectorVectorAdapterFactory factory,
        VectorSpacePlan plan,
        RedisVectorRoute route,
        RedisVectorOptions options)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        _plan = plan ?? throw new ArgumentNullException(nameof(plan));
        _route = route ?? throw new ArgumentNullException(nameof(route));
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public void Describe(ICapabilities capabilities) => capabilities
        .Add(VectorCaps.Knn)
        .Add(VectorCaps.Filters, RedisVectorFilter.Capabilities)
        .Add(VectorCaps.BulkUpsert)
        .Add(VectorCaps.BulkDelete)
        .Add(VectorCaps.ScoreNormalization)
        .Add(VectorCaps.DynamicCollections);

    public async Task Save(VectorPoint<TKey> point, VectorScope scope, CancellationToken ct = default)
    {
        ThrowIfDisposed();
        var prepared = Prepare(point, scope.Values);
        _route.Policy.Demand(DataOperationEffect.Write, $"save Redis vector point in space '{_plan.Name}'");
        var layout = Layout();
        await EnsureShape(layout, prepared.Projection.Dynamic.Select(static item => item.Key), ct).ConfigureAwait(false);
        _ = await ReplaceHash(layout, prepared, ScopeToken(scope), ct).ConfigureAwait(false);
    }

    public async Task<BatchResult<TKey>> Save(
        IReadOnlyList<VectorPoint<TKey>> points,
        VectorScope scope,
        CancellationToken ct = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(points);
        DemandBatch(points.Count);
        ct.ThrowIfCancellationRequested();
        var prepared = points.Select(point => Prepare(point, scope.Values)).ToArray();
        DemandUnique(prepared.Select(static item => item.StableId), "save");
        if (prepared.Length == 0) return new BatchResult<TKey>([], BatchAtomicity.NotGuaranteed);

        _route.Policy.Demand(DataOperationEffect.Write, $"save Redis vector batch in space '{_plan.Name}'");
        var layout = Layout();
        await EnsureShape(
            layout,
            prepared.SelectMany(static item => item.Projection.Dynamic).Select(static item => item.Key).Distinct(StringComparer.Ordinal),
            ct).ConfigureAwait(false);
        var scopeToken = ScopeToken(scope);
        var batch = _route.Data.CreateBatch();
        var pending = new Task<RedisResult>[prepared.Length];
        for (var index = 0; index < prepared.Length; index++)
        {
            var values = HashValues(prepared[index], scopeToken);
            pending[index] = batch.ScriptEvaluateAsync(
                Infrastructure.Constants.Scripts.ReplaceHash,
                [PointKey(layout, scopeToken, prepared[index].StableId)],
                values);
        }
        batch.Execute();
        var results = await Task.WhenAll(pending).WaitAsync(ct).ConfigureAwait(false);
        return new BatchResult<TKey>(
            prepared.Select((item, index) => new BatchItemResult<TKey>(
                index,
                item.Point.Id,
                (long)results[index] == 0 ? MutationOutcome.Inserted : MutationOutcome.Updated)),
            BatchAtomicity.NotGuaranteed);
    }

    public async Task<VectorPoint<TKey>?> Get(TKey id, VectorScope scope, CancellationToken ct = default)
    {
        ThrowIfDisposed();
        var stableId = Key(id);
        var layout = Layout();
        var shape = await ReadShape(layout, ct).ConfigureAwait(false);
        if (shape is null) return null;
        var scopeToken = ScopeToken(scope);
        if (scope.Predicate is not null &&
            !await IsVisible(layout, shape, stableId, scopeToken, scope.Predicate, ct).ConfigureAwait(false))
            return null;
        return await ReadPoint(layout, id, stableId, scopeToken, ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<VectorPoint<TKey>?>> Get(
        IReadOnlyList<TKey> ids,
        VectorScope scope,
        CancellationToken ct = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(ids);
        DemandBatch(ids.Count);
        ct.ThrowIfCancellationRequested();
        var output = new VectorPoint<TKey>?[ids.Count];
        if (ids.Count == 0) return output;
        var stable = ids.Select(Key).ToArray();
        var layout = Layout();
        var shape = await ReadShape(layout, ct).ConfigureAwait(false);
        if (shape is null) return output;
        var scopeToken = ScopeToken(scope);
        if (scope.Predicate is not null)
        {
            for (var index = 0; index < ids.Count; index++)
            {
                ct.ThrowIfCancellationRequested();
                if (await IsVisible(layout, shape, stable[index], scopeToken, scope.Predicate, ct).ConfigureAwait(false))
                    output[index] = await ReadPoint(layout, ids[index], stable[index], scopeToken, ct).ConfigureAwait(false);
            }
            return output;
        }

        var batch = _route.Data.CreateBatch();
        var pending = stable.Select(value => batch.HashGetAsync(
            PointKey(layout, scopeToken, value),
            [Infrastructure.Constants.Wire.Embedding, Infrastructure.Constants.Wire.Metadata])).ToArray();
        batch.Execute();
        var values = await Task.WhenAll(pending).WaitAsync(ct).ConfigureAwait(false);
        for (var index = 0; index < values.Length; index++)
            output[index] = Materialize(ids[index], values[index]);
        return output;
    }

    public async Task<bool> Delete(TKey id, VectorScope scope, CancellationToken ct = default)
    {
        ThrowIfDisposed();
        var stableId = Key(id);
        _route.Policy.Demand(DataOperationEffect.Write, $"delete Redis vector point from space '{_plan.Name}'");
        var layout = Layout();
        var shape = await ReadShape(layout, ct).ConfigureAwait(false);
        if (shape is null) return false;
        var scopeToken = ScopeToken(scope);
        if (scope.Predicate is not null &&
            !await IsVisible(layout, shape, stableId, scopeToken, scope.Predicate, ct).ConfigureAwait(false))
            return false;
        return await _route.Data.KeyDeleteAsync(PointKey(layout, scopeToken, stableId)).WaitAsync(ct).ConfigureAwait(false);
    }

    public async Task<BatchResult<TKey>> Delete(
        IReadOnlyList<TKey> ids,
        VectorScope scope,
        CancellationToken ct = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(ids);
        DemandBatch(ids.Count);
        ct.ThrowIfCancellationRequested();
        if (ids.Count == 0) return new BatchResult<TKey>([], BatchAtomicity.NotGuaranteed);
        var stable = ids.Select(Key).ToArray();
        DemandUnique(stable, "delete");
        _route.Policy.Demand(DataOperationEffect.Write, $"delete Redis vector batch from space '{_plan.Name}'");
        var layout = Layout();
        var shape = await ReadShape(layout, ct).ConfigureAwait(false);
        if (shape is null) return Missing(ids);
        var scopeToken = ScopeToken(scope);
        var allowed = new bool[ids.Count];
        if (scope.Predicate is null)
            Array.Fill(allowed, true);
        else
            for (var index = 0; index < stable.Length; index++)
                allowed[index] = await IsVisible(
                    layout, shape, stable[index], scopeToken, scope.Predicate, ct).ConfigureAwait(false);

        var batch = _route.Data.CreateBatch();
        var pending = new Task<bool>?[ids.Count];
        for (var index = 0; index < ids.Count; index++)
            if (allowed[index])
                pending[index] = batch.KeyDeleteAsync(PointKey(layout, scopeToken, stable[index]));
        batch.Execute();
        var outcomes = new BatchItemResult<TKey>[ids.Count];
        for (var index = 0; index < ids.Count; index++)
        {
            var deleted = pending[index] is not null &&
                          await pending[index]!.WaitAsync(ct).ConfigureAwait(false);
            outcomes[index] = new BatchItemResult<TKey>(
                index,
                ids[index],
                deleted ? MutationOutcome.Deleted : MutationOutcome.Missing);
        }
        return new BatchResult<TKey>(outcomes, BatchAtomicity.NotGuaranteed);
    }

    public async Task<VectorSearchResult<TKey>> Search(
        VectorSearchRequest request,
        VectorScope scope,
        CancellationToken ct = default)
    {
        ThrowIfDisposed();
        Validate(request);
        var layout = Layout();
        var shape = await ReadShape(layout, ct).ConfigureAwait(false);
        if (shape is null) return EmptyResult();
        var filter = Combine(request.Filter, scope.Predicate);
        shape = await RefreshShapeForFilter(layout, shape, filter, ct).ConfigureAwait(false);
        var scopeToken = ScopeToken(scope);
        await DemandOrderedValues(layout, scopeToken, filter, ct).ConfigureAwait(false);
        var compiled = RedisVectorFilter.Compile(filter, shape.Attributes);
        if (compiled.IsFalse) return EmptyResult();
        var primary = PrimaryFilter(scopeToken, compiled);
        var query = $"({primary})=>[KNN {_options.MaxSearchCandidates} " +
                    $"@{Infrastructure.Constants.Wire.Embedding} $BLOB AS {Infrastructure.Constants.Wire.Distance}]";
        var result = await Execute(
            Infrastructure.Constants.Commands.Search,
            [
                layout.Index,
                query,
                "PARAMS", 2, "BLOB", EmbeddingBytes(request.Embedding.Span),
                "SORTBY", Infrastructure.Constants.Wire.Distance, "ASC",
                "RETURN", 3,
                Infrastructure.Constants.Wire.Id,
                Infrastructure.Constants.Wire.Metadata,
                Infrastructure.Constants.Wire.Distance,
                "LIMIT", 0, _options.MaxSearchCandidates,
                "DIALECT", 2
            ],
            ct).ConfigureAwait(false);
        var matches = ParseSearch(result)
            .OrderBy(static item => item.Distance)
            .ThenBy(static item => item.StableId, StringComparer.Ordinal)
            .Select(item => new VectorMatch<TKey>(
                ParseKey(item.StableId),
                Similarity(item.Distance),
                string.IsNullOrWhiteSpace(item.Metadata) ? null : VectorMetadata.FromJson(item.Metadata)))
            .Where(item => request.MinimumSimilarity is null || item.Similarity >= request.MinimumSimilarity.Value)
            .Take(request.Top)
            .ToArray();
        return new VectorSearchResult<TKey>(
            matches,
            null,
            new VectorSearchExecution(_plan.Metric, VectorSearchAccuracy.Exact, null));
    }

    public async Task Clear(VectorScope scope, CancellationToken ct = default)
    {
        ThrowIfDisposed();
        _route.Policy.Demand(DataOperationEffect.Write, $"clear Redis vector points from space '{_plan.Name}'");
        var layout = Layout();
        var shape = await ReadShape(layout, ct).ConfigureAwait(false);
        if (shape is null) return;
        shape = await RefreshShapeForFilter(layout, shape, scope.Predicate, ct).ConfigureAwait(false);
        var scopeToken = ScopeToken(scope);
        await DemandOrderedValues(layout, scopeToken, scope.Predicate, ct).ConfigureAwait(false);
        var compiled = RedisVectorFilter.Compile(scope.Predicate, shape.Attributes);
        if (compiled.IsFalse) return;
        var query = PrimaryFilter(scopeToken, compiled);
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var result = await Execute(
                Infrastructure.Constants.Commands.Search,
                [layout.Index, query, "NOCONTENT", "LIMIT", 0, _options.MaxBatchPoints, "DIALECT", 2],
                ct).ConfigureAwait(false);
            var keys = ParseDocumentKeys(result);
            if (keys.Count == 0) return;
            var batch = _route.Data.CreateBatch();
            var pending = keys.Select(key => batch.KeyDeleteAsync(key)).ToArray();
            batch.Execute();
            _ = await Task.WhenAll(pending).WaitAsync(ct).ConfigureAwait(false);
        }
    }

    public Task Sync(VectorScope scope, CancellationToken ct = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(scope);
        ct.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }

    public async Task VectorEnsureCreated(CancellationToken ct = default)
    {
        _route.Policy.Demand(DataOperationEffect.SchemaOrAdmin, $"ensure Redis vector space '{_plan.Name}'");
        await EnsureShape(Layout(), [], ct).ConfigureAwait(false);
    }

    public Task Flush(CancellationToken ct = default) => Clear(VectorScope.Unscoped, ct);

    public void Dispose() => Interlocked.Exchange(ref _disposed, 1);

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }

    private PreparedPoint Prepare(VectorPoint<TKey> point, DataObject managedValues)
    {
        ArgumentNullException.ThrowIfNull(point);
        ValidateEmbedding(point.Embedding.Span, "point embedding");
        var metadata = VectorMetadata.ToJson(point.Metadata);
        if (metadata is not null && Encoding.UTF8.GetByteCount(metadata) > _options.MaxMetadataBytesPerPoint)
            throw new InvalidOperationException(
                $"RedisVector point metadata exceeds the configured {_options.MaxMetadataBytesPerPoint} byte limit.");
        var projection = RedisVectorFilter.Project(point.Metadata, managedValues, _options.MaxIndexedPaths);
        return new PreparedPoint(point, Key(point.Id), EmbeddingBytes(point.Embedding.Span), metadata, projection);
    }

    private async Task<bool> ReplaceHash(
        IndexLayout layout,
        PreparedPoint point,
        string scopeToken,
        CancellationToken ct)
    {
        var result = await _route.Data.ScriptEvaluateAsync(
            Infrastructure.Constants.Scripts.ReplaceHash,
            [PointKey(layout, scopeToken, point.StableId)],
            HashValues(point, scopeToken)).WaitAsync(ct).ConfigureAwait(false);
        return (long)result != 0;
    }

    private static RedisValue[] HashValues(PreparedPoint point, string scopeToken)
    {
        var values = new List<RedisValue>(16 + point.Projection.Dynamic.Count * 2)
        {
            Infrastructure.Constants.Wire.Id, point.StableId,
            Infrastructure.Constants.Wire.Key, StableToken(point.StableId),
            Infrastructure.Constants.Wire.Scope, scopeToken,
            Infrastructure.Constants.Wire.Embedding, point.Embedding
        };
        if (point.Metadata is not null)
        {
            values.Add(Infrastructure.Constants.Wire.Metadata);
            values.Add(point.Metadata);
        }
        Add(values, Infrastructure.Constants.Wire.Present, point.Projection.Present);
        Add(values, Infrastructure.Constants.Wire.Scalar, point.Projection.Scalar);
        Add(values, Infrastructure.Constants.Wire.Elements, point.Projection.Elements);
        Add(values, Infrastructure.Constants.Wire.Unordered, point.Projection.Unordered);
        foreach (var item in point.Projection.Dynamic)
        {
            values.Add(item.Key);
            values.Add(item.Value);
        }
        return values.ToArray();
    }

    private static void Add(ICollection<RedisValue> values, string field, string value)
    {
        if (value.Length == 0) return;
        values.Add(field);
        values.Add(value);
    }

    private async Task<VectorPoint<TKey>?> ReadPoint(
        IndexLayout layout,
        TKey id,
        string stableId,
        string scopeToken,
        CancellationToken ct)
    {
        var values = await _route.Data.HashGetAsync(
            PointKey(layout, scopeToken, stableId),
            [Infrastructure.Constants.Wire.Embedding, Infrastructure.Constants.Wire.Metadata])
            .WaitAsync(ct).ConfigureAwait(false);
        return Materialize(id, values);
    }

    private VectorPoint<TKey>? Materialize(TKey id, IReadOnlyList<RedisValue> values)
    {
        if (values.Count == 0 || values[0].IsNull) return null;
        var bytes = (byte[]?)values[0];
        if (bytes is null || bytes.Length != _plan.Dimensions * sizeof(float))
            throw new InvalidOperationException(
                $"RedisVector returned an invalid embedding for space '{_plan.Name}'. Expected {_plan.Dimensions} FLOAT32 values.");
        var embedding = new float[_plan.Dimensions];
        for (var index = 0; index < embedding.Length; index++)
        {
            embedding[index] = BinaryPrimitives.ReadSingleLittleEndian(bytes.AsSpan(index * sizeof(float), sizeof(float)));
            if (!float.IsFinite(embedding[index]))
                throw new InvalidOperationException("RedisVector returned a non-finite embedding value.");
        }
        var metadata = values.Count < 2 || values[1].IsNull
            ? null
            : VectorMetadata.FromJson(values[1].ToString());
        return new VectorPoint<TKey>(id, embedding, metadata);
    }

    private async Task<bool> IsVisible(
        IndexLayout layout,
        NativeShape shape,
        string stableId,
        string scopeToken,
        Filter predicate,
        CancellationToken ct)
    {
        shape = await RefreshShapeForFilter(layout, shape, predicate, ct).ConfigureAwait(false);
        await DemandOrderedValues(layout, scopeToken, predicate, ct).ConfigureAwait(false);
        var compiled = RedisVectorFilter.Compile(predicate, shape.Attributes);
        if (compiled.IsFalse) return false;
        var query = $"({PrimaryFilter(scopeToken, compiled)} " +
                    $"@{Infrastructure.Constants.Wire.Key}:{{{StableToken(stableId)}}})";
        var result = await Execute(
            Infrastructure.Constants.Commands.Search,
            [layout.Index, query, "NOCONTENT", "LIMIT", 0, 1, "DIALECT", 2],
            ct).ConfigureAwait(false);
        return ResultCount(result) > 0;
    }

    private static Filter? Combine(Filter? left, Filter? right) => left is null
        ? right
        : right is null
            ? left
            : Filter.All(left, right);

    private static string PrimaryFilter(string scopeToken, RedisVectorCompiledFilter filter)
    {
        var scope = $"@{Infrastructure.Constants.Wire.Scope}:{{{scopeToken}}}";
        return filter.IsTrue ? scope : $"({scope} {filter.Query})";
    }

    private async Task<NativeShape?> ReadShape(IndexLayout layout, CancellationToken ct)
    {
        ThrowIfDisposed();
        ct.ThrowIfCancellationRequested();
        if (_shapes.TryGetValue(layout.Index, out var cached)) return cached;
        var gate = _shapeGates.GetOrAdd(layout.Index, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_shapes.TryGetValue(layout.Index, out cached)) return cached;
            var shape = await LoadShape(layout, claimMarker: false, ct).ConfigureAwait(false);
            if (shape is not null) _shapes[layout.Index] = shape;
            return shape;
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<NativeShape> RefreshShapeForFilter(
        IndexLayout layout,
        NativeShape shape,
        Filter? filter,
        CancellationToken ct)
    {
        var required = RedisVectorFilter.RequiredDynamicFields(filter);
        if (required.All(shape.Attributes.Contains)) return shape;

        var gate = _shapeGates.GetOrAdd(layout.Index, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var refreshed = await LoadShape(layout, claimMarker: false, ct).ConfigureAwait(false)
                ?? throw WrongShape(layout, "the index disappeared while its filter schema was refreshed");
            _shapes[layout.Index] = refreshed;
            return refreshed;
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task DemandOrderedValues(
        IndexLayout layout,
        string scopeToken,
        Filter? filter,
        CancellationToken ct)
    {
        var paths = RedisVectorFilter.RequiredOrderedPaths(filter);
        if (paths.Count == 0) return;
        var query = $"(@{Infrastructure.Constants.Wire.Scope}:{{{scopeToken}}} " +
                    $"@{Infrastructure.Constants.Wire.Unordered}:{{{string.Join(" | ", paths)}}})";
        var result = await Execute(
            Infrastructure.Constants.Commands.Search,
            [layout.Index, query, "NOCONTENT", "LIMIT", 0, 1, "DIALECT", 2],
            ct).ConfigureAwait(false);
        if (ResultCount(result) > 0)
            throw RedisVectorFilter.UnorderedComparison(paths);
    }

    private async Task<NativeShape> EnsureShape(
        IndexLayout layout,
        IEnumerable<string> requiredDynamic,
        CancellationToken ct)
    {
        ThrowIfDisposed();
        ct.ThrowIfCancellationRequested();
        var required = requiredDynamic.Distinct(StringComparer.Ordinal).ToArray();
        var requiredPaths = required
            .Select(DynamicPath)
            .Where(static path => path is not null)
            .ToHashSet(StringComparer.Ordinal);
        if (requiredPaths.Count > _options.MaxIndexedPaths)
            throw DynamicPathLimit(requiredPaths.Count);
        var gate = _shapeGates.GetOrAdd(layout.Index, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct).ConfigureAwait(false);
        string? lockValue = null;
        try
        {
            lockValue = await AcquireSchemaLock(layout, ct).ConfigureAwait(false);
            var shape = _shapes.TryGetValue(layout.Index, out var cached)
                ? cached
                : await LoadShape(layout, claimMarker: true, ct).ConfigureAwait(false);
            if (shape is null)
            {
                _route.Policy.Demand(DataOperationEffect.SchemaOrAdmin, $"create Redis vector index '{layout.Index}'");
                await CreateShape(layout, ct).ConfigureAwait(false);
                shape = await LoadShape(layout, claimMarker: true, ct).ConfigureAwait(false)
                    ?? throw WrongShape(layout, "the index was not visible after FT.CREATE");
            }

            if (required.Any(field => !shape.Attributes.Contains(field)))
                shape = await LoadShape(layout, claimMarker: true, ct).ConfigureAwait(false)
                    ?? throw WrongShape(layout, "the index disappeared while its filter schema was refreshed");

            var admittedPaths = shape.Attributes
                .Select(DynamicPath)
                .Where(static path => path is not null)
                .Concat(requiredPaths)
                .ToHashSet(StringComparer.Ordinal);
            if (admittedPaths.Count > _options.MaxIndexedPaths)
                throw DynamicPathLimit(admittedPaths.Count);

            foreach (var field in required.Where(field => !shape.Attributes.Contains(field)))
            {
                _route.Policy.Demand(DataOperationEffect.SchemaOrAdmin, $"add Redis vector filter field '{field}'");
                try
                {
                    _ = await Execute(
                        Infrastructure.Constants.Commands.Alter,
                        [layout.Index, "SCHEMA", "ADD", field, "NUMERIC"],
                        ct).ConfigureAwait(false);
                }
                catch (RedisServerException error) when (error.Message.Contains("already exists", StringComparison.OrdinalIgnoreCase))
                {
                    // Another process won the same deterministic schema addition.
                }
                shape = await LoadShape(layout, claimMarker: true, ct).ConfigureAwait(false)
                    ?? throw WrongShape(layout, "the index disappeared during FT.ALTER");
                if (!shape.Attributes.Contains(field))
                    throw WrongShape(layout, $"numeric filter field '{field}' was not present after FT.ALTER");
            }
            _shapes[layout.Index] = shape;
            return shape;
        }
        finally
        {
            if (lockValue is not null)
                try { _ = await _route.Data.LockReleaseAsync(layout.SchemaLock, lockValue).ConfigureAwait(false); }
                catch (RedisException) { }
            gate.Release();
        }
    }

    private async Task<string> AcquireSchemaLock(IndexLayout layout, CancellationToken ct)
    {
        var value = Guid.NewGuid().ToString("N");
        while (!await _route.Data.LockTakeAsync(layout.SchemaLock, value, TimeSpan.FromMinutes(2))
                   .WaitAsync(ct)
                   .ConfigureAwait(false))
            await Task.Delay(25, ct).ConfigureAwait(false);
        return value;
    }

    private async Task CreateShape(IndexLayout layout, CancellationToken ct)
    {
        try
        {
            _ = await Execute(
                Infrastructure.Constants.Commands.Create,
                [
                    layout.Index,
                    "ON", "HASH",
                    "PREFIX", 1, layout.Prefix,
                    "SCHEMA",
                    Infrastructure.Constants.Wire.Scope, "TAG",
                    Infrastructure.Constants.Wire.Key, "TAG",
                    Infrastructure.Constants.Wire.Present, "TAG", "SEPARATOR", Infrastructure.Constants.Wire.TagSeparator,
                    Infrastructure.Constants.Wire.Scalar, "TAG", "SEPARATOR", Infrastructure.Constants.Wire.TagSeparator,
                    Infrastructure.Constants.Wire.Elements, "TAG", "SEPARATOR", Infrastructure.Constants.Wire.TagSeparator,
                    Infrastructure.Constants.Wire.Unordered, "TAG", "SEPARATOR", Infrastructure.Constants.Wire.TagSeparator,
                    Infrastructure.Constants.Wire.Embedding, "VECTOR", "FLAT", 6,
                    "TYPE", "FLOAT32",
                    "DIM", _plan.Dimensions,
                    "DISTANCE_METRIC", MetricName()
                ],
                ct).ConfigureAwait(false);
        }
        catch (RedisServerException error) when (error.Message.Contains("Index already exists", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }
        await WriteMarker(layout, ct).ConfigureAwait(false);
    }

    private async Task<NativeShape?> LoadShape(IndexLayout layout, bool claimMarker, CancellationToken ct)
    {
        var info = await TryInfo(layout, ct).ConfigureAwait(false);
        if (info is null) return null;
        var shape = ParseShape(layout, info);
        ValidateShape(layout, shape);
        var markerValue = await _route.Data.StringGetAsync(layout.Marker).WaitAsync(ct).ConfigureAwait(false);
        if (markerValue.IsNull)
        {
            if (!claimMarker)
                throw WrongShape(layout, "the native index has no Koan space marker");
            _route.Policy.Demand(DataOperationEffect.SchemaOrAdmin, $"claim Redis vector index '{layout.Index}'");
            await WriteMarker(layout, ct).ConfigureAwait(false);
            markerValue = await _route.Data.StringGetAsync(layout.Marker).WaitAsync(ct).ConfigureAwait(false);
            if (markerValue.IsNull)
                throw WrongShape(layout, "the Koan space marker was not visible after it was claimed");
        }
        PlanMarker? marker;
        try { marker = JsonSerializer.Deserialize<PlanMarker>(markerValue.ToString()); }
        catch (JsonException error) { throw WrongShape(layout, $"the Koan marker is invalid ({error.Message})"); }
        if (marker is null || marker.Version != Infrastructure.Constants.Wire.MarkerVersion)
            throw WrongShape(layout, "the Koan marker version is incompatible");
        if (marker.Index != layout.Index || marker.Prefix != layout.Prefix ||
            marker.Space != _plan.Name || marker.Dimensions != _plan.Dimensions ||
            marker.Metric != _plan.Metric.ToString() || marker.Model != _plan.Model)
        {
            throw WrongShape(layout,
                $"the persisted plan is space '{marker.Space}', dimension {marker.Dimensions}, metric '{marker.Metric}', model '{marker.Model ?? "<none>"}'");
        }
        return shape;
    }

    private async Task WriteMarker(IndexLayout layout, CancellationToken ct)
    {
        var marker = JsonSerializer.Serialize(new PlanMarker(
            Infrastructure.Constants.Wire.MarkerVersion,
            layout.Index,
            layout.Prefix,
            _plan.Name,
            _plan.Dimensions,
            _plan.Metric.ToString(),
            _plan.Model));
        _ = await _route.Data.StringSetAsync(layout.Marker, marker, when: When.NotExists)
            .WaitAsync(ct)
            .ConfigureAwait(false);
    }

    private async Task<RedisResult?> TryInfo(IndexLayout layout, CancellationToken ct)
    {
        try
        {
            return await Execute(Infrastructure.Constants.Commands.Info, [layout.Index], ct).ConfigureAwait(false);
        }
        catch (RedisServerException error) when (
            error.Message.Contains("Unknown Index name", StringComparison.OrdinalIgnoreCase) ||
            error.Message.Contains("no such index", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }
    }

    private static NativeShape ParseShape(IndexLayout layout, RedisResult info)
    {
        try
        {
            var root = info.ToDictionary(StringComparer.OrdinalIgnoreCase);
            var definition = root["index_definition"].ToDictionary(StringComparer.OrdinalIgnoreCase);
            var keyType = Text(definition["key_type"]);
            var prefixes = definition["prefixes"];
            var prefix = prefixes.Length > 0 ? Text(prefixes[0]) : string.Empty;
            var attributes = new Dictionary<string, IReadOnlyDictionary<string, RedisResult>>(StringComparer.Ordinal);
            var list = root["attributes"];
            for (var index = 0; index < list.Length; index++)
            {
                var descriptor = list[index].ToDictionary(StringComparer.OrdinalIgnoreCase);
                var identifier = descriptor.TryGetValue("identifier", out var value)
                    ? Text(value)
                    : descriptor.TryGetValue("attribute", out value) ? Text(value) : string.Empty;
                if (identifier.Length > 0) attributes[identifier] = descriptor;
            }
            return new NativeShape(keyType, prefix, prefixes.Length, attributes);
        }
        catch (Exception error) when (error is KeyNotFoundException or InvalidCastException or IndexOutOfRangeException)
        {
            throw new InvalidOperationException(
                $"RedisVector index '{layout.Index}' returned an FT.INFO shape that cannot be interpreted.", error);
        }
    }

    private void ValidateShape(IndexLayout layout, NativeShape shape)
    {
        if (!string.Equals(shape.KeyType, "HASH", StringComparison.OrdinalIgnoreCase))
            throw WrongShape(layout, $"native key type is '{shape.KeyType}', not HASH");
        if (shape.PrefixCount != 1)
            throw WrongShape(layout, $"native index has {shape.PrefixCount} prefixes, not exactly one");
        if (!string.Equals(shape.Prefix, layout.Prefix, StringComparison.Ordinal))
            throw WrongShape(layout, $"native prefix is '{shape.Prefix}', not '{layout.Prefix}'");
        RequireType(layout, shape, Infrastructure.Constants.Wire.Scope, "TAG");
        RequireType(layout, shape, Infrastructure.Constants.Wire.Key, "TAG");
        RequireSeparator(layout, Infrastructure.Constants.Wire.Present,
            RequireType(layout, shape, Infrastructure.Constants.Wire.Present, "TAG"));
        RequireSeparator(layout, Infrastructure.Constants.Wire.Scalar,
            RequireType(layout, shape, Infrastructure.Constants.Wire.Scalar, "TAG"));
        RequireSeparator(layout, Infrastructure.Constants.Wire.Elements,
            RequireType(layout, shape, Infrastructure.Constants.Wire.Elements, "TAG"));
        RequireSeparator(layout, Infrastructure.Constants.Wire.Unordered,
            RequireType(layout, shape, Infrastructure.Constants.Wire.Unordered, "TAG"));
        foreach (var field in shape.Attributes.Where(field => DynamicPath(field) is not null))
            _ = RequireType(layout, shape, field, "NUMERIC");
        var vector = RequireType(layout, shape, Infrastructure.Constants.Wire.Embedding, "VECTOR");
        var algorithm = Value(vector, "algorithm");
        var dataType = Value(vector, "data_type");
        var metric = Value(vector, "distance_metric");
        var dimensionText = Value(vector, "dim");
        if (!string.Equals(algorithm, "FLAT", StringComparison.OrdinalIgnoreCase))
            throw WrongShape(layout, $"native vector algorithm is '{algorithm}', not FLAT exact search");
        if (!string.Equals(dataType, "FLOAT32", StringComparison.OrdinalIgnoreCase))
            throw WrongShape(layout, $"native vector type is '{dataType}', not FLOAT32");
        if (!int.TryParse(dimensionText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var dimensions) ||
            dimensions != _plan.Dimensions)
            throw WrongShape(layout, $"native vector dimension is {dimensionText}, but the plan requires {_plan.Dimensions}");
        if (!string.Equals(metric, MetricName(), StringComparison.OrdinalIgnoreCase))
            throw WrongShape(layout, $"native metric is '{metric}', but the plan requires '{MetricName()}'");
    }

    private static IReadOnlyDictionary<string, RedisResult> RequireType(
        IndexLayout layout,
        NativeShape shape,
        string field,
        string expected)
    {
        if (!shape.Descriptors.TryGetValue(field, out var descriptor))
            throw new InvalidOperationException(
                $"RedisVector index '{layout.Index}' is missing required field '{field}'. Provision the declared Koan vector shape.");
        var actual = Value(descriptor, "type");
        if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                $"RedisVector index '{layout.Index}' field '{field}' has type '{actual}', not '{expected}'. Provision the declared Koan vector shape.");
        return descriptor;
    }

    private static string Value(IReadOnlyDictionary<string, RedisResult> values, string key) =>
        values.TryGetValue(key, out var value) ? Text(value) : string.Empty;

    private static void RequireSeparator(
        IndexLayout layout,
        string field,
        IReadOnlyDictionary<string, RedisResult> descriptor)
    {
        var separator = Value(descriptor, "separator");
        if (!string.Equals(separator, Infrastructure.Constants.Wire.TagSeparator, StringComparison.Ordinal))
            throw new InvalidOperationException(
                $"RedisVector index '{layout.Index}' field '{field}' uses TAG separator '{separator}', " +
                $"not '{Infrastructure.Constants.Wire.TagSeparator}'. Provision the declared Koan vector shape.");
    }

    private static string Text(RedisResult result) => result.IsNull ? string.Empty : (string?)result ?? string.Empty;

    private static string? DynamicPath(string field) =>
        field.StartsWith(Infrastructure.Constants.Wire.NumberPrefix, StringComparison.Ordinal) ||
        field.StartsWith(Infrastructure.Constants.Wire.SizePrefix, StringComparison.Ordinal)
            ? field[2..]
            : null;

    private InvalidOperationException DynamicPathLimit(int count) => new(
        $"RedisVector space '{_plan.Name}' would require {count} dynamic numeric/size metadata paths; " +
        $"the configured maximum is {_options.MaxIndexedPaths}. Increase RedisVectorOptions.MaxIndexedPaths " +
        "deliberately or reduce the filterable metadata shape.");

    private async Task<RedisResult> Execute(string command, object[] arguments, CancellationToken ct)
    {
        ThrowIfDisposed();
        ct.ThrowIfCancellationRequested();
        try
        {
            return await _route.Data.ExecuteAsync(command, arguments).WaitAsync(ct).ConfigureAwait(false);
        }
        catch (RedisServerException error) when (error.Message.Contains("unknown command", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "RedisVector requires Redis Search with vector support; the selected endpoint is plain Redis or lacks the Search module.",
                error);
        }
    }

    private IndexLayout Layout()
    {
        ThrowIfDisposed();
        var physical = VectorAdapterNaming.GetOrCompute<TEntity>(_services, _factory, _plan.Source);
        var index = Infrastructure.Constants.Wire.IndexPrefix + physical;
        var digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(index)))
            .ToLowerInvariant()[..24];
        return new IndexLayout(
            index,
            Infrastructure.Constants.Wire.KeyPrefixStart + digest + Infrastructure.Constants.Wire.KeyPrefixEnd,
            Infrastructure.Constants.Wire.MarkerPrefix + digest,
            Infrastructure.Constants.Wire.MarkerPrefix + digest + ":schema-lock");
    }

    private static RedisKey PointKey(IndexLayout layout, string scopeToken, string stableId) =>
        layout.Prefix + scopeToken + ":" + Encode(stableId);

    private static string ScopeToken(VectorScope scope)
    {
        ArgumentNullException.ThrowIfNull(scope);
        if (scope.Values.Properties.Count == 0 && scope.Identity.Length == 0) return "0";
        return StableToken(scope.Identity + "\u001f" + (VectorMetadata.ToJson(scope.Values) ?? string.Empty));
    }

    private static string StableToken(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private static string Encode(string value) => Convert.ToBase64String(Encoding.UTF8.GetBytes(value))
        .TrimEnd('=')
        .Replace('+', '-')
        .Replace('/', '_');

    private static byte[] EmbeddingBytes(ReadOnlySpan<float> embedding)
    {
        var bytes = new byte[embedding.Length * sizeof(float)];
        for (var index = 0; index < embedding.Length; index++)
            BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(index * sizeof(float), sizeof(float)), embedding[index]);
        return bytes;
    }

    private IReadOnlyList<SearchRow> ParseSearch(RedisResult result)
    {
        if (result.Resp3Type == ResultType.Map)
            return ParseSearchMap(result);

        if (result.Length == 0) return [];
        var rows = new List<SearchRow>(Math.Min(ResultCount(result), _options.MaxSearchCandidates));
        for (var index = 1; index + 1 < result.Length; index += 2)
        {
            rows.Add(ParseSearchFields(result[index + 1]));
        }
        return rows;
    }

    private IReadOnlyList<SearchRow> ParseSearchMap(RedisResult result)
    {
        var envelope = result.ToDictionary(StringComparer.OrdinalIgnoreCase);
        var count = ResultCount(envelope);
        if (!envelope.TryGetValue("results", out var documents))
        {
            if (count == 0) return [];
            throw MalformedSearchResponse();
        }

        var rows = new List<SearchRow>(Math.Min(count, _options.MaxSearchCandidates));
        for (var index = 0; index < documents.Length; index++)
        {
            var document = documents[index].ToDictionary(StringComparer.OrdinalIgnoreCase);
            if (!document.TryGetValue("extra_attributes", out var fields))
                throw MalformedSearchResponse();
            rows.Add(ParseSearchFields(fields));
        }
        return rows;
    }

    private static SearchRow ParseSearchFields(RedisResult fields)
    {
        if (fields.IsNull || fields.Length == 0)
            throw MalformedSearchResponse();

        string? id = null;
        string? metadata = null;
        double? distance = null;
        var values = fields.Resp3Type == ResultType.Map
            ? fields.ToDictionary(StringComparer.Ordinal)
            : null;
        if (values is not null)
        {
            if (values.TryGetValue(Infrastructure.Constants.Wire.Id, out var value)) id = Text(value);
            if (values.TryGetValue(Infrastructure.Constants.Wire.Metadata, out value)) metadata = Text(value);
            if (values.TryGetValue(Infrastructure.Constants.Wire.Distance, out value) &&
                double.TryParse(Text(value), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
                distance = parsed;
        }
        else
        {
            for (var field = 0; field + 1 < fields.Length; field += 2)
            {
                var name = Text(fields[field]);
                var value = fields[field + 1];
                if (name == Infrastructure.Constants.Wire.Id) id = Text(value);
                else if (name == Infrastructure.Constants.Wire.Metadata) metadata = Text(value);
                else if (name == Infrastructure.Constants.Wire.Distance &&
                         double.TryParse(Text(value), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
                    distance = parsed;
            }
        }

        if (id is null || distance is null || !double.IsFinite(distance.Value))
            throw MalformedSearchResponse();
        return new SearchRow(id, metadata, distance.Value);
    }

    private static IReadOnlyList<RedisKey> ParseDocumentKeys(RedisResult result)
    {
        if (result.Resp3Type == ResultType.Map)
        {
            var envelope = result.ToDictionary(StringComparer.OrdinalIgnoreCase);
            var count = ResultCount(envelope);
            if (!envelope.TryGetValue("results", out var documents))
            {
                if (count == 0) return [];
                throw MalformedSearchResponse();
            }

            var mappedKeys = new List<RedisKey>(Math.Min(count, documents.Length));
            for (var index = 0; index < documents.Length; index++)
            {
                var document = documents[index].ToDictionary(StringComparer.OrdinalIgnoreCase);
                if (!document.TryGetValue("id", out var key) || key.IsNull)
                    throw MalformedSearchResponse();
                mappedKeys.Add((RedisKey)key);
            }
            return mappedKeys;
        }

        if (result.Length <= 1) return [];
        var keys = new List<RedisKey>(result.Length - 1);
        for (var index = 1; index < result.Length; index++)
            keys.Add((RedisKey)result[index]);
        return keys;
    }

    private static int ResultCount(RedisResult result) => result.Resp3Type == ResultType.Map
        ? ResultCount(result.ToDictionary(StringComparer.OrdinalIgnoreCase))
        : result.Length == 0 ? 0 : checked((int)(long)result[0]);

    private static int ResultCount(IReadOnlyDictionary<string, RedisResult> envelope)
    {
        if (!envelope.TryGetValue("total_results", out var count) || count.IsNull)
            throw MalformedSearchResponse();
        return checked((int)(long)count);
    }

    private static InvalidOperationException MalformedSearchResponse() =>
        new("RedisVector received a malformed FT.SEARCH response.");

    private double Similarity(double distance)
    {
        var value = _plan.Metric switch
        {
            VectorMetric.Cosine => 1d - distance / 2d,
            VectorMetric.Euclidean => 1d / (1d + Math.Sqrt(Math.Max(0d, distance))),
            VectorMetric.DotProduct => Logistic(1d - distance),
            _ => throw new NotSupportedException($"RedisVector does not support metric '{_plan.Metric}'.")
        };
        return Math.Clamp(double.IsFinite(value) ? value : 0d, 0d, 1d);
    }

    private static double Logistic(double value) => value >= 0d
        ? 1d / (1d + Math.Exp(-value))
        : Math.Exp(value) / (1d + Math.Exp(value));

    private string MetricName() => _plan.Metric switch
    {
        VectorMetric.Cosine => "COSINE",
        VectorMetric.Euclidean => "L2",
        VectorMetric.DotProduct => "IP",
        _ => throw new NotSupportedException($"RedisVector does not support metric '{_plan.Metric}'.")
    };

    private void Validate(VectorSearchRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateEmbedding(request.Embedding.Span, "query embedding");
        if (request.Top <= 0 || request.Top > _options.MaxSearchCandidates)
            throw new ArgumentOutOfRangeException(nameof(request.Top),
                $"RedisVector Top must be positive and no greater than MaxSearchCandidates ({_options.MaxSearchCandidates}).");
        if (request.Space is not null && !string.Equals(request.Space, _plan.Name, StringComparison.Ordinal))
            throw new InvalidOperationException(
                $"RedisVector query requested space '{request.Space}'. Available space: {_plan.Name}.");
        if (request.Text is not null || request.SemanticWeight is not null)
            throw new NotSupportedException("RedisVector does not claim portable hybrid text semantics.");
        if (request.Continuation is not null)
            throw new NotSupportedException("RedisVector does not claim a stable native continuation snapshot.");
        if (request.MinimumSimilarity is < 0d or > 1d ||
            request.MinimumSimilarity is { } minimum && !double.IsFinite(minimum))
            throw new ArgumentOutOfRangeException(nameof(request.MinimumSimilarity),
                "Minimum similarity must be finite and in [0,1].");
    }

    private void ValidateEmbedding(ReadOnlySpan<float> embedding, string label)
    {
        if (embedding.Length != _plan.Dimensions)
            throw new ArgumentException(
                $"RedisVector {label} has {embedding.Length} dimensions; space '{_plan.Name}' requires {_plan.Dimensions}.");
        double squared = 0d;
        for (var index = 0; index < embedding.Length; index++)
        {
            if (!float.IsFinite(embedding[index]))
                throw new ArgumentException($"RedisVector {label} contains a non-finite value at index {index}.");
            squared += embedding[index] * (double)embedding[index];
        }
        if (squared == 0d && _plan.Metric == VectorMetric.Cosine)
            throw new ArgumentException("RedisVector cosine spaces do not accept a zero-magnitude embedding.");
    }

    private static string Key(TKey id) => id switch
    {
        string value => value,
        Guid value => value.ToString("D"),
        IFormattable value => value.ToString(null, CultureInfo.InvariantCulture)
            ?? throw new InvalidOperationException($"Vector identity type '{typeof(TKey).FullName}' produced no stable value."),
        _ => id.ToString()
            ?? throw new InvalidOperationException($"Vector identity type '{typeof(TKey).FullName}' produced no stable value.")
    };

    private static TKey ParseKey(string value)
    {
        if (typeof(TKey) == typeof(string)) return (TKey)(object)value;
        if (typeof(TKey) == typeof(Guid)) return (TKey)(object)Guid.ParseExact(value, "D");
        var converter = TypeDescriptor.GetConverter(typeof(TKey));
        if (converter.CanConvertFrom(typeof(string)))
            return (TKey)(converter.ConvertFromInvariantString(value)
                ?? throw new InvalidOperationException(
                    $"Vector identity '{value}' could not be converted to '{typeof(TKey).FullName}'."));
        return (TKey)Convert.ChangeType(value, typeof(TKey), CultureInfo.InvariantCulture);
    }

    private void DemandBatch(int count)
    {
        if (count > _options.MaxBatchPoints)
            throw new ArgumentOutOfRangeException(nameof(count),
                $"RedisVector batch contains {count} points; configured maximum is {_options.MaxBatchPoints}.");
    }

    private static void DemandUnique(IEnumerable<string> keys, string operation)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var key in keys)
            if (!seen.Add(key))
                throw new ArgumentException(
                    $"RedisVector batch {operation} contains duplicate identity '{key}'. Submit each identity once so ordered outcomes are unambiguous.",
                    nameof(keys));
    }

    private VectorSearchResult<TKey> EmptyResult() => new(
        [],
        null,
        new VectorSearchExecution(_plan.Metric, VectorSearchAccuracy.Exact, null));

    private static BatchResult<TKey> Missing(IReadOnlyList<TKey> ids) => new(
        ids.Select((id, index) => new BatchItemResult<TKey>(index, id, MutationOutcome.Missing)),
        BatchAtomicity.NotGuaranteed);

    private InvalidOperationException WrongShape(IndexLayout layout, string reason) => new(
        $"RedisVector index '{layout.Index}' cannot realize space '{_plan.Name}': {reason}. " +
        "Provision the declared FLAT/FLOAT32 shape or select the Redis source that owns this index.");

    private void ThrowIfDisposed() =>
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

    private sealed record PreparedPoint(
        VectorPoint<TKey> Point,
        string StableId,
        byte[] Embedding,
        string? Metadata,
        RedisVectorProjection Projection);

    private sealed record IndexLayout(string Index, string Prefix, string Marker, string SchemaLock);

    private sealed record NativeShape(
        string KeyType,
        string Prefix,
        int PrefixCount,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, RedisResult>> Descriptors)
    {
        internal IReadOnlySet<string> Attributes { get; } = Descriptors.Keys.ToHashSet(StringComparer.Ordinal);
    }

    private sealed record PlanMarker(
        string Version,
        string Index,
        string Prefix,
        string Space,
        int Dimensions,
        string Metric,
        string? Model);

    private sealed record SearchRow(string StableId, string? Metadata, double Distance);
}
