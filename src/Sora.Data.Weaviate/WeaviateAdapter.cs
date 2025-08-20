using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sora.Data.Abstractions;

namespace Sora.Data.Weaviate;

[Sora.Data.Abstractions.ProviderPriority(10)]
public sealed class WeaviateVectorAdapterFactory : IVectorAdapterFactory
{
    public bool CanHandle(string provider) => string.Equals(provider, "weaviate", StringComparison.OrdinalIgnoreCase);

    public IVectorSearchRepository<TEntity, TKey> Create<TEntity, TKey>(IServiceProvider sp)
        where TEntity : class, IEntity<TKey>
        where TKey : notnull
        => new WeaviateVectorRepository<TEntity, TKey>(sp.GetRequiredService<IHttpClientFactory>(), sp.GetRequiredService<IOptions<WeaviateOptions>>(), sp);
}

internal sealed class WeaviateVectorRepository<TEntity, TKey> : IVectorSearchRepository<TEntity, TKey>, IVectorCapabilities
    where TEntity : class, IEntity<TKey>
    where TKey : notnull
{
    private readonly HttpClient _http;
    private readonly WeaviateOptions _options;
    private readonly IServiceProvider _sp;
    private readonly ILogger<WeaviateVectorRepository<TEntity, TKey>>? _logger;
    private volatile bool _schemaEnsured;

    public VectorCapabilities Capabilities => VectorCapabilities.Knn | VectorCapabilities.Filters | VectorCapabilities.BulkUpsert | VectorCapabilities.BulkDelete;

    public WeaviateVectorRepository(IHttpClientFactory httpFactory, IOptions<WeaviateOptions> options, IServiceProvider sp)
    {
        _http = httpFactory.CreateClient("weaviate");
        _options = options.Value;
        _sp = sp;
        _logger = sp.GetService<ILogger<WeaviateVectorRepository<TEntity, TKey>>>();
        _http.BaseAddress = new Uri(_options.Endpoint);
        if (_http.Timeout == default)
            _http.Timeout = TimeSpan.FromSeconds(Math.Max(1, _options.DefaultTimeoutSeconds));
    }

    private string ClassName => Sora.Data.Core.Configuration.StorageNameRegistry.GetOrCompute<TEntity, TKey>(_sp);

    private async Task EnsureSchemaAsync(CancellationToken ct)
    {
        if (_schemaEnsured) return;
        using var _ = WeaviateTelemetry.Activity.StartActivity("vector.index.ensureCreated");
        var cls = ClassName;
        // Probe class
        var probe = await _http.GetAsync($"/v1/schema/{Uri.EscapeDataString(cls)}", ct);
        if (probe.IsSuccessStatusCode)
        {
            _schemaEnsured = true; return;
        }
        // Create class with manual vectors
        var body = new
        {
            @class = cls,
            vectorizer = "none",
            vectorIndexConfig = new { distance = _options.Metric },
            properties = Array.Empty<object>()
        };
        var create = await _http.PostAsJsonAsync("/v1/schema/classes", body, ct);
        if (!create.IsSuccessStatusCode)
        {
            var txt = await create.Content.ReadAsStringAsync(ct);
            throw new InvalidOperationException($"Weaviate ensure schema failed: {(int)create.StatusCode} {create.ReasonPhrase} {txt}");
        }
        _schemaEnsured = true;
    }

    // Upsert single vector using /v1/objects
    public async Task UpsertAsync(TKey id, float[] embedding, object? metadata = null, CancellationToken ct = default)
    {
        using var _ = WeaviateTelemetry.Activity.StartActivity("vector.upsert");
    await EnsureSchemaAsync(ct);
        var obj = new
        {
            class_ = ClassName,
            id = id!.ToString(),
            properties = metadata ?? new { },
            vector = embedding,
        };
        var resp = await _http.PostAsJsonAsync("/v1/objects", obj, ct);
        if (!resp.IsSuccessStatusCode)
        {
            var body = await resp.Content.ReadAsStringAsync(ct);
            throw new InvalidOperationException($"Weaviate upsert failed: {(int)resp.StatusCode} {resp.ReasonPhrase} {body}");
        }
    }

    public async Task<int> UpsertManyAsync(IEnumerable<(TKey Id, float[] Embedding, object? Metadata)> items, CancellationToken ct = default)
    {
        var count = 0;
        foreach (var (id, emb, meta) in items)
        {
            await UpsertAsync(id, emb, meta, ct);
            count++;
        }
        return count;
    }

    public async Task<bool> DeleteAsync(TKey id, CancellationToken ct = default)
    {
        using var _ = WeaviateTelemetry.Activity.StartActivity("vector.delete");
        var resp = await _http.DeleteAsync($"/v1/objects/{Uri.EscapeDataString(id!.ToString()!)}", ct);
        return resp.IsSuccessStatusCode;
    }

    public async Task<int> DeleteManyAsync(IEnumerable<TKey> ids, CancellationToken ct = default)
    {
        var count = 0;
        foreach (var id in ids)
        {
            if (await DeleteAsync(id, ct)) count++;
        }
        return count;
    }

    public async Task<VectorQueryResult<TKey>> SearchAsync(VectorQueryOptions options, CancellationToken ct = default)
    {
        using var _ = WeaviateTelemetry.Activity.StartActivity("vector.search");
    await EnsureSchemaAsync(ct);
        var topK = options.TopK ?? _options.DefaultTopK;
        if (topK > _options.MaxTopK) topK = _options.MaxTopK;

        // Weaviate GraphQL nearVector query
        var gql = new
        {
            query = $"query {{ Get {{ {ClassName} (nearVector: {{ vector: [{string.Join(",", options.Query.Select(f => f.ToString(System.Globalization.CultureInfo.InvariantCulture)))}] }}, limit: {topK}) {{ _additional {{ id distance }} }} }} }}"
        };
        var req = JsonContent.Create(gql);
        var resp = await _http.PostAsync("/v1/graphql", req, ct);
        if (!resp.IsSuccessStatusCode)
        {
            var body = await resp.Content.ReadAsStringAsync(ct);
            throw new InvalidOperationException($"Weaviate search failed: {(int)resp.StatusCode} {resp.ReasonPhrase} {body}");
        }
        var json = await resp.Content.ReadAsStringAsync(ct);
        var matches = ParseGraphQlIds(json);
        return new VectorQueryResult<TKey>(matches, ContinuationToken: null);
    }

    private static IReadOnlyList<VectorMatch<TKey>> ParseGraphQlIds(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var list = new List<VectorMatch<TKey>>();
        var data = doc.RootElement.GetProperty("data").GetProperty("Get");
        foreach (var prop in data.EnumerateObject())
        {
            foreach (var item in prop.Value.EnumerateArray())
            {
                var add = item.GetProperty("_additional");
                var idStr = add.GetProperty("id").GetString();
                var distance = add.TryGetProperty("distance", out var distEl) ? distEl.GetDouble() : 0.0;
                if (idStr is null) continue;
                TKey id = (TKey)Convert.ChangeType(idStr, typeof(TKey));
                // Weaviate distance: smaller is closer for cosine/l2; map to Score as inverse
                var score = 1.0 - distance;
                list.Add(new VectorMatch<TKey>(id, score, null));
            }
        }
        return list;
    }
}
