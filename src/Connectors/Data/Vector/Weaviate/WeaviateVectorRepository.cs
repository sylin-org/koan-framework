using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Instructions;
using Koan.Data.Vector.Abstractions;
using System.Net;
using Newtonsoft.Json;
using System.Security.Cryptography;
using Newtonsoft.Json.Linq;

namespace Koan.Data.Vector.Connector.Weaviate;

internal sealed class WeaviateVectorRepository<TEntity, TKey> : IVectorSearchRepository<TEntity, TKey>, IVectorCapabilities, IInstructionExecutor<TEntity>
    where TEntity : class, IEntity<TKey>
    where TKey : notnull
{
    private readonly HttpClient _http;
    private readonly WeaviateOptions _options;
    private readonly IServiceProvider _sp;
    private readonly ILogger<WeaviateVectorRepository<TEntity, TKey>>? _logger;
    private volatile bool _schemaEnsured;
    private volatile int _discoveredDimension = -1; // -1 means not discovered yet

    public VectorCapabilities Capabilities => VectorCapabilities.Knn | VectorCapabilities.Filters | VectorCapabilities.BulkUpsert | VectorCapabilities.BulkDelete;

    public WeaviateVectorRepository(IHttpClientFactory httpFactory, IOptions<WeaviateOptions> options, IServiceProvider sp)
    {
        _http = httpFactory.CreateClient("weaviate");
        _options = options.Value;
        _sp = sp;
        _logger = (ILogger<WeaviateVectorRepository<TEntity, TKey>>?)sp.GetService(typeof(ILogger<WeaviateVectorRepository<TEntity, TKey>>));
        _http.BaseAddress = new Uri(_options.Endpoint);
        if (_http.Timeout == default)
            _http.Timeout = TimeSpan.FromSeconds(Math.Max(1, _options.DefaultTimeoutSeconds));
    }

    private string ClassName => Core.Configuration.StorageNameRegistry.GetOrCompute<TEntity, TKey>(_sp);

    private async Task EnsureSchemaAsync(CancellationToken ct)
    {
        if (_schemaEnsured) return;

        // For backward compatibility, use configured dimension if no dimension discovered yet
        var effectiveDimension = _discoveredDimension > 0 ? _discoveredDimension : _options.Dimension;
        await EnsureSchemaAsync(effectiveDimension, ct);
    }

    private async Task EnsureSchemaAsync(int dimension, CancellationToken ct)
    {
        if (_schemaEnsured) return;
        using var _ = WeaviateTelemetry.Activity.StartActivity("vector.index.ensureCreated");
        var cls = ClassName;
        _logger?.LogDebug("Weaviate: ensure schema base={Base} class={Class} (resolved from {EntityType}) dimension={Dimension}", _http.BaseAddress, cls, typeof(TEntity).Name, dimension);
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
        var bodyJson = JsonConvert.SerializeObject(body, Formatting.Indented);
        _logger?.LogDebug("Weaviate: POST /v1/schema/classes for class {Class}. Schema body: {Body}", cls, bodyJson);
    var create = await _http.PostAsync("/v1/schema/classes", new StringContent(bodyJson, System.Text.Encoding.UTF8, "application/json"), ct);
        var createResponse = await create.Content.ReadAsStringAsync(ct);
        _logger?.LogDebug("Weaviate: schema creation response ({Status}): {Response}", (int)create.StatusCode, createResponse);
        if (!create.IsSuccessStatusCode)
        {
            // Fallback for older Weaviate versions that require POST /v1/schema
            if (create.StatusCode == HttpStatusCode.MethodNotAllowed)
            {
                _logger?.LogDebug("Weaviate: POST /v1/schema/classes returned 405; retrying legacy endpoint /v1/schema for class {Class}", cls);
                var legacy = await _http.PostAsync("/v1/schema", new StringContent(JsonConvert.SerializeObject(body), System.Text.Encoding.UTF8, "application/json"), ct);
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

        // Wait for schema to be fully available
        await WaitForSchemaReadyAsync(cls, ct);
        _schemaEnsured = true;
    }

    private async Task WaitForSchemaReadyAsync(string className, CancellationToken ct)
    {
        const int maxAttempts = 10;
        const int delayMs = 50;

        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                // Check if schema is queryable (more reliable than just checking existence)
                var checkUrl = $"/v1/schema/{Uri.EscapeDataString(className)}";
                var response = await _http.GetAsync(checkUrl, ct);

                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync(ct);
                    _logger?.LogDebug("Weaviate: readiness check response for {Class}: {Content}", className, content);
                    // Ensure response contains actual schema data
                    if (!string.IsNullOrWhiteSpace(content) && content.Contains(className))
                    {
                        _logger?.LogDebug("Weaviate: schema {Class} confirmed ready after {Attempts} attempts", className, attempt);
                        return;
                    }
                }

                _logger?.LogDebug("Weaviate: schema {Class} not ready, attempt {Attempt}/{MaxAttempts}", className, attempt, maxAttempts);

                if (attempt < maxAttempts)
                {
                    await Task.Delay(delayMs, ct);
                }
            }
            catch (Exception ex)
            {
                _logger?.LogDebug(ex, "Weaviate: schema readiness check failed, attempt {Attempt}/{MaxAttempts}", attempt, maxAttempts);
                if (attempt < maxAttempts)
                {
                    await Task.Delay(delayMs, ct);
                }
            }
        }

        _logger?.LogWarning("Weaviate: schema {Class} readiness check timed out after {MaxAttempts} attempts", className, maxAttempts);
        // Don't throw - let the subsequent operations handle any remaining timing issues
    }

    // Upsert single vector using /v1/objects
    public async Task UpsertAsync(TKey id, float[] embedding, object? metadata = null, CancellationToken ct = default)
    {
        using var _ = WeaviateTelemetry.Activity.StartActivity("vector.upsert");

        // Discover dimension from first embedding if not already discovered
        if (_discoveredDimension == -1 && embedding.Length > 0)
        {
            _discoveredDimension = embedding.Length;
            _logger?.LogInformation("Weaviate: discovered embedding dimension {Dimension} from first vector for class {Class}", _discoveredDimension, ClassName);
        }
        else if (_discoveredDimension > 0 && embedding.Length > 0 && _discoveredDimension != embedding.Length)
        {
            _logger?.LogWarning("Weaviate: dimension conflict detected! Previously discovered {PreviousDimension}, current embedding {CurrentDimension} for class {Class}. Updating discovery.", _discoveredDimension, embedding.Length, ClassName);
            _discoveredDimension = embedding.Length;
            // Reset schema to be recreated with new dimensions
            _schemaEnsured = false;
        }

        await EnsureSchemaAsync(ct);
        ValidateEmbedding(embedding);
        // Weaviate requires UUID ids; derive a deterministic UUID from the entity id (namespaced by class) for stable mapping
        var uuid = DeterministicGuidFromString(ClassName, id!.ToString()!);
        // Persist minimal properties including original doc id for reverse lookup
        // POST object includes class in payload
        var postObj = new Dictionary<string, object?>
        {
            ["class"] = ClassName,
            ["id"] = uuid.ToString(),
            ["properties"] = new { docId = id!.ToString() },
            ["vector"] = embedding,
        };
        // PUT object includes class (some Weaviate versions require it in both URL and payload)
        var putObj = new Dictionary<string, object?>
        {
            ["class"] = ClassName,
            ["id"] = uuid.ToString(),
            ["properties"] = new { docId = id!.ToString() },
            ["vector"] = embedding,
        };
        var putUrl = $"/v1/objects/{Uri.EscapeDataString(ClassName)}/{Uri.EscapeDataString(uuid.ToString())}";
        var postObjJson = JsonConvert.SerializeObject(postObj);
        var putObjJson = JsonConvert.SerializeObject(putObj);
        // For proper upsert: try POST first (create), then PUT if object already exists
        var postResp = await _http.PostAsync("/v1/objects", new StringContent(postObjJson, System.Text.Encoding.UTF8, "application/json"), ct);
        if (postResp.IsSuccessStatusCode)
        {
            return;
        }

        var postBody = await postResp.Content.ReadAsStringAsync(ct);

        // Check for schema timing issue on POST
        if (postBody.Contains("non-existing index", StringComparison.OrdinalIgnoreCase))
        {
            _logger?.LogDebug("Weaviate: schema not ready, retrying POST after schema wait");
            await WaitForSchemaReadyAsync(ClassName, ct);
            // Retry the POST once more
            var retryPost = await _http.PostAsync("/v1/objects", new StringContent(postObjJson, System.Text.Encoding.UTF8, "application/json"), ct);
            if (retryPost.IsSuccessStatusCode)
            {
                return;
            }
            var retryPostBody = await retryPost.Content.ReadAsStringAsync(ct);
            _logger?.LogWarning("Weaviate: POST retry failed ({Status}) {Body}", (int)retryPost.StatusCode, retryPostBody);
            // Continue to PUT attempt below
            postBody = retryPostBody;
        }

        // If POST failed because object already exists, try PUT for update
        if (postResp.StatusCode == HttpStatusCode.UnprocessableEntity ||
            postBody.Contains("already exists", StringComparison.OrdinalIgnoreCase))
        {
            var putResp = await _http.PutAsync(putUrl, new StringContent(putObjJson, System.Text.Encoding.UTF8, "application/json"), ct);
            if (putResp.IsSuccessStatusCode)
            {
                return;
            }

            var putBody = await putResp.Content.ReadAsStringAsync(ct);
            _logger?.LogError("Weaviate: PUT update failed ({Status}) {Body}", (int)putResp.StatusCode, putBody);
            throw new InvalidOperationException($"Weaviate upsert failed on both POST and PUT: POST={postResp.StatusCode} {postBody}, PUT={putResp.StatusCode} {putBody}");
        }

        // POST failed for other reasons
        _logger?.LogError("Weaviate: POST create failed ({Status}) {Body}", (int)postResp.StatusCode, postBody);
        throw new InvalidOperationException($"Weaviate upsert failed: {(int)postResp.StatusCode} {postResp.ReasonPhrase} {postBody}");
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

        // Discover dimension from query vector if not already discovered
        if (_discoveredDimension == -1 && options.Query.Length > 0)
        {
            _discoveredDimension = options.Query.Length;
            _logger?.LogDebug("Weaviate: discovered embedding dimension {Dimension} from query vector", _discoveredDimension);
        }

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
    var req = new StringContent(JsonConvert.SerializeObject(gql), System.Text.Encoding.UTF8, "application/json");
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
        // Use discovered dimension if available, otherwise fall back to configured dimension
        var expectedDimension = _discoveredDimension > 0 ? _discoveredDimension : _options.Dimension;

        if (expectedDimension > 0 && embedding.Length != expectedDimension)
        {
            var source = _discoveredDimension > 0 ? "discovered" : "configured";
            throw new ArgumentException($"Embedding dimension {embedding.Length} does not match {source} {expectedDimension}.");
        }
    }

    // Instruction execution: ensureCreated, stats, clear (destructive guard)
    public async Task<TResult> ExecuteAsync<TResult>(Instruction instruction, CancellationToken ct = default)
    {
        switch (instruction.Name)
        {
            case VectorInstructions.IndexEnsureCreated:
                await EnsureSchemaAsync(ct);
                return (TResult)(object)true;
            case VectorInstructions.IndexStats:
                {
                    await EnsureSchemaAsync(ct);
                    var cls = ClassName;
                    var gql = new { query = $"query {{ Aggregate {{ {cls} {{ meta {{ count }} }} }} }}" };
                    var req = new StringContent(JsonConvert.SerializeObject(gql), System.Text.Encoding.UTF8, "application/json");
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
            case VectorInstructions.IndexClear:
                {
                    var allow = instruction.Options != null && instruction.Options.TryGetValue("AllowDestructive", out var v) && v is bool b && b;
                    if (!allow) throw new NotSupportedException("Destructive clear requires Options.AllowDestructive=true.");
                    await EnsureSchemaAsync(ct);
                    var body = new
                    {
                        @class = ClassName,
                        where = new { @operator = "IsNotNull", path = new[] { "id" } }
                    };
                    var req = new StringContent(JsonConvert.SerializeObject(body), System.Text.Encoding.UTF8, "application/json");
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
        var root = JToken.Parse(json);
        var aggregate = root["data"]?["Aggregate"] as JObject;
        if (aggregate is null) return 0;
        foreach (var prop in aggregate.Properties())
        {
            var arr = prop.Value as JArray;
            var first = arr?.First as JObject;
            var meta = first?["meta"] as JObject;
            var count = meta?["count"]?.Value<int?>();
            if (count.HasValue) return count.Value;
        }
        return 0;
    }

    private static IReadOnlyList<VectorMatch<TKey>> ParseGraphQlIds(string json)
    {
        var root = JToken.Parse(json);
        var list = new List<VectorMatch<TKey>>();
        var get = root["data"]?["Get"] as JObject;
        if (get is null) return list;
        foreach (var prop in get.Properties())
        {
            if (prop.Value is not JArray arr) continue;
            foreach (var itemTok in arr)
            {
                var item = itemTok as JObject;
                if (item is null) continue;
                var add = item["_additional"] as JObject;
                var idStr = item["docId"]?.Value<string>() ?? add?["id"]?.Value<string>();
                var distance = add?["distance"]?.Value<double?>() ?? 0.0;
                if (idStr is null) continue;
                TKey id = (TKey)Convert.ChangeType(idStr, typeof(TKey));
                var score = 1.0 - distance;
                list.Add(new VectorMatch<TKey>(id, score, null));
            }
        }
        return list;
    }

    // Deterministic UUID (v5-like) from class namespace + id using SHA-1
    private static Guid DeterministicGuidFromString(string @namespace, string input)
    {
        using var sha1 = SHA1.Create();
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
