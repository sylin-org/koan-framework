using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Koan.AI.Contracts.Adapters;
using Koan.AI.Contracts.Models;
using Koan.Core.Adapters;
using Koan.Orchestration.Attributes;
using Koan.Orchestration.Models;
using Koan.Orchestration;
using System.Text;

namespace Koan.Ai.Provider.Ollama;

/// <summary>
/// Configuration options for Ollama adapter
/// </summary>
public class OllamaOptions
{
    public string? DefaultModel { get; set; }
}

internal sealed class OllamaAdapter : BaseKoanAdapter, IAiAdapter
{
    private readonly HttpClient _http;
    private readonly string _defaultModel;
    private UnifiedServiceMetadata? _orchestrationContext;

    public override ServiceType ServiceType => ServiceType.Ai;
    public override string AdapterId => "ollama";
    public override string DisplayName => "Ollama AI Provider";

    public override AdapterCapabilities Capabilities => AdapterCapabilities.Create()
        .WithHealth(HealthCapabilities.Basic | HealthCapabilities.ConnectionHealth | HealthCapabilities.ResponseTime)
        .WithConfiguration(ConfigurationCapabilities.EnvironmentVariables | ConfigurationCapabilities.ConfigurationFiles | ConfigurationCapabilities.OrchestrationAware)
        .WithSecurity(SecurityCapabilities.None)
        .WithData(ExtendedQueryCapabilities.VectorSearch | ExtendedQueryCapabilities.SemanticSearch | ExtendedQueryCapabilities.Embeddings)
        .WithCustom("chat", "streaming", "local_models");

    public string Id => AdapterId;
    public override string Name => DisplayName;
    public string Type => "ollama";

    public OllamaAdapter(HttpClient http, ILogger<OllamaAdapter> logger, IConfiguration configuration)
        : base(logger, configuration)
    {
        _http = http;
        var options = GetOptions<OllamaOptions>();
        var serviceDefault = GetServiceDefaultModel();

        _defaultModel = options.DefaultModel ?? serviceDefault ?? "all-minilm";
    }

    public bool CanServe(AiChatRequest request)
    {
        // Accept if no model specified or model looks like an ollama tag; be permissive by default.
        return true;
    }

    public async Task<AiChatResponse> ChatAsync(AiChatRequest request, CancellationToken ct = default)
    {
        var model = request.Model ?? _defaultModel;
        if (string.IsNullOrWhiteSpace(model))
            throw new InvalidOperationException("Ollama adapter requires a model name.");
        var prompt = BuildPrompt(request);
        var bodyObj = new Dictionary<string, object?>
        {
            ["model"] = model,
            ["prompt"] = prompt,
            ["stream"] = false,
            ["options"] = MapOptions(request.Options)
        };
        if (request.Options?.Think is bool thinkFlag)
            bodyObj["think"] = thinkFlag;
        var body = bodyObj;
        Logger.LogDebug("Ollama: POST {Path} model={Model}", "/api/generate", model);
    var payload = JsonConvert.SerializeObject(body, new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore });
    using var resp = await _http.PostAsync("/api/generate", new StringContent(payload, Encoding.UTF8, "application/json"), ct).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode)
        {
            var text = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            Logger.LogWarning("Ollama: generate failed ({Status}) body={Body}", (int)resp.StatusCode, text);
        }
    resp.EnsureSuccessStatusCode();
    var json = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
    var doc = JsonConvert.DeserializeObject<OllamaGenerateResponse>(json)
          ?? throw new InvalidOperationException("Empty response from Ollama.");
        return new AiChatResponse
        {
            Text = doc.response ?? string.Empty,
            FinishReason = doc.done_reason,
            Model = doc.model
        };
    }

    public async IAsyncEnumerable<AiChatChunk> StreamAsync(AiChatRequest request, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        var model = request.Model ?? _defaultModel;
        if (string.IsNullOrWhiteSpace(model))
            throw new InvalidOperationException("Ollama adapter requires a model name.");
        var prompt = BuildPrompt(request);
    var streamBody = new Dictionary<string, object?>
        {
            ["model"] = model,
            ["prompt"] = prompt,
            ["stream"] = true,
            ["options"] = MapOptions(request.Options)
        };
        if (request.Options?.Think is bool thinkFlag2)
            streamBody["think"] = thinkFlag2;
    var body = JsonConvert.SerializeObject(streamBody, new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore });
        using var httpReq = new HttpRequestMessage(HttpMethod.Post, "/api/generate")
        { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        Logger.LogDebug("Ollama: STREAM {Path} model={Model}", "/api/generate", model);
        using var resp = await _http.SendAsync(httpReq, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();
        await foreach (var part in ReadJsonLinesAsync<OllamaGenerateResponse>(resp, ct))
        {
            if (part is null) continue;
            if (!string.IsNullOrEmpty(part.response))
            {
                yield return new AiChatChunk { DeltaText = part.response };
            }
        }
    }

    public async Task<AiEmbeddingsResponse> EmbedAsync(AiEmbeddingsRequest request, CancellationToken ct = default)
    {
        var model = request.Model ?? _defaultModel;
        if (string.IsNullOrWhiteSpace(model))
            throw new InvalidOperationException("Ollama adapter requires a model name.");

        var vectors = new List<float[]>();
        foreach (var input in request.Input)
        {
            // Ollama embeddings API expects 'prompt' as the input field
            var body = new { model, prompt = input };
            var payload = JsonConvert.SerializeObject(body);
            using var resp = await _http.PostAsync("/api/embeddings", new StringContent(payload, Encoding.UTF8, "application/json"), ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
            {
                var text = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                Logger.LogWarning("Ollama: embeddings failed ({Status}) body={Body}", (int)resp.StatusCode, text);
            }
            resp.EnsureSuccessStatusCode();
            var json = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            var doc = JsonConvert.DeserializeObject<OllamaEmbeddingsResponse>(json)
                      ?? throw new InvalidOperationException("Empty response from Ollama.");
            vectors.Add(doc.embedding ?? Array.Empty<float>());
        }
        var dim = vectors.FirstOrDefault()?.Length ?? 0;
        Logger.LogDebug("Ollama: embeddings ok model={Model} dim={Dim} count={Count}", model, dim, vectors.Count);
        return new AiEmbeddingsResponse { Vectors = vectors, Model = model, Dimension = dim };
    }

    public async Task<IReadOnlyList<AiModelDescriptor>> ListModelsAsync(CancellationToken ct = default)
    {
    using var resp = await _http.GetAsync("/api/tags", ct).ConfigureAwait(false);
    resp.EnsureSuccessStatusCode();
    var json = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
    var doc = JsonConvert.DeserializeObject<OllamaTagsResponse>(json);
        var models = new List<AiModelDescriptor>();
        foreach (var m in doc?.models ?? Enumerable.Empty<OllamaTag>())
        {
            models.Add(new AiModelDescriptor
            {
                Name = m.name ?? string.Empty,
                Family = m.model,
                AdapterId = Id,
                AdapterType = Type
            });
        }
        return models;
    }

    public Task<AiCapabilities> GetCapabilitiesAsync(CancellationToken ct = default)
        => Task.FromResult(new AiCapabilities
        {
            AdapterId = Id,
            AdapterType = Type,
            SupportsChat = true,
            SupportsStreaming = true,
            SupportsEmbeddings = true
        });

    private static string BuildPrompt(AiChatRequest req)
    {
        if (req.Messages.Count == 1 && string.Equals(req.Messages[0].Role, "user", StringComparison.OrdinalIgnoreCase))
            return req.Messages[0].Content;
        var sb = new StringBuilder();
        foreach (var m in req.Messages)
        {
            if (string.Equals(m.Role, "system", StringComparison.OrdinalIgnoreCase))
                sb.AppendLine($"[system]\n{m.Content}\n");
            else if (string.Equals(m.Role, "user", StringComparison.OrdinalIgnoreCase))
                sb.AppendLine($"[user]\n{m.Content}\n");
            else if (string.Equals(m.Role, "assistant", StringComparison.OrdinalIgnoreCase))
                sb.AppendLine($"[assistant]\n{m.Content}\n");
        }
        return sb.ToString();
    }

    private static IDictionary<string, object?> MapOptions(AiPromptOptions? o)
    {
        if (o is null) return new Dictionary<string, object?>();
        var dict = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["temperature"] = o.Temperature,
            ["top_p"] = o.TopP,
            ["num_predict"] = o.MaxOutputTokens,
            ["stop"] = o.Stop,
            ["seed"] = o.Seed
        };
        if (o.VendorOptions is { Count: > 0 })
        {
            foreach (var kv in o.VendorOptions)
            {
                var normKey = NormalizeOllamaOptionKey(kv.Key);
                // Promote JsonElement to raw boxed value when possible
                dict[normKey] = kv.Value.Type switch
                {
                    JTokenType.String => kv.Value.Value<string>(),
                    JTokenType.Integer => kv.Value.Value<long>(),
                    JTokenType.Float => kv.Value.Value<double>(),
                    JTokenType.Boolean => kv.Value.Value<bool>(),
                    JTokenType.Array => kv.Value, // pass-through
                    JTokenType.Object => kv.Value,
                    _ => null
                };
            }
        }
        return dict;
    }

    private static object? TryGetNumber(JToken tok)
        => tok.Type == JTokenType.Integer ? tok.Value<long>() : (tok.Type == JTokenType.Float ? tok.Value<double>() : null);

    private static string NormalizeOllamaOptionKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key)) return key;
        var k = key.Trim();
        // Normalize common synonyms and casing to Ollama's expected names
        if (k.Equals("temp", StringComparison.OrdinalIgnoreCase) || k.Equals("temperature", StringComparison.OrdinalIgnoreCase))
            return "temperature";
        if (k.Equals("top_p", StringComparison.OrdinalIgnoreCase) || k.Equals("topp", StringComparison.OrdinalIgnoreCase) || k.Equals("topP", StringComparison.Ordinal))
            return "top_p";
        if (k.Equals("max_tokens", StringComparison.OrdinalIgnoreCase) || k.Equals("max_new_tokens", StringComparison.OrdinalIgnoreCase) || k.Equals("maxOutputTokens", StringComparison.Ordinal) || k.Equals("max_output_tokens", StringComparison.OrdinalIgnoreCase) || k.Equals("num_predict", StringComparison.OrdinalIgnoreCase))
            return "num_predict";
        if (k.Equals("stop_sequences", StringComparison.OrdinalIgnoreCase) || k.Equals("stopSequences", StringComparison.Ordinal) || k.Equals("stops", StringComparison.OrdinalIgnoreCase) || k.Equals("stop", StringComparison.OrdinalIgnoreCase))
            return "stop";
        if (k.Equals("seed", StringComparison.OrdinalIgnoreCase))
            return "seed";
        return k; // passthrough as-is
    }

    private static async IAsyncEnumerable<T?> ReadJsonLinesAsync<T>(HttpResponseMessage resp, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        await using var stream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        while (!reader.EndOfStream && !ct.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync().ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(line)) continue;
            T? obj;
            try { obj = JsonConvert.DeserializeObject<T>(line); }
            catch { continue; }
            yield return obj;
        }
    }

    private sealed class OllamaGenerateResponse
    {
        public string? model { get; set; }
        public string? response { get; set; }
        public bool done { get; set; }
        public string? done_reason { get; set; }
    }

    private sealed class OllamaEmbeddingsResponse
    {
        public float[]? embedding { get; set; }
    }

    private sealed class OllamaTagsResponse
    {
        public List<OllamaTag> models { get; set; } = new();
    }
    protected override async Task InitializeAdapterAsync(CancellationToken cancellationToken = default)
    {
        var baseUrl = GetConnectionString();
        if (!string.IsNullOrEmpty(baseUrl) && _http.BaseAddress == null)
        {
            _http.BaseAddress = new Uri(baseUrl);
        }

        await TestConnectivityAsync(cancellationToken);
        Logger.LogInformation("[{AdapterId}] Ollama connection established", AdapterId);
    }

    [OrchestrationAware]
    public async Task InitializeWithOrchestrationAsync(UnifiedServiceMetadata orchestrationContext, CancellationToken cancellationToken = default)
    {
        _orchestrationContext = orchestrationContext;
        Logger.LogInformation("[{AdapterId}] Initializing with orchestration context: {ServiceKind}", AdapterId, orchestrationContext.ServiceKind);

        var baseUrl = GetConnectionString();
        if (!string.IsNullOrEmpty(baseUrl))
        {
            _http.BaseAddress = new Uri(baseUrl);
        }

        if (orchestrationContext.HasCapability("container_managed"))
        {
            Logger.LogInformation("[{AdapterId}] Container is orchestration-managed, waiting for readiness", AdapterId);
            await WaitForContainerReadiness(cancellationToken);
        }

        await TestConnectivityAsync(cancellationToken);
        Logger.LogInformation("[{AdapterId}] Ollama connection established using orchestration-aware initialization", AdapterId);
    }

    protected override async Task<IReadOnlyDictionary<string, object?>?> CheckAdapterHealthAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            using var response = await _http.GetAsync("/", cancellationToken);
            stopwatch.Stop();

            var healthData = new Dictionary<string, object?>
            {
                ["status"] = response.IsSuccessStatusCode ? "healthy" : "unhealthy",
                ["response_time_ms"] = stopwatch.ElapsedMilliseconds,
                ["base_url"] = _http.BaseAddress?.ToString(),
                ["default_model"] = _defaultModel,
                ["orchestration_aware"] = _orchestrationContext != null
            };

            if (_orchestrationContext != null)
            {
                healthData["orchestration_mode"] = _orchestrationContext.IsOrchestrationAware ? "managed" : "standalone";
                healthData["capabilities"] = _orchestrationContext.Capabilities;
            }

            try
            {
                var models = await ListModelsAsync(cancellationToken);
                healthData["available_models"] = models.Count;
                healthData["model_list"] = models.Take(5).Select(m => m.Name).ToArray();
            }
            catch (Exception ex)
            {
                healthData["models_error"] = ex.Message;
            }

            return healthData;
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "[{AdapterId}] Health check failed", AdapterId);
            return new Dictionary<string, object?>
            {
                ["status"] = "unhealthy",
                ["error"] = ex.Message,
                ["orchestration_aware"] = _orchestrationContext != null
            };
        }
    }

    protected override Task<IReadOnlyDictionary<string, object?>?> GetAdapterBootstrapMetadataAsync(CancellationToken cancellationToken = default)
    {
        var metadata = new Dictionary<string, object?>
        {
            ["base_url"] = _http.BaseAddress?.ToString(),
            ["default_model"] = _defaultModel,
            ["provider"] = "Ollama",
            ["features"] = new[] { "chat", "streaming", "embeddings", "local_models" },
            ["runtime_capabilities"] = Capabilities.GetCapabilitySummary()
        };

        if (_orchestrationContext != null)
        {
            metadata["orchestration"] = new
            {
                service_kind = _orchestrationContext.ServiceKind.ToString(),
                is_managed = _orchestrationContext.IsOrchestrationAware,
                capabilities = _orchestrationContext.Capabilities,
                deployment_aware = true
            };
        }
        else
        {
            metadata["orchestration"] = new { deployment_aware = false };
        }

        return Task.FromResult<IReadOnlyDictionary<string, object?>?>(metadata);
    }

    private async Task TestConnectivityAsync(CancellationToken cancellationToken)
    {
        using var response = await _http.GetAsync("/", cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    private async Task WaitForContainerReadiness(CancellationToken cancellationToken)
    {
        const int maxRetries = 30;
        const int delayMs = 1000;

        for (int i = 0; i < maxRetries; i++)
        {
            try
            {
                using var response = await _http.GetAsync("/", cancellationToken);
                if (response.IsSuccessStatusCode)
                {
                    Logger.LogInformation("[{AdapterId}] Container is ready (attempt {Attempt})", AdapterId, i + 1);
                    return;
                }
            }
            catch (Exception ex) when (i < maxRetries - 1)
            {
                Logger.LogDebug(ex, "[{AdapterId}] Container not ready yet (attempt {Attempt}), retrying...", AdapterId, i + 1);
                await Task.Delay(delayMs, cancellationToken);
            }
        }

        throw new InvalidOperationException($"Container failed to become ready after {maxRetries} attempts");
    }

    private string? GetServiceDefaultModel()
    {
        try
        {
            // Read default model from KoanService attribute metadata
            var serviceDescriptorType = typeof(OllamaServiceDescriptor);
            var koanServiceAttribute = serviceDescriptorType.GetCustomAttributes(typeof(Koan.Orchestration.Attributes.KoanServiceAttribute), false)
                .FirstOrDefault() as Koan.Orchestration.Attributes.KoanServiceAttribute;

            if (koanServiceAttribute?.Capabilities != null)
            {
                foreach (var capability in koanServiceAttribute.Capabilities)
                {
                    if (capability.StartsWith("default_model=", StringComparison.OrdinalIgnoreCase))
                    {
                        return capability.Substring("default_model=".Length);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "GetServiceDefaultModel: Failed to read default model from service metadata");
        }

        return null;
    }

    private sealed class OllamaTag
    {
        public string? name { get; set; }
        public string? model { get; set; }
    }
}
