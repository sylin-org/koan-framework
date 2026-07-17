using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Instructions;
using Koan.Core.Capabilities;
using Koan.Data.Vector.Abstractions;
using Koan.Data.Vector.Abstractions.Capabilities;
using Koan.Data.Vector.Naming;

namespace Koan.Data.SearchEngine;

/// <summary>
/// The shared vector repository for the Apache-Lucene search engines (Elasticsearch 8.x, OpenSearch 2.x).
/// DATA-0103 folded the two ~79%-identical connectors into this one base behind a 3-member
/// <see cref="ISearchEngineDialect"/> seam: every REST/transport detail (HttpClient config, auth, NDJSON
/// bulk envelope, <c>_doc</c> PUT/DELETE, <c>_delete_by_query</c>, probe-then-create index flow, hit
/// parsing, scroll export, id normalization) lives here; only the kNN search body, the index mapping body,
/// and the similarity-token mapping come from the dialect. <see cref="ExportAll"/>/<c>GetCount</c>/index-stats
/// are implemented once here — OpenSearch supports the identical scroll API, so it gains the parity it
/// previously lacked.
/// </summary>
public sealed class SearchEngineVectorRepository<TEntity, TKey> :
    IVectorSearchRepository<TEntity, TKey>,
    IDescribesCapabilities,
    IInstructionExecutor<TEntity>
    where TEntity : class, IEntity<TKey>
    where TKey : notnull
{
    private readonly HttpClient _http;
    private readonly ISearchEngineVectorOptions _options;
    private readonly ISearchEngineDialect _dialect;
    private readonly ActivitySource _activity;
    private readonly IServiceProvider _services;
    private readonly ILogger? _logger;
    // Keyed by IndexName — one repo instance serves multiple partitions/storage names, so a
    // single bool would short-circuit EnsureIndex after the first partition is created and
    // every subsequent partition's writes would land in an auto-created index without
    // vector mapping. Per-IndexName tracking is the only correct shape.
    private readonly ConcurrentDictionary<string, byte> _ensuredIndexes = new(StringComparer.Ordinal);
    private int _discoveredDimension = -1;

    public SearchEngineVectorRepository(
        HttpClient http,
        ISearchEngineVectorOptions options,
        ISearchEngineDialect dialect,
        ActivitySource activity,
        ILogger? logger,
        IServiceProvider services)
    {
        _http = http;
        _options = options;
        _dialect = dialect;
        _activity = activity;
        _logger = logger;
        _services = services;
        ConfigureHttpClient();
    }

    public void Describe(ICapabilities caps) => caps
        .Add(VectorCaps.Knn).Add(VectorCaps.Filters, SearchEngineFilterTranslator.Caps)
        .Add(VectorCaps.BulkUpsert).Add(VectorCaps.BulkDelete);


    public async Task VectorEnsureCreated(CancellationToken ct = default)
    {
        var dimension = _discoveredDimension > 0 ? _discoveredDimension : _options.Dimension ?? -1;
        if (dimension <= 0)
        {
            throw new InvalidOperationException($"{_dialect.EngineLabel} vector dimension is unknown. Configure the adapter's Dimension or upsert a vector to allow discovery.");
        }

        await EnsureIndex(dimension, ct);
    }

    public async Task Upsert(TKey id, float[] embedding, object? metadata = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(embedding);
        if (embedding.Length == 0)
        {
            throw new ArgumentException("Embedding must contain values.", nameof(embedding));
        }

        using var _ = _activity.StartActivity("vector.upsert");

        var dimension = EnsureDimension(embedding.Length);
        await EnsureIndex(dimension, ct);

        var document = BuildDocument(id, embedding, metadata);
        var payload = document.ToString(Formatting.None);
        var docId = NormalizeId(id);
        var url = $"/{Uri.EscapeDataString(IndexName)}/_doc/{Uri.EscapeDataString(docId)}?refresh={_options.RefreshMode}";
        var response = await _http.PutAsync(url, new StringContent(payload, Encoding.UTF8, "application/json"), ct);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            throw new InvalidOperationException($"{_dialect.EngineLabel} upsert failed: {(int)response.StatusCode} {response.ReasonPhrase} {body}");
        }
    }

    public async Task<int> UpsertMany(IEnumerable<(TKey Id, float[] Embedding, object? Metadata)> items, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(items);

        using var _ = _activity.StartActivity("vector.bulkUpsert");

        var list = items.ToList();
        if (list.Count == 0)
        {
            return 0;
        }

        var dimension = EnsureDimension(list[0].Embedding.Length);
        await EnsureIndex(dimension, ct);

        var sb = new StringBuilder();
        foreach (var item in list)
        {
            if (item.Embedding.Length != dimension)
            {
                throw new InvalidOperationException($"Embedding dimension mismatch. Expected {dimension}, received {item.Embedding.Length}.");
            }

            var docId = NormalizeId(item.Id);
            var header = new JObject
            {
                ["index"] = new JObject
                {
                    ["_index"] = IndexName,
                    ["_id"] = docId
                }
            };
            sb.AppendLine(header.ToString(Formatting.None));

            var document = BuildDocument(item.Id, item.Embedding, item.Metadata);
            sb.AppendLine(document.ToString(Formatting.None));
        }

        var bulkUrl = $"/_bulk?refresh={_options.RefreshMode}";
        using var content = new StringContent(sb.ToString(), Encoding.UTF8, "application/x-ndjson");
        var resp = await _http.PostAsync(bulkUrl, content, ct);
        var respBody = await resp.Content.ReadAsStringAsync(ct);

        if (!resp.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"{_dialect.EngineLabel} bulk upsert failed: {(int)resp.StatusCode} {resp.ReasonPhrase} {respBody}");
        }

        var parsed = string.IsNullOrWhiteSpace(respBody) ? null : JObject.Parse(respBody);
        if (parsed?["errors"]?.Value<bool>() == true)
        {
            _logger?.LogWarning("{Engine} bulk upsert completed with errors: {Response}", _dialect.EngineLabel, respBody);
        }

        return list.Count;
    }

    public async Task<bool> Delete(TKey id, CancellationToken ct = default)
    {
        using var _ = _activity.StartActivity("vector.delete");

        await EnsureIndexInitialized(ct);

        var docId = NormalizeId(id);
        var url = $"/{Uri.EscapeDataString(IndexName)}/_doc/{Uri.EscapeDataString(docId)}?refresh={_options.RefreshMode}";
        var resp = await _http.DeleteAsync(url, ct);

        if (resp.StatusCode == HttpStatusCode.NotFound)
        {
            return false;
        }

        if (!resp.IsSuccessStatusCode)
        {
            var body = await resp.Content.ReadAsStringAsync(ct);
            throw new InvalidOperationException($"{_dialect.EngineLabel} delete failed: {(int)resp.StatusCode} {resp.ReasonPhrase} {body}");
        }

        return true;
    }

    public async Task<int> DeleteMany(IEnumerable<TKey> ids, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ids);
        using var _ = _activity.StartActivity("vector.bulkDelete");

        await EnsureIndexInitialized(ct);

        var list = ids.ToList();
        if (list.Count == 0)
        {
            return 0;
        }

        var sb = new StringBuilder();
        foreach (var id in list)
        {
            var docId = NormalizeId(id);
            var header = new JObject
            {
                ["delete"] = new JObject
                {
                    ["_index"] = IndexName,
                    ["_id"] = docId
                }
            };
            sb.AppendLine(header.ToString(Formatting.None));
        }

        var bulkUrl = $"/_bulk?refresh={_options.RefreshMode}";
        using var content = new StringContent(sb.ToString(), Encoding.UTF8, "application/x-ndjson");
        var resp = await _http.PostAsync(bulkUrl, content, ct);
        var respBody = await resp.Content.ReadAsStringAsync(ct);

        if (!resp.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"{_dialect.EngineLabel} bulk delete failed: {(int)resp.StatusCode} {resp.ReasonPhrase} {respBody}");
        }

        var parsed = string.IsNullOrWhiteSpace(respBody) ? null : JObject.Parse(respBody);
        if (parsed?["errors"]?.Value<bool>() == true)
        {
            _logger?.LogWarning("{Engine} bulk delete completed with errors: {Response}", _dialect.EngineLabel, respBody);
        }

        return list.Count;
    }

    public async Task<VectorQueryResult<TKey>> Search(VectorQueryOptions options, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (options.Query is null || options.Query.Length == 0)
        {
            throw new ArgumentException("Search query requires a vector with at least one value.", nameof(options));
        }

        using var _ = _activity.StartActivity("vector.search");

        var dimension = EnsureDimension(options.Query.Length);
        await EnsureIndex(dimension, ct);

        var topK = Math.Max(1, options.TopK ?? 10);
        var request = BuildSearchRequest(options, topK);
        var url = $"/{Uri.EscapeDataString(IndexName)}/_search";
        var resp = await _http.PostAsync(url, new StringContent(request.ToString(Formatting.None), Encoding.UTF8, "application/json"), ct);
        var body = await resp.Content.ReadAsStringAsync(ct);

        if (!resp.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"{_dialect.EngineLabel} search failed: {(int)resp.StatusCode} {resp.ReasonPhrase} {body}");
        }

        var parsed = string.IsNullOrWhiteSpace(body) ? new JObject() : JObject.Parse(body);
        var hits = parsed["hits"]?["hits"] as JArray ?? new JArray();
        var matches = new List<VectorMatch<TKey>>(hits.Count);

        foreach (var hit in hits.OfType<JObject>())
        {
            var id = hit.Value<string>("_id") ?? hit["_source"]?[_options.IdField]?.Value<string>();
            if (id is null)
            {
                continue;
            }

            var score = hit.Value<double?>("_score") ?? 0d;
            var source = hit["_source"] as JObject;
            object? metadata = null;
            if (source is not null)
            {
                metadata = source[_options.MetadataField]?.ToObject<object?>();
                metadata ??= source.ToObject<object?>();
            }

            matches.Add(new VectorMatch<TKey>(ConvertId(id), score, metadata));
        }

        var totalToken = parsed["hits"]?["total"] as JObject;
        var totalKind = totalToken?["relation"]?.Value<string>() switch
        {
            "eq" => VectorTotalKind.Exact,
            "gte" => VectorTotalKind.Estimated,
            _ => VectorTotalKind.Unknown
        };

        return new VectorQueryResult<TKey>(matches, ContinuationToken: null, totalKind);
    }

    public async IAsyncEnumerable<VectorExportBatch<TKey>> ExportAll(
        int? batchSize = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        using var _ = _activity.StartActivity("vector.export");

        var size = batchSize ?? 1000; // scroll default
        var scrollTime = "2m";

        // Check if index exists first
        var indexExistsUrl = $"/{Uri.EscapeDataString(IndexName)}";
        var headResp = await _http.SendAsync(new HttpRequestMessage(HttpMethod.Head, indexExistsUrl), ct);

        if (headResp.StatusCode == HttpStatusCode.NotFound)
        {
            _logger?.LogInformation("{Engine} index {IndexName} does not exist, export returns empty", _dialect.EngineLabel, IndexName);
            yield break;
        }

        // Initial search with scroll
        var initUrl = $"/{Uri.EscapeDataString(IndexName)}/_search?scroll={scrollTime}";
        var initBody = new JObject
        {
            ["size"] = size,
            ["query"] = new JObject { ["match_all"] = new JObject() },
            ["_source"] = new JArray { _options.IdField, _options.VectorField, _options.MetadataField }
        };

        var resp = await _http.PostAsync(initUrl, new StringContent(initBody.ToString(Formatting.None), Encoding.UTF8, "application/json"), ct);
        var respBody = await resp.Content.ReadAsStringAsync(ct);

        if (!resp.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"{_dialect.EngineLabel} export failed: {(int)resp.StatusCode} {resp.ReasonPhrase} {respBody}");
        }

        var json = JObject.Parse(respBody);
        var scrollId = json["_scroll_id"]?.Value<string>();
        var totalExported = 0;

        while (scrollId != null)
        {
            var hits = json["hits"]?["hits"] as JArray ?? new JArray();
            if (hits.Count == 0) break;

            foreach (var hit in hits.OfType<JObject>())
            {
                var source = hit["_source"] as JObject;
                if (source == null) continue;

                var id = source[_options.IdField]?.Value<string>();
                var vectorArray = source[_options.VectorField] as JArray;

                if (id != null && vectorArray != null)
                {
                    var embedding = vectorArray.Select(v => (float)(double)v).ToArray();
                    var metadata = source[_options.MetadataField]?.ToObject<object>();

                    yield return new VectorExportBatch<TKey>(ConvertId(id), embedding, metadata);
                    totalExported++;
                }
            }

            // Next scroll batch
            var scrollUrl = "/_search/scroll";
            var scrollBody = new JObject
            {
                ["scroll"] = scrollTime,
                ["scroll_id"] = scrollId
            };

            resp = await _http.PostAsync(scrollUrl, new StringContent(scrollBody.ToString(Formatting.None), Encoding.UTF8, "application/json"), ct);
            respBody = await resp.Content.ReadAsStringAsync(ct);

            if (!resp.IsSuccessStatusCode)
            {
                _logger?.LogWarning("{Engine} scroll continuation failed: {Status} {Reason}", _dialect.EngineLabel, resp.StatusCode, resp.ReasonPhrase);
                break;
            }

            json = JObject.Parse(respBody);
            var newScrollId = json["_scroll_id"]?.Value<string>();

            // If scroll ID changed, update it
            if (newScrollId != null && newScrollId != scrollId)
            {
                scrollId = newScrollId;
            }
            else if (hits.Count < size)
            {
                // Last batch
                break;
            }
        }

        // Clear scroll
        if (scrollId != null)
        {
            try
            {
                await _http.DeleteAsync($"/_search/scroll/{Uri.EscapeDataString(scrollId)}", ct);
            }
            catch (Exception ex)
            {
                _logger?.LogDebug(ex, "Failed to clear scroll context {ScrollId}", scrollId);
            }
        }

        _logger?.LogInformation("{Engine} vector export completed: {Count} vectors exported", _dialect.EngineLabel, totalExported);
    }

    public async Task<TResult> ExecuteAsync<TResult>(Instruction instruction, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(instruction);

        if (string.Equals(instruction.Name, DataInstructions.EnsureCreated, StringComparison.OrdinalIgnoreCase))
        {
            await VectorEnsureCreated(ct);
            return default!;
        }

        if (string.Equals(instruction.Name, DataInstructions.Clear, StringComparison.OrdinalIgnoreCase))
        {
            await Clear(ct);
            return default!;
        }

        if (string.Equals(instruction.Name, VectorInstructions.IndexStats, StringComparison.OrdinalIgnoreCase))
        {
            // For stats, don't require dimension - just check if index exists
            var count = await GetCount(ct);
            if (typeof(TResult) == typeof(int))
                return (TResult)(object)count;
            object result = new { count };
            return (TResult)result;
        }

        throw new NotSupportedException($"Instruction '{instruction.Name}' not supported by {_dialect.EngineLabel} vector adapter.");
    }

    private JObject BuildSearchRequest(VectorQueryOptions options, int topK)
    {
        var filter = SearchEngineFilterTranslator.TranslateWhereClause(options.Filter, _options.MetadataField, _dialect.EngineLabel);
        var request = _dialect.BuildSearchRequestBody(options.Query, topK, filter, _options);

        if (options.Timeout is { } timeout)
        {
            request["timeout"] = $"{(int)timeout.TotalMilliseconds}ms";
        }

        return request;
    }

    /// <summary>
    /// Drop the underlying index entirely. Subsequent operations re-create it lazily with the
    /// correct vector mapping — cleaner than _delete_by_query, which leaves the (possibly
    /// stale) mapping in place. Invalidates the per-IndexName ensured-cache so the next
    /// EnsureIndex call hits the real probe path.
    /// </summary>
    public async Task Flush(CancellationToken ct = default)
    {
        using var activity = _activity.StartActivity("vector.flush");

        var indexName = IndexName;
        var url = $"/{Uri.EscapeDataString(indexName)}";
        var resp = await _http.DeleteAsync(url, ct);

        // 404 means the index didn't exist; treat as a successful no-op flush.
        if (!resp.IsSuccessStatusCode && resp.StatusCode != HttpStatusCode.NotFound)
        {
            var body = await resp.Content.ReadAsStringAsync(ct);
            throw new InvalidOperationException($"{_dialect.EngineLabel} flush failed: {(int)resp.StatusCode} {resp.ReasonPhrase} {body}");
        }

        _ensuredIndexes.TryRemove(indexName, out _);
    }

    private async Task Clear(CancellationToken ct)
    {
        await EnsureIndexInitialized(ct);
        var url = $"/{Uri.EscapeDataString(IndexName)}/_delete_by_query?refresh={_options.RefreshMode}";
        var payload = new JObject
        {
            ["query"] = new JObject
            {
                ["match_all"] = new JObject()
            }
        };

        var resp = await _http.PostAsync(url, new StringContent(payload.ToString(Formatting.None), Encoding.UTF8, "application/json"), ct);
        if (!resp.IsSuccessStatusCode)
        {
            var body = await resp.Content.ReadAsStringAsync(ct);
            throw new InvalidOperationException($"{_dialect.EngineLabel} clear failed: {(int)resp.StatusCode} {resp.ReasonPhrase} {body}");
        }
    }

    private async Task<int> GetCount(CancellationToken ct)
    {
        // Check if index exists first - don't require dimension for stats
        var indexExistsUrl = $"/{Uri.EscapeDataString(IndexName)}";
        var headResp = await _http.SendAsync(new HttpRequestMessage(HttpMethod.Head, indexExistsUrl), ct);

        // If index doesn't exist (404), return 0
        if (headResp.StatusCode == HttpStatusCode.NotFound)
        {
            return 0;
        }

        var url = $"/{Uri.EscapeDataString(IndexName)}/_count";
        var resp = await _http.GetAsync(url, ct);
        if (!resp.IsSuccessStatusCode)
        {
            var body = await resp.Content.ReadAsStringAsync(ct);
            throw new InvalidOperationException($"{_dialect.EngineLabel} count failed: {(int)resp.StatusCode} {resp.ReasonPhrase} {body}");
        }

        var json = await resp.Content.ReadAsStringAsync(ct);
        var parsed = JObject.Parse(json);
        var count = parsed["count"]?.Value<int>() ?? 0;
        return count;
    }

    private async Task EnsureIndexInitialized(CancellationToken ct)
    {
        if (_ensuredIndexes.ContainsKey(IndexName))
        {
            return;
        }

        var dimension = _options.Dimension ?? _discoveredDimension;
        if (dimension > 0)
        {
            await EnsureIndex(dimension, ct);
        }
    }

    private async Task EnsureIndex(int dimension, CancellationToken ct)
    {
        var indexName = IndexName;
        if (_ensuredIndexes.ContainsKey(indexName))
        {
            return;
        }

        using var _ = _activity.StartActivity("vector.index.ensureCreated");

        var url = $"/{Uri.EscapeDataString(indexName)}";
        var probe = await _http.GetAsync(url, ct);
        if (probe.IsSuccessStatusCode)
        {
            _ensuredIndexes[indexName] = 0;
            return;
        }

        if (_options.DisableIndexAutoCreate)
        {
            throw new InvalidOperationException($"{_dialect.EngineLabel} index '{indexName}' does not exist and auto creation is disabled.");
        }

        var body = _dialect.BuildIndexBody(dimension, _dialect.MapSimilarityToken(_options.SimilarityMetric), _options);

        var create = await _http.PutAsync(url, new StringContent(body.ToString(Formatting.None), Encoding.UTF8, "application/json"), ct);
        if (!create.IsSuccessStatusCode && create.StatusCode != HttpStatusCode.BadRequest)
        {
            var text = await create.Content.ReadAsStringAsync(ct);
            throw new InvalidOperationException($"{_dialect.EngineLabel} index creation failed: {(int)create.StatusCode} {create.ReasonPhrase} {text}");
        }

        _ensuredIndexes[indexName] = 0;
    }

    private string IndexName
    {
        get
        {
            if (!string.IsNullOrEmpty(_options.IndexName))
            {
                // A pinned IndexName bypasses the partition+source name-fold (IndexPrefix does NOT — it decorates the
                // computed name) — warn once if that defeats active isolation.
                VectorAdapterNaming.WarnIfPinnedNameDefeatsIsolation<TEntity>(_options.IndexName!, "IndexName");
                return _options.IndexName!;
            }

            var baseName = VectorAdapterNaming.GetOrCompute<TEntity, TKey>(_services);
            baseName = baseName.Replace('#', '-').Replace('.', '-').ToLowerInvariant();
            if (!string.IsNullOrEmpty(_options.IndexPrefix))
            {
                return $"{_options.IndexPrefix!.TrimEnd('-')}-{baseName}".Trim('-');
            }

            return baseName;
        }
    }

    private void ConfigureHttpClient()
    {
        if (string.IsNullOrWhiteSpace(_options.Endpoint))
        {
            throw new InvalidOperationException($"{_dialect.EngineLabel} endpoint must be configured.");
        }

        _http.BaseAddress = new Uri(_options.Endpoint);
        if (_http.Timeout == default)
        {
            _http.Timeout = TimeSpan.FromSeconds(Math.Max(1, _options.DefaultTimeoutSeconds));
        }

        // ApiKey is the Elasticsearch 'ApiKey' Authorization scheme. OpenSearch does not natively use it
        // (it relies on Basic / the security plugin), but the branch is kept identical across both
        // engines because it is harmless when ApiKey is unset (the default) and divergence here would
        // re-introduce the very drift DATA-0103 collapses. Configure Username/Password for OpenSearch.
        if (!string.IsNullOrEmpty(_options.ApiKey))
        {
            _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("ApiKey", _options.ApiKey);
        }
        else if (!string.IsNullOrEmpty(_options.Username))
        {
            var token = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_options.Username}:{_options.Password ?? ""}"));
            _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", token);
        }
    }

    private JObject BuildDocument(TKey id, float[] embedding, object? metadata)
    {
        var source = new JObject
        {
            [_options.IdField] = NormalizeId(id),
            [_options.VectorField] = new JArray(embedding.Select(v => (double)v))
        };

        if (metadata is not null)
        {
            source[_options.MetadataField] = JObject.FromObject(metadata);
        }

        return source;
    }

    private int EnsureDimension(int dimension)
    {
        if (_discoveredDimension > 0 && _discoveredDimension != dimension)
        {
            _logger?.LogWarning("{Engine} vector dimension changed from {Previous} to {Current}. Using latest value.", _dialect.EngineLabel, _discoveredDimension, dimension);
        }

        _discoveredDimension = dimension;
        return dimension;
    }

    private string NormalizeId(TKey id)
    {
        if (id is string s) return s;
        if (id is Guid g) return g.ToString("N", CultureInfo.InvariantCulture);
        if (id is int i) return i.ToString(CultureInfo.InvariantCulture);
        if (id is long l) return l.ToString(CultureInfo.InvariantCulture);
        if (id is ValueType)
        {
            return Convert.ToString(id, CultureInfo.InvariantCulture) ?? id.ToString() ?? throw new InvalidOperationException("Unable to normalize identifier.");
        }

        return id.ToString() ?? throw new InvalidOperationException("Unable to normalize identifier.");
    }

    private TKey ConvertId(string value)
    {
        if (typeof(TKey) == typeof(string))
        {
            return (TKey)(object)value;
        }

        if (typeof(TKey) == typeof(Guid) && Guid.TryParse(value, out var guid))
        {
            return (TKey)(object)guid;
        }

        if (typeof(TKey).IsEnum)
        {
            return (TKey)Enum.Parse(typeof(TKey), value, ignoreCase: true);
        }

        return (TKey)Convert.ChangeType(value, typeof(TKey), CultureInfo.InvariantCulture);
    }
}
