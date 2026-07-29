using System.Buffers;
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

namespace Koan.Data.Vector.Connector.Milvus;

internal sealed class MilvusRepository<TEntity, TKey> :
    IVectorSearchRepository<TEntity, TKey>,
    IDescribesCapabilities,
    IDisposable,
    IAsyncDisposable
    where TEntity : class, IEntity<TKey>
    where TKey : notnull
{
    private readonly IServiceProvider _services;
    private readonly MilvusVectorAdapterFactory _factory;
    private readonly VectorSpacePlan _plan;
    private readonly MilvusRoute _route;
    private readonly MilvusOptions _options;
    private readonly MilvusClient _client;
    private readonly SemaphoreSlim _shapeGate = new(1, 1);
    private string? _readyCollection;
    private int _disposed;

    internal MilvusRepository(
        IServiceProvider services,
        MilvusVectorAdapterFactory factory,
        VectorSpacePlan plan,
        MilvusRoute route,
        MilvusOptions options,
        IHttpClientFactory http)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        _plan = plan ?? throw new ArgumentNullException(nameof(plan));
        _route = route ?? throw new ArgumentNullException(nameof(route));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _client = new MilvusClient(http ?? throw new ArgumentNullException(nameof(http)), route, options);
    }

    public void Describe(ICapabilities capabilities) => capabilities
        .Add(VectorCaps.Knn)
        .Add(VectorCaps.Filters, MilvusFilter.Capabilities)
        .Add(VectorCaps.BulkUpsert)
        .Add(VectorCaps.BulkDelete)
        .Add(VectorCaps.ScoreNormalization)
        .Add(VectorCaps.DynamicCollections);

    public async Task Save(VectorPoint<TKey> point, VectorScope scope, CancellationToken ct = default)
    {
        var prepared = Prepare(point, scope);
        _route.Policy.Demand(DataOperationEffect.Write, $"save Milvus point in space '{_plan.Name}'");
        var collection = Collection();
        await EnsureShape(collection, create: true, ct).ConfigureAwait(false);
        await Upsert(collection, [prepared], ct).ConfigureAwait(false);
    }

    public async Task<BatchResult<TKey>> Save(
        IReadOnlyList<VectorPoint<TKey>> points,
        VectorScope scope,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(points);
        DemandBatch(points.Count);
        var prepared = new PreparedPoint[points.Count];
        var unique = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < points.Count; index++)
        {
            ct.ThrowIfCancellationRequested();
            prepared[index] = Prepare(points[index], scope);
            if (!unique.Add(prepared[index].StorageId))
                throw new ArgumentException("Milvus batches require unique logical identities.", nameof(points));
        }
        if (prepared.Length == 0) return new BatchResult<TKey>([], BatchAtomicity.NotGuaranteed);

        _route.Policy.Demand(DataOperationEffect.Write, $"save Milvus batch in space '{_plan.Name}'");
        var collection = Collection();
        await EnsureShape(collection, create: true, ct).ConfigureAwait(false);
        var existing = await Fetch(collection, prepared.Select(static item => item.StorageId).ToArray(), ct)
            .ConfigureAwait(false);
        await Upsert(collection, prepared, ct).ConfigureAwait(false);
        var outcomes = prepared.Select((item, index) => new BatchItemResult<TKey>(
            index,
            item.Point.Id,
            existing.ContainsKey(item.StorageId) ? MutationOutcome.Updated : MutationOutcome.Inserted));
        return new BatchResult<TKey>(outcomes, BatchAtomicity.NotGuaranteed);
    }

    public async Task<VectorPoint<TKey>?> Get(TKey id, VectorScope scope, CancellationToken ct = default)
    {
        var collection = Collection();
        if (!await EnsureShape(collection, create: false, ct).ConfigureAwait(false)) return null;
        return await FetchOne(collection, StorageId(id, scope), id, ct).ConfigureAwait(false);
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
        var found = await Fetch(collection, storage.Distinct(StringComparer.Ordinal).ToArray(), ct).ConfigureAwait(false);
        for (var index = 0; index < ids.Count; index++)
            if (found.TryGetValue(storage[index], out var point))
            {
                if (!EqualityComparer<TKey>.Default.Equals(point.Id, ids[index]))
                    throw new InvalidOperationException("Milvus returned a conflicting Koan logical identity.");
                output[index] = point;
            }
        return output;
    }

    public async Task<bool> Delete(TKey id, VectorScope scope, CancellationToken ct = default)
    {
        _route.Policy.Demand(DataOperationEffect.Write, $"delete Milvus point from space '{_plan.Name}'");
        var collection = Collection();
        if (!await EnsureShape(collection, create: false, ct).ConfigureAwait(false)) return false;
        var storageId = StorageId(id, scope);
        if (await FetchOne(collection, storageId, id, ct).ConfigureAwait(false) is null) return false;
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
        _route.Policy.Demand(DataOperationEffect.Write, $"delete Milvus batch from space '{_plan.Name}'");
        var collection = Collection();
        if (!await EnsureShape(collection, create: false, ct).ConfigureAwait(false)) return Missing(ids);
        var storage = ids.Select(id => StorageId(id, scope)).ToArray();
        var existing = await Fetch(collection, storage.Distinct(StringComparer.Ordinal).ToArray(), ct).ConfigureAwait(false);
        var removed = new HashSet<string>(StringComparer.Ordinal);
        var delete = new List<string>();
        var outcomes = new BatchItemResult<TKey>[ids.Count];
        for (var index = 0; index < ids.Count; index++)
        {
            var found = existing.ContainsKey(storage[index]) && removed.Add(storage[index]);
            outcomes[index] = new BatchItemResult<TKey>(index, ids[index],
                found ? MutationOutcome.Deleted : MutationOutcome.Missing);
            if (found) delete.Add(storage[index]);
        }
        if (delete.Count > 0) await DeleteIds(collection, delete, ct).ConfigureAwait(false);
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
            ranked = await SearchNative(collection, request, requested, ct).ConfigureAwait(false);
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
                    $"Milvus cannot resolve a stable identity tie within the configured bound of {_options.MaxSearchCandidates} candidates.");
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
        _route.Policy.Demand(DataOperationEffect.Write, $"clear Milvus space '{_plan.Name}'");
        var collection = Collection();
        if (!await EnsureShape(collection, create: false, ct).ConfigureAwait(false)) return;
        var filter = scope.Predicate is null
            ? $"{Infrastructure.Constants.Wire.Id} != \"\""
            : MilvusFilter.Write(scope.Predicate);
        var ids = await QueryIds(collection, filter, _options.MaxClearPoints + 1, ct).ConfigureAwait(false);
        if (ids.Count > _options.MaxClearPoints)
            throw new InvalidOperationException(
                $"Milvus clear exceeds the configured {_options.MaxClearPoints} point safety bound.");
        if (ids.Count > 0) await DeleteIds(collection, ids, ct).ConfigureAwait(false);
    }

    public async Task Sync(VectorScope scope, CancellationToken ct = default)
    {
        ThrowIfDisposed();
        var collection = Collection();
        if (!await EnsureShape(collection, create: false, ct).ConfigureAwait(false)) return;
        await AwaitLoaded(collection, allowLoad: true, ct).ConfigureAwait(false);
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
        if (string.Equals(Volatile.Read(ref _readyCollection), collection, StringComparison.Ordinal)) return true;
        await _shapeGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (string.Equals(_readyCollection, collection, StringComparison.Ordinal)) return true;
            using var current = await DescribeCollection(collection, allowMissing: true, ct).ConfigureAwait(false);
            if (!current.IsMissing)
            {
                ValidateShape(collection, current.Data);
                await AwaitLoaded(collection, allowLoad: true, ct).ConfigureAwait(false);
                Volatile.Write(ref _readyCollection, collection);
                return true;
            }
            if (!create) return false;
            _route.Policy.Demand(DataOperationEffect.SchemaOrAdmin,
                $"create Milvus collection for space '{_plan.Name}'");
            using (var created = await _client.Post(
                "v2/vectordb/collections/create", CreateCollection(collection),
                "collection create", false, ct).ConfigureAwait(false))
            {
            }
            using var confirmed = await DescribeCollection(collection, allowMissing: false, ct).ConfigureAwait(false);
            ValidateShape(collection, confirmed.Data);
            await AwaitLoaded(collection, allowLoad: true, ct).ConfigureAwait(false);
            Volatile.Write(ref _readyCollection, collection);
            return true;
        }
        finally
        {
            _shapeGate.Release();
        }
    }

    private Task<MilvusResponse> DescribeCollection(string collection, bool allowMissing, CancellationToken ct) =>
        _client.Post("v2/vectordb/collections/describe", Json(writer =>
        {
            writer.WriteStartObject();
            WriteRoute(writer, collection);
            writer.WriteEndObject();
        }), "collection inspection", allowMissing, ct);

    private byte[] CreateCollection(string collection) => Json(writer =>
    {
        writer.WriteStartObject();
        WriteRoute(writer, collection);
        writer.WritePropertyName("schema");
        writer.WriteStartObject();
        writer.WriteBoolean("autoID", false);
        writer.WriteBoolean("enableDynamicField", false);
        writer.WritePropertyName("fields");
        writer.WriteStartArray();
        WriteField(writer, Infrastructure.Constants.Wire.Id, "VarChar", primary: true,
            "max_length", 64);
        WriteField(writer, Infrastructure.Constants.Wire.LogicalId, "VarChar", primary: false,
            "max_length", Infrastructure.Constants.Defaults.PrimaryKeyLength);
        WriteField(writer, Infrastructure.Constants.Wire.Vector, "FloatVector", primary: false,
            "dim", _plan.Dimensions);
        WriteField(writer, Infrastructure.Constants.Wire.Metadata, "JSON", primary: false, nullable: true);
        WriteField(writer, ContractField(), "Bool", primary: false, nullable: true);
        writer.WriteEndArray();
        writer.WriteEndObject();
        writer.WritePropertyName("indexParams");
        writer.WriteStartArray();
        writer.WriteStartObject();
        writer.WriteString("metricType", Metric());
        writer.WriteString("fieldName", Infrastructure.Constants.Wire.Vector);
        writer.WriteString("indexName", Infrastructure.Constants.Wire.Index);
        writer.WritePropertyName("params");
        writer.WriteStartObject();
        writer.WriteString("index_type", "HNSW");
        writer.WriteNumber("M", Infrastructure.Constants.Defaults.HnswM);
        writer.WriteNumber("efConstruction", Infrastructure.Constants.Defaults.HnswEfConstruction);
        writer.WriteEndObject();
        writer.WriteEndObject();
        writer.WriteEndArray();
        writer.WritePropertyName("params");
        writer.WriteStartObject();
        writer.WriteString("consistencyLevel", "Strong");
        writer.WriteEndObject();
        writer.WriteEndObject();
    });

    private static void WriteField(
        Utf8JsonWriter writer,
        string name,
        string type,
        bool primary,
        string? parameter = null,
        int value = 0,
        bool nullable = false)
    {
        writer.WriteStartObject();
        writer.WriteString("fieldName", name);
        writer.WriteString("dataType", type);
        writer.WriteBoolean("isPrimary", primary);
        if (nullable) writer.WriteBoolean("nullable", true);
        if (parameter is not null)
        {
            writer.WritePropertyName("elementTypeParams");
            writer.WriteStartObject();
            writer.WriteNumber(parameter, value);
            writer.WriteEndObject();
        }
        writer.WriteEndObject();
    }

    private void ValidateShape(string collection, JsonElement data)
    {
        if (data.ValueKind != JsonValueKind.Object)
            throw WrongShape(collection, "the provider returned no collection description");
        if (!data.TryGetProperty("enableDynamicField", out var dynamic) || dynamic.GetBoolean())
            throw WrongShape(collection, "dynamic fields are enabled");
        if (!data.TryGetProperty("fields", out var fields) || fields.ValueKind != JsonValueKind.Array)
            throw WrongShape(collection, "the fixed Koan fields are absent");
        ValidateField(collection, fields, Infrastructure.Constants.Wire.Id, "VarChar", primary: true, null);
        ValidateField(collection, fields, Infrastructure.Constants.Wire.LogicalId, "VarChar", primary: false, null);
        ValidateField(collection, fields, Infrastructure.Constants.Wire.Vector, "FloatVector", primary: false,
            _plan.Dimensions);
        ValidateField(collection, fields, Infrastructure.Constants.Wire.Metadata, "JSON", primary: false, null);
        ValidateField(collection, fields, ContractField(), "Bool", primary: false, null);
        if (!data.TryGetProperty("indexes", out var indexes) || indexes.ValueKind != JsonValueKind.Array ||
            !indexes.EnumerateArray().Any(index =>
                index.TryGetProperty("fieldName", out var field) &&
                string.Equals(field.GetString(), Infrastructure.Constants.Wire.Vector, StringComparison.Ordinal) &&
                index.TryGetProperty("metricType", out var metric) &&
                string.Equals(metric.GetString(), Metric(), StringComparison.OrdinalIgnoreCase)))
            throw WrongShape(collection, $"the '{Metric()}' vector index is absent");
    }

    private void ValidateField(
        string collection,
        JsonElement fields,
        string name,
        string type,
        bool primary,
        int? dimensions)
    {
        var field = fields.EnumerateArray().FirstOrDefault(item =>
            item.TryGetProperty("name", out var fieldName) &&
            string.Equals(fieldName.GetString(), name, StringComparison.Ordinal));
        if (field.ValueKind != JsonValueKind.Object ||
            !field.TryGetProperty("type", out var fieldType) ||
            !string.Equals(fieldType.GetString(), type, StringComparison.OrdinalIgnoreCase))
            throw WrongShape(collection, $"field '{name}' is absent or is not {type}");
        var isPrimary = field.TryGetProperty("primaryKey", out var primaryKey) && primaryKey.GetBoolean();
        if (isPrimary != primary) throw WrongShape(collection, $"field '{name}' primary-key role differs");
        if (dimensions is null) return;
        if (!field.TryGetProperty("params", out var parameters) || parameters.ValueKind != JsonValueKind.Array ||
            !parameters.EnumerateArray().Any(parameter =>
                parameter.TryGetProperty("key", out var key) && key.GetString() == "dim" &&
                parameter.TryGetProperty("value", out var value) && value.GetString() == dimensions.Value.ToString(CultureInfo.InvariantCulture)))
            throw WrongShape(collection, $"field '{name}' does not have {dimensions} dimensions");
    }

    private async Task AwaitLoaded(string collection, bool allowLoad, CancellationToken ct)
    {
        var deadline = DateTime.UtcNow.AddSeconds(_options.VisibilityTimeoutSeconds);
        var requested = false;
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            using var state = await _client.Post("v2/vectordb/collections/get_load_state", Json(writer =>
            {
                writer.WriteStartObject();
                WriteRoute(writer, collection);
                writer.WriteEndObject();
            }), "collection load inspection", false, ct).ConfigureAwait(false);
            if (state.Data.ValueKind == JsonValueKind.Object &&
                state.Data.TryGetProperty("loadState", out var load) &&
                string.Equals(load.GetString(), "LoadStateLoaded", StringComparison.OrdinalIgnoreCase))
                return;
            if (!requested && allowLoad)
            {
                _route.Policy.Demand(DataOperationEffect.SchemaOrAdmin,
                    $"load Milvus collection for space '{_plan.Name}'");
                using var loaded = await _client.Post("v2/vectordb/collections/load", Json(writer =>
                {
                    writer.WriteStartObject();
                    WriteRoute(writer, collection);
                    writer.WriteEndObject();
                }), "collection load", false, ct).ConfigureAwait(false);
                requested = true;
            }
            if (DateTime.UtcNow >= deadline)
                throw new TimeoutException(
                    $"Milvus did not load space '{_plan.Name}' within {_options.VisibilityTimeoutSeconds} seconds.");
            await Task.Delay(Infrastructure.Constants.Defaults.VisibilityPollMilliseconds, ct).ConfigureAwait(false);
        }
    }

    private PreparedPoint Prepare(VectorPoint<TKey> point, VectorScope scope)
    {
        ArgumentNullException.ThrowIfNull(point);
        ValidateEmbedding(point.Embedding.Span, "point embedding");
        var logical = Key(point.Id);
        if (logical.Length > Infrastructure.Constants.Defaults.PrimaryKeyLength)
            throw new ArgumentException(
                $"Milvus logical identity exceeds {Infrastructure.Constants.Defaults.PrimaryKeyLength} characters.");
        var metadata = VectorMetadata.ToJson(point.Metadata);
        if (metadata is not null && Encoding.UTF8.GetByteCount(metadata) > _options.MaxMetadataBytesPerPoint)
            throw new InvalidOperationException(
                $"Milvus point metadata exceeds the configured {_options.MaxMetadataBytesPerPoint} byte limit.");
        return new PreparedPoint(point, StorageId(point.Id, scope), logical, metadata);
    }

    private async Task Upsert(string collection, IReadOnlyList<PreparedPoint> points, CancellationToken ct)
    {
        using var response = await _client.Post("v2/vectordb/entities/upsert", Json(writer =>
        {
            writer.WriteStartObject();
            WriteRoute(writer, collection);
            writer.WritePropertyName("data");
            writer.WriteStartArray();
            foreach (var point in points)
            {
                writer.WriteStartObject();
                writer.WriteString(Infrastructure.Constants.Wire.Id, point.StorageId);
                writer.WriteString(Infrastructure.Constants.Wire.LogicalId, point.LogicalId);
                writer.WritePropertyName(Infrastructure.Constants.Wire.Vector);
                writer.WriteStartArray();
                foreach (var value in point.Point.Embedding.Span) writer.WriteNumberValue(value);
                writer.WriteEndArray();
                writer.WritePropertyName(Infrastructure.Constants.Wire.Metadata);
                if (point.Metadata is null) writer.WriteNullValue();
                else writer.WriteRawValue(point.Metadata);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteEndObject();
        }), points.Count == 1 ? "point upsert" : "batch upsert", false, ct).ConfigureAwait(false);
        if (response.Data.ValueKind == JsonValueKind.Object &&
            response.Data.TryGetProperty("upsertCount", out var count) && count.GetInt32() != points.Count)
            throw new InvalidOperationException("Milvus did not acknowledge every point in the upsert request.");
    }

    private async Task<Dictionary<string, VectorPoint<TKey>>> Fetch(
        string collection,
        IReadOnlyList<string> storageIds,
        CancellationToken ct)
    {
        if (storageIds.Count == 0) return new Dictionary<string, VectorPoint<TKey>>(StringComparer.Ordinal);
        using var response = await _client.Post("v2/vectordb/entities/get", Json(writer =>
        {
            writer.WriteStartObject();
            WriteRoute(writer, collection);
            writer.WritePropertyName("id");
            writer.WriteStartArray();
            foreach (var id in storageIds) writer.WriteStringValue(id);
            writer.WriteEndArray();
            WriteOutputFields(writer, includeVector: true);
            writer.WriteEndObject();
        }), "point retrieval", false, ct).ConfigureAwait(false);
        var output = new Dictionary<string, VectorPoint<TKey>>(StringComparer.Ordinal);
        if (response.Data.ValueKind != JsonValueKind.Array) return output;
        foreach (var row in response.Data.EnumerateArray())
        {
            var storageId = row.GetProperty(Infrastructure.Constants.Wire.Id).GetString()
                ?? throw new InvalidOperationException("Milvus returned an invalid storage identity.");
            output[storageId] = ReadPoint(row);
        }
        return output;
    }

    private async Task<VectorPoint<TKey>?> FetchOne(
        string collection,
        string storageId,
        TKey expected,
        CancellationToken ct)
    {
        var found = await Fetch(collection, [storageId], ct).ConfigureAwait(false);
        if (!found.TryGetValue(storageId, out var point)) return null;
        if (!EqualityComparer<TKey>.Default.Equals(point.Id, expected))
            throw new InvalidOperationException("Milvus returned a conflicting Koan logical identity.");
        return point;
    }

    private VectorPoint<TKey> ReadPoint(JsonElement row)
    {
        var logical = row.GetProperty(Infrastructure.Constants.Wire.LogicalId).GetString()
            ?? throw new InvalidOperationException("Milvus returned an invalid logical identity.");
        if (!row.TryGetProperty(Infrastructure.Constants.Wire.Vector, out var vector) ||
            vector.ValueKind != JsonValueKind.Array)
            throw new InvalidOperationException("Milvus returned no point embedding.");
        var values = vector.EnumerateArray().Select(static value => value.GetSingle()).ToArray();
        if (values.Length != _plan.Dimensions)
            throw new InvalidOperationException(
                $"Milvus point has {values.Length} dimensions; expected {_plan.Dimensions}.");
        return new VectorPoint<TKey>(ParseKey(logical), values, ReadMetadata(row));
    }

    private async Task<List<Ranked>> SearchNative(
        string collection,
        VectorSearchRequest request,
        int candidates,
        CancellationToken ct)
    {
        using var response = await _client.Post("v2/vectordb/entities/search", Json(writer =>
        {
            writer.WriteStartObject();
            WriteRoute(writer, collection);
            writer.WriteString("annsField", Infrastructure.Constants.Wire.Vector);
            writer.WritePropertyName("data");
            writer.WriteStartArray();
            writer.WriteStartArray();
            foreach (var value in request.Embedding.Span) writer.WriteNumberValue(value);
            writer.WriteEndArray();
            writer.WriteEndArray();
            writer.WriteNumber("limit", candidates);
            if (request.Filter is not null) writer.WriteString("filter", MilvusFilter.Write(request.Filter));
            WriteOutputFields(writer, includeVector: false);
            writer.WriteString("consistencyLevel", "Strong");
            writer.WritePropertyName("searchParams");
            writer.WriteStartObject();
            writer.WriteString("metricType", Metric());
            writer.WritePropertyName("params");
            writer.WriteStartObject();
            writer.WriteNumber("ef", Math.Max(64, candidates));
            writer.WriteEndObject();
            writer.WriteEndObject();
            writer.WriteEndObject();
        }), "vector search", false, ct).ConfigureAwait(false);
        var output = new List<Ranked>();
        if (response.Data.ValueKind != JsonValueKind.Array) return output;
        foreach (var row in response.Data.EnumerateArray())
        {
            var logical = row.GetProperty(Infrastructure.Constants.Wire.LogicalId).GetString()
                ?? throw new InvalidOperationException("Milvus returned an invalid logical identity.");
            var raw = row.TryGetProperty("distance", out var distance)
                ? distance.GetDouble()
                : row.TryGetProperty("score", out var score)
                    ? score.GetDouble()
                    : throw new InvalidOperationException("Milvus returned no vector score.");
            output.Add(new Ranked(ParseKey(logical), logical, raw, Similarity(raw), ReadMetadata(row)));
        }
        return output;
    }

    private async Task<IReadOnlyList<string>> QueryIds(
        string collection,
        string filter,
        int limit,
        CancellationToken ct)
    {
        using var response = await _client.Post("v2/vectordb/entities/query", Json(writer =>
        {
            writer.WriteStartObject();
            WriteRoute(writer, collection);
            writer.WriteString("filter", filter);
            writer.WriteNumber("limit", limit);
            writer.WritePropertyName("outputFields");
            writer.WriteStartArray();
            writer.WriteStringValue(Infrastructure.Constants.Wire.Id);
            writer.WriteEndArray();
            writer.WriteString("consistencyLevel", "Strong");
            writer.WriteEndObject();
        }), "bounded identity query", false, ct).ConfigureAwait(false);
        if (response.Data.ValueKind != JsonValueKind.Array) return [];
        return response.Data.EnumerateArray()
            .Select(row => row.GetProperty(Infrastructure.Constants.Wire.Id).GetString()
                ?? throw new InvalidOperationException("Milvus returned an invalid storage identity."))
            .ToArray();
    }

    private async Task DeleteIds(string collection, IReadOnlyList<string> storageIds, CancellationToken ct)
    {
        using var response = await _client.Post("v2/vectordb/entities/delete", Json(writer =>
        {
            writer.WriteStartObject();
            WriteRoute(writer, collection);
            writer.WriteString("filter", $"{Infrastructure.Constants.Wire.Id} in [{string.Join(',', storageIds.Select(static id => JsonSerializer.Serialize(id)))}]");
            writer.WriteEndObject();
        }), storageIds.Count == 1 ? "point delete" : "batch delete", false, ct).ConfigureAwait(false);
    }

    private void Validate(VectorSearchRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateEmbedding(request.Embedding.Span, "query embedding");
        if (request.Top <= 0 || request.Top >= _options.MaxSearchCandidates)
            throw new ArgumentOutOfRangeException(nameof(request),
                $"Milvus Top must be positive and smaller than MaxSearchCandidates ({_options.MaxSearchCandidates}).");
        if (request.Space is not null && !string.Equals(request.Space, _plan.Name, StringComparison.Ordinal))
            throw new InvalidOperationException(
                $"Milvus query requested space '{request.Space}'. Available space: {_plan.Name}.");
        if (request.Text is not null || request.SemanticWeight is not null)
            throw new NotSupportedException("Milvus adapter does not claim portable hybrid text semantics.");
        if (request.Continuation is not null)
            throw new NotSupportedException("Milvus adapter does not claim a stable search continuation snapshot.");
        if (request.MinimumSimilarity is < 0d or > 1d ||
            request.MinimumSimilarity is { } minimum && !double.IsFinite(minimum))
            throw new ArgumentOutOfRangeException(nameof(request), "Minimum similarity must be finite and in [0,1].");
    }

    private void ValidateEmbedding(ReadOnlySpan<float> embedding, string label)
    {
        if (embedding.Length != _plan.Dimensions)
            throw new ArgumentException(
                $"Milvus {label} has {embedding.Length} dimensions; space '{_plan.Name}' requires {_plan.Dimensions}.");
        for (var index = 0; index < embedding.Length; index++)
            if (!float.IsFinite(embedding[index]))
                throw new ArgumentException($"Milvus {label} contains a non-finite value at index {index}.");
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
        if (readable.Length == 0 || !char.IsAsciiLetter(readable[0]) && readable[0] != '_') readable = "k_" + readable;
        if (readable.Length > 230) readable = readable[..230];
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(logical)))[..16].ToLowerInvariant();
        return readable + "_" + hash;
    }

    private string StorageId(TKey id, VectorScope scope)
    {
        var value = $"{Collection()}\u001f{scope.Identity}\u001f{Key(id)}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    }

    private string ContractField()
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(new ContractShape(
            _plan.Dimensions, _plan.Metric.ToString(), _plan.Name, _plan.Model,
            Infrastructure.Constants.Wire.Id, Infrastructure.Constants.Wire.Vector,
            Infrastructure.Constants.Wire.Metadata, "HNSW"));
        var hash = Convert.ToHexString(SHA256.HashData(payload))[..24].ToLowerInvariant();
        return Infrastructure.Constants.Wire.ContractPrefix + hash;
    }

    private string Metric() => _plan.Metric switch
    {
        VectorMetric.Cosine => "COSINE",
        VectorMetric.Euclidean => "L2",
        VectorMetric.DotProduct => "IP",
        _ => throw new NotSupportedException($"Milvus does not support metric '{_plan.Metric}'.")
    };

    private double Similarity(double raw)
    {
        var value = _plan.Metric switch
        {
            VectorMetric.Cosine => (raw + 1d) / 2d,
            VectorMetric.Euclidean => 1d / (1d + Math.Sqrt(Math.Max(0d, raw))),
            VectorMetric.DotProduct => Logistic(raw),
            _ => throw new NotSupportedException()
        };
        return Math.Clamp(double.IsFinite(value) ? value : raw > 0d ? 1d : 0d, 0d, 1d);
    }

    private static double Logistic(double value) => value >= 0d
        ? 1d / (1d + Math.Exp(-value))
        : Math.Exp(value) / (1d + Math.Exp(value));

    private static double Norm(ReadOnlySpan<float> embedding)
    {
        double squared = 0d;
        foreach (var value in embedding) squared += value * (double)value;
        return Math.Sqrt(squared);
    }

    private static DataObject? ReadMetadata(JsonElement row)
    {
        if (!row.TryGetProperty(Infrastructure.Constants.Wire.Metadata, out var metadata) ||
            metadata.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return null;
        return VectorMetadata.FromJson(metadata.ValueKind == JsonValueKind.String
            ? metadata.GetString()
            : metadata.GetRawText());
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

    private void WriteRoute(Utf8JsonWriter writer, string collection)
    {
        writer.WriteString("dbName", _route.Database);
        writer.WriteString("collectionName", collection);
    }

    private static void WriteOutputFields(Utf8JsonWriter writer, bool includeVector)
    {
        writer.WritePropertyName("outputFields");
        writer.WriteStartArray();
        writer.WriteStringValue(Infrastructure.Constants.Wire.Id);
        writer.WriteStringValue(Infrastructure.Constants.Wire.LogicalId);
        if (includeVector) writer.WriteStringValue(Infrastructure.Constants.Wire.Vector);
        writer.WriteStringValue(Infrastructure.Constants.Wire.Metadata);
        writer.WriteEndArray();
    }

    private void DemandBatch(int count)
    {
        if (count > _options.MaxBatchPoints)
            throw new ArgumentOutOfRangeException(nameof(count),
                $"Milvus batch contains {count} points; configured maximum is {_options.MaxBatchPoints}.");
    }

    private static BatchResult<TKey> Missing(IReadOnlyList<TKey> ids) => new(
        ids.Select((id, index) => new BatchItemResult<TKey>(index, id, MutationOutcome.Missing)),
        BatchAtomicity.NotGuaranteed);

    private VectorSearchResult<TKey> Empty() => new(
        [], null, new VectorSearchExecution(_plan.Metric, VectorSearchAccuracy.Approximate, null));

    private InvalidOperationException WrongShape(string collection, string reason) => new(
        $"Milvus collection '{collection}' cannot realize space '{_plan.Name}': {reason}. " +
        "Provision the declared shape or select the source that owns this collection.");

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

    private sealed record PreparedPoint(VectorPoint<TKey> Point, string StorageId, string LogicalId, string? Metadata);
    private sealed record Ranked(TKey Id, string StableId, double RawScore, double Similarity, DataObject? Metadata);
    private sealed record ContractShape(
        int Dimensions,
        string Metric,
        string Space,
        string? Model,
        string Id,
        string Vector,
        string Metadata,
        string Index);
}
