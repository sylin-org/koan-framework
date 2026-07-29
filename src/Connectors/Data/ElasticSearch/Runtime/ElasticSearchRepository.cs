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

namespace Koan.Data.Connector.ElasticSearch;

internal sealed class ElasticSearchRepository<TEntity, TKey> :
    IVectorSearchRepository<TEntity, TKey>,
    IDescribesCapabilities,
    IDisposable,
    IAsyncDisposable
    where TEntity : class, IEntity<TKey>
    where TKey : notnull
{
    private readonly IServiceProvider _services;
    private readonly ElasticSearchVectorAdapterFactory _factory;
    private readonly VectorSpacePlan _plan;
    private readonly ElasticSearchRoute _route;
    private readonly ElasticSearchOptions _options;
    private readonly ElasticSearchClient _client;
    private readonly ConcurrentDictionary<string, byte> _ready = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _shapeGate = new(1, 1);
    private int _disposed;

    internal ElasticSearchRepository(
        IServiceProvider services,
        ElasticSearchVectorAdapterFactory factory,
        VectorSpacePlan plan,
        ElasticSearchRoute route,
        ElasticSearchOptions options,
        IHttpClientFactory http)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        _plan = plan ?? throw new ArgumentNullException(nameof(plan));
        _route = route ?? throw new ArgumentNullException(nameof(route));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _client = new ElasticSearchClient(http ?? throw new ArgumentNullException(nameof(http)), route, options);
    }

    public void Describe(ICapabilities capabilities) => capabilities
        .Add(VectorCaps.Knn)
        .Add(VectorCaps.Filters, ElasticSearchFilter.Capabilities)
        .Add(VectorCaps.BulkUpsert)
        .Add(VectorCaps.BulkDelete)
        .Add(VectorCaps.ScoreNormalization)
        .Add(VectorCaps.DynamicCollections);

    public async Task Save(VectorPoint<TKey> point, VectorScope scope, CancellationToken ct = default)
    {
        var prepared = Prepare(point, scope);
        _route.Policy.Demand(DataOperationEffect.Write, $"save Elasticsearch point in space '{_plan.Name}'");
        var index = IndexAlias();
        await EnsureShape(index, create: true, ct).ConfigureAwait(false);
        var body = Document(prepared);
        DemandRequest(body.Length);
        using var response = await _client.Put(
            $"{Escape(index)}/_doc/{prepared.StorageId}?refresh=true&require_alias=true",
            body,
            "point save",
            allowBadRequest: false,
            ct).ConfigureAwait(false);
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

        _route.Policy.Demand(DataOperationEffect.Write, $"save Elasticsearch batch in space '{_plan.Name}'");
        var alias = IndexAlias();
        await EnsureShape(alias, create: true, ct).ConfigureAwait(false);
        var body = Bulk(writer =>
        {
            foreach (var item in prepared)
            {
                Line(writer, json =>
                {
                    json.WriteStartObject();
                    json.WritePropertyName("index");
                    json.WriteStartObject();
                    json.WriteString("_index", alias);
                    json.WriteString("_id", item.StorageId);
                    json.WriteEndObject();
                    json.WriteEndObject();
                });
                Line(writer, json => WriteDocument(json, item));
            }
        });
        DemandRequest(body.Length);
        using var response = await _client.Post(
            "_bulk?refresh=true&require_alias=true",
            body,
            "batch save",
            readOnly: false,
            allowNotFound: false,
            "application/x-ndjson",
            ct).ConfigureAwait(false);
        return ParseSaveBatch(response, prepared);
    }

    public async Task<VectorPoint<TKey>?> Get(TKey id, VectorScope scope, CancellationToken ct = default)
    {
        var index = IndexAlias();
        if (!await EnsureShape(index, create: false, ct).ConfigureAwait(false)) return null;
        var storage = StorageId(id, scope);
        var found = await Fetch(index, [new Requested(id, storage)], scope, ct).ConfigureAwait(false);
        return found.TryGetValue(storage, out var point) ? point : null;
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
        var index = IndexAlias();
        if (!await EnsureShape(index, create: false, ct).ConfigureAwait(false)) return output;
        var requested = ids.Select(id => new Requested(id, StorageId(id, scope))).ToArray();
        var found = await Fetch(index, requested, scope, ct).ConfigureAwait(false);
        for (var position = 0; position < requested.Length; position++)
            if (found.TryGetValue(requested[position].StorageId, out var point)) output[position] = point;
        return output;
    }

    public async Task<bool> Delete(TKey id, VectorScope scope, CancellationToken ct = default)
    {
        var result = await Delete([id], scope, ct).ConfigureAwait(false);
        return result.Items[0].Outcome == MutationOutcome.Deleted;
    }

    public async Task<BatchResult<TKey>> Delete(
        IReadOnlyList<TKey> ids,
        VectorScope scope,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ids);
        DemandBatch(ids.Count);
        if (ids.Count == 0) return new BatchResult<TKey>([], BatchAtomicity.NotGuaranteed);
        _route.Policy.Demand(DataOperationEffect.Write, $"delete Elasticsearch batch from space '{_plan.Name}'");
        var alias = IndexAlias();
        if (!await EnsureShape(alias, create: false, ct).ConfigureAwait(false)) return Missing(ids);

        var requested = ids.Select(id => new Requested(id, StorageId(id, scope))).ToArray();
        var allowed = await Fetch(alias, requested, scope, ct).ConfigureAwait(false);
        var positions = new List<int>(requested.Length);
        var body = Bulk(writer =>
        {
            for (var position = 0; position < requested.Length; position++)
            {
                if (!allowed.ContainsKey(requested[position].StorageId)) continue;
                positions.Add(position);
                Line(writer, json =>
                {
                    json.WriteStartObject();
                    json.WritePropertyName("delete");
                    json.WriteStartObject();
                    json.WriteString("_index", alias);
                    json.WriteString("_id", requested[position].StorageId);
                    json.WriteEndObject();
                    json.WriteEndObject();
                });
            }
        });
        var outcomes = ids.Select((id, position) =>
            new BatchItemResult<TKey>(position, id, MutationOutcome.Missing)).ToArray();
        if (positions.Count == 0) return new BatchResult<TKey>(outcomes, BatchAtomicity.NotGuaranteed);
        DemandRequest(body.Length);
        using var response = await _client.Post(
            "_bulk?refresh=true&require_alias=true",
            body,
            "batch delete",
            readOnly: false,
            allowNotFound: false,
            "application/x-ndjson",
            ct).ConfigureAwait(false);
        ParseDeleteBatch(response, ids, positions, outcomes);
        return new BatchResult<TKey>(outcomes, BatchAtomicity.NotGuaranteed);
    }

    public async Task<VectorSearchResult<TKey>> Search(
        VectorSearchRequest request,
        VectorScope scope,
        CancellationToken ct = default)
    {
        Validate(request);
        var index = IndexAlias();
        if (!await EnsureShape(index, create: false, ct).ConfigureAwait(false)) return Empty();
        var candidates = Math.Min(_options.MaxSearchCandidates, checked(request.Top + 1));
        List<Ranked> ranked;
        while (true)
        {
            ranked = await Query(index, request, scope, candidates, ct).ConfigureAwait(false);
            ranked.Sort(static (left, right) =>
            {
                var score = right.Similarity.CompareTo(left.Similarity);
                return score != 0 ? score : StringComparer.Ordinal.Compare(left.StableId, right.StableId);
            });
            if (ranked.Count <= request.Top || ranked.Count < candidates ||
                ranked[request.Top - 1].NativeScore != ranked[request.Top].NativeScore)
                break;
            if (candidates >= _options.MaxSearchCandidates)
                throw new InvalidOperationException(
                    $"Elasticsearch cannot resolve a stable identity tie within the configured bound of " +
                    $"{_options.MaxSearchCandidates} candidates. Increase MaxSearchCandidates or narrow the vector space.");
            candidates = Math.Min(_options.MaxSearchCandidates, checked(candidates * 2));
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
        _route.Policy.Demand(DataOperationEffect.Write, $"clear Elasticsearch space '{_plan.Name}'");
        var index = IndexAlias();
        if (!await EnsureShape(index, create: false, ct).ConfigureAwait(false)) return;
        var body = Json(writer =>
        {
            writer.WriteStartObject();
            writer.WritePropertyName("query");
            ElasticSearchFilter.Write(writer, scope.Predicate, scope.Identity);
            writer.WriteEndObject();
        });
        DemandRequest(body.Length);
        using var response = await _client.Post(
            $"{Escape(index)}/_delete_by_query?refresh=true&conflicts=proceed",
            body,
            "space clear",
            readOnly: false,
            allowNotFound: true,
            "application/json",
            ct).ConfigureAwait(false);
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

    private async Task EnsureCreated(CancellationToken ct) =>
        _ = await EnsureShape(IndexAlias(), create: true, ct).ConfigureAwait(false);

    private async Task<bool> EnsureShape(string alias, bool create, CancellationToken ct)
    {
        ThrowIfDisposed();
        if (_ready.ContainsKey(alias)) return true;
        await _shapeGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_ready.ContainsKey(alias)) return true;
            using var current = await _client.Get(alias, "index inspection", true, ct).ConfigureAwait(false);
            if (!current.IsNotFound)
            {
                ValidateShape(alias, current);
                _ready.TryAdd(alias, 0);
                return true;
            }
            if (!create) return false;
            _route.Policy.Demand(DataOperationEffect.SchemaOrAdmin,
                $"create Elasticsearch index for space '{_plan.Name}'");
            var body = Json(writer => WriteIndex(writer, alias));
            DemandRequest(body.Length);
            using (var created = await _client.Put(
                       Backing(alias), body, "index create", allowBadRequest: true, ct).ConfigureAwait(false))
            {
            }
            using var confirmed = await _client.Get(alias, "created index inspection", false, ct)
                .ConfigureAwait(false);
            ValidateShape(alias, confirmed);
            _ready.TryAdd(alias, 0);
            return true;
        }
        finally
        {
            _shapeGate.Release();
        }
    }

    private void WriteIndex(Utf8JsonWriter writer, string alias)
    {
        writer.WriteStartObject();
        writer.WritePropertyName("settings");
        writer.WriteStartObject();
        writer.WritePropertyName("index");
        writer.WriteStartObject();
        writer.WriteNumber("number_of_shards", 1);
        writer.WriteNumber("number_of_replicas", 0);
        writer.WritePropertyName("mapping");
        writer.WriteStartObject();
        writer.WriteBoolean("exclude_source_vectors", false);
        writer.WriteEndObject();
        writer.WriteEndObject();
        writer.WriteEndObject();
        writer.WritePropertyName("mappings");
        writer.WriteStartObject();
        writer.WriteBoolean("dynamic", false);
        writer.WritePropertyName("_meta");
        writer.WriteStartObject();
        writer.WriteString(Infrastructure.Constants.Wire.Contract,
            Infrastructure.Constants.Wire.ContractVersion);
        writer.WriteString(Infrastructure.Constants.Wire.Space, _plan.Name);
        writer.WriteString(Infrastructure.Constants.Wire.Metric, Metric());
        if (_plan.Model is null) writer.WriteNull(Infrastructure.Constants.Wire.Model);
        else writer.WriteString(Infrastructure.Constants.Wire.Model, _plan.Model);
        writer.WriteEndObject();
        writer.WritePropertyName("properties");
        writer.WriteStartObject();
        WriteType(writer, Infrastructure.Constants.Wire.Id, "keyword");
        WriteType(writer, Infrastructure.Constants.Wire.Scope, "keyword");
        writer.WritePropertyName(Infrastructure.Constants.Wire.Vector);
        writer.WriteStartObject();
        writer.WriteString("type", "dense_vector");
        writer.WriteNumber("dims", _plan.Dimensions);
        writer.WriteBoolean("index", true);
        writer.WriteString("similarity", Metric());
        writer.WriteEndObject();
        writer.WritePropertyName(Infrastructure.Constants.Wire.Metadata);
        writer.WriteStartObject();
        writer.WriteString("type", "object");
        writer.WriteBoolean("enabled", false);
        writer.WriteEndObject();
        writer.WritePropertyName(Infrastructure.Constants.Wire.Index);
        writer.WriteStartObject();
        writer.WriteString("type", "object");
        writer.WriteBoolean("dynamic", false);
        writer.WritePropertyName("properties");
        writer.WriteStartObject();
        WriteNestedProjection(writer, Infrastructure.Constants.Wire.Values);
        WriteType(writer, Infrastructure.Constants.Wire.Exists, "keyword");
        WriteNestedProjection(writer, Infrastructure.Constants.Wire.Sizes);
        writer.WriteEndObject();
        writer.WriteEndObject();
        writer.WriteEndObject();
        writer.WriteEndObject();
        writer.WritePropertyName("aliases");
        writer.WriteStartObject();
        writer.WritePropertyName(alias);
        writer.WriteStartObject();
        writer.WriteBoolean("is_write_index", true);
        writer.WriteEndObject();
        writer.WriteEndObject();
        writer.WriteEndObject();
    }

    private void ValidateShape(string alias, ElasticSearchResponse response)
    {
        var root = response.Document?.RootElement
            ?? throw WrongShape(alias, "the provider returned no index description");
        var indexes = root.EnumerateObject().ToArray();
        if (indexes.Length != 1) throw WrongShape(alias, "the alias does not resolve to exactly one backing index");
        var index = indexes[0].Value;
        if (!index.TryGetProperty("aliases", out var aliases) ||
            !aliases.TryGetProperty(alias, out var aliasNode) ||
            !aliasNode.TryGetProperty("is_write_index", out var write) || !write.GetBoolean())
            throw WrongShape(alias, "the storage address is not a single write alias");
        if (!index.TryGetProperty("mappings", out var mappings) ||
            !mappings.TryGetProperty("_meta", out var meta) ||
            !Text(meta, Infrastructure.Constants.Wire.Contract, Infrastructure.Constants.Wire.ContractVersion) ||
            !Text(meta, Infrastructure.Constants.Wire.Space, _plan.Name) ||
            !Text(meta, Infrastructure.Constants.Wire.Metric, Metric()))
            throw WrongShape(alias, "the Koan space contract metadata is absent or incompatible");
        var modelMatches = meta.TryGetProperty(Infrastructure.Constants.Wire.Model, out var model) &&
            (_plan.Model is null ? model.ValueKind == JsonValueKind.Null : model.GetString() == _plan.Model);
        if (!modelMatches) throw WrongShape(alias, "the embedding model identity is incompatible");
        if (!mappings.TryGetProperty("properties", out var properties) ||
            !properties.TryGetProperty(Infrastructure.Constants.Wire.Vector, out var vector) ||
            !Text(vector, "type", "dense_vector") ||
            !vector.TryGetProperty("dims", out var dimensions) || dimensions.GetInt32() != _plan.Dimensions ||
            !Text(vector, "similarity", Metric()))
            throw WrongShape(alias, $"the dense vector mapping does not match {_plan.Dimensions} dimensions and {Metric()}");
        RequireType(alias, properties, Infrastructure.Constants.Wire.Id, "keyword");
        RequireType(alias, properties, Infrastructure.Constants.Wire.Scope, "keyword");
        RequireType(alias, properties, Infrastructure.Constants.Wire.Metadata, "object");
        if (!properties.TryGetProperty(Infrastructure.Constants.Wire.Index, out var projection) ||
            !projection.TryGetProperty("properties", out var projectionProperties))
            throw WrongShape(alias, $"field '{Infrastructure.Constants.Wire.Index}' must be a mapped object");
        RequireProjection(alias, projectionProperties, Infrastructure.Constants.Wire.Values);
        RequireType(alias, projectionProperties, Infrastructure.Constants.Wire.Exists, "keyword");
        RequireProjection(alias, projectionProperties, Infrastructure.Constants.Wire.Sizes);
    }

    private PreparedPoint Prepare(VectorPoint<TKey> point, VectorScope scope)
    {
        ArgumentNullException.ThrowIfNull(point);
        ValidateEmbedding(point.Embedding.Span, "point embedding");
        var metadata = VectorMetadata.ToJson(point.Metadata);
        if (metadata is not null && Encoding.UTF8.GetByteCount(metadata) > _options.MaxMetadataBytesPerPoint)
            throw new InvalidOperationException(
                $"Elasticsearch point metadata exceeds the configured {_options.MaxMetadataBytesPerPoint} byte limit.");
        return new PreparedPoint(point, StorageId(point.Id, scope), metadata, scope.Identity);
    }

    private byte[] Document(PreparedPoint point) => Json(writer => WriteDocument(writer, point));

    private void WriteDocument(Utf8JsonWriter writer, PreparedPoint point)
    {
        writer.WriteStartObject();
        writer.WriteString(Infrastructure.Constants.Wire.Id, Key(point.Point.Id));
        writer.WriteString(Infrastructure.Constants.Wire.Scope, point.Scope);
        writer.WritePropertyName(Infrastructure.Constants.Wire.Vector);
        writer.WriteStartArray();
        foreach (var value in point.Point.Embedding.Span) writer.WriteNumberValue(value);
        writer.WriteEndArray();
        if (point.MetadataJson is not null)
        {
            writer.WritePropertyName(Infrastructure.Constants.Wire.Metadata);
            writer.WriteRawValue(point.MetadataJson);
        }
        if (point.Point.Metadata is not null)
        {
            writer.WritePropertyName(Infrastructure.Constants.Wire.Index);
            ElasticSearchFilter.WriteProjection(writer, point.Point.Metadata);
        }
        writer.WriteEndObject();
    }

    private async Task<Dictionary<string, VectorPoint<TKey>>> Fetch(
        string index,
        IReadOnlyList<Requested> requested,
        VectorScope scope,
        CancellationToken ct)
    {
        var expected = requested.GroupBy(static item => item.StorageId, StringComparer.Ordinal)
            .ToDictionary(static group => group.Key, static group => group.First().Id, StringComparer.Ordinal);
        var body = Json(writer =>
        {
            writer.WriteStartObject();
            writer.WriteNumber("size", expected.Count);
            writer.WriteBoolean("track_total_hits", false);
            writer.WritePropertyName("query");
            writer.WriteStartObject();
            writer.WritePropertyName("bool");
            writer.WriteStartObject();
            writer.WritePropertyName("filter");
            writer.WriteStartArray();
            writer.WriteStartObject();
            writer.WritePropertyName("ids");
            writer.WriteStartObject();
            writer.WritePropertyName("values");
            writer.WriteStartArray();
            foreach (var id in expected.Keys) writer.WriteStringValue(id);
            writer.WriteEndArray();
            writer.WriteEndObject();
            writer.WriteEndObject();
            if (scope.Predicate is not null || !string.IsNullOrEmpty(scope.Identity))
                ElasticSearchFilter.Write(writer, scope.Predicate, scope.Identity);
            writer.WriteEndArray();
            writer.WriteEndObject();
            writer.WriteEndObject();
            writer.WriteBoolean("_source", true);
            writer.WriteEndObject();
        });
        DemandRequest(body.Length);
        using var response = await _client.Post(
            $"{Escape(index)}/_search",
            body,
            "point retrieval",
            readOnly: true,
            allowNotFound: false,
            "application/json",
            ct).ConfigureAwait(false);
        var output = new Dictionary<string, VectorPoint<TKey>>(StringComparer.Ordinal);
        foreach (var hit in Hits(response))
        {
            var storage = hit.GetProperty("_id").GetString()
                ?? throw new InvalidOperationException("Elasticsearch returned a point without a physical identity.");
            if (!expected.TryGetValue(storage, out var id)) continue;
            output[storage] = ReadPoint(hit, id);
        }
        return output;
    }

    private VectorPoint<TKey> ReadPoint(JsonElement hit, TKey id)
    {
        if (!hit.TryGetProperty("_source", out var source) ||
            !source.TryGetProperty(Infrastructure.Constants.Wire.Vector, out var vector) ||
            vector.ValueKind != JsonValueKind.Array)
            throw new InvalidOperationException($"Elasticsearch point '{Key(id)}' has no complete Koan vector source.");
        var embedding = vector.EnumerateArray().Select(static item => item.GetSingle()).ToArray();
        if (embedding.Length != _plan.Dimensions)
            throw new InvalidOperationException(
                $"Elasticsearch point '{Key(id)}' has {embedding.Length} dimensions; expected {_plan.Dimensions}.");
        var metadata = source.TryGetProperty(Infrastructure.Constants.Wire.Metadata, out var stored)
            ? VectorMetadata.FromJson(stored.GetRawText())
            : null;
        return new VectorPoint<TKey>(id, embedding, metadata);
    }

    private async Task<List<Ranked>> Query(
        string index,
        VectorSearchRequest request,
        VectorScope scope,
        int candidates,
        CancellationToken ct)
    {
        var nativeCandidates = Math.Min(_options.MaxSearchCandidates,
            Math.Max(candidates, checked(candidates * 4)));
        var body = Json(writer =>
        {
            writer.WriteStartObject();
            writer.WriteNumber("size", candidates);
            writer.WriteBoolean("track_total_hits", false);
            writer.WritePropertyName("knn");
            writer.WriteStartObject();
            writer.WriteString("field", Infrastructure.Constants.Wire.Vector);
            writer.WritePropertyName("query_vector");
            writer.WriteStartArray();
            foreach (var value in request.Embedding.Span) writer.WriteNumberValue(value);
            writer.WriteEndArray();
            writer.WriteNumber("k", candidates);
            writer.WriteNumber("num_candidates", nativeCandidates);
            if (request.Filter is not null || !string.IsNullOrEmpty(scope.Identity))
            {
                writer.WritePropertyName("filter");
                ElasticSearchFilter.Write(writer, request.Filter, scope.Identity);
            }
            writer.WriteEndObject();
            writer.WritePropertyName("_source");
            writer.WriteStartArray();
            writer.WriteStringValue(Infrastructure.Constants.Wire.Id);
            writer.WriteStringValue(Infrastructure.Constants.Wire.Metadata);
            writer.WriteEndArray();
            writer.WriteEndObject();
        });
        DemandRequest(body.Length);
        using var response = await _client.Post(
            $"{Escape(index)}/_search",
            body,
            "vector query",
            readOnly: true,
            allowNotFound: false,
            "application/json",
            ct).ConfigureAwait(false);
        var output = new List<Ranked>();
        foreach (var hit in Hits(response))
        {
            if (!hit.TryGetProperty("_source", out var source) ||
                !source.TryGetProperty(Infrastructure.Constants.Wire.Id, out var original))
                throw new InvalidOperationException("Elasticsearch returned a match without its Koan identity.");
            var id = ParseKey(original.GetString()
                ?? throw new InvalidOperationException("Elasticsearch returned a non-string Koan identity."));
            var native = hit.GetProperty("_score").GetDouble();
            var metadata = source.TryGetProperty(Infrastructure.Constants.Wire.Metadata, out var stored)
                ? VectorMetadata.FromJson(stored.GetRawText())
                : null;
            output.Add(new Ranked(id, Key(id), native, Similarity(native), metadata));
        }
        return output;
    }

    private BatchResult<TKey> ParseSaveBatch(ElasticSearchResponse response, IReadOnlyList<PreparedPoint> points)
    {
        var items = BulkItems(response, points.Count);
        var outcomes = new BatchItemResult<TKey>[points.Count];
        for (var index = 0; index < items.Length; index++)
        {
            var item = items[index].GetProperty("index");
            var status = item.GetProperty("status").GetInt32();
            outcomes[index] = new BatchItemResult<TKey>(index, points[index].Point.Id,
                status == 409 ? MutationOutcome.Conflict :
                status is >= 200 and < 300 && item.GetProperty("result").GetString() == "created"
                    ? MutationOutcome.Inserted :
                status is >= 200 and < 300 ? MutationOutcome.Updated :
                throw BulkFailure("save", index, status));
        }
        return new BatchResult<TKey>(outcomes, BatchAtomicity.NotGuaranteed);
    }

    private static void ParseDeleteBatch(
        ElasticSearchResponse response,
        IReadOnlyList<TKey> ids,
        IReadOnlyList<int> positions,
        BatchItemResult<TKey>[] outcomes)
    {
        var items = BulkItems(response, positions.Count);
        for (var itemIndex = 0; itemIndex < items.Length; itemIndex++)
        {
            var position = positions[itemIndex];
            var item = items[itemIndex].GetProperty("delete");
            var status = item.GetProperty("status").GetInt32();
            var result = item.TryGetProperty("result", out var resultNode) ? resultNode.GetString() : null;
            outcomes[position] = new BatchItemResult<TKey>(position, ids[position],
                status == 404 || result == "not_found" ? MutationOutcome.Missing :
                status == 409 ? MutationOutcome.Conflict :
                status is >= 200 and < 300 ? MutationOutcome.Deleted :
                throw BulkFailure("delete", position, status));
        }
    }

    private static JsonElement[] BulkItems(ElasticSearchResponse response, int expected)
    {
        var root = response.Document?.RootElement
            ?? throw new InvalidOperationException("Elasticsearch returned no bulk receipt.");
        if (!root.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array)
            throw new InvalidOperationException("Elasticsearch returned an invalid bulk receipt.");
        var output = items.EnumerateArray().ToArray();
        if (output.Length != expected)
            throw new InvalidOperationException(
                $"Elasticsearch bulk receipt contains {output.Length} items; expected {expected}.");
        return output;
    }

    private static InvalidOperationException BulkFailure(string operation, int index, int status) => new(
        $"Elasticsearch batch {operation} item {index} failed with HTTP {status}; earlier item outcomes may have committed.");

    private void Validate(VectorSearchRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateEmbedding(request.Embedding.Span, "query embedding");
        if (request.Top <= 0 || request.Top >= _options.MaxSearchCandidates)
            throw new ArgumentOutOfRangeException(nameof(request),
                $"Elasticsearch Top must be positive and smaller than MaxSearchCandidates ({_options.MaxSearchCandidates}).");
        if (request.Space is not null && !string.Equals(request.Space, _plan.Name, StringComparison.Ordinal))
            throw new InvalidOperationException(
                $"Elasticsearch query requested space '{request.Space}'. Available space: {_plan.Name}.");
        if (request.Text is not null || request.SemanticWeight is not null)
            throw new NotSupportedException("Elasticsearch adapter does not claim portable hybrid text semantics.");
        if (request.Continuation is not null)
            throw new NotSupportedException("Elasticsearch adapter does not claim a stable search continuation snapshot.");
        if (request.MinimumSimilarity is < 0d or > 1d ||
            request.MinimumSimilarity is { } minimum && !double.IsFinite(minimum))
            throw new ArgumentOutOfRangeException(nameof(request), "Minimum similarity must be finite and in [0,1].");
    }

    private void ValidateEmbedding(ReadOnlySpan<float> embedding, string label)
    {
        if (embedding.Length != _plan.Dimensions)
            throw new ArgumentException(
                $"Elasticsearch {label} has {embedding.Length} dimensions; space '{_plan.Name}' requires {_plan.Dimensions}.");
        for (var index = 0; index < embedding.Length; index++)
            if (!float.IsFinite(embedding[index]))
                throw new ArgumentException($"Elasticsearch {label} contains a non-finite value at index {index}.");
        if (_plan.Metric == VectorMetric.Cosine && Norm(embedding) == 0d)
            throw new ArgumentException("Cosine vector spaces do not accept a zero-magnitude embedding.");
    }

    private string IndexAlias()
    {
        ThrowIfDisposed();
        var logicalContainer = VectorAdapterNaming.GetOrCompute<TEntity>(_services, _factory, _plan.Source);
        var container = PhysicalIndexToken(logicalContainer);
        var space = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(_plan.Name)))
            .ToLowerInvariant()[..16];
        var available = 240 - space.Length - 1;
        if (container.Length > available) container = container[..available].TrimEnd('-', '_');
        return $"{container}-{space}";
    }

    private static string PhysicalIndexToken(string logical)
    {
        var digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(logical)))
            .ToLowerInvariant()[..24];
        var readable = new StringBuilder(Math.Min(logical.Length, 190));
        foreach (var value in logical)
        {
            if (readable.Length == 190) break;
            if (value is >= 'A' and <= 'Z') readable.Append((char)(value + ('a' - 'A')));
            else if (value is >= 'a' and <= 'z' or >= '0' and <= '9' || value is '-' or '_' or '.')
                readable.Append(value);
            else readable.Append('-');
        }
        var prefix = readable.ToString().Trim('-', '_', '.');
        if (prefix.Length == 0) prefix = "space";
        return $"koan-{prefix}-{digest}";
    }

    private static string Backing(string alias) => $"{alias}-000001";

    private string StorageId(TKey id, VectorScope scope)
    {
        var material = $"{typeof(TKey).FullName}\u001f{scope.Identity}\u001f{Key(id)}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(material))).ToLowerInvariant();
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
                    $"Vector identity '{value}' could not convert to '{typeof(TKey).FullName}'."));
        return (TKey)Convert.ChangeType(value, typeof(TKey), CultureInfo.InvariantCulture);
    }

    private string Metric() => _plan.Metric switch
    {
        VectorMetric.Cosine => "cosine",
        VectorMetric.Euclidean => "l2_norm",
        VectorMetric.DotProduct => "max_inner_product",
        _ => throw new NotSupportedException($"Elasticsearch does not support metric '{_plan.Metric}'.")
    };

    private double Similarity(double nativeScore)
    {
        var value = _plan.Metric switch
        {
            VectorMetric.Cosine => nativeScore,
            VectorMetric.Euclidean when nativeScore > 0d =>
                1d / (1d + Math.Sqrt(Math.Max(0d, (1d / nativeScore) - 1d))),
            VectorMetric.Euclidean => 0d,
            VectorMetric.DotProduct => Logistic(nativeScore >= 1d
                ? nativeScore - 1d
                : nativeScore > 0d ? 1d - (1d / nativeScore) : double.NegativeInfinity),
            _ => throw new NotSupportedException()
        };
        return Math.Clamp(double.IsFinite(value) ? value : nativeScore > 0d ? 1d : 0d, 0d, 1d);
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

    private static JsonElement[] Hits(ElasticSearchResponse response)
    {
        var root = response.Document?.RootElement
            ?? throw new InvalidOperationException("Elasticsearch returned no search result.");
        if (!root.TryGetProperty("hits", out var hitRoot) ||
            !hitRoot.TryGetProperty("hits", out var hits) || hits.ValueKind != JsonValueKind.Array)
            throw new InvalidOperationException("Elasticsearch returned an invalid search result.");
        return hits.EnumerateArray().ToArray();
    }

    private static void WriteType(Utf8JsonWriter writer, string name, string type)
    {
        writer.WritePropertyName(name);
        writer.WriteStartObject();
        writer.WriteString("type", type);
        writer.WriteEndObject();
    }

    private static void WriteNestedProjection(Utf8JsonWriter writer, string name)
    {
        writer.WritePropertyName(name);
        writer.WriteStartObject();
        writer.WriteString("type", "nested");
        writer.WritePropertyName("properties");
        writer.WriteStartObject();
        WriteType(writer, Infrastructure.Constants.Wire.Path, "keyword");
        WriteType(writer, Infrastructure.Constants.Wire.Value, "keyword");
        writer.WriteEndObject();
        writer.WriteEndObject();
    }

    private static bool Text(JsonElement element, string name, string expected) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String &&
        string.Equals(value.GetString(), expected, StringComparison.Ordinal);

    private static void RequireType(string alias, JsonElement properties, string field, string type)
    {
        if (!properties.TryGetProperty(field, out var value) || !Text(value, "type", type))
            throw new InvalidOperationException(
                $"Elasticsearch index alias '{alias}' cannot realize the declared Koan space: field '{field}' " +
                $"must have type '{type}'. Provision the declared shape or select the source that owns this index.");
    }

    private static void RequireProjection(string alias, JsonElement properties, string field)
    {
        RequireType(alias, properties, field, "nested");
        var projection = properties.GetProperty(field);
        if (!projection.TryGetProperty("properties", out var nested))
            throw new InvalidOperationException(
                $"Elasticsearch index alias '{alias}' cannot realize the declared Koan space: field '{field}' " +
                "has no path/value mapping.");
        RequireType(alias, nested, Infrastructure.Constants.Wire.Path, "keyword");
        RequireType(alias, nested, Infrastructure.Constants.Wire.Value, "keyword");
    }

    private static byte[] Json(Action<Utf8JsonWriter> write)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer)) write(writer);
        return buffer.WrittenSpan.ToArray();
    }

    private static byte[] Bulk(Action<ArrayBufferWriter<byte>> write)
    {
        var buffer = new ArrayBufferWriter<byte>();
        write(buffer);
        return buffer.WrittenSpan.ToArray();
    }

    private static void Line(ArrayBufferWriter<byte> buffer, Action<Utf8JsonWriter> write)
    {
        using (var writer = new Utf8JsonWriter(buffer)) write(writer);
        buffer.GetSpan(1)[0] = (byte)'\n';
        buffer.Advance(1);
    }

    private static string Escape(string value) => Uri.EscapeDataString(value);

    private void DemandBatch(int count)
    {
        if (count > _options.MaxBatchPoints)
            throw new ArgumentOutOfRangeException(nameof(count),
                $"Elasticsearch batch contains {count} points; configured maximum is {_options.MaxBatchPoints}.");
    }

    private void DemandRequest(int bytes)
    {
        if (bytes > _options.MaxRequestBytes)
            throw new InvalidOperationException(
                $"Elasticsearch request contains {bytes} bytes; configured maximum is {_options.MaxRequestBytes}.");
    }

    private static BatchResult<TKey> Missing(IReadOnlyList<TKey> ids) => new(
        ids.Select((id, index) => new BatchItemResult<TKey>(index, id, MutationOutcome.Missing)),
        BatchAtomicity.NotGuaranteed);

    private VectorSearchResult<TKey> Empty() => new(
        [], null, new VectorSearchExecution(_plan.Metric, VectorSearchAccuracy.Approximate, 0));

    private InvalidOperationException WrongShape(string alias, string reason) => new(
        $"Elasticsearch index alias '{alias}' cannot realize space '{_plan.Name}': {reason}. " +
        "Provision the declared shape or select the source that owns this index.");

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

    private sealed record PreparedPoint(VectorPoint<TKey> Point, string StorageId, string? MetadataJson, string Scope);
    private sealed record Requested(TKey Id, string StorageId);
    private sealed record Ranked(TKey Id, string StableId, double NativeScore, double Similarity, DataObject? Metadata);
}
