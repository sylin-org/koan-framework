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

namespace Koan.Data.Connector.OpenSearch;

internal sealed class OpenSearchRepository<TEntity, TKey> :
    IVectorSearchRepository<TEntity, TKey>,
    IDescribesCapabilities,
    IDisposable,
    IAsyncDisposable
    where TEntity : class, IEntity<TKey>
    where TKey : notnull
{
    private readonly IServiceProvider _services;
    private readonly OpenSearchVectorAdapterFactory _factory;
    private readonly VectorSpacePlan _plan;
    private readonly OpenSearchRoute _route;
    private readonly OpenSearchOptions _options;
    private readonly OpenSearchClient _client;
    private readonly ConcurrentDictionary<string, byte> _validated = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _shapeLock = new(1, 1);
    private int _disposed;

    internal OpenSearchRepository(
        IServiceProvider services,
        OpenSearchVectorAdapterFactory factory,
        VectorSpacePlan plan,
        OpenSearchRoute route,
        OpenSearchOptions options,
        IHttpClientFactory http)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        _plan = plan ?? throw new ArgumentNullException(nameof(plan));
        _route = route ?? throw new ArgumentNullException(nameof(route));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _client = new OpenSearchClient(http ?? throw new ArgumentNullException(nameof(http)), route, options);
    }

    public void Describe(ICapabilities capabilities) => capabilities
        .Add(VectorCaps.Knn)
        .Add(VectorCaps.Filters, OpenSearchFilter.Capabilities)
        .Add(VectorCaps.BulkUpsert)
        .Add(VectorCaps.BulkDelete)
        .Add(VectorCaps.ScoreNormalization)
        .Add(VectorCaps.DynamicCollections);

    public async Task Save(VectorPoint<TKey> point, VectorScope scope, CancellationToken ct = default)
    {
        var item = Prepare(point, scope);
        _route.Policy.Demand(DataOperationEffect.Write, $"save OpenSearch point in space '{_plan.Name}'");
        var alias = Alias();
        await Ensure(alias, mayCreate: true, ct).ConfigureAwait(false);
        var body = Serialize(writer => Document(writer, item));
        DemandRequest(body.Length);
        using var response = await _client.Put(
            $"{Escape(alias)}/_doc/{item.StorageId}?refresh=true&require_alias=true",
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
        var items = new PreparedPoint[points.Count];
        for (var index = 0; index < points.Count; index++)
        {
            ct.ThrowIfCancellationRequested();
            items[index] = Prepare(points[index], scope);
        }
        if (items.Length == 0) return new BatchResult<TKey>([], BatchAtomicity.NotGuaranteed);

        _route.Policy.Demand(DataOperationEffect.Write, $"save OpenSearch batch in space '{_plan.Name}'");
        var alias = Alias();
        await Ensure(alias, mayCreate: true, ct).ConfigureAwait(false);
        var body = Ndjson(buffer =>
        {
            foreach (var item in items)
            {
                Line(buffer, writer =>
                {
                    writer.WriteStartObject();
                    writer.WritePropertyName("index");
                    writer.WriteStartObject();
                    writer.WriteString("_index", alias);
                    writer.WriteString("_id", item.StorageId);
                    writer.WriteEndObject();
                    writer.WriteEndObject();
                });
                Line(buffer, writer => Document(writer, item));
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
        return SaveReceipt(response, items);
    }

    public async Task<VectorPoint<TKey>?> Get(TKey id, VectorScope scope, CancellationToken ct = default)
    {
        var alias = Alias();
        if (!await Ensure(alias, mayCreate: false, ct).ConfigureAwait(false)) return null;
        var storageId = PhysicalId(id, scope);
        var found = await Fetch(alias, [new Requested(id, storageId)], scope, ct).ConfigureAwait(false);
        return found.GetValueOrDefault(storageId);
    }

    public async Task<IReadOnlyList<VectorPoint<TKey>?>> Get(
        IReadOnlyList<TKey> ids,
        VectorScope scope,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ids);
        DemandBatch(ids.Count);
        var result = new VectorPoint<TKey>?[ids.Count];
        if (ids.Count == 0) return result;
        var alias = Alias();
        if (!await Ensure(alias, mayCreate: false, ct).ConfigureAwait(false)) return result;
        var requested = ids.Select(id => new Requested(id, PhysicalId(id, scope))).ToArray();
        var found = await Fetch(alias, requested, scope, ct).ConfigureAwait(false);
        for (var index = 0; index < requested.Length; index++)
            result[index] = found.GetValueOrDefault(requested[index].StorageId);
        return result;
    }

    public async Task<bool> Delete(TKey id, VectorScope scope, CancellationToken ct = default)
    {
        var receipt = await Delete([id], scope, ct).ConfigureAwait(false);
        return receipt.Items[0].Outcome == MutationOutcome.Deleted;
    }

    public async Task<BatchResult<TKey>> Delete(
        IReadOnlyList<TKey> ids,
        VectorScope scope,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ids);
        DemandBatch(ids.Count);
        if (ids.Count == 0) return new BatchResult<TKey>([], BatchAtomicity.NotGuaranteed);
        _route.Policy.Demand(DataOperationEffect.Write, $"delete OpenSearch batch from space '{_plan.Name}'");
        var alias = Alias();
        if (!await Ensure(alias, mayCreate: false, ct).ConfigureAwait(false)) return Missing(ids);

        var requested = ids.Select(id => new Requested(id, PhysicalId(id, scope))).ToArray();
        var visible = await Fetch(alias, requested, scope, ct).ConfigureAwait(false);
        var dispatched = new List<int>(requested.Length);
        var body = Ndjson(buffer =>
        {
            for (var index = 0; index < requested.Length; index++)
            {
                if (!visible.ContainsKey(requested[index].StorageId)) continue;
                dispatched.Add(index);
                Line(buffer, writer =>
                {
                    writer.WriteStartObject();
                    writer.WritePropertyName("delete");
                    writer.WriteStartObject();
                    writer.WriteString("_index", alias);
                    writer.WriteString("_id", requested[index].StorageId);
                    writer.WriteEndObject();
                    writer.WriteEndObject();
                });
            }
        });
        var outcomes = ids.Select((id, index) =>
            new BatchItemResult<TKey>(index, id, MutationOutcome.Missing)).ToArray();
        if (dispatched.Count == 0) return new BatchResult<TKey>(outcomes, BatchAtomicity.NotGuaranteed);
        DemandRequest(body.Length);
        using var response = await _client.Post(
            "_bulk?refresh=true&require_alias=true",
            body,
            "batch delete",
            readOnly: false,
            allowNotFound: false,
            "application/x-ndjson",
            ct).ConfigureAwait(false);
        DeleteReceipt(response, ids, dispatched, outcomes);
        return new BatchResult<TKey>(outcomes, BatchAtomicity.NotGuaranteed);
    }

    public async Task<VectorSearchResult<TKey>> Search(
        VectorSearchRequest request,
        VectorScope scope,
        CancellationToken ct = default)
    {
        Validate(request);
        var alias = Alias();
        if (!await Ensure(alias, mayCreate: false, ct).ConfigureAwait(false)) return Empty();
        var bound = Math.Min(_options.MaxSearchCandidates, checked(request.Top + 1));
        List<Ranked> ranked;
        while (true)
        {
            ranked = await Knn(alias, request, scope, bound, ct).ConfigureAwait(false);
            ranked.Sort(static (left, right) =>
            {
                var score = right.Similarity.CompareTo(left.Similarity);
                return score != 0 ? score : StringComparer.Ordinal.Compare(left.StableId, right.StableId);
            });
            if (ranked.Count <= request.Top || ranked.Count < bound ||
                ranked[request.Top - 1].NativeScore != ranked[request.Top].NativeScore)
                break;
            if (bound == _options.MaxSearchCandidates)
                throw new InvalidOperationException(
                    $"OpenSearch cannot resolve a stable identity tie within {_options.MaxSearchCandidates} candidates. " +
                    "Increase MaxSearchCandidates or narrow the vector space.");
            bound = Math.Min(_options.MaxSearchCandidates, checked(bound * 2));
        }
        var matches = ranked
            .Where(item => request.MinimumSimilarity is null || item.Similarity >= request.MinimumSimilarity.Value)
            .Take(request.Top)
            .Select(item => new VectorMatch<TKey>(item.Id, item.Similarity, item.Metadata))
            .ToArray();
        return new VectorSearchResult<TKey>(matches, null,
            new VectorSearchExecution(_plan.Metric, VectorSearchAccuracy.Approximate, null));
    }

    public async Task Clear(VectorScope scope, CancellationToken ct = default)
    {
        _route.Policy.Demand(DataOperationEffect.Write, $"clear OpenSearch space '{_plan.Name}'");
        var alias = Alias();
        if (!await Ensure(alias, mayCreate: false, ct).ConfigureAwait(false)) return;
        var body = Serialize(writer =>
        {
            writer.WriteStartObject();
            writer.WritePropertyName("query");
            OpenSearchFilter.Write(writer, scope.Predicate, scope.Identity);
            writer.WriteEndObject();
        });
        DemandRequest(body.Length);
        using var response = await _client.Post(
            $"{Escape(alias)}/_delete_by_query?refresh=true&conflicts=proceed",
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
        _shapeLock.Dispose();
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }

    private async Task EnsureCreated(CancellationToken ct) =>
        _ = await Ensure(Alias(), mayCreate: true, ct).ConfigureAwait(false);

    private async Task<bool> Ensure(string alias, bool mayCreate, CancellationToken ct)
    {
        ThrowIfDisposed();
        if (_validated.ContainsKey(alias)) return true;
        await _shapeLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_validated.ContainsKey(alias)) return true;
            using var current = await _client.Get(alias, "index inspection", true, ct).ConfigureAwait(false);
            if (!current.IsNotFound)
            {
                ValidateShape(alias, current);
                _validated.TryAdd(alias, 0);
                return true;
            }
            if (!mayCreate) return false;
            _route.Policy.Demand(DataOperationEffect.SchemaOrAdmin,
                $"create OpenSearch index for space '{_plan.Name}'");
            var body = Serialize(writer => Index(writer, alias));
            DemandRequest(body.Length);
            using (var created = await _client.Put(
                       Backing(alias), body, "index create", allowBadRequest: true, ct).ConfigureAwait(false))
            {
            }
            using var confirmed = await _client.Get(alias, "created index inspection", false, ct)
                .ConfigureAwait(false);
            ValidateShape(alias, confirmed);
            _validated.TryAdd(alias, 0);
            return true;
        }
        finally
        {
            _shapeLock.Release();
        }
    }

    private void Index(Utf8JsonWriter writer, string alias)
    {
        writer.WriteStartObject();
        writer.WritePropertyName("settings");
        writer.WriteStartObject();
        writer.WritePropertyName("index");
        writer.WriteStartObject();
        writer.WriteBoolean("knn", true);
        writer.WriteNumber("number_of_shards", 1);
        writer.WriteNumber("number_of_replicas", 0);
        writer.WriteEndObject();
        writer.WriteEndObject();
        writer.WritePropertyName("mappings");
        writer.WriteStartObject();
        writer.WriteBoolean("dynamic", false);
        writer.WritePropertyName("_meta");
        writer.WriteStartObject();
        writer.WriteString(Infrastructure.Constants.Wire.Contract, Infrastructure.Constants.Wire.ContractVersion);
        writer.WriteString(Infrastructure.Constants.Wire.Space, _plan.Name);
        writer.WriteString(Infrastructure.Constants.Wire.Metric, Space());
        writer.WriteString(Infrastructure.Constants.Wire.Engine, "lucene");
        if (_plan.Model is null) writer.WriteNull(Infrastructure.Constants.Wire.Model);
        else writer.WriteString(Infrastructure.Constants.Wire.Model, _plan.Model);
        writer.WriteEndObject();
        writer.WritePropertyName("properties");
        writer.WriteStartObject();
        Type(writer, Infrastructure.Constants.Wire.Id, "keyword");
        Type(writer, Infrastructure.Constants.Wire.Scope, "keyword");
        writer.WritePropertyName(Infrastructure.Constants.Wire.Vector);
        writer.WriteStartObject();
        writer.WriteString("type", "knn_vector");
        writer.WriteNumber("dimension", _plan.Dimensions);
        writer.WritePropertyName("method");
        writer.WriteStartObject();
        writer.WriteString("name", "hnsw");
        writer.WriteString("engine", "lucene");
        writer.WriteString("space_type", Space());
        writer.WriteEndObject();
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
        Projection(writer, Infrastructure.Constants.Wire.Values);
        Type(writer, Infrastructure.Constants.Wire.Exists, "keyword");
        Projection(writer, Infrastructure.Constants.Wire.Sizes);
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

    private void ValidateShape(string alias, OpenSearchResponse response)
    {
        var root = response.Document?.RootElement ?? throw WrongShape(alias, "the provider returned no index description");
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
            !Text(meta, Infrastructure.Constants.Wire.Metric, Space()) ||
            !Text(meta, Infrastructure.Constants.Wire.Engine, "lucene"))
            throw WrongShape(alias, "the Koan space contract metadata is absent or incompatible");
        var modelMatches = meta.TryGetProperty(Infrastructure.Constants.Wire.Model, out var model) &&
            (_plan.Model is null ? model.ValueKind == JsonValueKind.Null : model.GetString() == _plan.Model);
        if (!modelMatches) throw WrongShape(alias, "the embedding model identity is incompatible");
        if (!mappings.TryGetProperty("properties", out var properties) ||
            !properties.TryGetProperty(Infrastructure.Constants.Wire.Vector, out var vector) ||
            !Text(vector, "type", "knn_vector") ||
            !vector.TryGetProperty("dimension", out var dimensions) || dimensions.GetInt32() != _plan.Dimensions ||
            !vector.TryGetProperty("method", out var method) ||
            !Text(method, "name", "hnsw") || !Text(method, "engine", "lucene") || !Text(method, "space_type", Space()))
            throw WrongShape(alias, "the knn_vector mapping does not match the declared dimension, space, and Lucene HNSW engine");
        RequireType(alias, properties, Infrastructure.Constants.Wire.Id, "keyword");
        RequireType(alias, properties, Infrastructure.Constants.Wire.Scope, "keyword");
        RequireType(alias, properties, Infrastructure.Constants.Wire.Metadata, "object");
        if (!properties.TryGetProperty(Infrastructure.Constants.Wire.Index, out var filter) ||
            !filter.TryGetProperty("properties", out var filterProperties))
            throw WrongShape(alias, "the bounded filter projection is absent");
        RequireProjection(alias, filterProperties, Infrastructure.Constants.Wire.Values);
        RequireType(alias, filterProperties, Infrastructure.Constants.Wire.Exists, "keyword");
        RequireProjection(alias, filterProperties, Infrastructure.Constants.Wire.Sizes);
    }

    private PreparedPoint Prepare(VectorPoint<TKey> point, VectorScope scope)
    {
        ArgumentNullException.ThrowIfNull(point);
        ValidateEmbedding(point.Embedding.Span, "point embedding");
        var metadata = VectorMetadata.ToJson(point.Metadata);
        if (metadata is not null && Encoding.UTF8.GetByteCount(metadata) > _options.MaxMetadataBytesPerPoint)
            throw new InvalidOperationException(
                $"OpenSearch point metadata exceeds the configured {_options.MaxMetadataBytesPerPoint} byte limit.");
        return new PreparedPoint(point, PhysicalId(point.Id, scope), metadata, scope.Identity);
    }

    private void Document(Utf8JsonWriter writer, PreparedPoint item)
    {
        writer.WriteStartObject();
        writer.WriteString(Infrastructure.Constants.Wire.Id, Key(item.Point.Id));
        writer.WriteString(Infrastructure.Constants.Wire.Scope, item.Scope);
        writer.WritePropertyName(Infrastructure.Constants.Wire.Vector);
        writer.WriteStartArray();
        foreach (var value in item.Point.Embedding.Span) writer.WriteNumberValue(value);
        writer.WriteEndArray();
        if (item.MetadataJson is not null)
        {
            writer.WritePropertyName(Infrastructure.Constants.Wire.Metadata);
            writer.WriteRawValue(item.MetadataJson);
        }
        if (item.Point.Metadata is not null)
        {
            writer.WritePropertyName(Infrastructure.Constants.Wire.Index);
            OpenSearchFilter.WriteProjection(writer, item.Point.Metadata);
        }
        writer.WriteEndObject();
    }

    private async Task<Dictionary<string, VectorPoint<TKey>>> Fetch(
        string alias,
        IReadOnlyList<Requested> requested,
        VectorScope scope,
        CancellationToken ct)
    {
        var wanted = requested.GroupBy(static request => request.StorageId, StringComparer.Ordinal)
            .ToDictionary(static group => group.Key, static group => group.First().Id, StringComparer.Ordinal);
        var body = Serialize(writer =>
        {
            writer.WriteStartObject();
            writer.WriteNumber("size", wanted.Count);
            writer.WriteBoolean("track_total_hits", false);
            writer.WritePropertyName("query");
            writer.WriteStartObject();
            writer.WritePropertyName("bool");
            writer.WriteStartObject();
            writer.WritePropertyName("filter");
            writer.WriteStartArray();
            OpenSearchFilter.Write(writer, scope.Predicate, scope.Identity);
            writer.WriteStartObject();
            writer.WritePropertyName("ids");
            writer.WriteStartObject();
            writer.WritePropertyName("values");
            writer.WriteStartArray();
            foreach (var id in wanted.Keys) writer.WriteStringValue(id);
            writer.WriteEndArray();
            writer.WriteEndObject();
            writer.WriteEndObject();
            writer.WriteEndArray();
            writer.WriteEndObject();
            writer.WriteEndObject();
            writer.WritePropertyName("_source");
            writer.WriteStartArray();
            writer.WriteStringValue(Infrastructure.Constants.Wire.Id);
            writer.WriteStringValue(Infrastructure.Constants.Wire.Vector);
            writer.WriteStringValue(Infrastructure.Constants.Wire.Metadata);
            writer.WriteEndArray();
            writer.WriteEndObject();
        });
        DemandRequest(body.Length);
        using var response = await _client.Post(
            $"{Escape(alias)}/_search",
            body,
            "point retrieval",
            readOnly: true,
            allowNotFound: true,
            "application/json",
            ct).ConfigureAwait(false);
        if (response.IsNotFound) return new Dictionary<string, VectorPoint<TKey>>(StringComparer.Ordinal);
        var result = new Dictionary<string, VectorPoint<TKey>>(StringComparer.Ordinal);
        foreach (var hit in Hits(response))
        {
            var storageId = hit.GetProperty("_id").GetString()
                ?? throw new InvalidOperationException("OpenSearch returned a point without physical identity.");
            if (!wanted.TryGetValue(storageId, out var id)) continue;
            result[storageId] = Materialize(id, hit);
        }
        return result;
    }

    private VectorPoint<TKey> Materialize(TKey id, JsonElement hit)
    {
        if (!hit.TryGetProperty("_source", out var source) ||
            !source.TryGetProperty(Infrastructure.Constants.Wire.Vector, out var vector) ||
            vector.ValueKind != JsonValueKind.Array)
            throw new InvalidOperationException($"OpenSearch point '{Key(id)}' has no complete Koan vector source.");
        var embedding = vector.EnumerateArray().Select(static item => item.GetSingle()).ToArray();
        if (embedding.Length != _plan.Dimensions)
            throw new InvalidOperationException(
                $"OpenSearch point '{Key(id)}' has {embedding.Length} dimensions; expected {_plan.Dimensions}.");
        var metadata = source.TryGetProperty(Infrastructure.Constants.Wire.Metadata, out var stored)
            ? VectorMetadata.FromJson(stored.GetRawText())
            : null;
        return new VectorPoint<TKey>(id, embedding, metadata);
    }

    private async Task<List<Ranked>> Knn(
        string alias,
        VectorSearchRequest request,
        VectorScope scope,
        int bound,
        CancellationToken ct)
    {
        var body = Serialize(writer =>
        {
            writer.WriteStartObject();
            writer.WriteNumber("size", bound);
            writer.WriteBoolean("track_total_hits", false);
            writer.WritePropertyName("query");
            writer.WriteStartObject();
            writer.WritePropertyName("knn");
            writer.WriteStartObject();
            writer.WritePropertyName(Infrastructure.Constants.Wire.Vector);
            writer.WriteStartObject();
            writer.WritePropertyName("vector");
            writer.WriteStartArray();
            foreach (var value in request.Embedding.Span) writer.WriteNumberValue(value);
            writer.WriteEndArray();
            writer.WriteNumber("k", bound);
            if (request.Filter is not null || !string.IsNullOrEmpty(scope.Identity))
            {
                writer.WritePropertyName("filter");
                OpenSearchFilter.Write(writer, request.Filter, scope.Identity);
            }
            writer.WriteEndObject();
            writer.WriteEndObject();
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
            $"{Escape(alias)}/_search",
            body,
            "vector query",
            readOnly: true,
            allowNotFound: false,
            "application/json",
            ct).ConfigureAwait(false);
        var result = new List<Ranked>();
        foreach (var hit in Hits(response))
        {
            if (!hit.TryGetProperty("_source", out var source) ||
                !source.TryGetProperty(Infrastructure.Constants.Wire.Id, out var original))
                throw new InvalidOperationException("OpenSearch returned a match without its Koan identity.");
            var id = ParseKey(original.GetString()
                ?? throw new InvalidOperationException("OpenSearch returned a non-string Koan identity."));
            var native = hit.GetProperty("_score").GetDouble();
            var metadata = source.TryGetProperty(Infrastructure.Constants.Wire.Metadata, out var stored)
                ? VectorMetadata.FromJson(stored.GetRawText())
                : null;
            result.Add(new Ranked(id, Key(id), native, Similarity(native), metadata));
        }
        return result;
    }

    private BatchResult<TKey> SaveReceipt(OpenSearchResponse response, IReadOnlyList<PreparedPoint> items)
    {
        var receipts = BulkItems(response, items.Count);
        var outcomes = new BatchItemResult<TKey>[items.Count];
        for (var index = 0; index < receipts.Length; index++)
        {
            var receipt = receipts[index].GetProperty("index");
            var status = receipt.GetProperty("status").GetInt32();
            outcomes[index] = new BatchItemResult<TKey>(index, items[index].Point.Id,
                status == 409 ? MutationOutcome.Conflict :
                status is >= 200 and < 300 && receipt.GetProperty("result").GetString() == "created"
                    ? MutationOutcome.Inserted :
                status is >= 200 and < 300 ? MutationOutcome.Updated :
                throw BulkFailure("save", index, status));
        }
        return new BatchResult<TKey>(outcomes, BatchAtomicity.NotGuaranteed);
    }

    private static void DeleteReceipt(
        OpenSearchResponse response,
        IReadOnlyList<TKey> ids,
        IReadOnlyList<int> dispatched,
        BatchItemResult<TKey>[] outcomes)
    {
        var receipts = BulkItems(response, dispatched.Count);
        for (var receiptIndex = 0; receiptIndex < receipts.Length; receiptIndex++)
        {
            var input = dispatched[receiptIndex];
            var receipt = receipts[receiptIndex].GetProperty("delete");
            var status = receipt.GetProperty("status").GetInt32();
            outcomes[input] = new BatchItemResult<TKey>(input, ids[input],
                status == 404 ? MutationOutcome.Missing :
                status is >= 200 and < 300 ? MutationOutcome.Deleted :
                throw BulkFailure("delete", input, status));
        }
    }

    private static JsonElement[] BulkItems(OpenSearchResponse response, int expected)
    {
        var root = response.Document?.RootElement
            ?? throw new InvalidOperationException("OpenSearch returned no bulk receipt.");
        if (!root.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array)
            throw new InvalidOperationException("OpenSearch returned an invalid bulk receipt.");
        var output = items.EnumerateArray().ToArray();
        if (output.Length != expected)
            throw new InvalidOperationException(
                $"OpenSearch returned {output.Length} bulk receipts for {expected} dispatched operations.");
        return output;
    }

    private void Validate(VectorSearchRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateEmbedding(request.Embedding.Span, "query embedding");
        if (request.Top <= 0 || request.Top >= _options.MaxSearchCandidates)
            throw new ArgumentOutOfRangeException(nameof(request),
                $"OpenSearch Top must be positive and smaller than MaxSearchCandidates ({_options.MaxSearchCandidates}).");
        if (request.Space is not null && !string.Equals(request.Space, _plan.Name, StringComparison.Ordinal))
            throw new InvalidOperationException(
                $"OpenSearch query requested space '{request.Space}'. Available space: {_plan.Name}.");
        if (request.Text is not null || request.SemanticWeight is not null)
            throw new NotSupportedException("OpenSearch adapter does not claim portable hybrid text semantics.");
        if (request.Continuation is not null)
            throw new NotSupportedException("OpenSearch adapter does not claim a stable search continuation snapshot.");
        if (request.MinimumSimilarity is < 0d or > 1d ||
            request.MinimumSimilarity is { } minimum && !double.IsFinite(minimum))
            throw new ArgumentOutOfRangeException(nameof(request), "Minimum similarity must be finite and in [0,1].");
    }

    private void ValidateEmbedding(ReadOnlySpan<float> embedding, string label)
    {
        if (embedding.Length != _plan.Dimensions)
            throw new ArgumentException(
                $"OpenSearch {label} has {embedding.Length} dimensions; space '{_plan.Name}' requires {_plan.Dimensions}.");
        for (var index = 0; index < embedding.Length; index++)
            if (!float.IsFinite(embedding[index]))
                throw new ArgumentException($"OpenSearch {label} contains a non-finite value at index {index}.");
        if (_plan.Metric == VectorMetric.Cosine && Norm(embedding) == 0d)
            throw new ArgumentException("Cosine vector spaces do not accept a zero-magnitude embedding.");
    }

    private string Alias()
    {
        ThrowIfDisposed();
        var logical = VectorAdapterNaming.GetOrCompute<TEntity>(_services, _factory, _plan.Source);
        var container = IndexToken(logical);
        var space = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(_plan.Name)))
            .ToLowerInvariant()[..16];
        var available = 240 - space.Length - 1;
        if (container.Length > available) container = container[..available].TrimEnd('-', '_');
        return $"{container}-{space}";
    }

    private static string IndexToken(string logical)
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
        return $"koan-{(prefix.Length == 0 ? "space" : prefix)}-{digest}";
    }

    private static string Backing(string alias) => $"{alias}-000001";

    private string PhysicalId(TKey id, VectorScope scope)
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

    private string Space() => _plan.Metric switch
    {
        VectorMetric.Cosine => "cosinesimil",
        VectorMetric.Euclidean => "l2",
        VectorMetric.DotProduct => "innerproduct",
        _ => throw new NotSupportedException($"OpenSearch does not support metric '{_plan.Metric}'.")
    };

    private double Similarity(double nativeScore)
    {
        var value = _plan.Metric switch
        {
            VectorMetric.Cosine => nativeScore,
            VectorMetric.Euclidean when nativeScore > 0d =>
                1d / (1d + Math.Sqrt(Math.Max(0d, (1d / nativeScore) - 1d))),
            VectorMetric.Euclidean => 0d,
            VectorMetric.DotProduct => Logistic(nativeScore > 1d
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

    private VectorSearchResult<TKey> Empty() => new(
        [],
        null,
        new VectorSearchExecution(_plan.Metric, VectorSearchAccuracy.Approximate, null));

    private static BatchResult<TKey> Missing(IReadOnlyList<TKey> ids) => new(
        ids.Select((id, index) => new BatchItemResult<TKey>(index, id, MutationOutcome.Missing)).ToArray(),
        BatchAtomicity.NotGuaranteed);

    private static JsonElement[] Hits(OpenSearchResponse response)
    {
        var root = response.Document?.RootElement
            ?? throw new InvalidOperationException("OpenSearch returned no search result.");
        if (!root.TryGetProperty("hits", out var hitRoot) ||
            !hitRoot.TryGetProperty("hits", out var hits) || hits.ValueKind != JsonValueKind.Array)
            throw new InvalidOperationException("OpenSearch returned an invalid search result.");
        return hits.EnumerateArray().ToArray();
    }

    private static void Type(Utf8JsonWriter writer, string name, string type)
    {
        writer.WritePropertyName(name);
        writer.WriteStartObject();
        writer.WriteString("type", type);
        writer.WriteEndObject();
    }

    private static void Projection(Utf8JsonWriter writer, string name)
    {
        writer.WritePropertyName(name);
        writer.WriteStartObject();
        writer.WriteString("type", "nested");
        writer.WritePropertyName("properties");
        writer.WriteStartObject();
        Type(writer, Infrastructure.Constants.Wire.Path, "keyword");
        Type(writer, Infrastructure.Constants.Wire.Value, "keyword");
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
                $"OpenSearch index alias '{alias}' cannot realize the declared Koan space: field '{field}' " +
                $"must have type '{type}'. Provision the declared shape or select the source that owns this index.");
    }

    private static void RequireProjection(string alias, JsonElement properties, string field)
    {
        RequireType(alias, properties, field, "nested");
        var projection = properties.GetProperty(field);
        if (!projection.TryGetProperty("properties", out var nested))
            throw new InvalidOperationException(
                $"OpenSearch index alias '{alias}' cannot realize the declared Koan space: field '{field}' has no path/value mapping.");
        RequireType(alias, nested, Infrastructure.Constants.Wire.Path, "keyword");
        RequireType(alias, nested, Infrastructure.Constants.Wire.Value, "keyword");
    }

    private static byte[] Serialize(Action<Utf8JsonWriter> write)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer)) write(writer);
        return buffer.WrittenSpan.ToArray();
    }

    private static byte[] Ndjson(Action<ArrayBufferWriter<byte>> write)
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
                $"OpenSearch batch contains {count} points; configured maximum is {_options.MaxBatchPoints}.");
    }

    private void DemandRequest(int bytes)
    {
        if (bytes > _options.MaxRequestBytes)
            throw new InvalidOperationException(
                $"OpenSearch request contains {bytes} bytes; configured maximum is {_options.MaxRequestBytes}.");
    }

    private void ThrowIfDisposed() =>
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

    private InvalidOperationException WrongShape(string alias, string reason) => new(
        $"OpenSearch index alias '{alias}' cannot realize space '{_plan.Name}': {reason}. " +
        "Provision the declared shape or select the source that owns this index.");

    private static InvalidOperationException BulkFailure(string operation, int index, int status) => new(
        $"OpenSearch batch {operation} item {index} failed with HTTP status {status}; earlier items may have committed.");

    private sealed record PreparedPoint(
        VectorPoint<TKey> Point,
        string StorageId,
        string? MetadataJson,
        string Scope);

    private sealed record Requested(TKey Id, string StorageId);
    private sealed record Ranked(TKey Id, string StableId, double NativeScore, double Similarity, DataObject? Metadata);
}
