using System.Collections.Concurrent;
using System.ComponentModel;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Koan.Core.Capabilities;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Filtering;
using Koan.Data.Abstractions.Sources;
using Koan.Data.Vector.Abstractions;
using Koan.Data.Vector.Abstractions.Capabilities;
using Koan.Data.Vector.Naming;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Koan.Data.Vector.Connector.MongoAtlasVector;

internal sealed class MongoAtlasVectorRepository<TEntity, TKey> :
    IVectorSearchRepository<TEntity, TKey>,
    IDescribesCapabilities,
    IDisposable,
    IAsyncDisposable
    where TEntity : class, IEntity<TKey>
    where TKey : notnull
{
    private const string ShapeVersionField = "__koan_shape_version";
    private const string ShapeSpaceField = "__koan_shape_space";
    private const string ShapeDimensionsField = "__koan_shape_dimensions";
    private const string ShapeMetricField = "__koan_shape_metric";
    private const string ShapeModelField = "__koan_shape_model";

    private readonly IServiceProvider _services;
    private readonly MongoAtlasVectorAdapterFactory _factory;
    private readonly VectorSpacePlan _plan;
    private readonly MongoAtlasVectorRoute _route;
    private readonly MongoAtlasVectorClientManager _clients;
    private readonly MongoAtlasVectorOptions _options;
    private readonly ConcurrentDictionary<string, byte> _ready = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _shapeGate = new(1, 1);
    private int _disposed;

    internal MongoAtlasVectorRepository(
        IServiceProvider services,
        MongoAtlasVectorAdapterFactory factory,
        VectorSpacePlan plan,
        MongoAtlasVectorRoute route,
        MongoAtlasVectorClientManager clients,
        MongoAtlasVectorOptions options)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        _plan = plan ?? throw new ArgumentNullException(nameof(plan));
        _route = route ?? throw new ArgumentNullException(nameof(route));
        _clients = clients ?? throw new ArgumentNullException(nameof(clients));
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public void Describe(ICapabilities capabilities) => capabilities
        .Add(VectorCaps.Knn)
        .Add(VectorCaps.Filters, MongoAtlasVectorFilter.Capabilities)
        .Add(VectorCaps.BulkUpsert)
        .Add(VectorCaps.BulkDelete)
        .Add(VectorCaps.ScoreNormalization)
        .Add(VectorCaps.DynamicCollections);

    public async Task Save(VectorPoint<TKey> point, VectorScope scope, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var prepared = Prepare(point, scope);
        _route.Policy.Demand(DataOperationEffect.Write,
            $"save Atlas vector point in space '{_plan.Name}'");
        var collection = CollectionName();
        var native = await EnsureShape(collection, create: true, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Atlas vector shape creation returned no collection.");
        _ = await native.ReplaceOneAsync(
                new BsonDocument("_id", prepared.StorageId),
                Document(prepared),
                new ReplaceOptions { IsUpsert = true },
                ct)
            .ConfigureAwait(false);
        await AwaitVisible(native, prepared, visible: true, ct).ConfigureAwait(false);
    }

    public async Task<BatchResult<TKey>> Save(
        IReadOnlyList<VectorPoint<TKey>> points,
        VectorScope scope,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(points);
        DemandBatch(points.Count);
        var prepared = new PreparedPoint[points.Count];
        for (var index = 0; index < points.Count; index++)
        {
            ct.ThrowIfCancellationRequested();
            prepared[index] = Prepare(points[index], scope);
        }
        DemandUnique(prepared.Select(static item => item.StorageId), "save");
        if (prepared.Length == 0) return new BatchResult<TKey>([], BatchAtomicity.NotGuaranteed);

        _route.Policy.Demand(DataOperationEffect.Write,
            $"save Atlas vector batch in space '{_plan.Name}'");
        var collectionName = CollectionName();
        var collection = await EnsureShape(collectionName, create: true, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Atlas vector shape creation returned no collection.");
        var existing = await ExistingIds(collection, prepared.Select(static item => item.StorageId), ct)
            .ConfigureAwait(false);
        var writes = prepared
            .Select(item => (WriteModel<BsonDocument>)new ReplaceOneModel<BsonDocument>(
                new BsonDocument("_id", item.StorageId), Document(item)) { IsUpsert = true })
            .ToArray();
        _ = await collection.BulkWriteAsync(writes, new BulkWriteOptions { IsOrdered = true }, ct)
            .ConfigureAwait(false);

        var outcomes = new BatchItemResult<TKey>[prepared.Length];
        for (var index = 0; index < prepared.Length; index++)
        {
            var item = prepared[index];
            outcomes[index] = new BatchItemResult<TKey>(index, item.Point.Id,
                existing.Contains(item.StorageId) ? MutationOutcome.Updated : MutationOutcome.Inserted);
            await AwaitVisible(collection, item, visible: true, ct).ConfigureAwait(false);
        }
        return new BatchResult<TKey>(outcomes, BatchAtomicity.NotGuaranteed);
    }

    public async Task<VectorPoint<TKey>?> Get(TKey id, VectorScope scope, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var key = Key(id);
        var collection = await EnsureShape(CollectionName(), create: false, ct).ConfigureAwait(false);
        if (collection is null) return null;
        var found = await collection.Find(ScopedIdentity(StorageId(key, scope), scope))
            .FirstOrDefaultAsync(ct).ConfigureAwait(false);
        return found is null ? null : ReadPoint(found, id);
    }

    public async Task<IReadOnlyList<VectorPoint<TKey>?>> Get(
        IReadOnlyList<TKey> ids,
        VectorScope scope,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ids);
        DemandBatch(ids.Count);
        var output = new VectorPoint<TKey>?[ids.Count];
        if (ids.Count == 0) return output;
        var keys = ids.Select(Key).ToArray();
        var storage = keys.Select(key => StorageId(key, scope)).ToArray();
        var collection = await EnsureShape(CollectionName(), create: false, ct).ConfigureAwait(false);
        if (collection is null) return output;
        var filter = And(
            new BsonDocument("_id", new BsonDocument("$in", new BsonArray(storage))),
            ScopeMatch(scope));
        var documents = await collection.Find(filter).ToListAsync(ct).ConfigureAwait(false);
        var byId = documents.ToDictionary(static item => item["_id"].AsString, StringComparer.Ordinal);
        for (var index = 0; index < ids.Count; index++)
            if (byId.TryGetValue(storage[index], out var document))
                output[index] = ReadPoint(document, ids[index]);
        return output;
    }

    public async Task<bool> Delete(TKey id, VectorScope scope, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var key = Key(id);
        _route.Policy.Demand(DataOperationEffect.Write,
            $"delete Atlas vector point from space '{_plan.Name}'");
        var collection = await EnsureShape(CollectionName(), create: false, ct).ConfigureAwait(false);
        if (collection is null) return false;
        var filter = ScopedIdentity(StorageId(key, scope), scope);
        var existing = await collection.Find(filter).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        if (existing is null) return false;
        var prepared = PreparedFromStored(existing, id);
        var deleted = await collection.DeleteOneAsync(filter, ct).ConfigureAwait(false);
        if (deleted.DeletedCount == 0) return false;
        await AwaitVisible(collection, prepared, visible: false, ct).ConfigureAwait(false);
        return true;
    }

    public async Task<BatchResult<TKey>> Delete(
        IReadOnlyList<TKey> ids,
        VectorScope scope,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ids);
        DemandBatch(ids.Count);
        var keys = ids.Select(Key).ToArray();
        var storage = keys.Select(key => StorageId(key, scope)).ToArray();
        DemandUnique(storage, "delete");
        if (ids.Count == 0) return new BatchResult<TKey>([], BatchAtomicity.NotGuaranteed);
        _route.Policy.Demand(DataOperationEffect.Write,
            $"delete Atlas vector batch from space '{_plan.Name}'");
        var collection = await EnsureShape(CollectionName(), create: false, ct).ConfigureAwait(false);
        if (collection is null) return Missing(ids);

        var find = And(
            new BsonDocument("_id", new BsonDocument("$in", new BsonArray(storage))),
            ScopeMatch(scope));
        var documents = await collection.Find(find).ToListAsync(ct).ConfigureAwait(false);
        var existing = documents.ToDictionary(static item => item["_id"].AsString, StringComparer.Ordinal);
        var writes = storage.Where(existing.ContainsKey)
            .Select(value => (WriteModel<BsonDocument>)new DeleteOneModel<BsonDocument>(
                ScopedIdentity(value, scope)))
            .ToArray();
        if (writes.Length > 0)
            _ = await collection.BulkWriteAsync(writes, new BulkWriteOptions { IsOrdered = true }, ct)
                .ConfigureAwait(false);

        var outcomes = new BatchItemResult<TKey>[ids.Count];
        for (var index = 0; index < ids.Count; index++)
        {
            var found = existing.TryGetValue(storage[index], out var document);
            outcomes[index] = new BatchItemResult<TKey>(index, ids[index],
                found ? MutationOutcome.Deleted : MutationOutcome.Missing);
            if (found)
                await AwaitVisible(collection, PreparedFromStored(document!, ids[index]), visible: false, ct)
                    .ConfigureAwait(false);
        }
        return new BatchResult<TKey>(outcomes, BatchAtomicity.NotGuaranteed);
    }

    public async Task<VectorSearchResult<TKey>> Search(
        VectorSearchRequest request,
        VectorScope scope,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        Validate(request);
        var collection = await EnsureShape(CollectionName(), create: false, ct).ConfigureAwait(false);
        if (collection is null) return EmptyResult();
        var effective = request.Filter is null
            ? scope.Predicate
            : scope.Predicate is null || ReferenceEquals(request.Filter, scope.Predicate)
                ? request.Filter
                : Filter.All(request.Filter, scope.Predicate);
        var nativeFilter = NativeFilter(effective, scope.Identity);
        var requested = Math.Min(_options.MaxSearchCandidates, checked(request.Top + 1));
        List<RankedPoint> ranked;
        while (true)
        {
            ranked = await VectorQuery(collection, request.Embedding, requested, nativeFilter, ct)
                .ConfigureAwait(false);
            ranked.Sort(static (left, right) =>
            {
                var score = right.Similarity.CompareTo(left.Similarity);
                return score != 0 ? score : StringComparer.Ordinal.Compare(left.StableId, right.StableId);
            });
            if (ranked.Count <= request.Top || ranked.Count < requested ||
                ranked[request.Top - 1].RawScore != ranked[request.Top].RawScore)
                break;
            if (requested >= _options.MaxSearchCandidates)
                throw new InvalidOperationException(
                    $"MongoAtlasVector cannot resolve a stable identity tie within the configured bound of {_options.MaxSearchCandidates} candidates. " +
                    "Increase MaxSearchCandidates or narrow the vector space.");
            requested = Math.Min(_options.MaxSearchCandidates, checked(requested * 2));
        }

        var items = ranked
            .Where(item => request.MinimumSimilarity is null || item.Similarity >= request.MinimumSimilarity.Value)
            .Take(request.Top)
            .Select(item => new VectorMatch<TKey>(item.Id, item.Similarity, item.Metadata))
            .ToArray();
        return new VectorSearchResult<TKey>(items, null,
            new VectorSearchExecution(_plan.Metric, VectorSearchAccuracy.Exact, null));
    }

    public async Task Clear(VectorScope scope, CancellationToken ct = default)
    {
        _route.Policy.Demand(DataOperationEffect.Write,
            $"clear Atlas vector space '{_plan.Name}'");
        var collection = await EnsureShape(CollectionName(), create: false, ct).ConfigureAwait(false);
        if (collection is null) return;
        var filter = And(
            new BsonDocument("_id", new BsonDocument("$ne", Infrastructure.Constants.Wire.ShapeId)),
            ScopeMatch(scope));
        var documents = await collection.Find(filter).ToListAsync(ct).ConfigureAwait(false);
        if (documents.Count > _options.MaxSearchCandidates)
            throw new InvalidOperationException(
                $"MongoAtlasVector clear exceeds the configured {_options.MaxSearchCandidates} point visibility bound.");
        if (documents.Count == 0) return;
        _ = await collection.DeleteManyAsync(filter, ct).ConfigureAwait(false);
        foreach (var document in documents)
            await AwaitVisible(collection, PreparedFromStored(document, ParseKey(document[Infrastructure.Constants.Wire.Id].AsString)),
                    visible: false, ct)
                .ConfigureAwait(false);
    }

    public Task Sync(VectorScope scope, CancellationToken ct = default)
    {
        ThrowIfDisposed();
        ct.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }

    public async Task VectorEnsureCreated(CancellationToken ct = default)
    {
        _route.Policy.Demand(DataOperationEffect.SchemaOrAdmin,
            $"create Atlas vector space '{_plan.Name}'");
        _ = await EnsureShape(CollectionName(), create: true, ct).ConfigureAwait(false);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _shapeGate.Dispose();
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }

    private async Task<IMongoCollection<BsonDocument>?> EnsureShape(
        string collectionName,
        bool create,
        CancellationToken ct)
    {
        ThrowIfDisposed();
        var database = await _clients.Database(_route, ct).ConfigureAwait(false);
        var collection = database.GetCollection<BsonDocument>(collectionName);
        if (_ready.ContainsKey(collectionName)) return collection;
        await _shapeGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_ready.ContainsKey(collectionName)) return collection;
            var exists = await CollectionExists(database, collectionName, ct).ConfigureAwait(false);
            if (!exists)
            {
                if (!create) return null;
                _route.Policy.Demand(DataOperationEffect.SchemaOrAdmin,
                    $"create Atlas vector collection for space '{_plan.Name}'");
                try
                {
                    await database.CreateCollectionAsync(collectionName, cancellationToken: ct).ConfigureAwait(false);
                }
                catch (MongoCommandException error) when (error.Code == 48)
                {
                    // Another host won the collection-create race; validate its shape below.
                }
            }

            var index = await ReadIndex(collection, ct).ConfigureAwait(false);
            if (index is null)
            {
                if (!create) return null;
                _route.Policy.Demand(DataOperationEffect.SchemaOrAdmin,
                    $"create Atlas Search index for space '{_plan.Name}'");
                try
                {
                    _ = await collection.SearchIndexes.CreateOneAsync(
                            new CreateSearchIndexModel(
                                Infrastructure.Constants.Wire.Index,
                                SearchIndexType.Search,
                                IndexDefinition()),
                            ct)
                        .ConfigureAwait(false);
                }
                catch (MongoCommandException error) when (
                    error.Code is 68 or 85 or 86 ||
                    error.Message.Contains("already exists", StringComparison.OrdinalIgnoreCase))
                {
                    // Another host won the index-create race; inspect the winner.
                }
                catch (MongoCommandException error)
                {
                    throw MongoAtlasVectorClientManager.AtlasRequired(_route, error);
                }
                index = await WaitForIndex(collection, ct).ConfigureAwait(false);
            }
            else
            {
                ValidateIndex(collectionName, index);
                if (!Ready(index)) index = await WaitForIndex(collection, ct).ConfigureAwait(false);
            }
            ValidateIndex(collectionName, index);
            await EnsureMarker(collection, create, ct).ConfigureAwait(false);
            _ready.TryAdd(collectionName, 0);
            return collection;
        }
        finally
        {
            _shapeGate.Release();
        }
    }

    private async Task EnsureMarker(
        IMongoCollection<BsonDocument> collection,
        bool create,
        CancellationToken ct)
    {
        var filter = new BsonDocument("_id", Infrastructure.Constants.Wire.ShapeId);
        var marker = await collection.Find(filter).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        if (marker is null && create)
        {
            var expected = ShapeMarker();
            try
            {
                _ = await collection.UpdateOneAsync(
                        filter,
                        new BsonDocument("$setOnInsert", expected),
                        new UpdateOptions { IsUpsert = true },
                        ct)
                    .ConfigureAwait(false);
            }
            catch (MongoWriteException error) when (error.WriteError?.Category == ServerErrorCategory.DuplicateKey)
            {
                // Another host claimed the immutable marker; validate the winner.
            }
            marker = await collection.Find(filter).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        }
        if (marker is null)
            throw WrongShape(CollectionName(), "the immutable Koan shape marker is absent");
        ValidateMarker(CollectionName(), marker);
    }

    private async Task<BsonDocument?> ReadIndex(
        IMongoCollection<BsonDocument> collection,
        CancellationToken ct)
    {
        try
        {
            using var cursor = await collection.SearchIndexes.ListAsync(
                    Infrastructure.Constants.Wire.Index,
                    cancellationToken: ct)
                .ConfigureAwait(false);
            return await cursor.FirstOrDefaultAsync(ct).ConfigureAwait(false);
        }
        catch (MongoCommandException error) when (error.Code == 26)
        {
            return null;
        }
        catch (MongoCommandException error)
        {
            throw MongoAtlasVectorClientManager.AtlasRequired(_route, error);
        }
    }

    private async Task<BsonDocument> WaitForIndex(
        IMongoCollection<BsonDocument> collection,
        CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(_options.IndexReadyTimeoutSeconds));
        Exception? last = null;
        while (!timeout.IsCancellationRequested)
        {
            try
            {
                var current = await ReadIndex(collection, timeout.Token).ConfigureAwait(false);
                if (current is not null)
                {
                    ValidateIndex(CollectionName(), current);
                    if (Ready(current)) return current;
                }
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested && timeout.IsCancellationRequested)
            {
                break;
            }
            catch (MongoConnectionException error) when (!timeout.IsCancellationRequested)
            {
                last = error;
            }
            try
            {
                await Task.Delay(_options.VisibilityPollMilliseconds, timeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                break;
            }
        }
        ct.ThrowIfCancellationRequested();
        throw new TimeoutException(
            $"Atlas Search index '{Infrastructure.Constants.Wire.Index}' for space '{_plan.Name}' did not become READY and queryable " +
            $"within {_options.IndexReadyTimeoutSeconds} seconds.", last);
    }

    private void ValidateIndex(string collection, BsonDocument index)
    {
        var definition = index.GetValue("latestDefinition", index.GetValue("definition", BsonNull.Value));
        if (!definition.IsBsonDocument)
            throw WrongShape(collection, $"index '{Infrastructure.Constants.Wire.Index}' has no readable definition");
        var definitionDocument = definition.AsBsonDocument;
        if (!definitionDocument.GetValue("analyzer", "").AsString.Equals("lucene.keyword", StringComparison.Ordinal) ||
            !definitionDocument.GetValue("searchAnalyzer", "").AsString.Equals("lucene.keyword", StringComparison.Ordinal))
            throw WrongShape(collection, $"index '{Infrastructure.Constants.Wire.Index}' must use the 'lucene.keyword' analyzer for exact filters");
        if (!definitionDocument.TryGetValue("mappings", out var mappingsValue) ||
            !mappingsValue.IsBsonDocument ||
            !mappingsValue.AsBsonDocument.GetValue("dynamic", false).ToBoolean() ||
            !mappingsValue.AsBsonDocument.TryGetValue("fields", out var fieldsValue) ||
            !fieldsValue.IsBsonDocument ||
            !fieldsValue.AsBsonDocument.TryGetValue(Infrastructure.Constants.Wire.Embedding, out var vectorValue) ||
            !vectorValue.IsBsonDocument)
            throw WrongShape(collection, $"index '{Infrastructure.Constants.Wire.Index}' must have dynamic mappings and the Koan embedding mapping");
        var vector = vectorValue.AsBsonDocument;
        var dimensions = vector.GetValue("numDimensions", -1).ToInt32();
        if (dimensions != _plan.Dimensions)
            throw WrongShape(collection, $"dimension is {dimensions}, expected {_plan.Dimensions}");
        var metric = vector.GetValue("similarity", "").AsString;
        if (!metric.Equals(Metric(), StringComparison.OrdinalIgnoreCase))
            throw WrongShape(collection, $"metric is '{metric}', expected '{Metric()}'");
        var type = vector.GetValue("type", "").AsString;
        if (!type.Equals("vector", StringComparison.OrdinalIgnoreCase))
            throw WrongShape(collection, $"embedding mapping type is '{type}', expected 'vector'");
    }

    private void ValidateMarker(string collection, BsonDocument marker)
    {
        if (marker.GetValue(ShapeVersionField, -1).ToInt32() != 1)
            throw WrongShape(collection, "shape marker version is incompatible");
        if (!marker.GetValue(ShapeSpaceField, "").AsString.Equals(_plan.Name, StringComparison.Ordinal))
            throw WrongShape(collection, $"space marker differs from '{_plan.Name}'");
        if (marker.GetValue(ShapeDimensionsField, -1).ToInt32() != _plan.Dimensions)
            throw WrongShape(collection, $"marker dimension differs from {_plan.Dimensions}");
        if (!marker.GetValue(ShapeMetricField, "").AsString.Equals(Metric(), StringComparison.OrdinalIgnoreCase))
            throw WrongShape(collection, $"marker metric differs from '{Metric()}'");
        var model = marker.GetValue(ShapeModelField, BsonNull.Value);
        if (_plan.Model is null ? !model.IsBsonNull : model.IsBsonNull || !model.AsString.Equals(_plan.Model, StringComparison.Ordinal))
            throw WrongShape(collection, $"model marker differs from '{_plan.Model ?? "<none>"}'");
    }

    private BsonDocument ShapeMarker() => new()
    {
        ["_id"] = Infrastructure.Constants.Wire.ShapeId,
        [ShapeVersionField] = 1,
        [ShapeSpaceField] = _plan.Name,
        [ShapeDimensionsField] = _plan.Dimensions,
        [ShapeMetricField] = Metric(),
        [ShapeModelField] = _plan.Model is null ? BsonNull.Value : _plan.Model
    };

    private BsonDocument IndexDefinition() => new()
    {
        ["analyzer"] = "lucene.keyword",
        ["searchAnalyzer"] = "lucene.keyword",
        ["mappings"] = new BsonDocument
        {
            ["dynamic"] = true,
            ["fields"] = new BsonDocument
            {
                [Infrastructure.Constants.Wire.Embedding] = new BsonDocument
                {
                    ["type"] = "vector",
                    ["numDimensions"] = _plan.Dimensions,
                    ["similarity"] = Metric()
                }
            }
        }
    };

    private async Task<List<RankedPoint>> VectorQuery(
        IMongoCollection<BsonDocument> collection,
        ReadOnlyMemory<float> embedding,
        int limit,
        BsonDocument? filter,
        CancellationToken ct)
    {
        var native = new BsonDocument
        {
            ["path"] = Infrastructure.Constants.Wire.Embedding,
            ["queryVector"] = Embedding(embedding.Span),
            ["exact"] = true,
            ["limit"] = limit
        };
        if (filter is not null) native["filter"] = filter;
        var stages = new[]
        {
            new BsonDocument("$search", new BsonDocument
            {
                ["index"] = Infrastructure.Constants.Wire.Index,
                ["vectorSearch"] = native
            }),
            new BsonDocument("$project", new BsonDocument
            {
                [Infrastructure.Constants.Wire.Id] = 1,
                [Infrastructure.Constants.Wire.Metadata] = 1,
                [Infrastructure.Constants.Wire.Score] = new BsonDocument("$meta", "searchScore")
            })
        };
        try
        {
            var pipeline = PipelineDefinition<BsonDocument, BsonDocument>.Create(stages);
            using var cursor = await collection.AggregateAsync(pipeline, cancellationToken: ct).ConfigureAwait(false);
            var documents = await cursor.ToListAsync(ct).ConfigureAwait(false);
            var output = new List<RankedPoint>(documents.Count);
            foreach (var document in documents)
            {
                if (!document.TryGetValue(Infrastructure.Constants.Wire.Id, out var idValue) || !idValue.IsString)
                    continue;
                var raw = document.GetValue(Infrastructure.Constants.Wire.Score, BsonNull.Value);
                if (!raw.IsNumeric)
                    throw new InvalidOperationException("Atlas Search returned a result without a numeric score.");
                var score = raw.ToDouble();
                if (!double.IsFinite(score))
                    throw new InvalidOperationException("Atlas Search returned a non-finite vector score.");
                var stable = idValue.AsString;
                output.Add(new RankedPoint(
                    ParseKey(stable),
                    stable,
                    score,
                    NormalizeScore(score),
                    document.TryGetValue(Infrastructure.Constants.Wire.Metadata, out var metadata) && metadata.IsString
                        ? VectorMetadata.FromJson(metadata.AsString)
                        : null));
            }
            return output;
        }
        catch (MongoCommandException error)
        {
            throw MongoAtlasVectorClientManager.AtlasRequired(_route, error);
        }
    }

    private double NormalizeScore(double nativeScore)
    {
        if (_plan.Metric != VectorMetric.Euclidean)
            return Math.Clamp(nativeScore, 0d, 1d);

        if (nativeScore <= 0d) return 0d;
        var squaredDistance = Math.Max(0d, (1d / nativeScore) - 1d);
        return 1d / (1d + Math.Sqrt(squaredDistance));
    }

    private async Task AwaitVisible(
        IMongoCollection<BsonDocument> collection,
        PreparedPoint point,
        bool visible,
        CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(_options.MutationVisibilityTimeoutSeconds));
        Exception? last = null;
        var filter = MongoAtlasVectorFilter.SearchText(
            visible ? Infrastructure.Constants.Wire.Generation : Infrastructure.Constants.Wire.Key,
            visible ? point.Generation : point.StorageId);
        while (!timeout.IsCancellationRequested)
        {
            try
            {
                var found = await VectorQuery(collection, point.Point.Embedding, 1, filter, timeout.Token)
                    .ConfigureAwait(false);
                if ((found.Count > 0) == visible) return;
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested && timeout.IsCancellationRequested)
            {
                break;
            }
            catch (MongoConnectionException error) when (!timeout.IsCancellationRequested)
            {
                last = error;
            }
            try
            {
                await Task.Delay(_options.VisibilityPollMilliseconds, timeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                break;
            }
        }
        ct.ThrowIfCancellationRequested();
        var accepted = visible ? "accepted" : "deleted";
        var expectation = visible ? "the accepted revision" : "the deletion";
        throw new TimeoutException(
            $"Mongo accepted the {accepted} point '{Key(point.Point.Id)}', but Atlas Search did not expose {expectation} " +
            $"within {_options.MutationVisibilityTimeoutSeconds} seconds. The primary mutation may already be durable.", last);
    }

    private PreparedPoint Prepare(VectorPoint<TKey> point, VectorScope scope)
    {
        ArgumentNullException.ThrowIfNull(point);
        ValidateEmbedding(point.Embedding.Span, "point embedding");
        ValidateMetadata(point.Metadata, scope);
        var metadata = VectorMetadata.ToJson(point.Metadata);
        if (metadata is not null && Encoding.UTF8.GetByteCount(metadata) > _options.MaxMetadataBytesPerPoint)
            throw new InvalidOperationException(
                $"MongoAtlasVector point metadata exceeds the configured {_options.MaxMetadataBytesPerPoint} byte limit.");
        var key = Key(point.Id);
        return new PreparedPoint(
            point,
            key,
            StorageId(key, scope),
            Guid.NewGuid().ToString("N"),
            metadata,
            MongoAtlasVectorFilter.Project(point.Metadata),
            scope.Identity);
    }

    private PreparedPoint PreparedFromStored(BsonDocument document, TKey id)
    {
        var embedding = document[Infrastructure.Constants.Wire.Embedding].AsBsonArray
            .Select(static value => (float)value.ToDouble()).ToArray();
        return new PreparedPoint(
            new VectorPoint<TKey>(id, embedding, null),
            Key(id),
            document["_id"].AsString,
            document[Infrastructure.Constants.Wire.Generation].AsString,
            null,
            new MongoAtlasVectorFilter.Projection([], [], [], new BsonDocument(), new BsonDocument()),
            document.GetValue(Infrastructure.Constants.Wire.Scope, "").AsString);
    }

    private static BsonDocument Document(PreparedPoint point) => new()
    {
        ["_id"] = point.StorageId,
        [Infrastructure.Constants.Wire.Id] = point.Key,
        [Infrastructure.Constants.Wire.Key] = point.StorageId,
        [Infrastructure.Constants.Wire.Scope] = point.Scope,
        [Infrastructure.Constants.Wire.Generation] = point.Generation,
        [Infrastructure.Constants.Wire.Embedding] = Embedding(point.Point.Embedding.Span),
        [Infrastructure.Constants.Wire.Metadata] = point.MetadataJson is null ? BsonNull.Value : point.MetadataJson,
        [Infrastructure.Constants.Wire.Scalar] = point.Projection.Scalar,
        [Infrastructure.Constants.Wire.Elements] = point.Projection.Elements,
        [Infrastructure.Constants.Wire.Present] = point.Projection.Present,
        [Infrastructure.Constants.Wire.Numeric] = point.Projection.Numeric,
        [Infrastructure.Constants.Wire.Size] = point.Projection.Size
    };

    private VectorPoint<TKey> ReadPoint(BsonDocument document, TKey id)
    {
        if (!document.TryGetValue(Infrastructure.Constants.Wire.Embedding, out var embedding) || !embedding.IsBsonArray)
            throw new InvalidOperationException($"Atlas vector point '{Key(id)}' has no Koan embedding.");
        var values = embedding.AsBsonArray.Select(static item => (float)item.ToDouble()).ToArray();
        if (values.Length != _plan.Dimensions)
            throw new InvalidOperationException(
                $"Atlas vector point '{Key(id)}' has {values.Length} dimensions; expected {_plan.Dimensions}.");
        var metadata = document.TryGetValue(Infrastructure.Constants.Wire.Metadata, out var stored) && stored.IsString
            ? VectorMetadata.FromJson(stored.AsString)
            : null;
        return new VectorPoint<TKey>(id, values, metadata);
    }

    private void Validate(VectorSearchRequest request)
    {
        ThrowIfDisposed();
        ValidateEmbedding(request.Embedding.Span, "query embedding");
        if (request.Top <= 0) throw new ArgumentOutOfRangeException(nameof(request), "Vector Top must be greater than zero.");
        if (request.Top > _options.MaxSearchCandidates)
            throw new ArgumentOutOfRangeException(nameof(request),
                $"Vector Top exceeds the configured {_options.MaxSearchCandidates} candidate bound.");
        if (request.Space is not null && !request.Space.Equals(_plan.Name, StringComparison.Ordinal))
            throw new InvalidOperationException(
                $"Vector space '{request.Space}' is not declared for '{typeof(TEntity).Name}'. Available space: {_plan.Name}.");
        if (request.Text is not null || request.SemanticWeight is not null)
            throw new NotSupportedException("MongoAtlasVector does not claim hybrid text/vector search.");
        if (request.Continuation is not null)
            throw new NotSupportedException("MongoAtlasVector does not claim native continuation tokens.");
        if (request.MinimumSimilarity is { } minimum &&
            (!double.IsFinite(minimum) || minimum is < 0d or > 1d))
            throw new ArgumentOutOfRangeException(nameof(request), "Minimum similarity must be inside [0,1].");
    }

    private void ValidateEmbedding(ReadOnlySpan<float> embedding, string label)
    {
        if (embedding.Length == 0) throw new ArgumentException($"MongoAtlasVector {label} cannot be empty.");
        if (embedding.Length != _plan.Dimensions)
            throw new ArgumentException(
                $"MongoAtlasVector {label} has {embedding.Length} dimensions; space '{_plan.Name}' requires {_plan.Dimensions}.");
        double norm = 0d;
        for (var index = 0; index < embedding.Length; index++)
        {
            if (!float.IsFinite(embedding[index]))
                throw new ArgumentException($"MongoAtlasVector {label} contains a non-finite value at index {index}.");
            norm += embedding[index] * (double)embedding[index];
        }
        if (_plan.Metric == VectorMetric.Cosine && norm == 0d)
            throw new ArgumentException("Cosine vector spaces do not accept a zero-magnitude embedding.");
    }

    private static void ValidateMetadata(DataObject? metadata, VectorScope scope)
    {
        if (metadata is null) return;
        var allowed = scope.Values.Properties.Select(static property => property.Name)
            .ToHashSet(StringComparer.Ordinal);
        ValidateMetadataObject(metadata, allowed, topLevel: true);
    }

    private static void ValidateMetadataObject(DataObject value, IReadOnlySet<string> allowed, bool topLevel)
    {
        foreach (var property in value.Properties)
        {
            if (property.Name.StartsWith("__koan", StringComparison.OrdinalIgnoreCase) &&
                !(topLevel && allowed.Contains(property.Name)))
                throw new InvalidOperationException(
                    $"Vector metadata key '{property.Name}' is reserved for Koan-managed scope values.");
            switch (property.Value)
            {
                case DataObject child:
                    ValidateMetadataObject(child, allowed, topLevel: false);
                    break;
                case DataArray array:
                    foreach (var item in array.Items)
                        if (item is DataObject nested) ValidateMetadataObject(nested, allowed, topLevel: false);
                    break;
            }
        }
    }

    private BsonDocument? NativeFilter(Filter? filter, string scopeIdentity)
    {
        var clauses = new List<BsonDocument>(2);
        if (!string.IsNullOrEmpty(scopeIdentity))
            clauses.Add(MongoAtlasVectorFilter.SearchText(Infrastructure.Constants.Wire.Scope, scopeIdentity));
        if (filter is not null) clauses.Add(MongoAtlasVectorFilter.CompileSearch(filter));
        return clauses.Count switch
        {
            0 => null,
            1 => clauses[0],
            _ => new BsonDocument("compound", new BsonDocument("filter", new BsonArray(clauses)))
        };
    }

    private static BsonDocument ScopeMatch(VectorScope scope)
    {
        var clauses = new List<BsonDocument>(2);
        if (!string.IsNullOrEmpty(scope.Identity))
            clauses.Add(new BsonDocument(Infrastructure.Constants.Wire.Scope, scope.Identity));
        if (scope.Predicate is not null) clauses.Add(MongoAtlasVectorFilter.CompileMatch(scope.Predicate));
        return And(clauses.ToArray());
    }

    private static BsonDocument ScopedIdentity(string storageId, VectorScope scope) =>
        And(new BsonDocument("_id", storageId), ScopeMatch(scope));

    private static BsonDocument And(params BsonDocument[] values)
    {
        var clauses = values.Where(static value => value.ElementCount > 0).ToArray();
        return clauses.Length switch
        {
            0 => new BsonDocument(),
            1 => clauses[0],
            _ => new BsonDocument("$and", new BsonArray(clauses))
        };
    }

    private async Task<HashSet<string>> ExistingIds(
        IMongoCollection<BsonDocument> collection,
        IEnumerable<string> ids,
        CancellationToken ct)
    {
        var values = ids.ToArray();
        var projection = Builders<BsonDocument>.Projection.Include("_id");
        var documents = await collection.Find(new BsonDocument("_id", new BsonDocument("$in", new BsonArray(values))))
            .Project(projection).ToListAsync(ct).ConfigureAwait(false);
        return documents.Select(static document => document["_id"].AsString)
            .ToHashSet(StringComparer.Ordinal);
    }

    private static async Task<bool> CollectionExists(
        IMongoDatabase database,
        string collection,
        CancellationToken ct)
    {
        using var cursor = await database.ListCollectionNamesAsync(
                new ListCollectionNamesOptions { Filter = new BsonDocument("name", collection) },
                ct)
            .ConfigureAwait(false);
        return await cursor.AnyAsync(ct).ConfigureAwait(false);
    }

    private static bool Ready(BsonDocument index) =>
        index.GetValue("queryable", false).ToBoolean() &&
        index.GetValue("status", "").AsString.Equals("READY", StringComparison.OrdinalIgnoreCase);

    private string CollectionName()
    {
        ThrowIfDisposed();
        return VectorAdapterNaming.GetOrCompute<TEntity>(_services, _factory, _plan.Source);
    }

    private string Metric() => _plan.Metric switch
    {
        VectorMetric.Cosine => "cosine",
        VectorMetric.Euclidean => "euclidean",
        VectorMetric.DotProduct => "dotProduct",
        _ => throw new NotSupportedException($"MongoAtlasVector does not support metric '{_plan.Metric}'.")
    };

    private static BsonArray Embedding(ReadOnlySpan<float> embedding)
    {
        var values = new BsonArray(embedding.Length);
        foreach (var value in embedding) values.Add((double)value);
        return values;
    }

    private static string StorageId(string key, VectorScope scope)
    {
        var input = Encoding.UTF8.GetBytes(scope.Identity + "\u001f" + key);
        return Convert.ToHexStringLower(SHA256.HashData(input));
    }

    private static string Key(TKey id)
    {
        ArgumentNullException.ThrowIfNull(id);
        return id switch
        {
            string value => value,
            Guid value => value.ToString("D"),
            IFormattable value => value.ToString(null, CultureInfo.InvariantCulture)
                ?? throw new InvalidOperationException($"Vector identity type '{typeof(TKey).FullName}' produced no stable value."),
            _ => id.ToString()
                ?? throw new InvalidOperationException($"Vector identity type '{typeof(TKey).FullName}' produced no stable value.")
        };
    }

    private static TKey ParseKey(string value)
    {
        if (typeof(TKey) == typeof(string)) return (TKey)(object)value;
        if (typeof(TKey) == typeof(Guid)) return (TKey)(object)Guid.ParseExact(value, "D");
        var converter = TypeDescriptor.GetConverter(typeof(TKey));
        if (converter.CanConvertFrom(typeof(string)))
            return (TKey)(converter.ConvertFromInvariantString(value)
                ?? throw new InvalidOperationException(
                    $"Vector identity '{value}' could not convert to '{typeof(TKey).FullName}'."));
        return (TKey)Convert.ChangeType(value, typeof(TKey), CultureInfo.InvariantCulture);
    }

    private void DemandBatch(int count)
    {
        if (count > _options.MaxBatchPoints)
            throw new ArgumentOutOfRangeException(nameof(count),
                $"MongoAtlasVector batch size {count} exceeds the configured {_options.MaxBatchPoints} point limit.");
    }

    private static void DemandUnique(IEnumerable<string> ids, string operation)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var id in ids)
            if (!seen.Add(id))
                throw new ArgumentException(
                    $"MongoAtlasVector {operation} batches require unique identities after scope compilation.");
    }

    private static BatchResult<TKey> Missing(IReadOnlyList<TKey> ids) => new(
        ids.Select((id, index) => new BatchItemResult<TKey>(index, id, MutationOutcome.Missing)).ToArray(),
        BatchAtomicity.NotGuaranteed);

    private VectorSearchResult<TKey> EmptyResult() => new(
        [],
        null,
        new VectorSearchExecution(_plan.Metric, VectorSearchAccuracy.Exact, null));

    private InvalidOperationException WrongShape(string collection, string detail) => new(
        $"MongoAtlasVector collection '{_route.Database}.{collection}' is incompatible with space '{_plan.Name}': {detail}. " +
        "Choose a new source/partition or repair the Atlas Search index and Koan shape marker before retrying.");

    private void ThrowIfDisposed() =>
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

    private sealed record PreparedPoint(
        VectorPoint<TKey> Point,
        string Key,
        string StorageId,
        string Generation,
        string? MetadataJson,
        MongoAtlasVectorFilter.Projection Projection,
        string Scope);

    private sealed record RankedPoint(
        TKey Id,
        string StableId,
        double RawScore,
        double Similarity,
        DataObject? Metadata);
}
