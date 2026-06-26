using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Koan.Data.Abstractions;
using Koan.Core.Capabilities;
using Koan.Data.Vector.Abstractions;
using Koan.Data.Vector.Abstractions.Capabilities;
using Koan.Data.Vector.Abstractions.Configuration;

namespace Koan.Data.Vector.Connector.Milvus;

/// <summary>
/// Milvus 2.4 vector adapter. Targets the REST API surface under <c>/v2/vectordb/...</c>
/// exclusively — the older <c>/v2/collections/*</c> + <c>/v2/vectors/*</c> paths (pre-2.4) are
/// not supported.
///
/// <para>
/// Per-CollectionName ensured-cache so one repo instance correctly serves multiple partitions
/// (each <see cref="Koan.Data.Core.EntityContext.Partition"/> maps to its own collection name).
/// Application-level errors are surfaced: Milvus reports many failures as HTTP 200 with a
/// non-zero <c>code</c> in the JSON body, so HTTP-status checks alone aren't enough.
/// </para>
/// </summary>
internal sealed class MilvusVectorRepository<TEntity, TKey> :
    IVectorSearchRepository<TEntity, TKey>,
    IDescribesCapabilities
    where TEntity : class, IEntity<TKey>
    where TKey : notnull
{
    // Milvus's "success" code is sometimes 0 (entity ops, generic) and sometimes 200
    // (collection ops in quick-setup responses). Both are OK; anything else is an error.
    private static readonly HashSet<int> SuccessCodes = new() { 0, 200 };

    private readonly HttpClient _http;
    private readonly MilvusOptions _options;
    private readonly IServiceProvider _services;
    private readonly ILogger<MilvusVectorRepository<TEntity, TKey>>? _logger;
    private readonly ConcurrentDictionary<string, byte> _ensuredCollections = new(StringComparer.Ordinal);

    public MilvusVectorRepository(
        IHttpClientFactory httpFactory,
        IOptions<MilvusOptions> options,
        IServiceProvider services)
    {
        _http = httpFactory.CreateClient(Infrastructure.Constants.HttpClientName);
        _options = options.Value;
        _services = services;
        _logger = (ILogger<MilvusVectorRepository<TEntity, TKey>>?)services.GetService(typeof(ILogger<MilvusVectorRepository<TEntity, TKey>>));
        ConfigureHttpClient();
    }

    public void Describe(ICapabilities caps) => caps
        .Add(VectorCaps.Knn).Add(VectorCaps.Filters, MilvusFilterTranslator.Caps)
        .Add(VectorCaps.BulkUpsert).Add(VectorCaps.BulkDelete)
        .Add(VectorCaps.ScoreNormalization).Add(VectorCaps.DynamicCollections);


    // ─────────────────────────────────────────────────────────────────────────────
    // IVectorSearchRepository<TEntity, TKey>
    // ─────────────────────────────────────────────────────────────────────────────

    public async Task VectorEnsureCreated(CancellationToken ct = default)
    {
        var dimension = _options.Dimension ?? 0;
        if (dimension <= 0)
        {
            throw new InvalidOperationException(
                "Milvus vector dimension is unknown. Configure Koan:Data:Milvus:Dimension " +
                "(defaults to 1536 when unset) or call Upsert first to seed it.");
        }
        await EnsureCollection(dimension, ct);
    }

    public async Task Upsert(TKey id, float[] embedding, object? metadata = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(embedding);
        if (embedding.Length == 0) throw new ArgumentException("Embedding must contain values.", nameof(embedding));

        using var _ = MilvusTelemetry.Activity.StartActivity("vector.upsert");
        await EnsureCollection(embedding.Length, ct);

        var row = BuildEntityRow(id, embedding, metadata);
        var body = new JObject
        {
            ["dbName"] = _options.DatabaseName,
            ["collectionName"] = CollectionName,
            ["data"] = new JArray { row }
        };

        await PostAsync("/v2/vectordb/entities/upsert", body, "upsert", ct);
    }

    public async Task<int> UpsertMany(IEnumerable<(TKey Id, float[] Embedding, object? Metadata)> items, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(items);

        using var _ = MilvusTelemetry.Activity.StartActivity("vector.bulkUpsert");
        var list = items.ToList();
        if (list.Count == 0) return 0;

        var dimension = list[0].Embedding.Length;
        await EnsureCollection(dimension, ct);

        var rows = new JArray();
        foreach (var item in list)
        {
            if (item.Embedding.Length != dimension)
            {
                throw new InvalidOperationException(
                    $"Embedding dimension mismatch. Expected {dimension}, received {item.Embedding.Length}.");
            }
            rows.Add(BuildEntityRow(item.Id, item.Embedding, item.Metadata));
        }

        var body = new JObject
        {
            ["dbName"] = _options.DatabaseName,
            ["collectionName"] = CollectionName,
            ["data"] = rows
        };

        await PostAsync("/v2/vectordb/entities/upsert", body, "bulk upsert", ct);
        return list.Count;
    }

    public async Task<bool> Delete(TKey id, CancellationToken ct = default)
    {
        using var _ = MilvusTelemetry.Activity.StartActivity("vector.delete");
        await EnsureCollectionInitialized(ct);

        // Single-id delete uses `==` — `in [<id>]` has a Milvus 2.4 quirk where the delete
        // can be silently dropped when target entities were upserted in separate requests
        // rather than a single batch. The `==` operator goes through a different code path
        // that handles non-sealed segments correctly. Bulk delete continues to use `in [...]`
        // since `==` doesn't accept a list.
        var filter = $"{_options.PrimaryFieldName} == {FormatIdentifier(id)}";

        // Milvus REST delete returns 200 even when the id doesn't exist — no count or
        // row-found indicator. To honour the IVectorSearchRepository contract
        // (returns true iff the id was present) we pre-query. One extra round-trip per
        // single delete; bulk delete skips this (returns a best-effort count).
        var queryBody = new JObject
        {
            ["dbName"] = _options.DatabaseName,
            ["collectionName"] = CollectionName,
            ["filter"] = filter,
            ["outputFields"] = new JArray(_options.PrimaryFieldName),
            ["limit"] = 1
        };
        if (!string.IsNullOrWhiteSpace(_options.ConsistencyLevel))
        {
            queryBody["consistencyLevel"] = _options.ConsistencyLevel;
        }
        var queryResp = await PostAsync("/v2/vectordb/entities/query", queryBody, "delete pre-query", ct);
        var exists = (queryResp["data"] as JArray)?.Count > 0;
        if (!exists) return false;

        var body = new JObject
        {
            ["dbName"] = _options.DatabaseName,
            ["collectionName"] = CollectionName,
            ["filter"] = filter
        };
        await PostAsync("/v2/vectordb/entities/delete", body, "delete", ct);
        return true;
    }

    public async Task<int> DeleteMany(IEnumerable<TKey> ids, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ids);
        using var _ = MilvusTelemetry.Activity.StartActivity("vector.bulkDelete");
        await EnsureCollectionInitialized(ct);

        var list = ids.ToList();
        if (list.Count == 0) return 0;

        var body = new JObject
        {
            ["dbName"] = _options.DatabaseName,
            ["collectionName"] = CollectionName,
            ["filter"] = BuildIdFilter(list)
        };
        await PostAsync("/v2/vectordb/entities/delete", body, "bulk delete", ct);
        return list.Count;
    }

    public async Task<VectorQueryResult<TKey>> Search(VectorQueryOptions options, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (options.Query is null || options.Query.Length == 0)
        {
            throw new ArgumentException("Search query requires a vector with at least one value.", nameof(options));
        }

        using var _ = MilvusTelemetry.Activity.StartActivity("vector.search");
        await EnsureCollection(options.Query.Length, ct);

        var topK = Math.Max(1, options.TopK ?? _options.DefaultTopK);
        var filter = MilvusFilterTranslator.Translate(options.Filter, _options.MetadataFieldName);

        // Milvus 2.4 search body:
        //   - `data` is an ARRAY OF vectors (one or more queries in a batch)
        //   - `annsField` names the vector column to search against
        //   - `filter` is a string-expression metadata filter (renamed from `expr` in 2.3)
        //
        // The `new JArray { new JArray(values) }` shape matters: passing the inner JArray as a
        // constructor argument would unroll because JArray is IEnumerable<JToken>.
        var body = new JObject
        {
            ["dbName"] = _options.DatabaseName,
            ["collectionName"] = CollectionName,
            ["annsField"] = _options.VectorFieldName,
            ["data"] = new JArray { new JArray(options.Query.Select(v => (double)v)) },
            ["limit"] = topK,
            ["outputFields"] = new JArray(_options.PrimaryFieldName, _options.MetadataFieldName)
        };
        if (!string.IsNullOrWhiteSpace(filter)) body["filter"] = filter;
        if (options.Timeout is { } timeout) body["timeoutMs"] = (int)timeout.TotalMilliseconds;
        // Per-search consistency — Milvus 2.4 reads default to Bounded even when the collection
        // was created with a stronger level. Sending it on each read makes the configured level
        // actually take effect.
        if (!string.IsNullOrWhiteSpace(_options.ConsistencyLevel))
        {
            body["consistencyLevel"] = _options.ConsistencyLevel;
        }

        var parsed = await PostAsync("/v2/vectordb/entities/search", body, "search", ct);
        var results = parsed["data"] as JArray ?? new JArray();

        var matches = new List<VectorMatch<TKey>>(results.Count);
        foreach (var row in results.OfType<JObject>())
        {
            var idToken = row[_options.PrimaryFieldName] ?? row["id"];
            if (idToken is null) continue;

            var score = row.Value<double?>("score") ?? row.Value<double?>("distance") ?? 0d;
            var metadata = row[_options.MetadataFieldName]?.ToObject<object?>();
            matches.Add(new VectorMatch<TKey>(ConvertId(idToken), score, metadata));
        }

        return new VectorQueryResult<TKey>(matches, ContinuationToken: null, VectorTotalKind.Unknown);
    }

    public async Task Flush(CancellationToken ct = default)
    {
        using var activity = MilvusTelemetry.Activity.StartActivity("vector.flush");

        var collectionName = CollectionName;
        var body = new JObject
        {
            ["dbName"] = _options.DatabaseName,
            ["collectionName"] = collectionName
        };
        await PostAsync("/v2/vectordb/collections/drop", body, "flush", ct, allow404: true);
        _ensuredCollections.TryRemove(collectionName, out _);
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Collection management
    // ─────────────────────────────────────────────────────────────────────────────

    // A BLANK CollectionName (not just null) means "no override" — use the framework's storage naming. The
    // MilvusOptionsConfigurator binds an absent config key to "" (not null), so `?? ` alone would pin the empty name and
    // collapse every entity/partition/source onto one empty-named collection (an isolation breach). IsNullOrWhiteSpace
    // catches both null and "" (same fix + root cause as the Qdrant CollectionName getter).
    private string CollectionName
    {
        get
        {
            if (string.IsNullOrWhiteSpace(_options.CollectionName))
                return VectorAdapterNaming.GetOrCompute<TEntity, TKey>(_services);
            // A pinned CollectionName bypasses the partition+source name-fold — warn once if that defeats active isolation.
            VectorAdapterNaming.WarnIfPinnedNameDefeatsIsolation<TEntity>(_options.CollectionName!, "CollectionName");
            return _options.CollectionName!;
        }
    }

    private async Task EnsureCollectionInitialized(CancellationToken ct)
    {
        if (_ensuredCollections.ContainsKey(CollectionName)) return;
        var dimension = _options.Dimension ?? 0;
        if (dimension > 0) await EnsureCollection(dimension, ct);
    }

    private async Task EnsureCollection(int dimension, CancellationToken ct)
    {
        var collectionName = CollectionName;
        if (_ensuredCollections.ContainsKey(collectionName)) return;

        using var _ = MilvusTelemetry.Activity.StartActivity("vector.collection.ensure");

        if (await CollectionExists(collectionName, ct))
        {
            _ensuredCollections[collectionName] = 0;
            return;
        }

        if (!_options.AutoCreateCollection)
        {
            throw new InvalidOperationException(
                $"Milvus collection '{collectionName}' does not exist and auto-creation is disabled.");
        }

        // Milvus 2.4 "quick setup" body. For string-typed keys (TKey == string or Guid) we
        // must set idType=VarChar + params.max_length; otherwise Milvus defaults to Int64
        // and rejects every string id with a strconv.ParseInt error. The `params` field is
        // typed map[string]string on the server side, so each value must be string-shaped
        // even when it's logically a number.
        var isStringKey = typeof(TKey) == typeof(string) || typeof(TKey) == typeof(Guid);
        var body = new JObject
        {
            ["dbName"] = _options.DatabaseName,
            ["collectionName"] = collectionName,
            ["dimension"] = dimension,
            ["metricType"] = _options.Metric,
            ["primaryFieldName"] = _options.PrimaryFieldName,
            ["vectorFieldName"] = _options.VectorFieldName
        };
        if (isStringKey)
        {
            body["idType"] = "VarChar";
            body["params"] = new JObject { ["max_length"] = "512" };
        }
        // Propagate the configured consistency level to the collection (default Bounded means
        // reads-after-writes can lag, which breaks deterministic test expectations and most
        // operational use cases). Setting it once at create time means every search and query
        // against this collection inherits the level — no need to set it per-request.
        if (!string.IsNullOrWhiteSpace(_options.ConsistencyLevel))
        {
            body["consistencyLevel"] = _options.ConsistencyLevel;
        }

        await PostAsync("/v2/vectordb/collections/create", body, "collection create", ct);
        _ensuredCollections[collectionName] = 0;
    }

    private async Task<bool> CollectionExists(string collectionName, CancellationToken ct)
    {
        var body = new JObject
        {
            ["dbName"] = _options.DatabaseName,
            ["collectionName"] = collectionName
        };
        var parsed = await PostAsync("/v2/vectordb/collections/has", body, "collection has", ct);
        return parsed["data"]?["has"]?.Value<bool>() ?? false;
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Internals
    // ─────────────────────────────────────────────────────────────────────────────

    private JObject BuildEntityRow(TKey id, float[] embedding, object? metadata)
    {
        var row = new JObject
        {
            [_options.PrimaryFieldName] = FormatIdentifierValue(id),
            [_options.VectorFieldName] = new JArray(embedding.Select(v => (double)v))
        };
        if (metadata is not null) row[_options.MetadataFieldName] = JToken.FromObject(metadata);
        return row;
    }

    private string BuildIdFilter(IEnumerable<TKey> ids)
    {
        var formatted = string.Join(",", ids.Select(FormatIdentifier));
        return $"{_options.PrimaryFieldName} in [{formatted}]";
    }

    /// <summary>
    /// POST a JSON body and return the parsed response. Surfaces both HTTP-level failures
    /// (4xx/5xx → throw) AND Milvus app-level failures (HTTP 200 + non-success <c>code</c>
    /// in body → throw). When <paramref name="allow404"/> is set, a NotFound is treated as
    /// a successful no-op and an empty <see cref="JObject"/> returned.
    /// </summary>
    private async Task<JObject> PostAsync(string path, JObject body, string operation, CancellationToken ct, bool allow404 = false)
    {
        var content = new StringContent(body.ToString(Formatting.None), Encoding.UTF8, "application/json");
        var response = await _http.PostAsync(path, content, ct);
        var responseBody = await response.Content.ReadAsStringAsync(ct);

        if (allow404 && response.StatusCode == HttpStatusCode.NotFound)
        {
            return new JObject();
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"Milvus {operation} failed: HTTP {(int)response.StatusCode} {response.ReasonPhrase} {responseBody}");
        }

        if (string.IsNullOrWhiteSpace(responseBody)) return new JObject();

        JObject parsed;
        try { parsed = JObject.Parse(responseBody); }
        catch (JsonReaderException) { return new JObject(); }

        var code = parsed["code"]?.Value<int?>();
        if (code is not null && !SuccessCodes.Contains(code.Value))
        {
            var message = parsed["message"]?.Value<string>() ?? responseBody;
            throw new InvalidOperationException($"Milvus {operation} failed: code {code}: {message}");
        }

        return parsed;
    }

    private void ConfigureHttpClient()
    {
        if (string.IsNullOrWhiteSpace(_options.Endpoint))
        {
            throw new InvalidOperationException($"{Infrastructure.Constants.Configuration.Keys.Endpoint} must be configured.");
        }

        _http.BaseAddress = new Uri(_options.Endpoint);
        if (_http.Timeout == default)
        {
            _http.Timeout = TimeSpan.FromSeconds(Math.Max(1, _options.DefaultTimeoutSeconds));
        }

        if (!string.IsNullOrEmpty(_options.Token))
        {
            _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _options.Token);
        }
        else if (!string.IsNullOrEmpty(_options.Username))
        {
            var creds = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_options.Username}:{_options.Password ?? ""}"));
            _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", creds);
        }
    }

    // Identifier normalization. Two shapes:
    //   FormatIdentifier  → quoted string for use INSIDE a Milvus filter expression: `id in ["a","b"]`
    //   FormatIdentifierValue → JValue for the JSON body's data row
    private static string FormatIdentifier(TKey id)
    {
        if (id is string s) return Quote(s);
        if (id is Guid g) return Quote(g.ToString("N", CultureInfo.InvariantCulture));
        if (id is IFormattable f) return f.ToString(null, CultureInfo.InvariantCulture);
        return Quote(id.ToString() ?? "");
    }

    private static JValue FormatIdentifierValue(TKey id)
    {
        if (id is string s) return new JValue(s);
        if (id is Guid g) return new JValue(g.ToString("N", CultureInfo.InvariantCulture));
        if (id is IFormattable f) return new JValue(f.ToString(null, CultureInfo.InvariantCulture));
        return new JValue(id.ToString());
    }

    private static string Quote(string value) => $"\"{value.Replace("\"", "\\\"")}\"";

    private static TKey ConvertId(JToken token)
    {
        if (typeof(TKey) == typeof(string)) return (TKey)(object)token.Value<string>()!;
        if (typeof(TKey) == typeof(Guid)) return (TKey)(object)Guid.Parse(token.Value<string>()!);
        if (typeof(TKey).IsEnum) return (TKey)Enum.Parse(typeof(TKey), token.Value<string>()!, ignoreCase: true);
        return (TKey)Convert.ChangeType(token.Value<object>()!, typeof(TKey), CultureInfo.InvariantCulture);
    }
}
