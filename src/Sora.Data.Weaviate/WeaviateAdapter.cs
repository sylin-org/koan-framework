using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using System.Net.Http;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sora.Data.Abstractions;
using Sora.Data.Abstractions.Instructions;

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
    ValidateEmbedding(embedding);
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
        ValidateEmbedding(options.Query);
        var topK = options.TopK ?? _options.DefaultTopK;
        if (topK > _options.MaxTopK) topK = _options.MaxTopK;

        // Build optional filters (simple equality map)
        string whereClause = BuildWhereClause(options.Filter);
        var nearVector = $"nearVector: {{ vector: [{string.Join(",", options.Query.Select(f => f.ToString(System.Globalization.CultureInfo.InvariantCulture)))}] }}";
        var args = string.IsNullOrEmpty(whereClause)
            ? $"({nearVector}, limit: {topK})"
            : $"({nearVector}, limit: {topK}, where: {whereClause})";
        var gql = new
        {
            query = $"query {{ Get {{ {ClassName} {args} {{ _additional {{ id distance }} }} }} }}"
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

    private string BuildWhereClause(object? filter)
    {
        if (filter is null) return string.Empty;
        JsonElement el;
        try { el = JsonSerializer.SerializeToElement(filter); }
        catch { return string.Empty; }
        var result = BuildWhereClauseFromJson(el);
        return result ?? string.Empty;
    }

    private string? BuildWhereClauseFromJson(JsonElement el)
    {
        if (el.ValueKind == JsonValueKind.Object)
        {
            if (el.TryGetProperty("operator", out var opEl))
            {
                var op = opEl.GetString() ?? string.Empty;
                if (string.Equals(op, "And", StringComparison.OrdinalIgnoreCase) || string.Equals(op, "Or", StringComparison.OrdinalIgnoreCase) || string.Equals(op, "Not", StringComparison.OrdinalIgnoreCase))
                {
                    if (el.TryGetProperty("operands", out var opsEl) && opsEl.ValueKind == JsonValueKind.Array)
                    {
                        var items = new List<string>();
                        foreach (var child in opsEl.EnumerateArray())
                        {
                            var c = BuildWhereClauseFromJson(child);
                            if (!string.IsNullOrEmpty(c)) items.Add(c!);
                        }
                        if (items.Count == 0) return null;
                        var oper = char.ToUpperInvariant(op[0]) + op.Substring(1).ToLowerInvariant();
                        return oper == "Not"
                            ? $"{{ operator: Not, operands: [ {items[0]} ] }}"
                            : $"{{ operator: {oper}, operands: [ {string.Join(",", items)} ] }}";
                    }
                    return null;
                }
                // leaf comparison
                var path = el.TryGetProperty("path", out var pathEl) ? pathEl : default;
                var value = el.TryGetProperty("value", out var valEl) ? valEl : default;
                var pathArr = PathToGraphQl(path);
                var operName = NormalizeOperator(op);
                var valueField = ValueFieldAndLiteral(value, out var literal);
                if (pathArr is null || valueField is null) return null;
                return $"{{ path: {pathArr}, operator: {operName}, {valueField}: {literal} }}";
            }
            // Equality map shorthand
            var kvs = new List<string>();
            foreach (var prop in el.EnumerateObject())
            {
                var pathArr = $"[\"{EscapeGraphQl(prop.Name)}\"]";
                var vf = ValueFieldAndLiteral(prop.Value, out var lit);
                if (vf is null) continue;
                kvs.Add($"{{ path: {pathArr}, operator: Equal, {vf}: {lit} }}");
            }
            if (kvs.Count == 0) return null;
            if (kvs.Count == 1) return kvs[0];
            return $"{{ operator: And, operands: [ {string.Join(",", kvs)} ] }}";
        }
        return null;
    }

    private static string? PathToGraphQl(JsonElement pathEl)
    {
        if (pathEl.ValueKind == JsonValueKind.String)
            return $"[\"{EscapeGraphQl(pathEl.GetString())}\"]";
        if (pathEl.ValueKind == JsonValueKind.Array)
        {
            var parts = pathEl.EnumerateArray().Select(p => $"\"{EscapeGraphQl(p.GetString())}\"");
            return $"[{string.Join(",", parts)}]";
        }
        return null;
    }

    private static string NormalizeOperator(string op)
    {
        return op switch
        {
            var s when s.Equals("eq", StringComparison.OrdinalIgnoreCase) || s.Equals("equal", StringComparison.OrdinalIgnoreCase) => "Equal",
            var s when s.Equals("ne", StringComparison.OrdinalIgnoreCase) || s.Equals("notequal", StringComparison.OrdinalIgnoreCase) => "NotEqual",
            var s when s.Equals("gt", StringComparison.OrdinalIgnoreCase) || s.Equals("greaterthan", StringComparison.OrdinalIgnoreCase) => "GreaterThan",
            var s when s.Equals("gte", StringComparison.OrdinalIgnoreCase) || s.Equals("ge", StringComparison.OrdinalIgnoreCase) || s.Equals("greaterthanequal", StringComparison.OrdinalIgnoreCase) => "GreaterThanEqual",
            var s when s.Equals("lt", StringComparison.OrdinalIgnoreCase) || s.Equals("lessthan", StringComparison.OrdinalIgnoreCase) => "LessThan",
            var s when s.Equals("lte", StringComparison.OrdinalIgnoreCase) || s.Equals("le", StringComparison.OrdinalIgnoreCase) || s.Equals("lessthanequal", StringComparison.OrdinalIgnoreCase) => "LessThanEqual",
            var s when s.Equals("like", StringComparison.OrdinalIgnoreCase) => "Like",
            var s when s.Equals("contains", StringComparison.OrdinalIgnoreCase) => "ContainsAny",
            _ => op
        };
    }

    private static string? ValueFieldAndLiteral(JsonElement value, out string literal)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.String:
                literal = $"\"{EscapeGraphQl(value.GetString())}\""; return "valueText";
            case JsonValueKind.True:
            case JsonValueKind.False:
                literal = value.GetBoolean() ? "true" : "false"; return "valueBoolean";
            case JsonValueKind.Number:
                if (value.TryGetInt64(out var i)) { literal = i.ToString(System.Globalization.CultureInfo.InvariantCulture); return "valueInt"; }
                if (value.TryGetDouble(out var d)) { literal = d.ToString(System.Globalization.CultureInfo.InvariantCulture); return "valueNumber"; }
                literal = "0"; return "valueNumber";
            case JsonValueKind.Null:
                literal = "null"; return "valueText";
            default:
                literal = "\"\""; return null;
        }
    }

    private static string EscapeGraphQl(string? value)
        => (value ?? string.Empty).Replace("\\", "\\\\").Replace("\"", "\\\"");

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
