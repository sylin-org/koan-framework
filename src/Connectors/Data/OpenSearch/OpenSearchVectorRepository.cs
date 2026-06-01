using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Instructions;
using Koan.Data.Vector.Abstractions;
using Koan.Data.Vector.Abstractions.Configuration;

namespace Koan.Data.Connector.OpenSearch;

internal sealed class OpenSearchVectorRepository<TEntity, TKey> :
    IVectorSearchRepository<TEntity, TKey>,
    IVectorCapabilities,
    IInstructionExecutor<TEntity>
    where TEntity : class, IEntity<TKey>
    where TKey : notnull
{
    private readonly HttpClient _http;
    private readonly OpenSearchOptions _options;
    private readonly IServiceProvider _services;
    private readonly ILogger<OpenSearchVectorRepository<TEntity, TKey>>? _logger;
    // Keyed by IndexName — one repo instance serves multiple partitions/storage names, so a
    // single bool would short-circuit EnsureIndex after the first partition is created. Per-
    // IndexName tracking is the only correct shape (same fix as Elasticsearch).
    private readonly ConcurrentDictionary<string, byte> _ensuredIndexes = new(StringComparer.Ordinal);
    private int _discoveredDimension = -1;

    public OpenSearchVectorRepository(
        IHttpClientFactory httpFactory,
        IOptions<OpenSearchOptions> options,
        IServiceProvider services)
    {
        _http = httpFactory.CreateClient(Infrastructure.Constants.HttpClientName);
        _options = options.Value;
        _services = services;
        _logger = (ILogger<OpenSearchVectorRepository<TEntity, TKey>>?)services.GetService(typeof(ILogger<OpenSearchVectorRepository<TEntity, TKey>>));
        ConfigureHttpClient();
    }

    public VectorCapabilities Capabilities =>
        VectorCapabilities.Knn |
        VectorCapabilities.Filters |
        VectorCapabilities.BulkUpsert |
        VectorCapabilities.BulkDelete;

    // AI-0036 §10 / DATA-0097 P1: operator-aware metadata-filter capabilities.
    public Koan.Data.Abstractions.Filtering.VectorFilterCapabilities FilterCapabilities => OpenSearchFilterTranslator.Caps;

    public async Task VectorEnsureCreated(CancellationToken ct = default)
    {
        var dimension = _discoveredDimension > 0 ? _discoveredDimension : _options.Dimension ?? -1;
        if (dimension <= 0)
        {
            throw new InvalidOperationException("OpenSearch vector dimension is unknown. Configure Koan:Data:OpenSearch:Dimension or upsert a vector to allow discovery.");
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

        using var _ = OpenSearchTelemetry.Activity.StartActivity("vector.upsert");

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
            throw new InvalidOperationException($"OpenSearch upsert failed: {(int)response.StatusCode} {response.ReasonPhrase} {body}");
        }
    }

    public async Task<int> UpsertMany(IEnumerable<(TKey Id, float[] Embedding, object? Metadata)> items, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(items);

        using var _ = OpenSearchTelemetry.Activity.StartActivity("vector.bulkUpsert");

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
            throw new InvalidOperationException($"OpenSearch bulk upsert failed: {(int)resp.StatusCode} {resp.ReasonPhrase} {respBody}");
        }

        var parsed = string.IsNullOrWhiteSpace(respBody) ? null : JObject.Parse(respBody);
        if (parsed?["errors"]?.Value<bool>() == true)
        {
            _logger?.LogWarning("OpenSearch bulk upsert completed with errors: {Response}", respBody);
        }

        return list.Count;
    }

    public async Task<bool> Delete(TKey id, CancellationToken ct = default)
    {
        using var _ = OpenSearchTelemetry.Activity.StartActivity("vector.delete");

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
            throw new InvalidOperationException($"OpenSearch delete failed: {(int)resp.StatusCode} {resp.ReasonPhrase} {body}");
        }

        return true;
    }

    public async Task<int> DeleteMany(IEnumerable<TKey> ids, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ids);
        using var _ = OpenSearchTelemetry.Activity.StartActivity("vector.bulkDelete");

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
            throw new InvalidOperationException($"OpenSearch bulk delete failed: {(int)resp.StatusCode} {resp.ReasonPhrase} {respBody}");
        }

        var parsed = string.IsNullOrWhiteSpace(respBody) ? null : JObject.Parse(respBody);
        if (parsed?["errors"]?.Value<bool>() == true)
        {
            _logger?.LogWarning("OpenSearch bulk delete completed with errors: {Response}", respBody);
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

        using var _ = OpenSearchTelemetry.Activity.StartActivity("vector.search");

        var dimension = EnsureDimension(options.Query.Length);
        await EnsureIndex(dimension, ct);

        var topK = Math.Max(1, options.TopK ?? 10);
        var request = BuildSearchRequest(options, topK);
        var url = $"/{Uri.EscapeDataString(IndexName)}/_search";
        var resp = await _http.PostAsync(url, new StringContent(request.ToString(Formatting.None), Encoding.UTF8, "application/json"), ct);
        var body = await resp.Content.ReadAsStringAsync(ct);

        if (!resp.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"OpenSearch search failed: {(int)resp.StatusCode} {resp.ReasonPhrase} {body}");
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

        throw new NotSupportedException($"Instruction '{instruction.Name}' not supported by OpenSearch vector adapter.");
    }

    private JObject BuildSearchRequest(VectorQueryOptions options, int topK)
    {
        // OpenSearch 2.x KNN query shape (differs from Elasticsearch's 8.x):
        //   { "query": { "knn": { "<field>": { "vector": [...], "k": N, "filter"?: {...} } } } }
        // Elasticsearch's top-level "knn" with "field"/"query_vector" is rejected by OS with
        // "Unknown key for a START_OBJECT in [knn]".
        var knnFieldBody = new JObject
        {
            ["vector"] = new JArray(options.Query.Select(v => (double)v)),
            ["k"] = topK
        };

        var filter = OpenSearchFilterTranslator.TranslateWhereClause(options.Filter, _options.MetadataField);
        if (filter is not null)
        {
            knnFieldBody["filter"] = new JObject
            {
                ["bool"] = new JObject
                {
                    ["filter"] = new JArray(filter)
                }
            };
        }

        var request = new JObject
        {
            ["size"] = topK,
            ["query"] = new JObject
            {
                ["knn"] = new JObject
                {
                    [_options.VectorField] = knnFieldBody
                }
            },
            ["_source"] = new JArray(_options.MetadataField, _options.IdField)
        };

        if (options.Timeout is { } timeout)
        {
            request["timeout"] = $"{(int)timeout.TotalMilliseconds}ms";
        }

        return request;
    }

    /// <summary>
    /// Drop the underlying index entirely. Next operation re-creates it lazily with the proper
    /// knn_vector mapping. Invalidates the per-IndexName ensured-cache.
    /// </summary>
    public async Task Flush(CancellationToken ct = default)
    {
        using var activity = OpenSearchTelemetry.Activity.StartActivity("vector.flush");

        var indexName = IndexName;
        var url = $"/{Uri.EscapeDataString(indexName)}";
        var resp = await _http.DeleteAsync(url, ct);

        // 404 → no-op
        if (!resp.IsSuccessStatusCode && resp.StatusCode != HttpStatusCode.NotFound)
        {
            var body = await resp.Content.ReadAsStringAsync(ct);
            throw new InvalidOperationException($"OpenSearch flush failed: {(int)resp.StatusCode} {resp.ReasonPhrase} {body}");
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
            throw new InvalidOperationException($"OpenSearch clear failed: {(int)resp.StatusCode} {resp.ReasonPhrase} {body}");
        }
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

        using var _ = OpenSearchTelemetry.Activity.StartActivity("vector.index.ensureCreated");

        var url = $"/{Uri.EscapeDataString(indexName)}";
        var probe = await _http.GetAsync(url, ct);
        if (probe.IsSuccessStatusCode)
        {
            _ensuredIndexes[indexName] = 0;
            return;
        }

        if (_options.DisableIndexAutoCreate)
        {
            throw new InvalidOperationException($"OpenSearch index '{indexName}' does not exist and auto creation is disabled.");
        }

        // OpenSearch 2.x KNN index requires `index.knn = true` in settings and `knn_vector`
        // field type with a method config (engine + space_type). This differs sharply from
        // Elasticsearch's `dense_vector` + top-level `similarity` model.
        var body = new JObject
        {
            ["settings"] = new JObject
            {
                ["index"] = new JObject
                {
                    ["knn"] = true,
                    ["number_of_shards"] = 1,
                    ["number_of_replicas"] = 0
                }
            },
            ["mappings"] = new JObject
            {
                ["properties"] = new JObject
                {
                    [_options.IdField] = new JObject { ["type"] = "keyword" },
                    [_options.VectorField] = new JObject
                    {
                        ["type"] = "knn_vector",
                        ["dimension"] = dimension,
                        ["method"] = new JObject
                        {
                            ["name"] = "hnsw",
                            ["engine"] = "lucene",
                            ["space_type"] = MapSpaceType(_options.SimilarityMetric)
                        }
                    },
                    [_options.MetadataField] = new JObject { ["type"] = "object", ["dynamic"] = true }
                }
            }
        };

        var create = await _http.PutAsync(url, new StringContent(body.ToString(Formatting.None), Encoding.UTF8, "application/json"), ct);
        if (!create.IsSuccessStatusCode && create.StatusCode != HttpStatusCode.BadRequest)
        {
            var text = await create.Content.ReadAsStringAsync(ct);
            throw new InvalidOperationException($"OpenSearch index creation failed: {(int)create.StatusCode} {create.ReasonPhrase} {text}");
        }

        _ensuredIndexes[indexName] = 0;
    }

    /// <summary>
    /// Map the cross-provider similarity metric token onto OpenSearch's KNN space_type values.
    /// OS supports: l2, cosinesimil, innerproduct, l1, linf, hamming, hammingbit. The framework's
    /// Options field accepts the Elasticsearch-friendly tokens ("cosine", "l2", "dotproduct")
    /// so adapters look the same to users; we translate at the API boundary.
    /// </summary>
    private static string MapSpaceType(string metric) => metric?.ToLowerInvariant() switch
    {
        "cosine" or "cosinesimil" => "cosinesimil",
        "l2" or "euclidean"        => "l2",
        "dot" or "dotproduct" or "innerproduct" => "innerproduct",
        "l1"                       => "l1",
        "linf"                     => "linf",
        _                          => "cosinesimil"
    };

    private string IndexName
    {
        get
        {
            if (!string.IsNullOrEmpty(_options.IndexName))
            {
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
            throw new InvalidOperationException("Koan:Data:OpenSearch:Endpoint must be configured.");
        }

        _http.BaseAddress = new Uri(_options.Endpoint);
        if (_http.Timeout == default)
        {
            _http.Timeout = TimeSpan.FromSeconds(Math.Max(1, _options.DefaultTimeoutSeconds));
        }

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
            _logger?.LogWarning("OpenSearch vector dimension changed from {Previous} to {Current}. Using latest value.", _discoveredDimension, dimension);
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

