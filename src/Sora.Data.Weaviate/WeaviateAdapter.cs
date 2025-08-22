using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;
using Sora.Data.Abstractions;
using Sora.Data.Abstractions.Instructions;
using Sora.Data.Vector.Abstractions;
using System.Net.Http.Json;
using System.Text.Json;
using System.Security.Cryptography;
using System.Net;

namespace Sora.Data.Weaviate;

[Sora.Data.Abstractions.ProviderPriority(10)]
public sealed class WeaviateVectorAdapterFactory : IVectorAdapterFactory
{
    public bool CanHandle(string provider) => string.Equals(provider, "weaviate", StringComparison.OrdinalIgnoreCase);

    public IVectorSearchRepository<TEntity, TKey> Create<TEntity, TKey>(IServiceProvider sp)
        where TEntity : class, IEntity<TKey>
        where TKey : notnull
    {
        var httpFactory = (IHttpClientFactory?)sp.GetService(typeof(IHttpClientFactory))
            ?? throw new InvalidOperationException("IHttpClientFactory not registered; call services.AddHttpClient().");
        var options = (IOptions<WeaviateOptions>?)sp.GetService(typeof(IOptions<WeaviateOptions>))
            ?? throw new InvalidOperationException("WeaviateOptions not configured; bind Sora:Data:Weaviate.");
        return new WeaviateVectorRepository<TEntity, TKey>(httpFactory, options, sp);
    }
}

internal sealed class WeaviateVectorRepository<TEntity, TKey> : IVectorSearchRepository<TEntity, TKey>, IVectorCapabilities, IInstructionExecutor<TEntity>
    where TEntity : class, IEntity<TKey>
    where TKey : notnull
{
    private readonly HttpClient _http;
    private readonly WeaviateOptions _options;
    private readonly IServiceProvider _sp;
    private readonly Microsoft.Extensions.Logging.ILogger<WeaviateVectorRepository<TEntity, TKey>>? _logger;
    private volatile bool _schemaEnsured;

    public VectorCapabilities Capabilities => VectorCapabilities.Knn | VectorCapabilities.Filters | VectorCapabilities.BulkUpsert | VectorCapabilities.BulkDelete;

    public WeaviateVectorRepository(IHttpClientFactory httpFactory, IOptions<WeaviateOptions> options, IServiceProvider sp)
    {
        _http = httpFactory.CreateClient("weaviate");
        _options = options.Value;
        _sp = sp;
        _logger = (Microsoft.Extensions.Logging.ILogger<WeaviateVectorRepository<TEntity, TKey>>?)sp.GetService(typeof(Microsoft.Extensions.Logging.ILogger<WeaviateVectorRepository<TEntity, TKey>>));
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
        _logger?.LogDebug("Weaviate: ensure schema base={Base} class={Class}", _http.BaseAddress, cls);
        // Probe class
        var probe = await _http.GetAsync($"/v1/schema/{Uri.EscapeDataString(cls)}", ct);
        _logger?.LogDebug("Weaviate: GET /v1/schema/{Class} -> {Status}", cls, (int)probe.StatusCode);
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
            // Minimal schema: store original document id for reverse mapping
            properties = new object[]
            {
                new { name = "docId", dataType = new[] { "text" } }
            }
        };
        _logger?.LogDebug("Weaviate: POST /v1/schema/classes for class {Class}", cls);
        var create = await _http.PostAsJsonAsync("/v1/schema/classes", body, ct);
        if (!create.IsSuccessStatusCode)
        {
            // Fallback for older Weaviate versions that require POST /v1/schema
            if (create.StatusCode == System.Net.HttpStatusCode.MethodNotAllowed)
            {
                _logger?.LogDebug("Weaviate: POST /v1/schema/classes returned 405; retrying legacy endpoint /v1/schema for class {Class}", cls);
                var legacy = await _http.PostAsJsonAsync("/v1/schema", body, ct);
                if (!legacy.IsSuccessStatusCode)
                {
                    var ltxt = await legacy.Content.ReadAsStringAsync(ct);
                    _logger?.LogDebug("Weaviate: legacy schema POST failed ({Status}) {Body}", (int)legacy.StatusCode, ltxt);
                    throw new InvalidOperationException($"Weaviate ensure schema failed (legacy): {(int)legacy.StatusCode} {legacy.ReasonPhrase} {ltxt}");
                }
            }
            else
            {
                var txt = await create.Content.ReadAsStringAsync(ct);
                _logger?.LogDebug("Weaviate: schema POST failed ({Status}) {Body}", (int)create.StatusCode, txt);
                throw new InvalidOperationException($"Weaviate ensure schema failed: {(int)create.StatusCode} {create.ReasonPhrase} {txt}");
            }
        }
        _schemaEnsured = true;
    }

    // Upsert single vector using /v1/objects
    public async Task UpsertAsync(TKey id, float[] embedding, object? metadata = null, CancellationToken ct = default)
    {
        using var _ = WeaviateTelemetry.Activity.StartActivity("vector.upsert");
        await EnsureSchemaAsync(ct);
        ValidateEmbedding(embedding);
        // Weaviate requires UUID ids; derive a deterministic UUID from the entity id (namespaced by class) for stable mapping
        var uuid = DeterministicGuidFromString(ClassName, id!.ToString()!);
        // Persist minimal properties including original doc id for reverse lookup
        var obj = new Dictionary<string, object?>
        {
            ["class"] = ClassName,
            ["id"] = uuid.ToString(),
            ["properties"] = new { docId = id!.ToString() },
            ["vector"] = embedding,
        };
        var putUrl = $"/v1/objects/{Uri.EscapeDataString(uuid.ToString())}";
        _logger?.LogDebug("Weaviate: PUT {Url} class={Class} id={Id} uuid={Uuid} vecDim={Dim}", putUrl, ClassName, id, uuid, embedding.Length);
        var resp = await _http.PutAsJsonAsync(putUrl, obj, ct);
        if (!resp.IsSuccessStatusCode)
        {
            var body = await resp.Content.ReadAsStringAsync(ct);
            // Some deployments return 404/405 for create-via-PUT; some buggy builds return 500 "no object with id ..."
            var missingOnPut = resp.StatusCode == HttpStatusCode.NotFound
                || resp.StatusCode == HttpStatusCode.MethodNotAllowed
                || (resp.StatusCode == HttpStatusCode.InternalServerError && body.Contains("no object with id", StringComparison.OrdinalIgnoreCase));
            if (missingOnPut)
            {
                _logger?.LogDebug("Weaviate: PUT failed ({Status}); retrying POST /v1/objects", (int)resp.StatusCode);
                var post = await _http.PostAsJsonAsync("/v1/objects", obj, ct);
                if (!post.IsSuccessStatusCode)
                {
                    var ptxt = await post.Content.ReadAsStringAsync(ct);
                    // If POST says already exists, attempt a final PUT update (race or concurrent create)
                    if ((int)post.StatusCode == 422 && ptxt.Contains("already exists", StringComparison.OrdinalIgnoreCase))
                    {
                        _logger?.LogDebug("Weaviate: POST reported existing object; retrying final PUT to update {Url}", putUrl);
                        var put2 = await _http.PutAsJsonAsync(putUrl, obj, ct);
                        if (!put2.IsSuccessStatusCode)
                        {
                            var p2txt = await put2.Content.ReadAsStringAsync(ct);
                            _logger?.LogDebug("Weaviate: upsert (final PUT) failed ({Status}) {Body}", (int)put2.StatusCode, p2txt);
                            throw new InvalidOperationException($"Weaviate upsert failed: {(int)put2.StatusCode} {put2.ReasonPhrase} {p2txt}");
                        }
                        return;
                    }
                    _logger?.LogDebug("Weaviate: upsert (POST fallback) failed ({Status}) {Body}", (int)post.StatusCode, ptxt);
                    throw new InvalidOperationException($"Weaviate upsert failed: {(int)post.StatusCode} {post.ReasonPhrase} {ptxt}");
                }
                return;
            }
            // If a POST was previously created and PUT still reports an error, surface the body for triage
            _logger?.LogDebug("Weaviate: upsert (PUT) failed ({Status}) {Body}", (int)resp.StatusCode, body);
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
        var uuid = DeterministicGuidFromString(ClassName, id!.ToString()!);
        var resp = await _http.DeleteAsync($"/v1/objects/{Uri.EscapeDataString(uuid.ToString())}", ct);
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
        ValidateEmbedding(options.Query);
        var topK = options.TopK ?? _options.DefaultTopK;
        if (topK > _options.MaxTopK) topK = _options.MaxTopK;

        // Build optional filters using shared AST + dedicated translator
        string whereClause = WeaviateFilterTranslator.TranslateWhereClause(options.Filter);
        var nearVector = $"nearVector: {{ vector: [{string.Join(",", options.Query.Select(f => f.ToString(System.Globalization.CultureInfo.InvariantCulture)))}] }}";
        var args = string.IsNullOrEmpty(whereClause)
            ? $"({nearVector}, limit: {topK})"
            : $"({nearVector}, limit: {topK}, where: {whereClause})";
        var gql = new
        {
            // Request docId alongside _additional so we can map back to original ids
            query = $"query {{ Get {{ {ClassName} {args} {{ docId _additional {{ id distance }} }} }} }}"
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

    // Translator helpers moved to WeaviateFilterTranslator

    private void ValidateEmbedding(float[] embedding)
    {
        if (_options.Dimension > 0 && embedding.Length != _options.Dimension)
            throw new ArgumentException($"Embedding dimension {embedding.Length} does not match configured {_options.Dimension}.");
    }

    // Instruction execution: ensureCreated, stats, clear (destructive guard)
    public async Task<TResult> ExecuteAsync<TResult>(Instruction instruction, CancellationToken ct = default)
    {
        switch (instruction.Name)
        {
            case Sora.Data.Vector.VectorInstructions.IndexEnsureCreated:
                await EnsureSchemaAsync(ct);
                return (TResult)(object)true;
            case Sora.Data.Vector.VectorInstructions.IndexStats:
                {
                    await EnsureSchemaAsync(ct);
                    var cls = ClassName;
                    var gql = new { query = $"query {{ Aggregate {{ {cls} {{ meta {{ count }} }} }} }}" };
                    var req = JsonContent.Create(gql);
                    var resp = await _http.PostAsync("/v1/graphql", req, ct);
                    if (!resp.IsSuccessStatusCode)
                    {
                        var body = await resp.Content.ReadAsStringAsync(ct);
                        throw new InvalidOperationException($"Weaviate stats failed: {(int)resp.StatusCode} {resp.ReasonPhrase} {body}");
                    }
                    var txt = await resp.Content.ReadAsStringAsync(ct);
                    var count = ParseAggregateCount(txt);
                    if (typeof(TResult) == typeof(int))
                        return (TResult)(object)count;
                    object result = new { count };
                    return (TResult)result;
                }
            case Sora.Data.Vector.VectorInstructions.IndexClear:
                {
                    var allow = instruction.Options != null && instruction.Options.TryGetValue("AllowDestructive", out var v) && v is bool b && b;
                    if (!allow) throw new NotSupportedException("Destructive clear requires Options.AllowDestructive=true.");
                    await EnsureSchemaAsync(ct);
                    var body = new
                    {
                        @class = ClassName,
                        where = new { @operator = "IsNotNull", path = new[] { "id" } }
                    };
                    var req = JsonContent.Create(body);
                    var resp = await _http.PostAsync("/v1/batch/objects/delete", req, ct);
                    if (!resp.IsSuccessStatusCode)
                    {
                        var txt = await resp.Content.ReadAsStringAsync(ct);
                        throw new InvalidOperationException($"Weaviate clear failed: {(int)resp.StatusCode} {resp.ReasonPhrase} {txt}");
                    }
                    // Return approximate deleted count if provided; else 0
                    object ok = 0;
                    return (TResult)ok;
                }
            default:
                throw new NotSupportedException($"Instruction '{instruction.Name}' not supported by Weaviate vector adapter.");
        }
    }

    private static int ParseAggregateCount(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var data = doc.RootElement.GetProperty("data").GetProperty("Aggregate");
        foreach (var cls in data.EnumerateObject())
        {
            var arr = cls.Value.EnumerateArray();
            if (arr.MoveNext())
            {
                var meta = arr.Current.GetProperty("meta");
                if (meta.TryGetProperty("count", out var countEl))
                    return countEl.GetInt32();
            }
        }
        return 0;
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
                var idStr = item.TryGetProperty("docId", out var docIdEl) && docIdEl.ValueKind == JsonValueKind.String
                    ? docIdEl.GetString()
                    : add.GetProperty("id").GetString();
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

    // Deterministic UUID (v5-like) from class namespace + id using SHA-1
    private static Guid DeterministicGuidFromString(string @namespace, string input)
    {
        using var sha1 = System.Security.Cryptography.SHA1.Create();
        var nsBytes = System.Text.Encoding.UTF8.GetBytes(@namespace + ":");
        var nameBytes = System.Text.Encoding.UTF8.GetBytes(input);
        var all = new byte[nsBytes.Length + nameBytes.Length];
        Buffer.BlockCopy(nsBytes, 0, all, 0, nsBytes.Length);
        Buffer.BlockCopy(nameBytes, 0, all, nsBytes.Length, nameBytes.Length);
        var hash = sha1.ComputeHash(all);
        // take first 16 bytes
        Array.Resize(ref hash, 16);
        // Set version to 5 (name-based, SHA-1) and variant RFC 4122
        hash[6] = (byte)((hash[6] & 0x0F) | (5 << 4));
        hash[8] = (byte)((hash[8] & 0x3F) | 0x80);
        return new Guid(hash);
    }
}
