using System.Buffers;
using System.Collections.Concurrent;
using System.ComponentModel;
using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Koan.Core.Capabilities;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Sources;
using Koan.Data.Vector.Abstractions;
using Koan.Data.Vector.Abstractions.Capabilities;
using Koan.Data.Vector.Naming;

namespace Koan.Data.Vector.Connector.Chroma;

internal sealed class ChromaRepository<TEntity, TKey> :
    IVectorSearchRepository<TEntity, TKey>,
    IDescribesCapabilities,
    IDisposable,
    IAsyncDisposable
    where TEntity : class, IEntity<TKey>
    where TKey : notnull
{
    private readonly IServiceProvider _services;
    private readonly ChromaVectorAdapterFactory _factory;
    private readonly VectorSpacePlan _plan;
    private readonly ChromaRoute _route;
    private readonly ChromaOptions _options;
    private readonly ChromaClient _client;
    private readonly ConcurrentDictionary<string, string> _collections = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _shapeGate = new(1, 1);
    private int _disposed;

    internal ChromaRepository(
        IServiceProvider services,
        ChromaVectorAdapterFactory factory,
        VectorSpacePlan plan,
        ChromaRoute route,
        ChromaOptions options,
        IHttpClientFactory http)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        _plan = plan ?? throw new ArgumentNullException(nameof(plan));
        _route = route ?? throw new ArgumentNullException(nameof(route));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _client = new ChromaClient(http ?? throw new ArgumentNullException(nameof(http)), route, options);
    }

    public void Describe(ICapabilities capabilities) => capabilities
        .Add(VectorCaps.Knn)
        .Add(VectorCaps.Filters, ChromaFilter.Capabilities)
        .Add(VectorCaps.BulkUpsert)
        .Add(VectorCaps.BulkDelete)
        .Add(VectorCaps.ScoreNormalization)
        .Add(VectorCaps.DynamicCollections);

    public async Task Save(VectorPoint<TKey> point, VectorScope scope, CancellationToken ct = default)
    {
        var prepared = Prepare(point, scope);
        _route.Policy.Demand(DataOperationEffect.Write, $"save Chroma point in space '{_plan.Name}'");
        var collection = Collection();
        await EnsureShape(collection, create: true, ct).ConfigureAwait(false);
        await Upsert(collection, [prepared], "point save", ct).ConfigureAwait(false);
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
        if (prepared.Length == 0) return new BatchResult<TKey>([], BatchAtomicity.NotGuaranteed);

        _route.Policy.Demand(DataOperationEffect.Write, $"save Chroma batch in space '{_plan.Name}'");
        var collection = Collection();
        await EnsureShape(collection, create: true, ct).ConfigureAwait(false);
        var existing = await Fetch(collection, prepared.Select(static item => item.StorageId), scope, withEmbeddings: false, ct)
            .ConfigureAwait(false);
        var seen = existing.Keys.ToHashSet(StringComparer.Ordinal);
        var outcomes = new BatchItemResult<TKey>[prepared.Length];
        var final = new Dictionary<string, PreparedPoint>(StringComparer.Ordinal);
        for (var index = 0; index < prepared.Length; index++)
        {
            var item = prepared[index];
            var existed = !seen.Add(item.StorageId);
            outcomes[index] = new BatchItemResult<TKey>(index, item.Point.Id,
                existed ? MutationOutcome.Updated : MutationOutcome.Inserted);
            final[item.StorageId] = item;
        }
        await Upsert(collection, final.Values, "batch save", ct).ConfigureAwait(false);
        return new BatchResult<TKey>(outcomes, BatchAtomicity.NotGuaranteed);
    }

    public async Task<VectorPoint<TKey>?> Get(TKey id, VectorScope scope, CancellationToken ct = default)
    {
        var collection = Collection();
        if (!await EnsureShape(collection, create: false, ct).ConfigureAwait(false)) return null;
        var found = await Fetch(collection, [StorageId(id, scope)], scope, withEmbeddings: true, ct).ConfigureAwait(false);
        return found.TryGetValue(StorageId(id, scope), out var point) ? point : null;
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
        var collection = Collection();
        if (!await EnsureShape(collection, create: false, ct).ConfigureAwait(false)) return output;
        var storage = ids.Select(id => StorageId(id, scope)).ToArray();
        var found = await Fetch(collection, storage, scope, withEmbeddings: true, ct).ConfigureAwait(false);
        for (var index = 0; index < ids.Count; index++)
            if (found.TryGetValue(storage[index], out var point)) output[index] = point;
        return output;
    }

    public async Task<bool> Delete(TKey id, VectorScope scope, CancellationToken ct = default)
    {
        _route.Policy.Demand(DataOperationEffect.Write, $"delete Chroma point from space '{_plan.Name}'");
        var collection = Collection();
        if (!await EnsureShape(collection, create: false, ct).ConfigureAwait(false)) return false;
        var storageId = StorageId(id, scope);
        var existing = await Fetch(collection, [storageId], scope, withEmbeddings: false, ct).ConfigureAwait(false);
        if (!existing.ContainsKey(storageId)) return false;
        await DeleteIds(collection, [storageId], ct).ConfigureAwait(false);
        return true;
    }

    public async Task<BatchResult<TKey>> Delete(
        IReadOnlyList<TKey> ids,
        VectorScope scope,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ids);
        DemandBatch(ids.Count);
        if (ids.Count == 0) return new BatchResult<TKey>([], BatchAtomicity.NotGuaranteed);
        _route.Policy.Demand(DataOperationEffect.Write, $"delete Chroma batch from space '{_plan.Name}'");
        var collection = Collection();
        if (!await EnsureShape(collection, create: false, ct).ConfigureAwait(false))
            return Missing(ids);
        var storage = ids.Select(id => StorageId(id, scope)).ToArray();
        var existing = await Fetch(collection, storage, scope, withEmbeddings: false, ct).ConfigureAwait(false);
        var outcomes = new BatchItemResult<TKey>[ids.Count];
        var remove = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < ids.Count; index++)
        {
            var found = existing.ContainsKey(storage[index]);
            outcomes[index] = new BatchItemResult<TKey>(index, ids[index],
                found ? MutationOutcome.Deleted : MutationOutcome.Missing);
            if (found) remove.Add(storage[index]);
        }
        if (remove.Count > 0) await DeleteIds(collection, remove, ct).ConfigureAwait(false);
        return new BatchResult<TKey>(outcomes, BatchAtomicity.NotGuaranteed);
    }

    public async Task<VectorSearchResult<TKey>> Search(
        VectorSearchRequest request,
        VectorScope scope,
        CancellationToken ct = default)
    {
        Validate(request);
        var collection = Collection();
        if (!await EnsureShape(collection, create: false, ct).ConfigureAwait(false)) return Empty();
        var count = await CountCollection(collection, ct).ConfigureAwait(false);
        // A where-clause or a similarity threshold makes the cutoff depend on how many candidates the
        // store scanned, so those searches request the widest honest window instead of top+1.
        var wide = request.Filter is not null || request.MinimumSimilarity is not null;
        var ceiling = Math.Min(count, _options.MaxSearchCandidates);
        if (ceiling == 0) return Empty();
        var requested = wide ? ceiling : Math.Min(ceiling, checked(request.Top + 1));
        List<Ranked> ranked;
        while (true)
        {
            ranked = await Query(collection, request, requested, ct).ConfigureAwait(false);
            if (request.MinimumSimilarity is { } threshold)
                ranked.RemoveAll(item => item.Similarity < threshold);
            ranked.Sort(static (left, right) =>
            {
                var score = right.Similarity.CompareTo(left.Similarity);
                return score != 0 ? score : StringComparer.Ordinal.Compare(left.StableId, right.StableId);
            });
            if (ranked.Count <= request.Top || ranked.Count < requested || requested >= ceiling) break;
            if (ranked[request.Top - 1].Similarity != ranked[request.Top].Similarity) break;
            requested = Math.Min(ceiling, checked(requested * 2));
        }
        if (ranked.Count > request.Top && requested >= ceiling && ceiling < count &&
            ranked[request.Top - 1].Similarity == ranked[request.Top].Similarity)
            throw new InvalidOperationException(
                $"Chroma cannot resolve a stable identity tie within the configured bound of {_options.MaxSearchCandidates} candidates. " +
                "Increase MaxSearchCandidates or narrow the vector space.");
        var items = ranked
            .Take(request.Top)
            .Select(item => new VectorMatch<TKey>(item.Id, item.Similarity, item.Metadata))
            .ToArray();
        return new VectorSearchResult<TKey>(items, null,
            new VectorSearchExecution(_plan.Metric, VectorSearchAccuracy.Approximate, null));
    }

    public async Task Clear(VectorScope scope, CancellationToken ct = default)
    {
        _route.Policy.Demand(DataOperationEffect.Write, $"clear Chroma space '{_plan.Name}'");
        var collection = Collection();
        if (!await EnsureShape(collection, create: false, ct).ConfigureAwait(false)) return;
        var id = await RequireCollectionId(collection, ct).ConfigureAwait(false);
        var body = Json(writer =>
        {
            writer.WriteStartObject();
            writer.WritePropertyName("where");
            writer.WriteStartObject();
            WriteClearWhere(writer, scope);
            writer.WriteEndObject();
            writer.WriteEndObject();
        });
        await ExecuteAgainstCollection(collection, id,
            (collectionId, token) => _client.Post(
                ItemPath(collectionId, "delete"), body, "space clear", false, token), ct).ConfigureAwait(false);
    }

    public Task Sync(VectorScope scope, CancellationToken ct = default)
    {
        ThrowIfDisposed();
        ct.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }

    public Task VectorEnsureCreated(CancellationToken ct = default) => EnsureCreated(ct);

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _client.Dispose();
        _shapeGate.Dispose();
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }

    private async Task EnsureCreated(CancellationToken ct)
    {
        _ = await EnsureShape(Collection(), create: true, ct).ConfigureAwait(false);
    }

    private async Task<bool> EnsureShape(string collection, bool create, CancellationToken ct)
    {
        ThrowIfDisposed();
        if (_collections.ContainsKey(collection)) return true;
        await _shapeGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_collections.ContainsKey(collection)) return true;
            using var current = await _client.Get(
                CollectionPath(collection), "collection inspection", allowNotFound: true, ct).ConfigureAwait(false);
            if (current.IsNotFound)
            {
                if (!create) return false;
                _route.Policy.Demand(DataOperationEffect.SchemaOrAdmin,
                    $"create Chroma collection for space '{_plan.Name}'");
                using (var created = await _client.Post(
                    $"{_client.BasePath}/collections",
                    Json(writer => WriteCollection(writer, collection)),
                    "collection create",
                    allowNotFound: false,
                    ct,
                    allowConflict: true).ConfigureAwait(false))
                {
                }
            }
            using var confirmed = await _client.Get(
                CollectionPath(collection), "created collection inspection", allowNotFound: false, ct).ConfigureAwait(false);
            _collections[collection] = ValidateShape(collection, confirmed);
            return true;
        }
        finally
        {
            _shapeGate.Release();
        }
    }

    private void WriteCollection(Utf8JsonWriter writer, string collection)
    {
        writer.WriteStartObject();
        writer.WriteString("name", collection);
        writer.WriteBoolean("get_or_create", false);
        writer.WritePropertyName("configuration");
        writer.WriteStartObject();
        writer.WritePropertyName("hnsw");
        writer.WriteStartObject();
        writer.WriteString("space", Distance());
        writer.WriteEndObject();
        writer.WriteEndObject();
        writer.WritePropertyName("metadata");
        writer.WriteStartObject();
        writer.WriteString(Infrastructure.Constants.Wire.CollectionSpace, _plan.Name);
        if (_plan.Model is not null)
            writer.WriteString(Infrastructure.Constants.Wire.CollectionModel, _plan.Model);
        writer.WriteEndObject();
        writer.WriteEndObject();
    }

    private string ValidateShape(string collection, ChromaResponse response)
    {
        var root = response.Document?.RootElement
            ?? throw new InvalidOperationException($"Chroma returned no description for collection '{collection}'.");
        if (!root.TryGetProperty("configuration_json", out var configuration) ||
            !configuration.TryGetProperty("hnsw", out var hnsw) ||
            !hnsw.TryGetProperty("space", out var space))
            throw WrongShape(collection, "the declared vector index is absent");
        if (!string.Equals(space.GetString(), Distance(), StringComparison.OrdinalIgnoreCase))
            throw WrongShape(collection, $"metric is '{space.GetString()}', expected '{Distance()}'");
        if (root.TryGetProperty("dimension", out var dimension) && dimension.ValueKind == JsonValueKind.Number &&
            dimension.GetInt32() != _plan.Dimensions)
            throw WrongShape(collection, $"dimension is {dimension.GetInt32()}, expected {_plan.Dimensions}");
        if (_plan.Model is not null)
        {
            if (!root.TryGetProperty("metadata", out var metadata) ||
                !metadata.TryGetProperty(Infrastructure.Constants.Wire.CollectionModel, out var model) ||
                !string.Equals(model.GetString(), _plan.Model, StringComparison.Ordinal))
                throw WrongShape(collection, $"model metadata does not match '{_plan.Model}'");
        }
        return root.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String
            ? id.GetString() ?? throw WrongShape(collection, "the collection id is blank")
            : throw WrongShape(collection, "the collection id is absent");
    }

    private PreparedPoint Prepare(VectorPoint<TKey> point, VectorScope scope)
    {
        ArgumentNullException.ThrowIfNull(point);
        ValidateEmbedding(point.Embedding.Span, "point embedding");
        var metadata = VectorMetadata.ToJson(point.Metadata);
        if (metadata is not null && Encoding.UTF8.GetByteCount(metadata) > _options.MaxMetadataBytesPerPoint)
            throw new InvalidOperationException(
                $"Chroma point metadata exceeds the configured {_options.MaxMetadataBytesPerPoint} byte limit.");
        if (_plan.Metric == VectorMetric.Cosine && Norm(point.Embedding.Span) == 0d)
            throw new ArgumentException("Cosine vector spaces do not accept a zero-magnitude embedding.", nameof(point));
        return new PreparedPoint(point, StorageId(point.Id, scope), Key(point.Id), scope.Identity, metadata);
    }

    private async Task Upsert(string collection, IReadOnlyCollection<PreparedPoint> points, string operation, CancellationToken ct)
    {
        var body = Json(writer =>
        {
            writer.WriteStartObject();
            writer.WritePropertyName("ids");
            writer.WriteStartArray();
            foreach (var point in points) writer.WriteStringValue(point.StorageId);
            writer.WriteEndArray();
            writer.WritePropertyName("embeddings");
            writer.WriteStartArray();
            foreach (var point in points)
            {
                writer.WriteStartArray();
                foreach (var value in point.Point.Embedding.Span) writer.WriteNumberValue(value);
                writer.WriteEndArray();
            }
            writer.WriteEndArray();
            writer.WritePropertyName("metadatas");
            writer.WriteStartArray();
            foreach (var point in points)
                ChromaFilter.WritePointMetadata(writer, point.OriginalKey, point.Scope, point.MetadataJson, point.Point.Metadata);
            writer.WriteEndArray();
            writer.WriteNull("documents");
            writer.WriteEndObject();
        });
        await ExecuteAgainstCollection(collection, null,
            (collectionId, token) => _client.Post(
                ItemPath(collectionId, "upsert"), body, operation, false, token), ct).ConfigureAwait(false);
    }

    private async Task<Dictionary<string, VectorPoint<TKey>>> Fetch(
        string collection,
        IEnumerable<string> storageIds,
        VectorScope scope,
        bool withEmbeddings,
        CancellationToken ct)
    {
        var ids = storageIds.ToArray();
        var body = Json(writer =>
        {
            writer.WriteStartObject();
            writer.WritePropertyName("ids");
            writer.WriteStartArray();
            foreach (var id in ids) writer.WriteStringValue(id);
            writer.WriteEndArray();
            var scopeMatch = ScopeWhere(scope);
            if (scopeMatch is not null)
            {
                writer.WritePropertyName("where");
                writer.WriteStartObject();
                scopeMatch(writer);
                writer.WriteEndObject();
            }
            writer.WritePropertyName("include");
            writer.WriteStartArray();
            writer.WriteStringValue("metadatas");
            if (withEmbeddings) writer.WriteStringValue("embeddings");
            writer.WriteEndArray();
            writer.WriteEndObject();
        });
        var response = await ExecuteAgainstCollection(collection, null,
            (collectionId, token) => _client.Post(
                ItemPath(collectionId, "get"), body, "point retrieval", false, token), ct).ConfigureAwait(false);
        var result = Result(response, "point retrieval");
        if (!result.TryGetProperty("ids", out var returned) || returned.ValueKind != JsonValueKind.Array)
            throw new InvalidOperationException("Chroma point retrieval returned an invalid result shape.");
        var embeddings = result.TryGetProperty("embeddings", out var storedEmbeddings) &&
            storedEmbeddings.ValueKind == JsonValueKind.Array
                ? storedEmbeddings
                : default;
        var metadatas = result.TryGetProperty("metadatas", out var storedMetadatas) &&
            storedMetadatas.ValueKind == JsonValueKind.Array
                ? storedMetadatas
                : default;
        var output = new Dictionary<string, VectorPoint<TKey>>(StringComparer.Ordinal);
        var index = 0;
        foreach (var storageId in returned.EnumerateArray())
        {
            var metadata = metadatas.ValueKind == JsonValueKind.Array && index < metadatas.GetArrayLength()
                ? metadatas[index]
                : default;
            var vector = embeddings.ValueKind == JsonValueKind.Array && index < embeddings.GetArrayLength()
                ? embeddings[index]
                : default;
            output[storageId.GetString()
                ?? throw new InvalidOperationException("Chroma returned a blank point identity.")] =
                ReadPoint(metadata, vector, withEmbeddings);
            index++;
        }
        return output;
    }

    private VectorPoint<TKey> ReadPoint(JsonElement metadata, JsonElement vector, bool withEmbeddings)
    {
        string? originalKey = null;
        DataObject? neutral = null;
        if (metadata.ValueKind == JsonValueKind.Object)
        {
            if (metadata.TryGetProperty(Infrastructure.Constants.Wire.Id, out var storedKey) &&
                storedKey.ValueKind == JsonValueKind.String)
                originalKey = storedKey.GetString();
            if (metadata.TryGetProperty(Infrastructure.Constants.Wire.Metadata, out var stored) &&
                stored.ValueKind == JsonValueKind.String)
                neutral = VectorMetadata.FromJson(stored.GetString());
        }
        if (originalKey is null)
            throw new InvalidOperationException("Chroma point has no Koan identity metadata.");
        if (!withEmbeddings) return new VectorPoint<TKey>(ParseKey(originalKey), Array.Empty<float>(), neutral);
        if (vector.ValueKind != JsonValueKind.Array)
            throw new InvalidOperationException($"Chroma point '{originalKey}' has no embedding.");
        var values = vector.EnumerateArray().Select(static value => value.GetSingle()).ToArray();
        if (values.Length != _plan.Dimensions)
            throw new InvalidOperationException(
                $"Chroma point '{originalKey}' has {values.Length} dimensions; expected {_plan.Dimensions}.");
        return new VectorPoint<TKey>(ParseKey(originalKey), values, neutral);
    }

    private async Task<List<Ranked>> Query(string collection, VectorSearchRequest request, int candidates, CancellationToken ct)
    {
        var body = Json(writer =>
        {
            writer.WriteStartObject();
            writer.WritePropertyName("query_embeddings");
            writer.WriteStartArray();
            writer.WriteStartArray();
            foreach (var value in request.Embedding.Span) writer.WriteNumberValue(value);
            writer.WriteEndArray();
            writer.WriteEndArray();
            writer.WriteNumber("n_results", candidates);
            if (request.Filter is not null)
            {
                writer.WritePropertyName("where");
                ChromaFilter.Write(writer, request.Filter);
            }
            writer.WritePropertyName("include");
            writer.WriteStartArray();
            writer.WriteStringValue("metadatas");
            writer.WriteStringValue("distances");
            writer.WriteEndArray();
            writer.WriteEndObject();
        });
        var response = await ExecuteAgainstCollection(collection, null,
            (collectionId, token) => _client.Post(
                ItemPath(collectionId, "query"), body, "vector query", false, token), ct).ConfigureAwait(false);
        var result = Result(response, "vector query");
        if (!result.TryGetProperty("ids", out var ids) || ids.ValueKind != JsonValueKind.Array || ids.GetArrayLength() != 1 ||
            !result.TryGetProperty("distances", out var distances) || distances.ValueKind != JsonValueKind.Array ||
            distances.GetArrayLength() != 1)
            throw new InvalidOperationException("Chroma vector query returned an invalid result shape.");
        var points = ids[0];
        var scores = distances[0];
        var metadatas = result.TryGetProperty("metadatas", out var storedMetadatas) &&
            storedMetadatas.ValueKind == JsonValueKind.Array && storedMetadatas.GetArrayLength() == 1
                ? storedMetadatas[0]
                : default;
        if (points.ValueKind != JsonValueKind.Array || scores.ValueKind != JsonValueKind.Array ||
            points.GetArrayLength() != scores.GetArrayLength())
            throw new InvalidOperationException("Chroma vector query returned misaligned results.");
        var output = new List<Ranked>(points.GetArrayLength());
        var index = 0;
        foreach (var storageId in points.EnumerateArray())
        {
            var metadata = metadatas.ValueKind == JsonValueKind.Array && index < metadatas.GetArrayLength()
                ? metadatas[index]
                : default;
            string? originalKey = null;
            DataObject? neutral = null;
            if (metadata.ValueKind == JsonValueKind.Object)
            {
                if (metadata.TryGetProperty(Infrastructure.Constants.Wire.Id, out var storedKey) &&
                    storedKey.ValueKind == JsonValueKind.String)
                    originalKey = storedKey.GetString();
                if (metadata.TryGetProperty(Infrastructure.Constants.Wire.Metadata, out var stored) &&
                    stored.ValueKind == JsonValueKind.String)
                    neutral = VectorMetadata.FromJson(stored.GetString());
            }
            if (originalKey is null)
                throw new InvalidOperationException("Chroma query returned a point without its Koan identity metadata.");
            var raw = scores[index].GetDouble();
            output.Add(new Ranked(ParseKey(originalKey), originalKey, raw, Similarity(raw), neutral));
            index++;
        }
        return output;
    }

    private async Task DeleteIds(string collection, IEnumerable<string> ids, CancellationToken ct)
    {
        var idList = ids.ToArray();
        var body = Json(writer =>
        {
            writer.WriteStartObject();
            writer.WritePropertyName("ids");
            writer.WriteStartArray();
            foreach (var id in idList) writer.WriteStringValue(id);
            writer.WriteEndArray();
            writer.WriteEndObject();
        });
        await ExecuteAgainstCollection(collection, null,
            (collectionId, token) => _client.Post(
                ItemPath(collectionId, "delete"), body, "point delete", false, token), ct).ConfigureAwait(false);
    }

    /// <summary>Runs one collection-item call, refreshing the cached collection id once when the
    /// collection was deleted and recreated underneath us (its id changes on recreation).</summary>
    private async Task<T> ExecuteAgainstCollection<T>(
        string collection,
        string? knownId,
        Func<string, CancellationToken, Task<T>> operation,
        CancellationToken ct)
    {
        ThrowIfDisposed();
        var id = knownId ?? await RequireCollectionId(collection, ct).ConfigureAwait(false);
        try
        {
            return await operation(id, ct).ConfigureAwait(false);
        }
        catch (HttpRequestException error) when (error.StatusCode == HttpStatusCode.NotFound)
        {
            _collections.TryRemove(collection, out _);
            id = await RequireCollectionId(collection, ct).ConfigureAwait(false);
            return await operation(id, ct).ConfigureAwait(false);
        }
    }

    private async Task<string> RequireCollectionId(string collection, CancellationToken ct)
    {
        if (_collections.TryGetValue(collection, out var cached)) return cached;
        await _shapeGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_collections.TryGetValue(collection, out var cachedTwice)) return cachedTwice;
            using var current = await _client.Get(
                CollectionPath(collection), "collection inspection", allowNotFound: false, ct).ConfigureAwait(false);
            var id = ValidateShape(collection, current);
            _collections[collection] = id;
            return id;
        }
        finally
        {
            _shapeGate.Release();
        }
    }

    private async Task<int> CountCollection(string collection, CancellationToken ct)
    {
        var response = await ExecuteAgainstCollection(collection, null,
            (collectionId, token) => _client.Get(
                ItemPath(collectionId, "count"), "collection count", false, token), ct).ConfigureAwait(false);
        var root = response.Document?.RootElement
            ?? throw new InvalidOperationException("Chroma returned no collection count.");
        if (root.ValueKind != JsonValueKind.Number)
            throw new InvalidOperationException("Chroma returned an invalid collection count.");
        var count = root.GetInt64();
        return count < 0 || count > int.MaxValue
            ? throw new InvalidOperationException($"Chroma reported an out-of-range collection count {count}.")
            : (int)count;
    }

    private void WriteClearWhere(Utf8JsonWriter writer, VectorScope scope)
    {
        // Chroma refuses an empty where-clause, so "everything in scope" is spelled as the always-true
        // reserved-key predicate combined with any scope identity and scope predicate.
        var scopeMatch = ScopeWhere(scope);
        if (scope.Predicate is null && scopeMatch is null)
        {
            WriteAlwaysTrue(writer);
            return;
        }
        writer.WritePropertyName("$and");
        writer.WriteStartArray();
        writer.WriteStartObject();
        WriteAlwaysTrue(writer);
        writer.WriteEndObject();
        if (scopeMatch is not null)
        {
            writer.WriteStartObject();
            scopeMatch(writer);
            writer.WriteEndObject();
        }
        if (scope.Predicate is not null)
        {
            writer.WriteStartObject();
            ChromaFilter.Write(writer, scope.Predicate);
            writer.WriteEndObject();
        }
        writer.WriteEndArray();
    }

    private static void WriteAlwaysTrue(Utf8JsonWriter writer)
    {
        writer.WritePropertyName(Infrastructure.Constants.Wire.Id);
        writer.WriteStartObject();
        writer.WritePropertyName("$ne");
        writer.WriteStringValue(string.Empty);
        writer.WriteEndObject();
    }

    /// <summary>The read-isolation where-clause for a scoped by-id fetch: the compiled scope identity
    /// must match the stamped storage scope.</summary>
    private static Action<Utf8JsonWriter>? ScopeWhere(VectorScope scope)
    {
        if (string.IsNullOrEmpty(scope.Identity)) return null;
        return writer =>
        {
            writer.WritePropertyName(Infrastructure.Constants.Wire.Scope);
            writer.WriteStartObject();
            writer.WritePropertyName("$eq");
            writer.WriteStringValue(scope.Identity);
            writer.WriteEndObject();
        };
    }

    private void Validate(VectorSearchRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateEmbedding(request.Embedding.Span, "query embedding");
        if (request.Top <= 0 || request.Top >= _options.MaxSearchCandidates)
            throw new ArgumentOutOfRangeException(nameof(request),
                $"Chroma Top must be positive and smaller than MaxSearchCandidates ({_options.MaxSearchCandidates}).");
        if (request.Space is not null && !string.Equals(request.Space, _plan.Name, StringComparison.Ordinal))
            throw new InvalidOperationException(
                $"Chroma query requested space '{request.Space}'. Available space: {_plan.Name}.");
        if (request.Text is not null || request.SemanticWeight is not null)
            throw new NotSupportedException("Chroma adapter does not claim portable hybrid text semantics.");
        if (request.Continuation is not null)
            throw new NotSupportedException("Chroma adapter does not claim a stable search continuation snapshot.");
        if (request.MinimumSimilarity is < 0d or > 1d ||
            request.MinimumSimilarity is { } minimum && !double.IsFinite(minimum))
            throw new ArgumentOutOfRangeException(nameof(request), "Minimum similarity must be finite and in [0,1].");
    }

    private void ValidateEmbedding(ReadOnlySpan<float> embedding, string label)
    {
        if (embedding.Length != _plan.Dimensions)
            throw new ArgumentException(
                $"Chroma {label} has {embedding.Length} dimensions; space '{_plan.Name}' requires {_plan.Dimensions}.");
        for (var index = 0; index < embedding.Length; index++)
            if (!float.IsFinite(embedding[index]))
                throw new ArgumentException($"Chroma {label} contains a non-finite value at index {index}.");
        if (_plan.Metric == VectorMetric.Cosine && Norm(embedding) == 0d)
            throw new ArgumentException("Cosine vector spaces do not accept a zero-magnitude embedding.");
    }

    private string StorageId(TKey id, VectorScope scope)
    {
        var key = Key(id);
        // Scope-compiled identities fold into a deterministic uuid so a scoped row never collides with
        // its unscoped twin; Chroma ids accept any non-blank string, so plain keys stay verbatim.
        return string.IsNullOrEmpty(scope.Identity) ? key : UuidV5($"scope:{scope.Identity}\u001f{key}").ToString("D");
    }

    private Guid UuidV5(string value)
    {
        var namespaceBytes = Infrastructure.Constants.ScopeIdNamespace.ToByteArray();
        SwapByteOrder(namespaceBytes);
        var valueBytes = Encoding.UTF8.GetBytes(value);
        var input = new byte[namespaceBytes.Length + valueBytes.Length];
        namespaceBytes.CopyTo(input, 0);
        valueBytes.CopyTo(input, namespaceBytes.Length);
        var hash = SHA1.HashData(input);
        hash[6] = (byte)((hash[6] & 0x0f) | 0x50);
        hash[8] = (byte)((hash[8] & 0x3f) | 0x80);
        var guid = hash.AsSpan(0, 16).ToArray();
        SwapByteOrder(guid);
        return new Guid(guid);
    }

    private static void SwapByteOrder(byte[] guid)
    {
        (guid[0], guid[3]) = (guid[3], guid[0]);
        (guid[1], guid[2]) = (guid[2], guid[1]);
        (guid[4], guid[5]) = (guid[5], guid[4]);
        (guid[6], guid[7]) = (guid[7], guid[6]);
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
                ?? throw new InvalidOperationException($"Vector identity '{value}' could not convert to '{typeof(TKey).FullName}'."));
        return (TKey)Convert.ChangeType(value, typeof(TKey), CultureInfo.InvariantCulture);
    }

    private string Collection()
    {
        ThrowIfDisposed();
        return VectorAdapterNaming.GetOrCompute<TEntity>(_services, _factory, _plan.Source);
    }

    private string CollectionPath(string collection) =>
        $"{_client.BasePath}/collections/{ChromaClient.Escape(collection)}";

    private string ItemPath(string collectionId, string operation) =>
        $"{_client.BasePath}/collections/{ChromaClient.Escape(collectionId)}/{operation}";

    private string Distance() => _plan.Metric switch
    {
        VectorMetric.Cosine => "cosine",
        VectorMetric.Euclidean => "l2",
        VectorMetric.DotProduct => "ip",
        _ => throw new NotSupportedException($"Chroma does not support metric '{_plan.Metric}'.")
    };

    /// <summary>Maps one raw Chroma distance onto the shared [0,1] higher-is-closer similarity scale.
    /// Chroma distances: cosine = 1−sim ∈ [0,2]; l2 = squared Euclidean; ip = 1−inner-product.</summary>
    private double Similarity(double distance)
    {
        var value = _plan.Metric switch
        {
            VectorMetric.Cosine => 1d - (distance / 2d),
            VectorMetric.Euclidean => 1d / (1d + Math.Max(0d, distance)),
            VectorMetric.DotProduct => 1d / (1d + Math.Exp(-(1d - distance))),
            _ => throw new NotSupportedException()
        };
        return Math.Clamp(double.IsFinite(value) ? value : distance <= 0d ? 1d : 0d, 0d, 1d);
    }

    private static double Norm(ReadOnlySpan<float> embedding)
    {
        double squared = 0d;
        foreach (var value in embedding) squared += value * (double)value;
        return Math.Sqrt(squared);
    }

    private static JsonElement Result(ChromaResponse response, string operation)
    {
        var root = response.Document?.RootElement
            ?? throw new InvalidOperationException($"Chroma returned no JSON result for {operation}.");
        return root;
    }

    private static byte[] Json(Action<Utf8JsonWriter> write)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer)) write(writer);
        return buffer.WrittenSpan.ToArray();
    }

    private void DemandBatch(int count)
    {
        if (count > _options.MaxBatchPoints)
            throw new ArgumentOutOfRangeException(nameof(count),
                $"Chroma batch contains {count} points; configured maximum is {_options.MaxBatchPoints}.");
    }

    private static BatchResult<TKey> Missing(IReadOnlyList<TKey> ids) => new(
        ids.Select((id, index) => new BatchItemResult<TKey>(index, id, MutationOutcome.Missing)),
        BatchAtomicity.NotGuaranteed);

    private VectorSearchResult<TKey> Empty() => new(
        [], null, new VectorSearchExecution(_plan.Metric, VectorSearchAccuracy.Approximate, 0));

    private InvalidOperationException WrongShape(string collection, string reason) => new(
        $"Chroma collection '{collection}' cannot realize space '{_plan.Name}': {reason}. " +
        "Provision the declared shape or select the source that owns this collection.");

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

    private sealed record PreparedPoint(
        VectorPoint<TKey> Point,
        string StorageId,
        string OriginalKey,
        string Scope,
        string? MetadataJson);

    private sealed record Ranked(TKey Id, string StableId, double RawDistance, double Similarity, DataObject? Metadata);
}
