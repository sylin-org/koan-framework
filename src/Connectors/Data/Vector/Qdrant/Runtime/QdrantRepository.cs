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

namespace Koan.Data.Vector.Connector.Qdrant;

internal sealed class QdrantRepository<TEntity, TKey> :
    IVectorSearchRepository<TEntity, TKey>,
    IDescribesCapabilities,
    IDisposable,
    IAsyncDisposable
    where TEntity : class, IEntity<TKey>
    where TKey : notnull
{
    private readonly IServiceProvider _services;
    private readonly QdrantVectorAdapterFactory _factory;
    private readonly VectorSpacePlan _plan;
    private readonly QdrantRoute _route;
    private readonly QdrantOptions _options;
    private readonly QdrantClient _client;
    private readonly ConcurrentDictionary<string, byte> _ready = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _shapeGate = new(1, 1);
    private int _disposed;

    internal QdrantRepository(
        IServiceProvider services,
        QdrantVectorAdapterFactory factory,
        VectorSpacePlan plan,
        QdrantRoute route,
        QdrantOptions options,
        IHttpClientFactory http)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        _plan = plan ?? throw new ArgumentNullException(nameof(plan));
        _route = route ?? throw new ArgumentNullException(nameof(route));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _client = new QdrantClient(http ?? throw new ArgumentNullException(nameof(http)), route, options);
    }

    public void Describe(ICapabilities capabilities) => capabilities
        .Add(VectorCaps.Knn)
        .Add(VectorCaps.Filters, QdrantFilter.Capabilities)
        .Add(VectorCaps.BulkUpsert)
        .Add(VectorCaps.BulkDelete)
        .Add(VectorCaps.ScoreNormalization)
        .Add(VectorCaps.DynamicCollections);

    public async Task Save(VectorPoint<TKey> point, VectorScope scope, CancellationToken ct = default)
    {
        var prepared = Prepare(point, scope);
        _route.Policy.Demand(DataOperationEffect.Write, $"save Qdrant point in space '{_plan.Name}'");
        var collection = Collection();
        await EnsureShape(collection, create: true, ct).ConfigureAwait(false);
        using var response = await _client.Put(
            PointsPath(collection),
            Json(writer => WriteUpsert(writer, [prepared])),
            "point save",
            allowConflict: false,
            ct).ConfigureAwait(false);
        RequireCompleted(response, "point save");
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

        _route.Policy.Demand(DataOperationEffect.Write, $"save Qdrant batch in space '{_plan.Name}'");
        var collection = Collection();
        await EnsureShape(collection, create: true, ct).ConfigureAwait(false);
        var existing = await Fetch(collection, prepared.Select(static item => item.Point.Id).ToArray(), scope, ct)
            .ConfigureAwait(false);
        var seen = existing.Keys.ToHashSet(StringComparer.Ordinal);
        var outcomes = new BatchItemResult<TKey>[prepared.Length];
        var final = new Dictionary<string, PreparedPoint>(StringComparer.Ordinal);
        for (var index = 0; index < prepared.Length; index++)
        {
            var item = prepared[index];
            var existed = !seen.Add(item.StorageId.Text);
            outcomes[index] = new BatchItemResult<TKey>(index, item.Point.Id,
                existed ? MutationOutcome.Updated : MutationOutcome.Inserted);
            final[item.StorageId.Text] = item;
        }
        using var response = await _client.Put(
            PointsPath(collection),
            Json(writer => WriteUpsert(writer, final.Values)),
            "batch save",
            allowConflict: false,
            ct).ConfigureAwait(false);
        RequireCompleted(response, "batch save");
        return new BatchResult<TKey>(outcomes, BatchAtomicity.NotGuaranteed);
    }

    public async Task<VectorPoint<TKey>?> Get(TKey id, VectorScope scope, CancellationToken ct = default)
    {
        var collection = Collection();
        if (!await EnsureShape(collection, create: false, ct).ConfigureAwait(false)) return null;
        var found = await Fetch(collection, [id], scope, ct).ConfigureAwait(false);
        return found.TryGetValue(StorageId(id, scope).Text, out var point) ? point : null;
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
        var found = await Fetch(collection, ids, scope, ct).ConfigureAwait(false);
        for (var index = 0; index < ids.Count; index++)
            if (found.TryGetValue(StorageId(ids[index], scope).Text, out var point)) output[index] = point;
        return output;
    }

    public async Task<bool> Delete(TKey id, VectorScope scope, CancellationToken ct = default)
    {
        _route.Policy.Demand(DataOperationEffect.Write, $"delete Qdrant point from space '{_plan.Name}'");
        var collection = Collection();
        if (!await EnsureShape(collection, create: false, ct).ConfigureAwait(false)) return false;
        var storageId = StorageId(id, scope);
        var existing = await Fetch(collection, [id], scope, ct).ConfigureAwait(false);
        if (!existing.ContainsKey(storageId.Text)) return false;
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
        _route.Policy.Demand(DataOperationEffect.Write, $"delete Qdrant batch from space '{_plan.Name}'");
        var collection = Collection();
        if (!await EnsureShape(collection, create: false, ct).ConfigureAwait(false))
            return Missing(ids);
        var existing = await Fetch(collection, ids, scope, ct).ConfigureAwait(false);
        var outcomes = new BatchItemResult<TKey>[ids.Count];
        var remove = new Dictionary<string, StoragePointId>(StringComparer.Ordinal);
        for (var index = 0; index < ids.Count; index++)
        {
            var storageId = StorageId(ids[index], scope);
            var found = existing.ContainsKey(storageId.Text);
            outcomes[index] = new BatchItemResult<TKey>(index, ids[index],
                found ? MutationOutcome.Deleted : MutationOutcome.Missing);
            if (found) remove[storageId.Text] = storageId;
        }
        if (remove.Count > 0) await DeleteIds(collection, remove.Values, ct).ConfigureAwait(false);
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
                ranked[request.Top - 1].RawScore != ranked[request.Top].RawScore)
                break;
            if (requested >= _options.MaxSearchCandidates)
                throw new InvalidOperationException(
                    $"Qdrant cannot resolve a stable identity tie within the configured bound of {_options.MaxSearchCandidates} candidates. " +
                    "Increase MaxSearchCandidates or narrow the vector space.");
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
        _route.Policy.Demand(DataOperationEffect.Write, $"clear Qdrant space '{_plan.Name}'");
        var collection = Collection();
        if (!await EnsureShape(collection, create: false, ct).ConfigureAwait(false)) return;
        var body = Json(writer =>
        {
            writer.WriteStartObject();
            writer.WritePropertyName("filter");
            QdrantFilter.Write(writer, scope.Predicate,
                string.IsNullOrEmpty(scope.Identity) ? null : w => WriteScopeMatch(w, scope.Identity));
            writer.WriteEndObject();
        });
        using var response = await _client.Post(
            $"collections/{Escape(collection)}/points/delete?wait=true",
            body, "space clear", false, ct).ConfigureAwait(false);
        RequireCompleted(response, "space clear");
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
        if (_ready.ContainsKey(collection)) return true;
        await _shapeGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_ready.ContainsKey(collection)) return true;
            using var current = await _client.Get(
                $"collections/{Escape(collection)}", "collection inspection", true, ct).ConfigureAwait(false);
            if (!current.IsNotFound)
            {
                ValidateShape(collection, current);
                _ready.TryAdd(collection, 0);
                return true;
            }
            if (!create) return false;
            _route.Policy.Demand(DataOperationEffect.SchemaOrAdmin,
                $"create Qdrant collection for space '{_plan.Name}'");
            using (var created = await _client.Put(
                $"collections/{Escape(collection)}",
                Json(WriteCollection),
                "collection create",
                allowConflict: true,
                ct).ConfigureAwait(false))
            {
            }
            using var confirmed = await _client.Get(
                $"collections/{Escape(collection)}", "created collection inspection", false, ct).ConfigureAwait(false);
            ValidateShape(collection, confirmed);
            _ready.TryAdd(collection, 0);
            return true;
        }
        finally
        {
            _shapeGate.Release();
        }
    }

    private void WriteCollection(Utf8JsonWriter writer)
    {
        writer.WriteStartObject();
        writer.WritePropertyName("vectors");
        writer.WriteStartObject();
        writer.WritePropertyName(_plan.Name);
        writer.WriteStartObject();
        writer.WriteNumber("size", _plan.Dimensions);
        writer.WriteString("distance", Distance());
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

    private void ValidateShape(string collection, QdrantResponse response)
    {
        var root = response.Document?.RootElement
            ?? throw new InvalidOperationException($"Qdrant returned no description for collection '{collection}'.");
        if (!root.TryGetProperty("result", out var result) ||
            !result.TryGetProperty("config", out var config) ||
            !config.TryGetProperty("params", out var parameters) ||
            !parameters.TryGetProperty("vectors", out var vectors) ||
            vectors.ValueKind != JsonValueKind.Object ||
            !vectors.TryGetProperty(_plan.Name, out var slot) ||
            !slot.TryGetProperty("size", out var size) ||
            !slot.TryGetProperty("distance", out var distance))
            throw WrongShape(collection, "the declared named vector is absent");
        if (size.GetInt32() != _plan.Dimensions)
            throw WrongShape(collection, $"dimension is {size.GetInt32()}, expected {_plan.Dimensions}");
        if (!string.Equals(distance.GetString(), Distance(), StringComparison.OrdinalIgnoreCase))
            throw WrongShape(collection, $"metric is '{distance.GetString()}', expected '{Distance()}'");
        if (_plan.Model is not null)
        {
            if (!result.TryGetProperty("metadata", out var metadata) ||
                !metadata.TryGetProperty(Infrastructure.Constants.Wire.CollectionModel, out var model) ||
                !string.Equals(model.GetString(), _plan.Model, StringComparison.Ordinal))
                throw WrongShape(collection, $"model metadata does not match '{_plan.Model}'");
        }
    }

    private PreparedPoint Prepare(VectorPoint<TKey> point, VectorScope scope)
    {
        ArgumentNullException.ThrowIfNull(point);
        ValidateEmbedding(point.Embedding.Span, "point embedding");
        var metadata = VectorMetadata.ToJson(point.Metadata);
        if (metadata is not null && Encoding.UTF8.GetByteCount(metadata) > _options.MaxMetadataBytesPerPoint)
            throw new InvalidOperationException(
                $"Qdrant point metadata exceeds the configured {_options.MaxMetadataBytesPerPoint} byte limit.");
        var norm = Norm(point.Embedding.Span);
        if (_plan.Metric == VectorMetric.Cosine && norm == 0d)
            throw new ArgumentException("Cosine vector spaces do not accept a zero-magnitude embedding.", nameof(point));
        return new PreparedPoint(point, StorageId(point.Id, scope), metadata, norm, scope.Identity);
    }

    private void WriteUpsert(Utf8JsonWriter writer, IEnumerable<PreparedPoint> points)
    {
        writer.WriteStartObject();
        writer.WritePropertyName("points");
        writer.WriteStartArray();
        foreach (var point in points)
        {
            writer.WriteStartObject();
            writer.WritePropertyName("id");
            point.StorageId.Write(writer);
            writer.WritePropertyName("vector");
            writer.WriteStartObject();
            writer.WritePropertyName(_plan.Name);
            writer.WriteStartArray();
            foreach (var value in point.Point.Embedding.Span) writer.WriteNumberValue(value);
            writer.WriteEndArray();
            writer.WriteEndObject();
            writer.WritePropertyName("payload");
            writer.WriteStartObject();
            writer.WriteString(Infrastructure.Constants.Wire.Id, Key(point.Point.Id));
            writer.WriteString(Infrastructure.Constants.Wire.Scope, point.Scope);
            writer.WriteNumber(Infrastructure.Constants.Wire.Norm, point.Norm);
            if (point.MetadataJson is not null)
            {
                writer.WritePropertyName(Infrastructure.Constants.Wire.Metadata);
                writer.WriteRawValue(point.MetadataJson);
            }
            if (point.Point.Metadata is not null)
            {
                writer.WritePropertyName(Infrastructure.Constants.Wire.Index);
                QdrantFilter.WriteIndex(writer, point.Point.Metadata);
            }
            writer.WriteEndObject();
            writer.WriteEndObject();
        }
        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    private async Task<Dictionary<string, VectorPoint<TKey>>> Fetch(
        string collection,
        IReadOnlyList<TKey> ids,
        VectorScope scope,
        CancellationToken ct)
    {
        var storage = ids.Select(id => StorageId(id, scope)).ToArray();
        var expected = new Dictionary<string, TKey>(StringComparer.Ordinal);
        for (var index = 0; index < storage.Length; index++) expected[storage[index].Text] = ids[index];
        var body = Json(writer =>
        {
            writer.WriteStartObject();
            if (scope.Predicate is null)
            {
                writer.WritePropertyName("ids");
                WriteIds(writer, storage);
            }
            else
            {
                writer.WritePropertyName("filter");
                QdrantFilter.Write(writer, scope.Predicate, w => WriteHasIds(w, storage));
                writer.WriteNumber("limit", storage.Length);
            }
            writer.WriteBoolean("with_payload", true);
            writer.WriteBoolean("with_vector", true);
            writer.WriteEndObject();
        });
        var path = scope.Predicate is null
            ? $"collections/{Escape(collection)}/points"
            : $"collections/{Escape(collection)}/points/scroll";
        using var response = await _client.Post(path, body, "point retrieval", false, ct).ConfigureAwait(false);
        var result = Result(response);
        var points = scope.Predicate is null
            ? result
            : result.TryGetProperty("points", out var nested) ? nested : default;
        if (points.ValueKind != JsonValueKind.Array)
            throw new InvalidOperationException("Qdrant point retrieval returned an invalid result shape.");
        var output = new Dictionary<string, VectorPoint<TKey>>(StringComparer.Ordinal);
        foreach (var item in points.EnumerateArray())
        {
            var storageId = ResponseId(item.GetProperty("id"));
            if (!expected.TryGetValue(storageId, out var id)) continue;
            output[storageId] = ReadPoint(item, id);
        }
        return output;
    }

    private VectorPoint<TKey> ReadPoint(JsonElement point, TKey id)
    {
        if (!point.TryGetProperty("vector", out var vectors) ||
            !vectors.TryGetProperty(_plan.Name, out var vector) || vector.ValueKind != JsonValueKind.Array)
            throw new InvalidOperationException($"Qdrant point '{Key(id)}' has no vector for space '{_plan.Name}'.");
        var values = vector.EnumerateArray().Select(static value => value.GetSingle()).ToArray();
        if (values.Length != _plan.Dimensions)
            throw new InvalidOperationException(
                $"Qdrant point '{Key(id)}' has {values.Length} dimensions; expected {_plan.Dimensions}.");
        if (!point.TryGetProperty("payload", out var payload) || payload.ValueKind != JsonValueKind.Object)
            throw new InvalidOperationException($"Qdrant point '{Key(id)}' has no Koan payload.");
        if (_plan.Metric == VectorMetric.Cosine &&
            payload.TryGetProperty(Infrastructure.Constants.Wire.Norm, out var norm))
        {
            var scale = norm.GetSingle();
            for (var index = 0; index < values.Length; index++) values[index] *= scale;
        }
        var metadata = payload.TryGetProperty(Infrastructure.Constants.Wire.Metadata, out var stored)
            ? VectorMetadata.FromJson(stored.GetRawText())
            : null;
        return new VectorPoint<TKey>(id, values, metadata);
    }

    private async Task<List<Ranked>> Query(
        string collection,
        VectorSearchRequest request,
        int candidates,
        CancellationToken ct)
    {
        var body = Json(writer =>
        {
            writer.WriteStartObject();
            writer.WritePropertyName("query");
            writer.WriteStartArray();
            foreach (var value in request.Embedding.Span) writer.WriteNumberValue(value);
            writer.WriteEndArray();
            writer.WriteString("using", _plan.Name);
            writer.WriteNumber("limit", candidates);
            writer.WriteBoolean("with_payload", true);
            writer.WriteBoolean("with_vector", false);
            writer.WritePropertyName("params");
            writer.WriteStartObject();
            writer.WriteBoolean("exact", false);
            writer.WriteEndObject();
            if (request.Filter is not null)
            {
                writer.WritePropertyName("filter");
                QdrantFilter.Write(writer, request.Filter);
            }
            if (request.MinimumSimilarity is not null)
                writer.WriteNumber("score_threshold", ProviderThreshold(request.MinimumSimilarity.Value));
            writer.WriteEndObject();
        });
        using var response = await _client.Post(
            $"collections/{Escape(collection)}/points/query",
            body, "vector query", false, ct).ConfigureAwait(false);
        var result = Result(response);
        if (!result.TryGetProperty("points", out var points) || points.ValueKind != JsonValueKind.Array)
            throw new InvalidOperationException("Qdrant vector query returned an invalid result shape.");
        var output = new List<Ranked>(points.GetArrayLength());
        foreach (var point in points.EnumerateArray())
        {
            if (!point.TryGetProperty("payload", out var payload) ||
                !payload.TryGetProperty(Infrastructure.Constants.Wire.Id, out var original))
                throw new InvalidOperationException("Qdrant query returned a point without its Koan identity payload.");
            var key = ParseKey(original.GetString()
                ?? throw new InvalidOperationException("Qdrant returned a non-string Koan identity payload."));
            var raw = point.GetProperty("score").GetDouble();
            var similarity = Similarity(raw);
            var metadata = payload.TryGetProperty(Infrastructure.Constants.Wire.Metadata, out var stored)
                ? VectorMetadata.FromJson(stored.GetRawText())
                : null;
            output.Add(new Ranked(key, Key(key), raw, similarity, metadata));
        }
        return output;
    }

    private async Task DeleteIds(string collection, IEnumerable<StoragePointId> ids, CancellationToken ct)
    {
        var body = Json(writer =>
        {
            writer.WriteStartObject();
            writer.WritePropertyName("points");
            WriteIds(writer, ids);
            writer.WriteEndObject();
        });
        using var response = await _client.Post(
            $"collections/{Escape(collection)}/points/delete?wait=true",
            body, "point delete", false, ct).ConfigureAwait(false);
        RequireCompleted(response, "point delete");
    }

    private void Validate(VectorSearchRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateEmbedding(request.Embedding.Span, "query embedding");
        if (request.Top <= 0 || request.Top >= _options.MaxSearchCandidates)
            throw new ArgumentOutOfRangeException(nameof(request),
                $"Qdrant Top must be positive and smaller than MaxSearchCandidates ({_options.MaxSearchCandidates}).");
        if (request.Space is not null && !string.Equals(request.Space, _plan.Name, StringComparison.Ordinal))
            throw new InvalidOperationException(
                $"Qdrant query requested space '{request.Space}'. Available space: {_plan.Name}.");
        if (request.Text is not null || request.SemanticWeight is not null)
            throw new NotSupportedException("Qdrant adapter does not claim portable hybrid text semantics.");
        if (request.Continuation is not null)
            throw new NotSupportedException("Qdrant adapter does not claim a stable search continuation snapshot.");
        if (request.MinimumSimilarity is < 0d or > 1d ||
            request.MinimumSimilarity is { } minimum && !double.IsFinite(minimum))
            throw new ArgumentOutOfRangeException(nameof(request), "Minimum similarity must be finite and in [0,1].");
    }

    private void ValidateEmbedding(ReadOnlySpan<float> embedding, string label)
    {
        if (embedding.Length != _plan.Dimensions)
            throw new ArgumentException(
                $"Qdrant {label} has {embedding.Length} dimensions; space '{_plan.Name}' requires {_plan.Dimensions}.");
        for (var index = 0; index < embedding.Length; index++)
            if (!float.IsFinite(embedding[index]))
                throw new ArgumentException($"Qdrant {label} contains a non-finite value at index {index}.");
        if (_plan.Metric == VectorMetric.Cosine && Norm(embedding) == 0d)
            throw new ArgumentException("Cosine vector spaces do not accept a zero-magnitude embedding.");
    }

    private StoragePointId StorageId(TKey id, VectorScope scope)
    {
        var key = Key(id);
        if (!string.IsNullOrEmpty(scope.Identity))
            return StoragePointId.Uuid(UuidV5($"scope:{scope.Identity}\u001f{key}"));
        return id switch
        {
            Guid guid => StoragePointId.Uuid(guid),
            string text when Guid.TryParse(text, out var guid) => StoragePointId.Uuid(guid),
            string text => StoragePointId.Uuid(UuidV5(text)),
            byte value => StoragePointId.Number(value),
            ushort value => StoragePointId.Number(value),
            uint value => StoragePointId.Number(value),
            ulong value => StoragePointId.Number(value),
            sbyte value when value >= 0 => StoragePointId.Number((ulong)value),
            short value when value >= 0 => StoragePointId.Number((ulong)value),
            int value when value >= 0 => StoragePointId.Number((ulong)value),
            long value when value >= 0 => StoragePointId.Number((ulong)value),
            sbyte or short or int or long => StoragePointId.Uuid(UuidV5($"negative:{typeof(TKey).FullName}:{key}")),
            _ => StoragePointId.Uuid(UuidV5(key))
        };
    }

    private Guid UuidV5(string value)
    {
        var namespaceBytes = Infrastructure.Constants.StringIdNamespace.ToByteArray();
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

    private string Distance() => _plan.Metric switch
    {
        VectorMetric.Cosine => "Cosine",
        VectorMetric.Euclidean => "Euclid",
        VectorMetric.DotProduct => "Dot",
        _ => throw new NotSupportedException($"Qdrant does not support metric '{_plan.Metric}'.")
    };

    private double Similarity(double score)
    {
        var value = _plan.Metric switch
        {
            VectorMetric.Cosine => (score + 1d) / 2d,
            VectorMetric.Euclidean => 1d / (1d + Math.Max(0d, score)),
            VectorMetric.DotProduct when score >= 0d => 1d / (1d + Math.Exp(-score)),
            VectorMetric.DotProduct => Math.Exp(score) / (1d + Math.Exp(score)),
            _ => throw new NotSupportedException()
        };
        return Math.Clamp(double.IsFinite(value) ? value : score > 0d ? 1d : 0d, 0d, 1d);
    }

    private double ProviderThreshold(double similarity) => _plan.Metric switch
    {
        VectorMetric.Cosine => (2d * similarity) - 1d,
        VectorMetric.Euclidean => similarity == 0d ? double.MaxValue : (1d / similarity) - 1d,
        VectorMetric.DotProduct => similarity switch
        {
            <= 0d => -double.MaxValue,
            >= 1d => double.MaxValue,
            _ => Math.Log(similarity / (1d - similarity))
        },
        _ => throw new NotSupportedException()
    };

    private static double Norm(ReadOnlySpan<float> embedding)
    {
        double squared = 0d;
        foreach (var value in embedding) squared += value * (double)value;
        return Math.Sqrt(squared);
    }

    private static JsonElement Result(QdrantResponse response)
    {
        var root = response.Document?.RootElement
            ?? throw new InvalidOperationException("Qdrant returned no JSON result.");
        return root.TryGetProperty("result", out var result)
            ? result
            : throw new InvalidOperationException("Qdrant returned JSON without a result value.");
    }

    private static void RequireCompleted(QdrantResponse response, string operation)
    {
        var result = Result(response);
        if (!result.TryGetProperty("status", out var status) ||
            !string.Equals(status.GetString(), "completed", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                $"Qdrant {operation} did not confirm completed visibility for the awaited mutation.");
    }

    private static string ResponseId(JsonElement id) => id.ValueKind switch
    {
        JsonValueKind.String => Guid.TryParse(id.GetString(), out var guid)
            ? guid.ToString("D")
            : id.GetString()!,
        JsonValueKind.Number => id.GetUInt64().ToString(CultureInfo.InvariantCulture),
        _ => throw new InvalidOperationException("Qdrant returned an unsupported point identity shape.")
    };

    private static void WriteIds(Utf8JsonWriter writer, IEnumerable<StoragePointId> ids)
    {
        writer.WriteStartArray();
        foreach (var id in ids) id.Write(writer);
        writer.WriteEndArray();
    }

    private static void WriteHasIds(Utf8JsonWriter writer, IEnumerable<StoragePointId> ids)
    {
        writer.WriteStartObject();
        writer.WritePropertyName("has_id");
        WriteIds(writer, ids);
        writer.WriteEndObject();
    }

    private static void WriteScopeMatch(Utf8JsonWriter writer, string scope)
    {
        writer.WriteStartObject();
        writer.WriteString("key", Infrastructure.Constants.Wire.Scope);
        writer.WritePropertyName("match");
        writer.WriteStartObject();
        writer.WriteString("value", scope);
        writer.WriteEndObject();
        writer.WriteEndObject();
    }

    private static byte[] Json(Action<Utf8JsonWriter> write)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer)) write(writer);
        return buffer.WrittenSpan.ToArray();
    }

    private static string Escape(string value) => Uri.EscapeDataString(value);
    private static string PointsPath(string collection) => $"collections/{Escape(collection)}/points?wait=true";

    private void DemandBatch(int count)
    {
        if (count > _options.MaxBatchPoints)
            throw new ArgumentOutOfRangeException(nameof(count),
                $"Qdrant batch contains {count} points; configured maximum is {_options.MaxBatchPoints}.");
    }

    private static BatchResult<TKey> Missing(IReadOnlyList<TKey> ids) => new(
        ids.Select((id, index) => new BatchItemResult<TKey>(index, id, MutationOutcome.Missing)),
        BatchAtomicity.NotGuaranteed);

    private VectorSearchResult<TKey> Empty() => new(
        [], null, new VectorSearchExecution(_plan.Metric, VectorSearchAccuracy.Approximate, 0));

    private InvalidOperationException WrongShape(string collection, string reason) => new(
        $"Qdrant collection '{collection}' cannot realize space '{_plan.Name}': {reason}. " +
        "Provision the declared shape or select the source that owns this collection.");

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

    private sealed record PreparedPoint(
        VectorPoint<TKey> Point,
        StoragePointId StorageId,
        string? MetadataJson,
        double Norm,
        string Scope);

    private sealed record Ranked(TKey Id, string StableId, double RawScore, double Similarity, DataObject? Metadata);

    private readonly record struct StoragePointId(string Text, ulong? Numeric)
    {
        internal static StoragePointId Uuid(Guid value) => new(value.ToString("D"), null);
        internal static StoragePointId Number(ulong value) => new(value.ToString(CultureInfo.InvariantCulture), value);
        internal void Write(Utf8JsonWriter writer)
        {
            if (Numeric is { } value) writer.WriteNumberValue(value);
            else writer.WriteStringValue(Text);
        }
    }
}
