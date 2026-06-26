using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
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

namespace Koan.Data.Vector.Connector.Qdrant;

/// <summary>
/// Qdrant vector adapter. Targets the REST API surface under <c>/collections/{name}/...</c>.
///
/// <para>
/// Qdrant constrains point ids to UUID or unsigned-64 only. For Koan's typical
/// <see cref="Entity{T}"/> default — string keys that are GUID v7 values — this is a no-op:
/// the string parses as a Guid and goes through verbatim. Arbitrary string keys (e.g. "v1",
/// "alpha-1") get projected via UUIDv5 from a fixed namespace; the original string is always
/// preserved in <c>payload.&lt;IdField&gt;</c> so search results round-trip the caller's id.
/// </para>
///
/// <para>
/// All writes use <c>wait=true</c> by default. Qdrant exposes this as an explicit query
/// parameter that blocks until the operation is committed AND visible to the next read. That
/// removes the eventual-consistency gymnastics other adapters need; it's the main reason this
/// adapter sets <c>SupportsDeleteImmediatelyVisibleToSearch=true</c>.
/// </para>
/// </summary>
internal sealed class QdrantVectorRepository<TEntity, TKey> :
    IVectorSearchRepository<TEntity, TKey>,
    IDescribesCapabilities
    where TEntity : class, IEntity<TKey>
    where TKey : notnull
{
    private readonly HttpClient _http;
    private readonly QdrantOptions _options;
    private readonly IServiceProvider _services;
    private readonly ILogger<QdrantVectorRepository<TEntity, TKey>>? _logger;
    private readonly ConcurrentDictionary<string, byte> _ensuredCollections = new(StringComparer.Ordinal);

    public QdrantVectorRepository(
        IHttpClientFactory httpFactory,
        IOptions<QdrantOptions> options,
        IServiceProvider services)
    {
        _http = httpFactory.CreateClient(Infrastructure.Constants.HttpClientName);
        _options = options.Value;
        _services = services;
        _logger = (ILogger<QdrantVectorRepository<TEntity, TKey>>?)services.GetService(typeof(ILogger<QdrantVectorRepository<TEntity, TKey>>));
        ConfigureHttpClient();
    }

    public void Describe(ICapabilities caps) => caps
        .Add(VectorCaps.Knn).Add(VectorCaps.Filters, QdrantFilterTranslator.Caps)
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
                "Qdrant vector dimension is unknown. Configure Koan:Data:Qdrant:Dimension " +
                "(defaults to 1536 when unset) or call Upsert first to seed it.");
        }
        await EnsureCollection(dimension, ct);
    }

    public async Task Upsert(TKey id, float[] embedding, object? metadata = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(embedding);
        if (embedding.Length == 0) throw new ArgumentException("Embedding must contain values.", nameof(embedding));

        using var _ = QdrantTelemetry.Activity.StartActivity("vector.upsert");
        await EnsureCollection(embedding.Length, ct);

        var body = new JObject
        {
            ["points"] = new JArray { BuildPoint(id, embedding, metadata) }
        };
        await PutAsync(PointsUrl(), body, "upsert", ct);
    }

    public async Task<int> UpsertMany(IEnumerable<(TKey Id, float[] Embedding, object? Metadata)> items, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(items);

        using var _ = QdrantTelemetry.Activity.StartActivity("vector.bulkUpsert");
        var list = items.ToList();
        if (list.Count == 0) return 0;

        var dimension = list[0].Embedding.Length;
        await EnsureCollection(dimension, ct);

        var points = new JArray();
        foreach (var item in list)
        {
            if (item.Embedding.Length != dimension)
            {
                throw new InvalidOperationException(
                    $"Embedding dimension mismatch. Expected {dimension}, received {item.Embedding.Length}.");
            }
            points.Add(BuildPoint(item.Id, item.Embedding, item.Metadata));
        }

        var body = new JObject { ["points"] = points };
        await PutAsync(PointsUrl(), body, "bulk upsert", ct);
        return list.Count;
    }

    public async Task<bool> Delete(TKey id, CancellationToken ct = default)
    {
        using var _ = QdrantTelemetry.Activity.StartActivity("vector.delete");
        await EnsureCollectionInitialized(ct);

        // Qdrant's delete endpoint returns 200 whether the point existed or not — no "not found"
        // signal in the response. To honour IVectorSearchRepository's contract (true iff the id
        // was present) we probe with /points/{id} first. One extra round-trip per single delete;
        // bulk delete returns best-effort count instead.
        var pointId = ProjectId(id);
        var exists = await PointExists(pointId, ct);
        if (!exists) return false;

        var body = new JObject
        {
            ["points"] = new JArray(pointId)
        };
        await PostAsync(DeleteUrl(), body, "delete", ct);
        return true;
    }

    public async Task<int> DeleteMany(IEnumerable<TKey> ids, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ids);
        using var _ = QdrantTelemetry.Activity.StartActivity("vector.bulkDelete");
        await EnsureCollectionInitialized(ct);

        var list = ids.ToList();
        if (list.Count == 0) return 0;

        var body = new JObject
        {
            ["points"] = new JArray(list.Select(ProjectId).ToArray<object>())
        };
        await PostAsync(DeleteUrl(), body, "bulk delete", ct);
        return list.Count;
    }

    public async Task<float[]?> GetEmbedding(TKey id, CancellationToken ct = default)
    {
        await EnsureCollectionInitialized(ct);
        var pointId = ProjectId(id);

        // /points/{id}?with_vector=true returns the stored embedding. 404 means the point doesn't
        // exist (or the collection doesn't); either way, contract says return null.
        var url = $"/collections/{Uri.EscapeDataString(CollectionName)}/points/{Uri.EscapeDataString(pointId)}?with_vector=true&with_payload=false";
        var resp = await _http.GetAsync(url, ct);
        if (resp.StatusCode == HttpStatusCode.NotFound) return null;
        if (!resp.IsSuccessStatusCode) await ThrowHttpFailure(resp, "get embedding", ct);

        var json = await resp.Content.ReadAsStringAsync(ct);
        if (string.IsNullOrWhiteSpace(json)) return null;
        var parsed = JObject.Parse(json);
        var vectorToken = parsed["result"]?["vector"];
        return ExtractVector(vectorToken);
    }

    public async Task<Dictionary<TKey, float[]>> GetEmbeddings(IEnumerable<TKey> ids, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ids);
        await EnsureCollectionInitialized(ct);

        var keyByPointId = new Dictionary<string, TKey>(StringComparer.Ordinal);
        var pointIds = new JArray();
        foreach (var id in ids)
        {
            var pid = ProjectId(id);
            keyByPointId[pid] = id;
            pointIds.Add(pid);
        }

        // Batch fetch via /points (POST) with ids + with_vector. Missing ids are silently omitted
        // from the response — matches the contract.
        var url = $"/collections/{Uri.EscapeDataString(CollectionName)}/points";
        var body = new JObject
        {
            ["ids"] = pointIds,
            ["with_vector"] = true,
            ["with_payload"] = false
        };
        var resp = await _http.PostAsync(url,
            new StringContent(body.ToString(Formatting.None), Encoding.UTF8, "application/json"), ct);
        if (!resp.IsSuccessStatusCode) await ThrowHttpFailure(resp, "get embeddings", ct);

        var json = await resp.Content.ReadAsStringAsync(ct);
        var parsed = string.IsNullOrWhiteSpace(json) ? new JObject() : JObject.Parse(json);
        var result = new Dictionary<TKey, float[]>();
        if (parsed["result"] is JArray points)
        {
            foreach (var p in points.OfType<JObject>())
            {
                var pid = p["id"]?.ToString();
                if (pid is null || !keyByPointId.TryGetValue(pid, out var originalKey)) continue;
                var vec = ExtractVector(p["vector"]);
                if (vec is not null) result[originalKey] = vec;
            }
        }
        return result;
    }

    public async Task<VectorQueryResult<TKey>> Search(VectorQueryOptions options, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (options.Query is null || options.Query.Length == 0)
        {
            throw new ArgumentException("Search query requires a vector with at least one value.", nameof(options));
        }

        using var _ = QdrantTelemetry.Activity.StartActivity("vector.search");
        await EnsureCollection(options.Query.Length, ct);

        var topK = Math.Max(1, options.TopK ?? _options.DefaultTopK);
        var filter = QdrantFilterTranslator.Translate(options.Filter, _options.MetadataField);

        // Qdrant search body. `vector` can be a bare array (single unnamed vector slot) or an
        // object { name, vector } for named-vector collections. We always use a named slot so the
        // adapter stays consistent regardless of upgrades — sending { name, vector } works for
        // both shapes.
        var body = new JObject
        {
            ["vector"] = new JObject
            {
                ["name"] = _options.VectorField,
                ["vector"] = new JArray(options.Query.Select(v => (double)v))
            },
            ["limit"] = topK,
            ["with_payload"] = true,
            ["with_vector"] = false
        };
        if (filter is not null) body["filter"] = filter;

        // Search-time params block. Quantization rescore/oversampling only meaningful when
        // quantization is active for the collection; otherwise we'd be sending no-op fields.
        var searchParams = new JObject();
        if (options.Timeout is { } timeout)
        {
            searchParams["timeout"] = (int)Math.Ceiling(timeout.TotalSeconds);
        }
        if (_options.Quantization is { IsEnabled: true } q)
        {
            searchParams["quantization"] = new JObject
            {
                ["rescore"] = q.Rescore,
                ["oversampling"] = q.Oversampling
            };
        }
        if (searchParams.HasValues) body["params"] = searchParams;

        var url = $"/collections/{Uri.EscapeDataString(CollectionName)}/points/search";
        var parsed = await PostJson(url, body, "search", ct);
        var hits = parsed["result"] as JArray ?? new JArray();

        var matches = new List<VectorMatch<TKey>>(hits.Count);
        foreach (var hit in hits.OfType<JObject>())
        {
            var payload = hit["payload"] as JObject;
            // Original caller-supplied id is in payload.<IdField>. Fallback to the Qdrant
            // point id only if the payload is missing — that case shouldn't happen for points
            // we wrote ourselves, but keeps reads against externally-populated collections
            // working.
            var idStr = payload?[_options.IdField]?.Value<string>() ?? hit["id"]?.ToString();
            if (idStr is null) continue;

            var score = hit.Value<double?>("score") ?? 0d;
            object? metadata = payload?[_options.MetadataField]?.ToObject<object?>();
            matches.Add(new VectorMatch<TKey>(ConvertId(idStr), score, metadata));
        }

        return new VectorQueryResult<TKey>(matches, ContinuationToken: null, VectorTotalKind.Unknown);
    }

    public async IAsyncEnumerable<VectorExportBatch<TKey>> ExportAll(
        int? batchSize = null,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        using var _ = QdrantTelemetry.Activity.StartActivity("vector.export");
        await EnsureCollectionInitialized(ct);

        var size = batchSize ?? 256;
        JToken? offset = null;

        // Qdrant exposes /points/scroll which is paginated by an opaque offset token. Loop until
        // the server returns next_page_offset == null. Each page emits its points; we extract
        // both the vector and the original id from payload for the caller.
        while (true)
        {
            ct.ThrowIfCancellationRequested();

            var body = new JObject
            {
                ["limit"] = size,
                ["with_payload"] = true,
                ["with_vector"] = true
            };
            if (offset is not null && offset.Type != JTokenType.Null) body["offset"] = offset;

            var url = $"/collections/{Uri.EscapeDataString(CollectionName)}/points/scroll";
            var parsed = await PostJson(url, body, "export scroll", ct);
            var result = parsed["result"] as JObject;
            var points = result?["points"] as JArray ?? new JArray();
            if (points.Count == 0) yield break;

            foreach (var p in points.OfType<JObject>())
            {
                var payload = p["payload"] as JObject;
                var idStr = payload?[_options.IdField]?.Value<string>() ?? p["id"]?.ToString();
                if (idStr is null) continue;
                var vec = ExtractVector(p["vector"]);
                if (vec is null) continue;
                var metadata = payload?[_options.MetadataField]?.ToObject<object?>();
                yield return new VectorExportBatch<TKey>(ConvertId(idStr), vec, metadata);
            }

            offset = result?["next_page_offset"];
            if (offset is null || offset.Type == JTokenType.Null) yield break;
        }
    }

    public async Task Flush(CancellationToken ct = default)
    {
        using var activity = QdrantTelemetry.Activity.StartActivity("vector.flush");

        var collectionName = CollectionName;
        var url = $"/collections/{Uri.EscapeDataString(collectionName)}";
        var resp = await _http.DeleteAsync(url, ct);
        if (!resp.IsSuccessStatusCode && resp.StatusCode != HttpStatusCode.NotFound)
        {
            await ThrowHttpFailure(resp, "flush", ct);
        }
        _ensuredCollections.TryRemove(collectionName, out _);
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Collection management
    // ─────────────────────────────────────────────────────────────────────────────

    // A BLANK CollectionName (not just null) means "no override" — use the framework's storage naming. The
    // QdrantOptionsConfigurator binds an absent config key to "" (not null), so `?? ` alone would treat "" as a pinned
    // empty name and produce "/collections/" → 404. IsNullOrWhiteSpace catches both the null and the "" path.
    private string CollectionName
        => string.IsNullOrWhiteSpace(_options.CollectionName)
            ? VectorAdapterNaming.GetOrCompute<TEntity, TKey>(_services)
            : _options.CollectionName!;

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

        using var _ = QdrantTelemetry.Activity.StartActivity("vector.collection.ensure");

        if (await CollectionExists(collectionName, ct))
        {
            _ensuredCollections[collectionName] = 0;
            return;
        }

        if (!_options.AutoCreateCollection)
        {
            throw new InvalidOperationException(
                $"Qdrant collection '{collectionName}' does not exist and auto-creation is disabled.");
        }

        // Named-vector collection: vectors keyed by VectorField. Simpler "vectors" object (no
        // outer name) would also work for a single embedding, but the named-slot shape leaves
        // room for future multi-embedding scenarios without a breaking format change.
        var body = new JObject
        {
            ["vectors"] = new JObject
            {
                [_options.VectorField] = new JObject
                {
                    ["size"] = dimension,
                    ["distance"] = NormalizeDistance(_options.Distance),
                    ["on_disk"] = _options.OnDisk
                }
            }
        };

        // Lean-by-default quantization. Pairs with on_disk=true on the vector config above to
        // produce the actual ~4× memory win: float32 originals on disk, uint8 codebook in RAM
        // (or on disk per AlwaysRam). Without on_disk the originals also stay in RAM and you
        // pay a memory penalty instead. The two settings are designed to move together.
        var quantizationBlock = BuildQuantizationConfig(_options.Quantization);
        if (quantizationBlock is not null)
        {
            body["quantization_config"] = quantizationBlock;
        }

        var url = $"/collections/{Uri.EscapeDataString(collectionName)}";
        var resp = await _http.PutAsync(url,
            new StringContent(body.ToString(Formatting.None), Encoding.UTF8, "application/json"), ct);

        // Qdrant returns 200 on success. 409/400 with "already exists" is also fine — concurrent
        // ensure races. Anything else is a real error.
        if (!resp.IsSuccessStatusCode)
        {
            var errBody = await resp.Content.ReadAsStringAsync(ct);
            if (!errBody.Contains("already exists", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Qdrant collection create failed: HTTP {(int)resp.StatusCode} {resp.ReasonPhrase} {errBody}");
            }
        }
        _ensuredCollections[collectionName] = 0;
    }

    private async Task<bool> CollectionExists(string collectionName, CancellationToken ct)
    {
        var url = $"/collections/{Uri.EscapeDataString(collectionName)}/exists";
        var resp = await _http.GetAsync(url, ct);
        if (!resp.IsSuccessStatusCode) return false;
        var json = await resp.Content.ReadAsStringAsync(ct);
        if (string.IsNullOrWhiteSpace(json)) return false;
        var parsed = JObject.Parse(json);
        return parsed["result"]?["exists"]?.Value<bool>() ?? false;
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Internals
    // ─────────────────────────────────────────────────────────────────────────────

    private JObject BuildPoint(TKey id, float[] embedding, object? metadata)
    {
        var payload = new JObject
        {
            [_options.IdField] = NormalizeIdAsString(id)
        };
        if (metadata is not null) payload[_options.MetadataField] = JToken.FromObject(metadata);

        return new JObject
        {
            ["id"] = ProjectIdToken(id),
            ["vector"] = new JObject
            {
                [_options.VectorField] = new JArray(embedding.Select(v => (double)v))
            },
            ["payload"] = payload
        };
    }

    private async Task<bool> PointExists(string pointId, CancellationToken ct)
    {
        var url = $"/collections/{Uri.EscapeDataString(CollectionName)}/points/{Uri.EscapeDataString(pointId)}";
        var resp = await _http.GetAsync(url, ct);
        if (resp.StatusCode == HttpStatusCode.NotFound) return false;
        return resp.IsSuccessStatusCode;
    }

    private async Task<JObject> PostJson(string url, JObject body, string operation, CancellationToken ct)
    {
        var resp = await SendWithRetry(HttpMethod.Post, url, body, operation, ct);
        var json = await resp.Content.ReadAsStringAsync(ct);
        return string.IsNullOrWhiteSpace(json) ? new JObject() : JObject.Parse(json);
    }

    private async Task PostAsync(string url, JObject body, string operation, CancellationToken ct)
        => (await SendWithRetry(HttpMethod.Post, url, body, operation, ct)).Dispose();

    private async Task PutAsync(string url, JObject body, string operation, CancellationToken ct)
        => (await SendWithRetry(HttpMethod.Put, url, body, operation, ct)).Dispose();

    // Qdrant uses an internal raft for replica consensus even on single-node deployments.
    // Operations immediately following collection creation can race the replica activation and
    // surface as a transient HTTP 500 with the message:
    //   "Failed to apply operation to at least one `Active` replica. ... Please retry."
    // The API documents this as transient and explicitly tells callers to retry. This helper
    // does that — bounded exponential backoff on 5xx + the retry-hint message in the body.
    private async Task<HttpResponseMessage> SendWithRetry(HttpMethod method, string url, JObject body, string operation, CancellationToken ct)
    {
        const int MaxAttempts = 4;
        var delayMs = 50;
        Exception? lastException = null;

        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            var content = new StringContent(body.ToString(Formatting.None), Encoding.UTF8, "application/json");
            using var request = new HttpRequestMessage(method, url) { Content = content };
            var resp = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            if (resp.IsSuccessStatusCode) return resp;

            var errBody = await resp.Content.ReadAsStringAsync(ct);
            if (attempt < MaxAttempts && IsTransientRetryable(resp, errBody))
            {
                resp.Dispose();
                _logger?.LogDebug("Qdrant {Operation} returned transient {Status} (attempt {Attempt}/{Max}); retrying in {Delay}ms",
                    operation, (int)resp.StatusCode, attempt, MaxAttempts, delayMs);
                await Task.Delay(delayMs, ct);
                delayMs *= 2;
                continue;
            }

            resp.Dispose();
            lastException = new InvalidOperationException(
                $"Qdrant {operation} failed: HTTP {(int)resp.StatusCode} {resp.ReasonPhrase} {errBody}");
            throw lastException;
        }

        throw lastException ?? new InvalidOperationException($"Qdrant {operation} failed after {MaxAttempts} attempts.");
    }

    private static bool IsTransientRetryable(HttpResponseMessage resp, string body)
    {
        // 5xx is broadly retryable; the explicit "Please retry" hint also covers cases where
        // Qdrant surfaces these as 400 instead of 500 in some versions.
        if ((int)resp.StatusCode >= 500) return true;
        if (body.Contains("Please retry", StringComparison.OrdinalIgnoreCase)) return true;
        if (body.Contains("not ready", StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    private static async Task ThrowHttpFailure(HttpResponseMessage resp, string operation, CancellationToken ct)
    {
        var body = await resp.Content.ReadAsStringAsync(ct);
        throw new InvalidOperationException(
            $"Qdrant {operation} failed: HTTP {(int)resp.StatusCode} {resp.ReasonPhrase} {body}");
    }

    private string PointsUrl()
    {
        var wait = _options.WaitForResult ? "true" : "false";
        return $"/collections/{Uri.EscapeDataString(CollectionName)}/points?wait={wait}";
    }

    private string DeleteUrl()
    {
        var wait = _options.WaitForResult ? "true" : "false";
        return $"/collections/{Uri.EscapeDataString(CollectionName)}/points/delete?wait={wait}";
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

        if (!string.IsNullOrEmpty(_options.ApiKey))
        {
            // Qdrant Cloud / TLS deployments use a custom `api-key` header (not Authorization).
            if (_http.DefaultRequestHeaders.Contains("api-key"))
            {
                _http.DefaultRequestHeaders.Remove("api-key");
            }
            _http.DefaultRequestHeaders.Add("api-key", _options.ApiKey);
        }
    }

    /// <summary>
    /// Translates <see cref="QuantizationOptions"/> into the Qdrant collection-create
    /// <c>quantization_config</c> block. Returns null when quantization is disabled (null
    /// options or Type=None) so the caller can omit the property entirely from the body.
    /// </summary>
    private static JObject? BuildQuantizationConfig(QuantizationOptions? q)
    {
        if (q is null || !q.IsEnabled) return null;

        // Qdrant wraps each mode's config under a discriminator key (scalar | product | binary).
        // The wire format also differs per mode, so we shape each case explicitly rather than
        // emitting a generic structure that could drift from the API.
        return q.Type.Trim().ToLowerInvariant() switch
        {
            "scalar" => new JObject
            {
                ["scalar"] = new JObject
                {
                    ["type"] = "int8",
                    ["quantile"] = q.Quantile ?? 0.99,
                    ["always_ram"] = q.AlwaysRam
                }
            },
            "product" => new JObject
            {
                ["product"] = new JObject
                {
                    ["compression"] = q.Compression ?? "x16",
                    ["always_ram"] = q.AlwaysRam
                }
            },
            "binary" => new JObject
            {
                ["binary"] = new JObject
                {
                    ["always_ram"] = q.AlwaysRam
                }
            },
            _ => throw new InvalidOperationException(
                $"Unknown Qdrant quantization type '{q.Type}'. Valid values: Scalar, Product, Binary, None.")
        };
    }

    private static string NormalizeDistance(string distance)
    {
        // Qdrant accepts: Cosine, Euclid, Dot, Manhattan. Normalize common aliases users might
        // bring from other adapters (e.g. ES "l2_norm" → Euclid).
        return distance?.Trim().ToUpperInvariant() switch
        {
            "COSINE" => "Cosine",
            "EUCLID" or "EUCLIDEAN" or "L2" or "L2_NORM" => "Euclid",
            "DOT" or "DOTPRODUCT" or "INNERPRODUCT" or "IP" => "Dot",
            "MANHATTAN" or "L1" => "Manhattan",
            _ => "Cosine"
        };
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // ID projection. Qdrant point ids are either UUID or u64 — no arbitrary strings.
    //
    //   ProjectIdToken → JToken for use in JSON request bodies (UUID-shaped string or u64)
    //   ProjectId      → string form of the same, used in URL paths and lookup tables
    //
    // Translation strategy (selected per TKey for zero-overhead in the common Entity<T> case):
    //   TKey == Guid                              → UUID native
    //   TKey == string AND parses as Guid         → UUID native (Entity<T> + GUID v7 default)
    //   TKey == string AND not Guid-shaped        → UUIDv5(namespace, original) — safety net
    //   TKey ∈ {int, long, uint, ulong, short...} → cast to u64 / non-negative range
    //
    // The original caller-supplied identifier is always preserved in payload.<IdField> so the
    // mapping is reversible at read-time without keeping client-side state.
    // ─────────────────────────────────────────────────────────────────────────────

    private static JToken ProjectIdToken(TKey id)
    {
        if (id is Guid g) return new JValue(g.ToString("D", CultureInfo.InvariantCulture));
        if (id is string s)
        {
            if (Guid.TryParse(s, out var asGuid))
            {
                return new JValue(asGuid.ToString("D", CultureInfo.InvariantCulture));
            }
            return new JValue(DeriveUuidV5(Infrastructure.Constants.StringIdNamespace, s).ToString("D", CultureInfo.InvariantCulture));
        }
        // Numeric path — Qdrant accepts unsigned 64-bit integers as ids.
        if (id is sbyte sb) return new JValue(sb < 0 ? 0UL : (ulong)sb);
        if (id is byte b) return new JValue((ulong)b);
        if (id is short sh) return new JValue(sh < 0 ? 0UL : (ulong)sh);
        if (id is ushort ush) return new JValue((ulong)ush);
        if (id is int i) return new JValue(i < 0 ? 0UL : (ulong)i);
        if (id is uint ui) return new JValue((ulong)ui);
        if (id is long l) return new JValue(l < 0 ? 0UL : (ulong)l);
        if (id is ulong ul) return new JValue(ul);
        // Anything else — including enums — falls back to UUIDv5 over the string form. This is
        // the safety net; we don't expect to hit it for any well-behaved Entity<T,TKey>.
        var fallback = id.ToString() ?? "";
        return new JValue(DeriveUuidV5(Infrastructure.Constants.StringIdNamespace, fallback).ToString("D", CultureInfo.InvariantCulture));
    }

    private static string ProjectId(TKey id)
    {
        var token = ProjectIdToken(id);
        return token.Type switch
        {
            JTokenType.Integer => ((ulong)token).ToString(CultureInfo.InvariantCulture),
            _ => token.Value<string>() ?? throw new InvalidOperationException("Failed to project id.")
        };
    }

    private static string NormalizeIdAsString(TKey id)
    {
        // Round-trippable string form of the caller's identifier. Stored in payload.<IdField>
        // so search results can reconstruct the original TKey regardless of how the point id
        // was projected onto Qdrant's id space.
        if (id is Guid g) return g.ToString("D", CultureInfo.InvariantCulture);
        if (id is string s) return s;
        if (id is IFormattable f) return f.ToString(null, CultureInfo.InvariantCulture);
        return id.ToString() ?? "";
    }

    private static TKey ConvertId(string value)
    {
        if (typeof(TKey) == typeof(string)) return (TKey)(object)value;
        if (typeof(TKey) == typeof(Guid) && Guid.TryParse(value, out var guid)) return (TKey)(object)guid;
        if (typeof(TKey).IsEnum) return (TKey)Enum.Parse(typeof(TKey), value, ignoreCase: true);
        return (TKey)Convert.ChangeType(value, typeof(TKey), CultureInfo.InvariantCulture);
    }

    private static float[]? ExtractVector(JToken? token)
    {
        if (token is null || token.Type == JTokenType.Null) return null;

        // Single vector → bare JArray. Named-vector collection → JObject keyed by vector name.
        // Try both shapes; named-vector lookup uses the configured field name.
        if (token is JArray bareArray)
        {
            return bareArray.Select(v => (float)(double)v).ToArray();
        }
        if (token is JObject named)
        {
            // Take the first named vector we find — for this adapter there's only ever one slot.
            foreach (var prop in named.Properties())
            {
                if (prop.Value is JArray arr)
                {
                    return arr.Select(v => (float)(double)v).ToArray();
                }
            }
        }
        return null;
    }

    /// <summary>
    /// RFC 4122 §4.3 UUIDv5 derivation (namespace + name, SHA-1 based). Deterministic: the same
    /// (namespace, name) pair always yields the same UUID, so a Koan string key projected here
    /// always lands on the same Qdrant point id.
    /// </summary>
    private static Guid DeriveUuidV5(Guid namespaceId, string name)
    {
        var namespaceBytes = namespaceId.ToByteArray();
        SwapByteOrder(namespaceBytes);
        var nameBytes = Encoding.UTF8.GetBytes(name);
        using var sha1 = SHA1.Create();
        sha1.TransformBlock(namespaceBytes, 0, namespaceBytes.Length, null, 0);
        sha1.TransformFinalBlock(nameBytes, 0, nameBytes.Length);
        var hash = sha1.Hash!;
        var newGuid = new byte[16];
        Array.Copy(hash, 0, newGuid, 0, 16);
        // Set version (5) and RFC variant.
        newGuid[6] = (byte)((newGuid[6] & 0x0F) | (5 << 4));
        newGuid[8] = (byte)((newGuid[8] & 0x3F) | 0x80);
        SwapByteOrder(newGuid);
        return new Guid(newGuid);
    }

    private static void SwapByteOrder(byte[] guid)
    {
        // .NET Guid layout is little-endian for the first three fields; UUID/RFC 4122 is
        // big-endian. Swap so we hash/emit in canonical RFC order.
        (guid[0], guid[3]) = (guid[3], guid[0]);
        (guid[1], guid[2]) = (guid[2], guid[1]);
        (guid[4], guid[5]) = (guid[5], guid[4]);
        (guid[6], guid[7]) = (guid[7], guid[6]);
    }
}
