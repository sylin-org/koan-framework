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

namespace Koan.Data.Vector.Connector.Milvus;

internal sealed class MilvusVectorRepository<TEntity, TKey> :
    IVectorSearchRepository<TEntity, TKey>,
    IVectorCapabilities,
    IInstructionExecutor<TEntity>
    where TEntity : class, IEntity<TKey>
    where TKey : notnull
{
    private readonly HttpClient _http;
    private readonly MilvusOptions _options;
    private readonly IServiceProvider _services;
    private readonly ILogger<MilvusVectorRepository<TEntity, TKey>>? _logger;
    private volatile bool _collectionEnsured;
    private int _discoveredDimension = -1;

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

    public VectorCapabilities Capabilities =>
        VectorCapabilities.Knn |
        VectorCapabilities.Filters |
        VectorCapabilities.BulkUpsert |
        VectorCapabilities.BulkDelete |
        VectorCapabilities.ScoreNormalization;

    public async Task VectorEnsureCreatedAsync(CancellationToken ct = default)
    {
        var dimension = _discoveredDimension > 0 ? _discoveredDimension : _options.Dimension ?? -1;
        if (dimension <= 0)
        {
            throw new InvalidOperationException("Milvus vector dimension is unknown. Configure Koan:Data:Milvus:Dimension or upsert a vector to allow discovery.");
        }

        await EnsureCollectionAsync(dimension, ct);
    }

    public async Task UpsertAsync(TKey id, float[] embedding, object? metadata = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(embedding);
        if (embedding.Length == 0)
        {
            throw new ArgumentException("Embedding must contain values.", nameof(embedding));
        }

        using var _ = MilvusTelemetry.Activity.StartActivity("vector.upsert");

        var dimension = EnsureDimension(embedding.Length);
        await EnsureCollectionAsync(dimension, ct);

        var payload = BuildUpsertBody(new[] { (id, embedding, metadata) });
        var response = await _http.PostAsync("/v2/vectors/upsert", payload, ct);
        await EnsureSuccess(response, "Milvus upsert", ct);
    }

    public async Task<int> UpsertManyAsync(IEnumerable<(TKey Id, float[] Embedding, object? Metadata)> items, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(items);
        using var _ = MilvusTelemetry.Activity.StartActivity("vector.bulkUpsert");

        var list = items.ToList();
        if (list.Count == 0)
        {
            return 0;
        }

        var dimension = EnsureDimension(list[0].Embedding.Length);
        await EnsureCollectionAsync(dimension, ct);

        foreach (var item in list)
        {
            if (item.Embedding.Length != dimension)
            {
                throw new InvalidOperationException($"Embedding dimension mismatch. Expected {dimension}, received {item.Embedding.Length}.");
            }
        }

        var payload = BuildUpsertBody(list.Select(i => (i.Id, i.Embedding, i.Metadata)));
        var response = await _http.PostAsync("/v2/vectors/upsert", payload, ct);
        await EnsureSuccess(response, "Milvus bulk upsert", ct);
        return list.Count;
    }

    public async Task<bool> DeleteAsync(TKey id, CancellationToken ct = default)
    {
        using var _ = MilvusTelemetry.Activity.StartActivity("vector.delete");
        await EnsureCollectionInitializedAsync(ct);

        var expr = $"{_options.PrimaryFieldName} in [{FormatIdentifier(id)}]";
        var payload = BuildDeleteBody(expr);
        var response = await _http.PostAsync("/v2/vectors/delete", payload, ct);
        await EnsureSuccess(response, "Milvus delete", ct, allowNotFound: true);
        return response.StatusCode != HttpStatusCode.NotFound;
    }

    public async Task<int> DeleteManyAsync(IEnumerable<TKey> ids, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ids);
        using var _ = MilvusTelemetry.Activity.StartActivity("vector.bulkDelete");
        await EnsureCollectionInitializedAsync(ct);

        var list = ids.ToList();
        if (list.Count == 0)
        {
            return 0;
        }

        var expr = $"{_options.PrimaryFieldName} in [{string.Join(",", list.Select(FormatIdentifier))}]";
        var payload = BuildDeleteBody(expr);
        var response = await _http.PostAsync("/v2/vectors/delete", payload, ct);
        await EnsureSuccess(response, "Milvus bulk delete", ct, allowNotFound: true);
        return list.Count;
    }

    public async Task<VectorQueryResult<TKey>> SearchAsync(VectorQueryOptions options, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (options.Query is null || options.Query.Length == 0)
        {
            throw new ArgumentException("Search query requires a vector with at least one value.", nameof(options));
        }

        using var _ = MilvusTelemetry.Activity.StartActivity("vector.search");

        var dimension = EnsureDimension(options.Query.Length);
        await EnsureCollectionAsync(dimension, ct);

        var topK = Math.Max(1, options.TopK ?? 10);
        var request = BuildSearchBody(options, topK);
        var response = await _http.PostAsync("/v2/vectors/search", request, ct);
        var content = await EnsureSuccess(response, "Milvus search", ct);

        var parsed = string.IsNullOrWhiteSpace(content) ? new JObject() : JObject.Parse(content);
        var results = parsed["results"] as JArray ?? new JArray();
        var matches = new List<VectorMatch<TKey>>(results.Count);

        foreach (var row in results.OfType<JObject>())
        {
            var idToken = row[_options.PrimaryFieldName] ?? row["id"];
            if (idToken is null)
            {
                continue;
            }

            var id = ConvertId(idToken);
            var score = row.Value<double?>("score") ?? row.Value<double?>("distance") ?? 0d;
            var metadata = row[_options.MetadataFieldName]?.ToObject<object?>();
            matches.Add(new VectorMatch<TKey>(id, score, metadata));
        }

        var total = parsed["total"]?.Value<int?>();
        var totalKind = total.HasValue ? VectorTotalKind.Estimated : VectorTotalKind.Unknown;

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

        throw new NotSupportedException($"Instruction '{instruction.Name}' not supported by Milvus vector adapter.");
    }

    private HttpContent BuildUpsertBody(IEnumerable<(TKey Id, float[] Embedding, object? Metadata)> items)
    {
        var rows = new JArray();
        foreach (var item in items)
        {
            var row = new JObject
            {
                [_options.PrimaryFieldName] = FormatIdentifierValue(item.Id),
                [_options.VectorFieldName] = new JArray(item.Embedding.Select(v => (double)v))
            };
            if (item.Metadata is not null)
            {
                row[_options.MetadataFieldName] = JToken.FromObject(item.Metadata);
            }
            rows.Add(row);
        }

        var body = new JObject
        {
            ["dbName"] = _options.DatabaseName,
            ["collectionName"] = CollectionName,
            ["consistencyLevel"] = _options.ConsistencyLevel,
            ["data"] = rows
        };

        return new StringContent(body.ToString(Formatting.None), Encoding.UTF8, "application/json");
    }

    private HttpContent BuildDeleteBody(string expression)
    {
        var body = new JObject
        {
            ["dbName"] = _options.DatabaseName,
            ["collectionName"] = CollectionName,
            ["expr"] = expression
        };
        return new StringContent(body.ToString(Formatting.None), Encoding.UTF8, "application/json");
    }

    private HttpContent BuildSearchBody(VectorQueryOptions options, int topK)
    {
        var filter = MilvusFilterTranslator.Translate(options.Filter, _options.MetadataFieldName);
        var body = new JObject
        {
            ["dbName"] = _options.DatabaseName,
            ["collectionName"] = CollectionName,
            ["vectorFieldName"] = _options.VectorFieldName,
            ["vectors"] = new JArray(new JArray(options.Query.Select(v => (double)v))),
            ["limit"] = topK,
            ["metricType"] = _options.Metric,
            ["outputFields"] = new JArray(_options.PrimaryFieldName, _options.MetadataFieldName)
        };

        if (!string.IsNullOrWhiteSpace(filter))
        {
            body["expr"] = filter;
        }

        if (options.Timeout is { } timeout)
        {
            body["timeoutMs"] = (int)timeout.TotalMilliseconds;
        }

        return new StringContent(body.ToString(Formatting.None), Encoding.UTF8, "application/json");
    }

    private async Task ClearAsync(CancellationToken ct)
    {
        await EnsureCollectionInitializedAsync(ct);
        var payload = BuildDeleteBody("true");
        var response = await _http.PostAsync("/v2/vectors/delete", payload, ct);
        await EnsureSuccess(response, "Milvus clear", ct, allowNotFound: true);
    }

    private async Task EnsureCollectionInitializedAsync(CancellationToken ct)
    {
        if (_collectionEnsured)
        {
            return;
        }

        var dimension = _options.Dimension ?? _discoveredDimension;
        if (dimension > 0)
        {
            await EnsureCollectionAsync(dimension, ct);
        }
    }

    private async Task EnsureCollectionAsync(int dimension, CancellationToken ct)
    {
        if (_collectionEnsured)
        {
            return;
        }

        using var _ = MilvusTelemetry.Activity.StartActivity("vector.collection.ensure");

        if (await CollectionExistsAsync(ct))
        {
            _collectionEnsured = true;
            return;
        }

        if (!_options.AutoCreateCollection)
        {
            throw new InvalidOperationException($"Milvus collection '{CollectionName}' does not exist and auto creation is disabled.");
        }

        var body = new JObject
        {
            ["dbName"] = _options.DatabaseName,
            ["collectionName"] = CollectionName,
            ["dimension"] = dimension,
            ["metricType"] = _options.Metric,
            ["primaryFieldName"] = _options.PrimaryFieldName,
            ["vectorFieldName"] = _options.VectorFieldName,
            ["metadataFieldName"] = _options.MetadataFieldName
        };

        var response = await _http.PostAsync("/v2/collections/create", new StringContent(body.ToString(Formatting.None), Encoding.UTF8, "application/json"), ct);
        await EnsureSuccess(response, "Milvus collection create", ct);
        _collectionEnsured = true;
    }

    private async Task<bool> CollectionExistsAsync(CancellationToken ct)
    {
        var url = $"/v2/collections/{Uri.EscapeDataString(CollectionName)}?dbName={Uri.EscapeDataString(_options.DatabaseName)}";
        var response = await _http.GetAsync(url, ct);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return false;
        }

        if (!response.IsSuccessStatusCode)
        {
            await EnsureSuccess(response, "Milvus collection describe", ct);
        }

        return true;
    }

    private string CollectionName
        => _options.CollectionName ?? Koan.Data.Core.Configuration.StorageNameRegistry.GetOrCompute<TEntity, TKey>(_services);

    private int EnsureDimension(int dimension)
    {
        if (_discoveredDimension > 0 && _discoveredDimension != dimension)
        {
            _logger?.LogWarning("Milvus vector dimension changed from {Previous} to {Current}. Using latest value.", _discoveredDimension, dimension);
        }

        _discoveredDimension = dimension;
        return dimension;
    }

    private void ConfigureHttpClient()
    {
        if (string.IsNullOrWhiteSpace(_options.Endpoint))
        {
            throw new InvalidOperationException("Koan:Data:Milvus:Endpoint must be configured.");
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
            var token = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_options.Username}:{_options.Password ?? string.Empty}"));
            _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", token);
        }
    }

    private async Task<string> EnsureSuccess(HttpResponseMessage response, string operation, CancellationToken ct, bool allowNotFound = false)
    {
        if (allowNotFound && response.StatusCode == HttpStatusCode.NotFound)
        {
            return string.Empty;
        }

        if (!response.IsSuccessStatusCode)
        {
            var failure = await response.Content.ReadAsStringAsync(ct);
            throw new InvalidOperationException($"{operation} failed: {(int)response.StatusCode} {response.ReasonPhrase} {failure}");
        }

        return await response.Content.ReadAsStringAsync(ct);
    }

    private string FormatIdentifier(TKey id)
    {
        if (typeof(TKey) == typeof(string))
        {
            return FormatString((string)(object)id);
        }

        if (id is Guid guid)
        {
            return FormatString(guid.ToString("N", CultureInfo.InvariantCulture));
        }

        if (id is IFormattable formattable)
        {
            return formattable.ToString(null, CultureInfo.InvariantCulture);
        }

        return FormatString(id.ToString() ?? string.Empty);
    }

    private JValue FormatIdentifierValue(TKey id)
    {
        if (typeof(TKey) == typeof(string) || id is Guid)
        {
            return new JValue(NormalizeStringId(id));
        }

        if (id is IFormattable formattable)
        {
            return new JValue(formattable.ToString(null, CultureInfo.InvariantCulture));
        }

        return new JValue(id.ToString());
    }

    private string NormalizeStringId(TKey id)
        => id switch
        {
            string s => s,
            Guid guid => guid.ToString("N", CultureInfo.InvariantCulture),
            _ => id.ToString() ?? string.Empty
        };

    private string FormatString(string value)
        => $"\"{value.Replace("\"", "\\\"")}\"";

    private TKey ConvertId(JToken token)
    {
        if (typeof(TKey) == typeof(string))
        {
            return (TKey)(object)token.Value<string>()!;
        }

        if (typeof(TKey) == typeof(Guid))
        {
            return (TKey)(object)Guid.Parse(token.Value<string>()!);
        }

        if (typeof(TKey).IsEnum)
        {
            return (TKey)Enum.Parse(typeof(TKey), token.Value<string>()!, ignoreCase: true);
        }

        return (TKey)Convert.ChangeType(token.Value<object>()!, typeof(TKey), CultureInfo.InvariantCulture);
    }
}

