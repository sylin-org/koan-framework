using System.Buffers;
using System.Collections.Concurrent;
using System.ComponentModel;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Koan.Core.Capabilities;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Sources;
using Koan.Data.Vector.Abstractions;
using Koan.Data.Vector.Abstractions.Capabilities;
using Koan.Data.Vector.Naming;

namespace Koan.Data.Vector.Connector.Weaviate;

internal sealed class WeaviateRepository<TEntity, TKey> :
    IVectorSearchRepository<TEntity, TKey>,
    IDescribesCapabilities,
    IDisposable,
    IAsyncDisposable
    where TEntity : class, IEntity<TKey>
    where TKey : notnull
{
    private readonly IServiceProvider _services;
    private readonly WeaviateVectorAdapterFactory _factory;
    private readonly VectorSpacePlan _plan;
    private readonly WeaviateRoute _route;
    private readonly WeaviateOptions _options;
    private readonly WeaviateClient _client;
    private readonly ConcurrentDictionary<string, byte> _ready = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _shapeGate = new(1, 1);
    private int _disposed;

    internal WeaviateRepository(
        IServiceProvider services,
        WeaviateVectorAdapterFactory factory,
        VectorSpacePlan plan,
        WeaviateRoute route,
        WeaviateOptions options,
        IHttpClientFactory http)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        _plan = plan ?? throw new ArgumentNullException(nameof(plan));
        _route = route ?? throw new ArgumentNullException(nameof(route));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _client = new WeaviateClient(http ?? throw new ArgumentNullException(nameof(http)), route, options);
    }

    public void Describe(ICapabilities capabilities) => capabilities
        .Add(VectorCaps.Knn)
        .Add(VectorCaps.Filters, WeaviateFilter.Capabilities)
        .Add(VectorCaps.ScoreNormalization)
        .Add(VectorCaps.DynamicCollections);

    public async Task Save(VectorPoint<TKey> point, VectorScope scope, CancellationToken ct = default)
    {
        var prepared = Prepare(point, scope);
        _route.Policy.Demand(DataOperationEffect.Write, $"save Weaviate point in space '{_plan.Name}'");
        var collection = Collection();
        await EnsureShape(collection, create: true, ct).ConfigureAwait(false);
        await SavePrepared(collection, prepared, ct).ConfigureAwait(false);
        await Sync(scope, ct).ConfigureAwait(false);
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

        _route.Policy.Demand(DataOperationEffect.Write, $"save Weaviate batch in space '{_plan.Name}'");
        var collection = Collection();
        await EnsureShape(collection, create: true, ct).ConfigureAwait(false);
        var existing = await Fetch(collection, prepared.Select(static item => item.Point.Id).ToArray(), scope, ct)
            .ConfigureAwait(false);
        var seen = existing.Keys.ToHashSet(StringComparer.Ordinal);
        var outcomes = new BatchItemResult<TKey>[prepared.Length];
        for (var index = 0; index < prepared.Length; index++)
        {
            var item = prepared[index];
            outcomes[index] = new BatchItemResult<TKey>(index, item.Point.Id,
                seen.Add(item.StorageId) ? MutationOutcome.Inserted : MutationOutcome.Updated);
            await SavePrepared(collection, item, ct).ConfigureAwait(false);
        }
        await Sync(scope, ct).ConfigureAwait(false);
        return new BatchResult<TKey>(outcomes, BatchAtomicity.NotGuaranteed);
    }

    public async Task<VectorPoint<TKey>?> Get(TKey id, VectorScope scope, CancellationToken ct = default)
    {
        var collection = Collection();
        if (!await EnsureShape(collection, create: false, ct).ConfigureAwait(false)) return null;
        return await FetchOne(collection, id, scope, ct).ConfigureAwait(false);
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
        var unique = ids.Distinct().ToArray();
        var tasks = unique.Select(id => FetchOne(collection, id, scope, ct)).ToArray();
        var values = await Task.WhenAll(tasks).ConfigureAwait(false);
        var found = new Dictionary<TKey, VectorPoint<TKey>>();
        for (var index = 0; index < unique.Length; index++)
            if (values[index] is { } point) found[unique[index]] = point;
        for (var index = 0; index < ids.Count; index++)
            if (found.TryGetValue(ids[index], out var point)) output[index] = point;
        return output;
    }

    public async Task<bool> Delete(TKey id, VectorScope scope, CancellationToken ct = default)
    {
        _route.Policy.Demand(DataOperationEffect.Write, $"delete Weaviate point from space '{_plan.Name}'");
        var collection = Collection();
        if (!await EnsureShape(collection, create: false, ct).ConfigureAwait(false)) return false;
        if (await FetchOne(collection, id, scope, ct).ConfigureAwait(false) is null) return false;
        await DeleteStorageId(collection, StorageId(id, scope), ct).ConfigureAwait(false);
        await Sync(scope, ct).ConfigureAwait(false);
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
        _route.Policy.Demand(DataOperationEffect.Write, $"delete Weaviate batch from space '{_plan.Name}'");
        var collection = Collection();
        if (!await EnsureShape(collection, create: false, ct).ConfigureAwait(false)) return Missing(ids);
        var existing = await Fetch(collection, ids, scope, ct).ConfigureAwait(false);
        var outcomes = new BatchItemResult<TKey>[ids.Count];
        var removed = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < ids.Count; index++)
        {
            var storageId = StorageId(ids[index], scope);
            var found = existing.ContainsKey(storageId) && removed.Add(storageId);
            outcomes[index] = new BatchItemResult<TKey>(index, ids[index],
                found ? MutationOutcome.Deleted : MutationOutcome.Missing);
            if (found) await DeleteStorageId(collection, storageId, ct).ConfigureAwait(false);
        }
        await Sync(scope, ct).ConfigureAwait(false);
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
        var requested = Math.Min(_options.MaxSearchCandidates, checked(request.Top + 1));
        List<Ranked> ranked;
        while (true)
        {
            ranked = await Query(collection, request, requested, ct).ConfigureAwait(false);
            ranked.Sort(static (left, right) =>
            {
                var score = right.Similarity.CompareTo(left.Similarity);
                return score != 0 ? score : StringComparer.Ordinal.Compare(left.StableId, right.StableId);
            });
            if (ranked.Count <= request.Top || ranked.Count < requested ||
                ranked[request.Top - 1].RawDistance != ranked[request.Top].RawDistance)
                break;
            if (requested >= _options.MaxSearchCandidates)
                throw new InvalidOperationException(
                    $"Weaviate cannot resolve a stable identity tie within the configured bound of {_options.MaxSearchCandidates} candidates.");
            requested = Math.Min(_options.MaxSearchCandidates, checked(requested * 2));
        }
        var items = ranked
            .Where(item => request.MinimumSimilarity is null || item.Similarity >= request.MinimumSimilarity.Value)
            .Take(request.Top)
            .Select(item => new VectorMatch<TKey>(item.Id, item.Similarity, item.Metadata))
            .ToArray();
        return new VectorSearchResult<TKey>(items, null,
            new VectorSearchExecution(_plan.Metric, VectorSearchAccuracy.Approximate, null));
    }

    public async Task Clear(VectorScope scope, CancellationToken ct = default)
    {
        _route.Policy.Demand(DataOperationEffect.Write, $"clear Weaviate space '{_plan.Name}'");
        var collection = Collection();
        if (!await EnsureShape(collection, create: false, ct).ConfigureAwait(false)) return;
        var ids = await QueryStorageIds(collection, scope.Predicate, _options.MaxClearPoints + 1, ct)
            .ConfigureAwait(false);
        if (ids.Count > _options.MaxClearPoints)
            throw new InvalidOperationException(
                $"Weaviate clear exceeds the configured {_options.MaxClearPoints} point safety bound.");
        foreach (var id in ids) await DeleteStorageId(collection, id, ct).ConfigureAwait(false);
        await Sync(scope, ct).ConfigureAwait(false);
    }

    public async Task Sync(VectorScope scope, CancellationToken ct = default)
    {
        ThrowIfDisposed();
        var collection = Collection();
        if (!await EnsureShape(collection, create: false, ct).ConfigureAwait(false)) return;
        var deadline = DateTime.UtcNow.AddSeconds(_options.VisibilityTimeoutSeconds);
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            using var response = await _client.Get(
                $"v1/nodes/{Escape(collection)}?output=verbose", "vector visibility", false, ct)
                .ConfigureAwait(false);
            if (IsIndexReady(response)) return;
            if (DateTime.UtcNow >= deadline)
                throw new TimeoutException(
                    $"Weaviate did not make space '{_plan.Name}' visible within {_options.VisibilityTimeoutSeconds} seconds.");
            await Task.Delay(Infrastructure.Constants.Defaults.VisibilityPollMilliseconds, ct).ConfigureAwait(false);
        }
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

    private async Task EnsureCreated(CancellationToken ct) =>
        _ = await EnsureShape(Collection(), create: true, ct).ConfigureAwait(false);

    private async Task<bool> EnsureShape(string collection, bool create, CancellationToken ct)
    {
        ThrowIfDisposed();
        if (_ready.ContainsKey(collection)) return true;
        await _shapeGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_ready.ContainsKey(collection)) return true;
            using var current = await _client.Get(
                $"v1/schema/{Escape(collection)}", "collection inspection", true, ct).ConfigureAwait(false);
            if (!current.IsNotFound)
            {
                ValidateShape(collection, current);
                _ready.TryAdd(collection, 0);
                return true;
            }
            if (!create) return false;
            _route.Policy.Demand(DataOperationEffect.SchemaOrAdmin,
                $"create Weaviate collection for space '{_plan.Name}'");
            using (var created = await _client.Post(
                "v1/schema", Json(writer => WriteCollection(writer, collection)),
                "collection create", allowConflict: true, ct).ConfigureAwait(false))
            {
            }
            using var confirmed = await _client.Get(
                $"v1/schema/{Escape(collection)}", "created collection inspection", false, ct)
                .ConfigureAwait(false);
            ValidateShape(collection, confirmed);
            _ready.TryAdd(collection, 0);
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
        writer.WriteString("class", collection);
        writer.WriteString("description", ContractMarker());
        writer.WriteString("vectorizer", "none");
        writer.WriteString("vectorIndexType", "hnsw");
        writer.WritePropertyName("vectorIndexConfig");
        writer.WriteStartObject();
        writer.WriteString("distance", Distance());
        writer.WriteEndObject();
        writer.WritePropertyName("invertedIndexConfig");
        writer.WriteStartObject();
        writer.WriteBoolean("indexNullState", true);
        writer.WriteEndObject();
        writer.WritePropertyName("properties");
        writer.WriteStartArray();
        WriteProperty(writer, Infrastructure.Constants.Wire.Id, "text", tokenization: true);
        WriteProperty(writer, Infrastructure.Constants.Wire.Metadata, "blob", tokenization: false);
        WriteProperty(writer, Infrastructure.Constants.Wire.Terms, "text[]", tokenization: true);
        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    private static void WriteProperty(Utf8JsonWriter writer, string name, string type, bool tokenization)
    {
        writer.WriteStartObject();
        writer.WriteString("name", name);
        writer.WritePropertyName("dataType");
        writer.WriteStartArray();
        writer.WriteStringValue(type);
        writer.WriteEndArray();
        if (tokenization) writer.WriteString("tokenization", "field");
        writer.WriteBoolean("indexFilterable", tokenization);
        writer.WriteBoolean("indexSearchable", false);
        writer.WriteEndObject();
    }

    private void ValidateShape(string collection, WeaviateResponse response)
    {
        var root = response.Document?.RootElement
            ?? throw WrongShape(collection, "the provider returned no collection description");
        if (!root.TryGetProperty("description", out var description) ||
            !string.Equals(description.GetString(), ContractMarker(), StringComparison.Ordinal))
            throw WrongShape(collection, "the Koan dimensions, metric, space, or model contract marker differs");
        if (!root.TryGetProperty("vectorizer", out var vectorizer) ||
            !string.Equals(vectorizer.GetString(), "none", StringComparison.OrdinalIgnoreCase))
            throw WrongShape(collection, "vectorizer is not self-provided ('none')");
        if (!root.TryGetProperty("vectorIndexConfig", out var index) ||
            !index.TryGetProperty("distance", out var distance) ||
            !string.Equals(distance.GetString(), Distance(), StringComparison.OrdinalIgnoreCase))
            throw WrongShape(collection, $"distance is not '{Distance()}'");
        if (!root.TryGetProperty("properties", out var properties) || properties.ValueKind != JsonValueKind.Array)
            throw WrongShape(collection, "the fixed Koan properties are absent");
        var names = properties.EnumerateArray()
            .Where(static item => item.TryGetProperty("name", out _))
            .Select(static item => item.GetProperty("name").GetString())
            .ToHashSet(StringComparer.Ordinal);
        foreach (var name in new[]
                 {
                     Infrastructure.Constants.Wire.Id,
                     Infrastructure.Constants.Wire.Metadata,
                     Infrastructure.Constants.Wire.Terms
                 })
            if (!names.Contains(name)) throw WrongShape(collection, $"fixed property '{name}' is absent");
    }

    private PreparedPoint Prepare(VectorPoint<TKey> point, VectorScope scope)
    {
        ArgumentNullException.ThrowIfNull(point);
        ValidateEmbedding(point.Embedding.Span, "point embedding");
        var metadataJson = VectorMetadata.ToJson(point.Metadata);
        if (metadataJson is not null && Encoding.UTF8.GetByteCount(metadataJson) > _options.MaxMetadataBytesPerPoint)
            throw new InvalidOperationException(
                $"Weaviate point metadata exceeds the configured {_options.MaxMetadataBytesPerPoint} byte limit.");
        return new PreparedPoint(
            point,
            StorageId(point.Id, scope),
            metadataJson is null ? null : Convert.ToBase64String(Encoding.UTF8.GetBytes(metadataJson)),
            WeaviateFilter.Project(point.Metadata));
    }

    private async Task SavePrepared(string collection, PreparedPoint point, CancellationToken ct)
    {
        var body = Json(writer => WriteObject(writer, collection, point));
        using var created = await _client.Post(
            "v1/objects?consistency_level=ALL", body, "point create", allowConflict: true, ct)
            .ConfigureAwait(false);
        if (!created.IsConflict) return;
        using var updated = await _client.Put(
            $"v1/objects/{Escape(collection)}/{point.StorageId}?consistency_level=ALL",
            body, "point replace", ct).ConfigureAwait(false);
    }

    private static void WriteObject(Utf8JsonWriter writer, string collection, PreparedPoint point)
    {
        writer.WriteStartObject();
        writer.WriteString("class", collection);
        writer.WriteString("id", point.StorageId);
        writer.WritePropertyName("properties");
        writer.WriteStartObject();
        writer.WriteString(Infrastructure.Constants.Wire.Id, Key(point.Point.Id));
        if (point.Metadata is not null)
            writer.WriteString(Infrastructure.Constants.Wire.Metadata, point.Metadata);
        writer.WritePropertyName(Infrastructure.Constants.Wire.Terms);
        writer.WriteStartArray();
        foreach (var term in point.Terms) writer.WriteStringValue(term);
        writer.WriteEndArray();
        writer.WriteEndObject();
        writer.WritePropertyName("vector");
        writer.WriteStartArray();
        foreach (var value in point.Point.Embedding.Span) writer.WriteNumberValue(value);
        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    private async Task<Dictionary<string, VectorPoint<TKey>>> Fetch(
        string collection,
        IReadOnlyList<TKey> ids,
        VectorScope scope,
        CancellationToken ct)
    {
        var output = new Dictionary<string, VectorPoint<TKey>>(StringComparer.Ordinal);
        foreach (var id in ids.Distinct())
        {
            var point = await FetchOne(collection, id, scope, ct).ConfigureAwait(false);
            if (point is not null) output[StorageId(id, scope)] = point;
        }
        return output;
    }

    private async Task<VectorPoint<TKey>?> FetchOne(
        string collection,
        TKey id,
        VectorScope scope,
        CancellationToken ct)
    {
        using var response = await _client.Get(
            $"v1/objects/{Escape(collection)}/{StorageId(id, scope)}?include=vector&consistency_level=ALL",
            "point retrieval", true, ct).ConfigureAwait(false);
        if (response.IsNotFound) return null;
        var root = response.Document?.RootElement
            ?? throw new InvalidOperationException("Weaviate point retrieval returned no JSON object.");
        if (!root.TryGetProperty("properties", out var properties) ||
            !properties.TryGetProperty(Infrastructure.Constants.Wire.Id, out var logicalId) ||
            !string.Equals(logicalId.GetString(), Key(id), StringComparison.Ordinal))
            throw new InvalidOperationException("Weaviate point retrieval returned an invalid Koan identity projection.");
        var vector = ReadVector(root);
        if (vector.Length != _plan.Dimensions)
            throw new InvalidOperationException(
                $"Weaviate point has {vector.Length} dimensions; expected {_plan.Dimensions}.");
        return new VectorPoint<TKey>(id, vector, ReadMetadata(properties));
    }

    private async Task<List<Ranked>> Query(
        string collection,
        VectorSearchRequest request,
        int candidates,
        CancellationToken ct)
    {
        var args = new StringBuilder();
        args.Append("nearVector:{vector:[")
            .Append(string.Join(',', request.Embedding.Span.ToArray().Select(value =>
                value.ToString("R", CultureInfo.InvariantCulture))));
        if (request.MinimumSimilarity is { } minimum)
            args.Append("],distance:").Append(ProviderThreshold(minimum).ToString("R", CultureInfo.InvariantCulture));
        else
            args.Append(']');
        args.Append("},limit:").Append(candidates.ToString(CultureInfo.InvariantCulture));
        if (request.Filter is not null) args.Append(",where:").Append(WeaviateFilter.Write(request.Filter));
        var query = $"{{Get{{{collection}({args}){{{Infrastructure.Constants.Wire.Id} {Infrastructure.Constants.Wire.Metadata} _additional{{distance id}}}}}}}}";
        using var response = await GraphQl(query, "vector query", ct).ConfigureAwait(false);
        var objects = GraphQlObjects(response, collection);
        var output = new List<Ranked>(objects.GetArrayLength());
        foreach (var item in objects.EnumerateArray())
        {
            var logical = item.GetProperty(Infrastructure.Constants.Wire.Id).GetString()
                ?? throw new InvalidOperationException("Weaviate query returned an invalid Koan identity projection.");
            var key = ParseKey(logical);
            var additional = item.GetProperty("_additional");
            var distance = additional.GetProperty("distance").GetDouble();
            output.Add(new Ranked(key, logical, distance, Similarity(distance), ReadMetadata(item)));
        }
        return output;
    }

    private async Task<IReadOnlyList<string>> QueryStorageIds(
        string collection,
        Koan.Data.Abstractions.Filtering.Filter? filter,
        int limit,
        CancellationToken ct)
    {
        var where = filter is null ? string.Empty : ",where:" + WeaviateFilter.Write(filter);
        var query = $"{{Get{{{collection}(limit:{limit.ToString(CultureInfo.InvariantCulture)}{where}){{_additional{{id}}}}}}}}";
        using var response = await GraphQl(query, "clear preflight", ct).ConfigureAwait(false);
        return GraphQlObjects(response, collection).EnumerateArray()
            .Select(static item => item.GetProperty("_additional").GetProperty("id").GetString()
                ?? throw new InvalidOperationException("Weaviate returned an invalid object identity."))
            .ToArray();
    }

    private Task<WeaviateResponse> GraphQl(string query, string operation, CancellationToken ct) =>
        _client.Post("v1/graphql", Json(writer =>
        {
            writer.WriteStartObject();
            writer.WriteString("query", query);
            writer.WriteEndObject();
        }), operation, allowConflict: false, ct);

    private static JsonElement GraphQlObjects(WeaviateResponse response, string collection)
    {
        var root = response.Document?.RootElement
            ?? throw new InvalidOperationException("Weaviate GraphQL returned no JSON result.");
        if (root.TryGetProperty("errors", out var errors) && errors.ValueKind == JsonValueKind.Array &&
            errors.GetArrayLength() > 0)
            throw new InvalidOperationException("Weaviate GraphQL rejected the bounded vector operation.");
        if (!root.TryGetProperty("data", out var data) || !data.TryGetProperty("Get", out var get) ||
            !get.TryGetProperty(collection, out var objects) || objects.ValueKind != JsonValueKind.Array)
            throw new InvalidOperationException("Weaviate GraphQL returned an invalid result shape.");
        return objects;
    }

    private async Task DeleteStorageId(string collection, string storageId, CancellationToken ct)
    {
        using var response = await _client.Delete(
            $"v1/objects/{Escape(collection)}/{storageId}?consistency_level=ALL",
            "point delete", allowNotFound: true, ct).ConfigureAwait(false);
    }

    private void Validate(VectorSearchRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateEmbedding(request.Embedding.Span, "query embedding");
        if (request.Top <= 0 || request.Top >= _options.MaxSearchCandidates)
            throw new ArgumentOutOfRangeException(nameof(request),
                $"Weaviate Top must be positive and smaller than MaxSearchCandidates ({_options.MaxSearchCandidates}).");
        if (request.Space is not null && !string.Equals(request.Space, _plan.Name, StringComparison.Ordinal))
            throw new InvalidOperationException(
                $"Weaviate query requested space '{request.Space}'. Available space: {_plan.Name}.");
        if (request.Text is not null || request.SemanticWeight is not null)
            throw new NotSupportedException("Weaviate adapter does not claim portable hybrid text semantics.");
        if (request.Continuation is not null)
            throw new NotSupportedException("Weaviate adapter does not claim a stable search continuation snapshot.");
        if (request.MinimumSimilarity is < 0d or > 1d ||
            request.MinimumSimilarity is { } minimum && !double.IsFinite(minimum))
            throw new ArgumentOutOfRangeException(nameof(request), "Minimum similarity must be finite and in [0,1].");
    }

    private void ValidateEmbedding(ReadOnlySpan<float> embedding, string label)
    {
        if (embedding.Length != _plan.Dimensions)
            throw new ArgumentException(
                $"Weaviate {label} has {embedding.Length} dimensions; space '{_plan.Name}' requires {_plan.Dimensions}.");
        for (var index = 0; index < embedding.Length; index++)
            if (!float.IsFinite(embedding[index]))
                throw new ArgumentException($"Weaviate {label} contains a non-finite value at index {index}.");
        if (_plan.Metric == VectorMetric.Cosine && Norm(embedding) == 0d)
            throw new ArgumentException("Cosine vector spaces do not accept a zero-magnitude embedding.");
    }

    private string Collection() => PhysicalName(
        VectorAdapterNaming.GetOrCompute<TEntity>(_services, _factory, _plan.Source));

    internal static string PhysicalName(string logical)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(logical);
        var readable = new string(logical.Select(static character =>
            char.IsAsciiLetterOrDigit(character) || character == '_' ? character : '_').ToArray());
        if (readable.Length == 0) readable = "Space";
        readable = char.ToUpperInvariant(readable[0]) + readable[1..];
        if (readable.Length > 48) readable = readable[..48];
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(logical)))[..16];
        return readable + "_" + hash;
    }

    private string StorageId(TKey id, VectorScope scope) =>
        UuidV5($"{Collection()}\u001f{scope.Identity}\u001f{Key(id)}").ToString("D");

    private static Guid UuidV5(string value)
    {
        var namespaceBytes = Infrastructure.Constants.PointNamespace.ToByteArray();
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

    private string ContractMarker()
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(new ContractShape(
            _plan.Dimensions, _plan.Metric.ToString(), _plan.Name, _plan.Model));
        return Infrastructure.Constants.Wire.ContractPrefix + Base64Url(payload);
    }

    private string Distance() => _plan.Metric switch
    {
        VectorMetric.Cosine => "cosine",
        VectorMetric.Euclidean => "l2-squared",
        VectorMetric.DotProduct => "dot",
        _ => throw new NotSupportedException($"Weaviate does not support metric '{_plan.Metric}'.")
    };

    private double Similarity(double distance)
    {
        var value = _plan.Metric switch
        {
            VectorMetric.Cosine => 1d - (distance / 2d),
            VectorMetric.Euclidean => 1d / (1d + Math.Sqrt(Math.Max(0d, distance))),
            VectorMetric.DotProduct => Logistic(-distance),
            _ => throw new NotSupportedException()
        };
        return Math.Clamp(double.IsFinite(value) ? value : distance < 0d ? 1d : 0d, 0d, 1d);
    }

    private double ProviderThreshold(double similarity) => _plan.Metric switch
    {
        VectorMetric.Cosine => 2d * (1d - similarity),
        VectorMetric.Euclidean => similarity == 0d ? double.MaxValue : Math.Pow((1d / similarity) - 1d, 2d),
        VectorMetric.DotProduct => similarity switch
        {
            <= 0d => double.MaxValue,
            >= 1d => -double.MaxValue,
            _ => -Math.Log(similarity / (1d - similarity))
        },
        _ => throw new NotSupportedException()
    };

    private static double Logistic(double value) => value >= 0d
        ? 1d / (1d + Math.Exp(-value))
        : Math.Exp(value) / (1d + Math.Exp(value));

    private static double Norm(ReadOnlySpan<float> embedding)
    {
        double squared = 0d;
        foreach (var value in embedding) squared += value * (double)value;
        return Math.Sqrt(squared);
    }

    private static float[] ReadVector(JsonElement root)
    {
        if (!root.TryGetProperty("vector", out var vector))
            throw new InvalidOperationException("Weaviate point has no vector.");
        if (vector.ValueKind == JsonValueKind.Object && vector.TryGetProperty("default", out var named)) vector = named;
        if (vector.ValueKind != JsonValueKind.Array)
            throw new InvalidOperationException("Weaviate point returned an invalid vector shape.");
        return vector.EnumerateArray().Select(static value => value.GetSingle()).ToArray();
    }

    private static DataObject? ReadMetadata(JsonElement properties)
    {
        if (!properties.TryGetProperty(Infrastructure.Constants.Wire.Metadata, out var metadata) ||
            metadata.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return null;
        var encoded = metadata.GetString()
            ?? throw new InvalidOperationException("Weaviate point returned invalid Koan metadata.");
        return VectorMetadata.FromJson(Encoding.UTF8.GetString(Convert.FromBase64String(encoded)));
    }

    private static bool IsIndexReady(WeaviateResponse response)
    {
        var root = response.Document?.RootElement
            ?? throw new InvalidOperationException("Weaviate node inspection returned no JSON result.");
        if (!root.TryGetProperty("nodes", out var nodes) || nodes.ValueKind != JsonValueKind.Array)
            throw new InvalidOperationException("Weaviate node inspection returned an invalid result shape.");
        var observed = false;
        foreach (var node in nodes.EnumerateArray())
        {
            if (!node.TryGetProperty("shards", out var shards) || shards.ValueKind != JsonValueKind.Array) continue;
            foreach (var shard in shards.EnumerateArray())
            {
                observed = true;
                if (shard.TryGetProperty("vectorQueueLength", out var queue) && queue.GetInt64() > 0) return false;
                if (shard.TryGetProperty("vectorIndexingStatus", out var status) &&
                    !string.Equals(status.GetString(), "READY", StringComparison.OrdinalIgnoreCase)) return false;
            }
        }
        return observed || nodes.GetArrayLength() > 0;
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
                ?? throw new InvalidOperationException($"Vector identity could not convert to '{typeof(TKey).FullName}'."));
        return (TKey)Convert.ChangeType(value, typeof(TKey), CultureInfo.InvariantCulture);
    }

    private static byte[] Json(Action<Utf8JsonWriter> write)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer)) write(writer);
        return buffer.WrittenSpan.ToArray();
    }

    private static string Base64Url(ReadOnlySpan<byte> value) =>
        Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    private static string Escape(string value) => Uri.EscapeDataString(value);

    private void DemandBatch(int count)
    {
        if (count > _options.MaxBatchPoints)
            throw new ArgumentOutOfRangeException(nameof(count),
                $"Weaviate batch contains {count} points; configured maximum is {_options.MaxBatchPoints}.");
    }

    private static BatchResult<TKey> Missing(IReadOnlyList<TKey> ids) => new(
        ids.Select((id, index) => new BatchItemResult<TKey>(index, id, MutationOutcome.Missing)),
        BatchAtomicity.NotGuaranteed);

    private VectorSearchResult<TKey> Empty() => new(
        [], null, new VectorSearchExecution(_plan.Metric, VectorSearchAccuracy.Approximate, null));

    private InvalidOperationException WrongShape(string collection, string reason) => new(
        $"Weaviate collection '{collection}' cannot realize space '{_plan.Name}': {reason}. " +
        "Provision the declared shape or select the source that owns this collection.");

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

    private sealed record PreparedPoint(
        VectorPoint<TKey> Point,
        string StorageId,
        string? Metadata,
        IReadOnlyList<string> Terms);

    private sealed record Ranked(
        TKey Id,
        string StableId,
        double RawDistance,
        double Similarity,
        DataObject? Metadata);

    private sealed record ContractShape(int Dimensions, string Metric, string Space, string? Model);
}
