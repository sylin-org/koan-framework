using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Instructions;
using Koan.Data.Abstractions.Naming;
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

    public VectorCapabilities Capabilities => VectorCapabilities.Knn | VectorCapabilities.Filters | VectorCapabilities.BulkUpsert | VectorCapabilities.BulkDelete | VectorCapabilities.Hybrid | VectorCapabilities.DynamicCollections;

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

    private string ClassName
    {
        get
        {
            // DATA-0086: Use unified naming provider system via StorageNameRegistry
            // Automatically handles partitions via EntityContext and adapter factory's INamingProvider
            return Koan.Data.Core.Configuration.StorageNameRegistry.GetOrCompute<TEntity, TKey>(_sp);
        }
    }

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
        var properties = new List<object>
        {
            new { name = "docId", dataType = new[] { "text" } },
            new
            {
                name = "searchText",
                dataType = new[] { "text" },
                indexSearchable = true,      // Enable BM25 indexing
                tokenization = "word"
            }
        };

        // Add filterable properties for common recommendation system use cases
        // These enable filter push-down at the vector database layer
        properties.Add(new { name = "genres", dataType = new[] { "text[]" } });
        properties.Add(new { name = "tags", dataType = new[] { "text[]" } });
        properties.Add(new { name = "rating", dataType = new[] { "number" } });
        properties.Add(new { name = "year", dataType = new[] { "int" } });
        properties.Add(new { name = "episodes", dataType = new[] { "int" } });
        properties.Add(new { name = "mediaTypeId", dataType = new[] { "text" } });
        properties.Add(new { name = "popularity", dataType = new[] { "number" } });

        var body = new
        {
            @class = cls,
            vectorizer = "none",
            vectorIndexConfig = new { distance = _options.Metric },
            properties = properties.ToArray()
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

        // Build properties including searchText from metadata if available
        var properties = new Dictionary<string, object?> { ["docId"] = id!.ToString() };
        if (metadata is IReadOnlyDictionary<string, object> metaDict && metaDict.TryGetValue("searchText", out var searchText))
        {
            properties["searchText"] = searchText;
        }

        // Persist minimal properties including original doc id for reverse lookup
        // POST object includes class in payload
        var postObj = new Dictionary<string, object?>
        {
            ["class"] = ClassName,
            ["id"] = uuid.ToString(),
            ["properties"] = properties,
            ["vector"] = embedding,
        };
        // PUT object includes class (some Weaviate versions require it in both URL and payload)
        var putObj = new Dictionary<string, object?>
        {
            ["class"] = ClassName,
            ["id"] = uuid.ToString(),
            ["properties"] = properties,
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

    public async Task<float[]?> GetEmbeddingAsync(TKey id, CancellationToken ct = default)
    {
        using var _ = WeaviateTelemetry.Activity.StartActivity("vector.getEmbedding");

        await EnsureSchemaAsync(ct);

        var uuid = DeterministicGuidFromString(ClassName, id!.ToString()!);

        // Query for the specific object with vector
        var gql = new
        {
            query = $@"query {{
                Get {{
                    {ClassName}(where: {{ path: [""docId""], operator: Equal, valueText: ""{id!.ToString()}"" }}, limit: 1) {{
                        _additional {{
                            vector
                        }}
                    }}
                }}
            }}"
        };

        var req = new StringContent(JsonConvert.SerializeObject(gql), System.Text.Encoding.UTF8, "application/json");
        var resp = await _http.PostAsync("/v1/graphql", req, ct);

        if (!resp.IsSuccessStatusCode)
        {
            var body = await resp.Content.ReadAsStringAsync(ct);
            throw new InvalidOperationException($"Weaviate GetEmbedding failed: {(int)resp.StatusCode} {resp.ReasonPhrase} {body}");
        }

        var json = await resp.Content.ReadAsStringAsync(ct);
        var parsed = JObject.Parse(json);
        var objects = parsed["data"]?["Get"]?[ClassName] as JArray;

        if (objects == null || objects.Count == 0)
        {
            return null; // No vector found for this ID
        }

        var additional = objects[0]?["_additional"] as JObject;
        var vectorArray = additional?["vector"] as JArray;

        if (vectorArray != null)
        {
            return vectorArray.Select(v => (float)(double)v).ToArray();
        }

        return null;
    }

    public async Task<Dictionary<TKey, float[]>> GetEmbeddingsAsync(IEnumerable<TKey> ids, CancellationToken ct = default)
    {
        using var _ = WeaviateTelemetry.Activity.StartActivity("vector.getEmbeddings");

        await EnsureSchemaAsync(ct);

        var result = new Dictionary<TKey, float[]>();
        var idsList = ids.ToList();

        if (idsList.Count == 0)
        {
            return result;
        }

        // Build WHERE clause for multiple IDs using OR operator
        var idConditions = idsList
            .Select(id => $@"{{ path: [""docId""], operator: Equal, valueText: ""{id!.ToString()}"" }}")
            .ToList();

        var whereClause = idConditions.Count == 1
            ? idConditions[0]
            : $@"{{ operator: Or, operands: [{string.Join(", ", idConditions)}] }}";

        var gql = new
        {
            query = $@"query {{
                Get {{
                    {ClassName}(where: {whereClause}, limit: {idsList.Count}) {{
                        docId
                        _additional {{
                            vector
                        }}
                    }}
                }}
            }}"
        };

        var req = new StringContent(JsonConvert.SerializeObject(gql), System.Text.Encoding.UTF8, "application/json");
        var resp = await _http.PostAsync("/v1/graphql", req, ct);

        if (!resp.IsSuccessStatusCode)
        {
            var body = await resp.Content.ReadAsStringAsync(ct);
            throw new InvalidOperationException($"Weaviate GetEmbeddings failed: {(int)resp.StatusCode} {resp.ReasonPhrase} {body}");
        }

        var json = await resp.Content.ReadAsStringAsync(ct);
        var parsed = JObject.Parse(json);
        var objects = parsed["data"]?["Get"]?[ClassName] as JArray;

        if (objects == null)
        {
            return result;
        }

        foreach (var obj in objects.OfType<JObject>())
        {
            var docId = obj["docId"]?.Value<string>();
            var additional = obj["_additional"] as JObject;
            var vectorArray = additional?["vector"] as JArray;

            if (docId != null && vectorArray != null)
            {
                var embedding = vectorArray.Select(v => (float)(double)v).ToArray();
                TKey id = (TKey)Convert.ChangeType(docId, typeof(TKey));
                result[id] = embedding;
            }
        }

        return result;
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

        // Determine search mode: hybrid or pure vector
        string searchClause;
        if (!string.IsNullOrWhiteSpace(options.SearchText))
        {
            // Hybrid mode: vector + BM25
            var alpha = options.Alpha ?? 0.5;
            var escapedText = options.SearchText.Replace("\"", "\\\"");
            var vectorStr = string.Join(",", options.Query.Select(f => f.ToString(System.Globalization.CultureInfo.InvariantCulture)));
            searchClause = $@"hybrid: {{
                query: ""{escapedText}"",
                vector: [{vectorStr}],
                alpha: {alpha.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)}
            }}";
        }
        else
        {
            // Pure vector mode
            var vectorStr = string.Join(",", options.Query.Select(f => f.ToString(System.Globalization.CultureInfo.InvariantCulture)));
            searchClause = $"nearVector: {{ vector: [{vectorStr}] }}";
        }

        var args = string.IsNullOrEmpty(whereClause)
            ? $"({searchClause}, limit: {topK})"
            : $"({searchClause}, limit: {topK}, where: {whereClause})";
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

    public async Task FlushAsync(CancellationToken ct = default)
    {
        using var _ = WeaviateTelemetry.Activity.StartActivity("vector.flush");
        var cls = ClassName;

        // Flush by deleting and recreating the schema (standard Weaviate pattern)
        _logger?.LogInformation("Weaviate: flushing class {Class} (delete + recreate schema)", cls);

        // Delete the class schema (removes all objects)
        var deleteResp = await _http.DeleteAsync($"/v1/schema/{Uri.EscapeDataString(cls)}", ct);

        if (!deleteResp.IsSuccessStatusCode && deleteResp.StatusCode != HttpStatusCode.NotFound)
        {
            var txt = await deleteResp.Content.ReadAsStringAsync(ct);
            _logger?.LogError("Weaviate schema delete failed ({Status}): {Body}", (int)deleteResp.StatusCode, txt);
            throw new InvalidOperationException($"Weaviate flush failed: {(int)deleteResp.StatusCode} {deleteResp.ReasonPhrase} {txt}");
        }

        // Reset the schema ensured flag so EnsureSchemaAsync will recreate
        _schemaEnsured = false;

        // Recreate the schema
        await EnsureSchemaAsync(ct);

        _logger?.LogInformation("Weaviate: flushed all vectors for class {Class}", cls);
    }

    public async IAsyncEnumerable<VectorExportBatch<TKey>> ExportAllAsync(
        int? batchSize = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        using var _ = WeaviateTelemetry.Activity.StartActivity("vector.export");

        await EnsureSchemaAsync(ct);

        var limit = batchSize ?? 100; // Weaviate cursor default
        var totalExported = 0;
        string? afterCursor = null; // Cursor-based pagination (no 10k limit)

        while (true)
        {
            // GraphQL query to fetch objects with vectors using cursor pagination
            // Note: Weaviate limits offset pagination to 10,000 objects
            var afterClause = afterCursor != null ? $", after: \"{afterCursor}\"" : "";
            var gql = new
            {
                query = $@"query {{
                    Get {{
                        {ClassName}(limit: {limit}{afterClause}) {{
                            docId
                            _additional {{
                                id
                                vector
                            }}
                        }}
                    }}
                }}"
            };

            var req = new StringContent(JsonConvert.SerializeObject(gql), System.Text.Encoding.UTF8, "application/json");
            var resp = await _http.PostAsync("/v1/graphql", req, ct);

            if (!resp.IsSuccessStatusCode)
            {
                var body = await resp.Content.ReadAsStringAsync(ct);
                throw new InvalidOperationException($"Weaviate export failed: {(int)resp.StatusCode} {resp.ReasonPhrase} {body}");
            }

            var json = await resp.Content.ReadAsStringAsync(ct);
            var parsed = JObject.Parse(json);
            var objects = parsed["data"]?["Get"]?[ClassName] as JArray;

            if (objects == null || objects.Count == 0)
            {
                break;
            }

            string? lastUuid = null;
            foreach (var obj in objects.OfType<JObject>())
            {
                var docId = obj["docId"]?.Value<string>();
                var additional = obj["_additional"] as JObject;
                var vectorArray = additional?["vector"] as JArray;
                var uuid = additional?["id"]?.Value<string>();

                if (docId != null && vectorArray != null)
                {
                    var embedding = vectorArray.Select(v => (float)(double)v).ToArray();

                    // Build metadata from the object (excluding _additional and docId)
                    var metadata = new JObject(obj);
                    metadata.Remove("_additional");
                    metadata.Remove("docId");

                    // Convert ID using same approach as ParseGraphQlIds
                    TKey id = (TKey)Convert.ChangeType(docId, typeof(TKey));

                    yield return new VectorExportBatch<TKey>(id, embedding, metadata.Count > 0 ? metadata.ToObject<object>() : null);
                    totalExported++;

                    if (uuid != null)
                    {
                        lastUuid = uuid;
                    }
                }
            }

            // Check if we've reached the end
            if (objects.Count < limit)
            {
                break;
            }

            // Update cursor for next page
            afterCursor = lastUuid;

            if (totalExported % 1000 == 0)
            {
                _logger?.LogDebug("Weaviate export progress: {Count} vectors exported...", totalExported);
            }
        }

        _logger?.LogInformation("Weaviate vector export completed: {Count} vectors exported", totalExported);
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
                    // Backward compatibility: require AllowDestructive option for instruction-based clear
                    var allow = instruction.Options != null && instruction.Options.TryGetValue("AllowDestructive", out var v) && v is bool b && b;
                    if (!allow) throw new NotSupportedException("Destructive clear requires Options.AllowDestructive=true.");

                    // Delegate to FlushAsync which implements the actual clear logic
                    await FlushAsync(ct);
                    return (TResult)(object)true;
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
