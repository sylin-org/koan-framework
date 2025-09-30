using System;
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

namespace Koan.Data.Connector.ElasticSearch;

internal sealed class ElasticSearchVectorRepository<TEntity, TKey> :
    IVectorSearchRepository<TEntity, TKey>,
    IVectorCapabilities,
    IInstructionExecutor<TEntity>
    where TEntity : class, IEntity<TKey>
    where TKey : notnull
{
    private readonly HttpClient _http;
    private readonly ElasticSearchOptions _options;
    private readonly IServiceProvider _services;
    private readonly ILogger<ElasticSearchVectorRepository<TEntity, TKey>>? _logger;
    private volatile bool _indexEnsured;
    private int _discoveredDimension = -1;

    public ElasticSearchVectorRepository(
        IHttpClientFactory httpFactory,
        IOptions<ElasticSearchOptions> options,
        IServiceProvider services)
    {
        _http = httpFactory.CreateClient(Infrastructure.Constants.HttpClientName);
        _options = options.Value;
        _services = services;
        _logger = (ILogger<ElasticSearchVectorRepository<TEntity, TKey>>?)services.GetService(typeof(ILogger<ElasticSearchVectorRepository<TEntity, TKey>>));
        ConfigureHttpClient();
    }

    public VectorCapabilities Capabilities =>
        VectorCapabilities.Knn |
        VectorCapabilities.Filters |
        VectorCapabilities.BulkUpsert |
        VectorCapabilities.BulkDelete;

    public async Task VectorEnsureCreatedAsync(CancellationToken ct = default)
    {
        var dimension = _discoveredDimension > 0 ? _discoveredDimension : _options.Dimension ?? -1;
        if (dimension <= 0)
        {
            throw new InvalidOperationException("Elasticsearch vector dimension is unknown. Configure Koan:Data:ElasticSearch:Dimension or upsert a vector to allow discovery.");
        }

        await EnsureIndexAsync(dimension, ct);
    }

    public async Task UpsertAsync(TKey id, float[] embedding, object? metadata = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(embedding);
        if (embedding.Length == 0)
        {
            throw new ArgumentException("Embedding must contain values.", nameof(embedding));
        }

        using var _ = ElasticSearchTelemetry.Activity.StartActivity("vector.upsert");

        var dimension = EnsureDimension(embedding.Length);
        await EnsureIndexAsync(dimension, ct);

        var document = BuildDocument(id, embedding, metadata);
        var payload = document.ToString(Formatting.None);
        var docId = NormalizeId(id);
        var url = $"/{Uri.EscapeDataString(IndexName)}/_doc/{Uri.EscapeDataString(docId)}?refresh={_options.RefreshMode}";
        var response = await _http.PutAsync(url, new StringContent(payload, Encoding.UTF8, "application/json"), ct);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            throw new InvalidOperationException($"Elasticsearch upsert failed: {(int)response.StatusCode} {response.ReasonPhrase} {body}");
        }
    }

    public async Task<int> UpsertManyAsync(IEnumerable<(TKey Id, float[] Embedding, object? Metadata)> items, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(items);

        using var _ = ElasticSearchTelemetry.Activity.StartActivity("vector.bulkUpsert");

        var list = items.ToList();
        if (list.Count == 0)
        {
            return 0;
        }

        var dimension = EnsureDimension(list[0].Embedding.Length);
        await EnsureIndexAsync(dimension, ct);

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
            throw new InvalidOperationException($"Elasticsearch bulk upsert failed: {(int)resp.StatusCode} {resp.ReasonPhrase} {respBody}");
        }

        var parsed = string.IsNullOrWhiteSpace(respBody) ? null : JObject.Parse(respBody);
        if (parsed?["errors"]?.Value<bool>() == true)
        {
            _logger?.LogWarning("Elasticsearch bulk upsert completed with errors: {Response}", respBody);
        }

        return list.Count;
    }

    public async Task<bool> DeleteAsync(TKey id, CancellationToken ct = default)
    {
        using var _ = ElasticSearchTelemetry.Activity.StartActivity("vector.delete");

        await EnsureIndexInitializedAsync(ct);

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
            throw new InvalidOperationException($"Elasticsearch delete failed: {(int)resp.StatusCode} {resp.ReasonPhrase} {body}");
        }

        return true;
    }

    public async Task<int> DeleteManyAsync(IEnumerable<TKey> ids, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ids);
        using var _ = ElasticSearchTelemetry.Activity.StartActivity("vector.bulkDelete");

        await EnsureIndexInitializedAsync(ct);

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
            throw new InvalidOperationException($"Elasticsearch bulk delete failed: {(int)resp.StatusCode} {resp.ReasonPhrase} {respBody}");
        }

        var parsed = string.IsNullOrWhiteSpace(respBody) ? null : JObject.Parse(respBody);
        if (parsed?["errors"]?.Value<bool>() == true)
        {
            _logger?.LogWarning("Elasticsearch bulk delete completed with errors: {Response}", respBody);
        }

        return list.Count;
    }

    public async Task<VectorQueryResult<TKey>> SearchAsync(VectorQueryOptions options, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (options.Query is null || options.Query.Length == 0)
        {
            throw new ArgumentException("Search query requires a vector with at least one value.", nameof(options));
        }

        using var _ = ElasticSearchTelemetry.Activity.StartActivity("vector.search");

        var dimension = EnsureDimension(options.Query.Length);
        await EnsureIndexAsync(dimension, ct);

        var topK = Math.Max(1, options.TopK ?? 10);
        var request = BuildSearchRequest(options, topK);
        var url = $"/{Uri.EscapeDataString(IndexName)}/_search";
        var resp = await _http.PostAsync(url, new StringContent(request.ToString(Formatting.None), Encoding.UTF8, "application/json"), ct);
        var body = await resp.Content.ReadAsStringAsync(ct);

        if (!resp.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Elasticsearch search failed: {(int)resp.StatusCode} {resp.ReasonPhrase} {body}");
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
            await VectorEnsureCreatedAsync(ct);
            return default!;
        }

        if (string.Equals(instruction.Name, DataInstructions.Clear, StringComparison.OrdinalIgnoreCase))
        {
            await ClearAsync(ct);
            return default!;
        }

        throw new NotSupportedException($"Instruction '{instruction.Name}' not supported by Elasticsearch vector adapter.");
    }

    private JObject BuildSearchRequest(VectorQueryOptions options, int topK)
    {
        var request = new JObject
        {
            ["size"] = topK,
            ["knn"] = new JObject
            {
                ["field"] = _options.VectorField,
                ["query_vector"] = new JArray(options.Query.Select(v => (double)v)),
                ["k"] = topK,
                ["num_candidates"] = Math.Max(topK, topK * 2)
            },
            ["_source"] = new JArray(_options.MetadataField, _options.IdField)
        };

        var filter = ElasticSearchFilterTranslator.TranslateWhereClause(options.Filter);
        if (filter is not null)
        {
            request["query"] = new JObject
            {
                ["bool"] = new JObject
                {
                    ["filter"] = new JArray(filter)
                }
            };
        }

        if (options.Timeout is { } timeout)
        {
            request["timeout"] = $"{(int)timeout.TotalMilliseconds}ms";
        }

        return request;
    }

    private async Task ClearAsync(CancellationToken ct)
    {
        await EnsureIndexInitializedAsync(ct);
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
            throw new InvalidOperationException($"Elasticsearch clear failed: {(int)resp.StatusCode} {resp.ReasonPhrase} {body}");
        }
    }

    private async Task EnsureIndexInitializedAsync(CancellationToken ct)
    {
        if (_indexEnsured)
        {
            return;
        }

        var dimension = _options.Dimension ?? _discoveredDimension;
        if (dimension > 0)
        {
            await EnsureIndexAsync(dimension, ct);
        }
    }

    private async Task EnsureIndexAsync(int dimension, CancellationToken ct)
    {
        if (_indexEnsured)
        {
            return;
        }

        using var _ = ElasticSearchTelemetry.Activity.StartActivity("vector.index.ensureCreated");

        var url = $"/{Uri.EscapeDataString(IndexName)}";
        var probe = await _http.GetAsync(url, ct);
        if (probe.IsSuccessStatusCode)
        {
            _indexEnsured = true;
            return;
        }

        if (_options.DisableIndexAutoCreate)
        {
            throw new InvalidOperationException($"Elasticsearch index '{IndexName}' does not exist and auto creation is disabled.");
        }

        var body = new JObject
        {
            ["settings"] = new JObject
            {
                ["index"] = new JObject
                {
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
                        ["type"] = "dense_vector",
                        ["dims"] = dimension,
                        ["index"] = true,
                        ["similarity"] = _options.SimilarityMetric
                    },
                    [_options.MetadataField] = new JObject { ["type"] = "object", ["dynamic"] = true }
                }
            }
        };

        var create = await _http.PutAsync(url, new StringContent(body.ToString(Formatting.None), Encoding.UTF8, "application/json"), ct);
        if (!create.IsSuccessStatusCode && create.StatusCode != HttpStatusCode.BadRequest)
        {
            var text = await create.Content.ReadAsStringAsync(ct);
            throw new InvalidOperationException($"Elasticsearch index creation failed: {(int)create.StatusCode} {create.ReasonPhrase} {text}");
        }

        _indexEnsured = true;
    }

    private string IndexName
    {
        get
        {
            if (!string.IsNullOrEmpty(_options.IndexName))
            {
                return _options.IndexName!;
            }

            var baseName = Koan.Data.Core.Configuration.StorageNameRegistry.GetOrCompute<TEntity, TKey>(_services);
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
            throw new InvalidOperationException("Koan:Data:ElasticSearch:Endpoint must be configured.");
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
            var token = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_options.Username}:{_options.Password ?? string.Empty}"));
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
            _logger?.LogWarning("Elasticsearch vector dimension changed from {Previous} to {Current}. Using latest value.", _discoveredDimension, dimension);
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

