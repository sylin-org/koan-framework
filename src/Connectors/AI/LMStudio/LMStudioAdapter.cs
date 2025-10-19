using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Koan.AI.Contracts.Adapters;
using Koan.AI.Contracts.Models;
using Koan.Core.Adapters;
using Koan.Orchestration;
using Koan.Orchestration.Attributes;
using Koan.Orchestration.Models;
using Koan.AI.Connector.LMStudio.Options;
using Koan.AI.Connector.LMStudio.Infrastructure;

namespace Koan.AI.Connector.LMStudio;

[AiAdapterDescriptor(priority: 12, Weight = 2)]
internal sealed class LMStudioAdapter : BaseKoanAdapter,
    IAiAdapter,
    IAdapterReadiness,
    IAdapterReadinessConfiguration,
    IAsyncAdapterInitializer
{
    private readonly HttpClient _http;
    private readonly LMStudioOptions _options;
    private readonly string _defaultModel;
    private readonly AdapterReadinessConfiguration _readiness;
    private readonly AdaptersReadinessOptions _readinessDefaults;
    private readonly ReadinessStateManager _stateManager = new();
    private readonly object _initGate = new();
    private Task? _initializationTask;
    private UnifiedServiceMetadata? _orchestrationContext;

    public override ServiceType ServiceType => ServiceType.Ai;
    public override string AdapterId => Constants.Adapter.Type;
    public override string DisplayName => "LM Studio AI Provider";

    public override AdapterCapabilities Capabilities => AdapterCapabilities.Create()
        .WithHealth(HealthCapabilities.Basic | HealthCapabilities.ConnectionHealth | HealthCapabilities.ResponseTime)
        .WithConfiguration(ConfigurationCapabilities.EnvironmentVariables | ConfigurationCapabilities.ConfigurationFiles | ConfigurationCapabilities.OrchestrationAware)
    .WithSecurity(SecurityCapabilities.Authentication | SecurityCapabilities.TokenBased)
        .WithCustom("chat", "streaming", "openai_compatible");

    public string Id => AdapterId;
    public override string Name => DisplayName;
    public string Type => Constants.Adapter.Type;
    public IAiModelManager? ModelManager => null;

    public LMStudioAdapter(
        HttpClient http,
        ILogger<LMStudioAdapter> logger,
        IConfiguration configuration,
        AdaptersReadinessOptions? readinessDefaults = null,
        LMStudioOptions? resolvedOptions = null)
        : base(logger, configuration)
    {
        _http = http;
        _readinessDefaults = readinessDefaults ?? new AdaptersReadinessOptions();
        _options = resolvedOptions ?? GetOptions<LMStudioOptions>();
        _readiness = (AdapterReadinessConfiguration)(_options.Readiness ?? new AdapterReadinessConfiguration());

        if (_readiness.Timeout <= TimeSpan.Zero)
        {
            _readiness.Timeout = _readinessDefaults.DefaultTimeout;
        }

        _defaultModel = _options.DefaultModel ?? string.Empty;

        ApplyConfiguredBaseAddress();

        _stateManager.StateChanged += (_, args) => ReadinessStateChanged?.Invoke(this, args);
    }

    public bool CanServe(AiChatRequest request)
    {
        return true;
    }

    public async Task<AiChatResponse> ChatAsync(AiChatRequest request, CancellationToken ct = default)
    {
        var httpClient = GetHttpClientForRequest(request.InternalConnectionString);
        var model = ResolveModel(request.Model);

        var payload = BuildChatPayload(request, model, stream: false);
        using var message = new HttpRequestMessage(HttpMethod.Post, Constants.Discovery.ChatPath)
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };

        AttachAuth(message);

        using var response = await httpClient.SendAsync(message, ct).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            Logger.LogWarning("LM Studio chat request failed ({Status}): {Body}", (int)response.StatusCode, body);
            response.EnsureSuccessStatusCode();
        }

        var doc = JsonConvert.DeserializeObject<ChatCompletionResponse>(body)
                  ?? throw new InvalidOperationException("LM Studio returned an empty response.");

        var first = doc.choices?.FirstOrDefault();
        var text = first?.message?.content ?? string.Empty;

        return new AiChatResponse
        {
            Text = text,
            FinishReason = first?.finish_reason,
            Model = doc.model,
            AdapterId = AdapterId
        };
    }

    public async IAsyncEnumerable<AiChatChunk> StreamAsync(
        AiChatRequest request,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        await WaitForReadinessAsync(null, ct).ConfigureAwait(false);

        var httpClient = GetHttpClientForRequest(request.InternalConnectionString);
        var model = ResolveModel(request.Model);

        var payload = BuildChatPayload(request, model, stream: true);
        using var message = new HttpRequestMessage(HttpMethod.Post, Constants.Discovery.ChatPath)
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };

        message.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        AttachAuth(message);

        using var response = await httpClient.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        await foreach (var chunk in ReadServerSentEvents(response, ct).ConfigureAwait(false))
        {
            if (chunk is null)
            {
                continue;
            }

            if (chunk.choices is null || chunk.choices.Count == 0)
            {
                continue;
            }

            var delta = chunk.choices[0].delta;
            if (delta?.content is string content && content.Length > 0)
            {
                yield return new AiChatChunk
                {
                    DeltaText = content,
                    Model = chunk.model,
                    AdapterId = AdapterId
                };
            }
        }
    }

    public async Task<AiEmbeddingsResponse> EmbedAsync(AiEmbeddingsRequest request, CancellationToken ct = default)
    {
        var httpClient = GetHttpClientForRequest(request.InternalConnectionString);
        var model = ResolveModel(request.Model);

        var payload = new
        {
            model,
            input = request.Input.ToArray()
        };

        using var message = new HttpRequestMessage(HttpMethod.Post, Constants.Discovery.EmbeddingsPath)
        {
            Content = new StringContent(JsonConvert.SerializeObject(payload), Encoding.UTF8, "application/json")
        };

        AttachAuth(message);

        using var response = await httpClient.SendAsync(message, ct).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            Logger.LogWarning("LM Studio embeddings request failed ({Status}): {Body}", (int)response.StatusCode, body);
            response.EnsureSuccessStatusCode();
        }

        var doc = JsonConvert.DeserializeObject<EmbeddingResponse>(body)
                  ?? throw new InvalidOperationException("LM Studio returned an empty embedding response.");

        var vectors = doc.data?.Select(d => d.embedding?.ToArray() ?? Array.Empty<float>()).ToList() ?? new List<float[]>();
        var dimension = vectors.FirstOrDefault()?.Length ?? 0;

        return new AiEmbeddingsResponse
        {
            Vectors = vectors,
            Model = doc.model,
            Dimension = dimension
        };
    }

    public async Task<IReadOnlyList<AiModelDescriptor>> ListModelsAsync(CancellationToken ct = default)
    {
        var httpClient = GetHttpClientForRequest(null);
        using var message = new HttpRequestMessage(HttpMethod.Get, Constants.Discovery.ModelsPath);
        AttachAuth(message);

        using var response = await httpClient.SendAsync(message, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

        var doc = JsonConvert.DeserializeObject<ModelsResponse>(body);
        if (doc?.data is null)
        {
            return Array.Empty<AiModelDescriptor>();
        }

        return doc.data.Select(m => new AiModelDescriptor
        {
            Name = m.id ?? string.Empty,
            Family = m.owned_by,
            AdapterId = AdapterId,
            AdapterType = Type
        }).ToList();
    }

    public Task<AiCapabilities> GetCapabilitiesAsync(CancellationToken ct = default)
    {
        return Task.FromResult(new AiCapabilities
        {
            AdapterId = AdapterId,
            AdapterType = Type,
            SupportsChat = true,
            SupportsStreaming = true,
            SupportsEmbeddings = true
        });
    }

    public AdapterReadinessState ReadinessState => _stateManager.State;
    public bool IsReady => _stateManager.IsReady;
    public TimeSpan ReadinessTimeout => _readiness.Timeout > TimeSpan.Zero ? _readiness.Timeout : _readinessDefaults.DefaultTimeout;

    public event EventHandler<ReadinessStateChangedEventArgs>? ReadinessStateChanged;

    public ReadinessStateManager StateManager => _stateManager;

    public Task<bool> IsReadyAsync(CancellationToken ct = default)
    {
        _ = EnsureInitializationStarted();
        return Task.FromResult(_stateManager.IsReady);
    }

    public async Task WaitForReadinessAsync(TimeSpan? timeout = null, CancellationToken ct = default)
    {
        var initTask = EnsureInitializationStarted();

        if (_stateManager.IsReady)
        {
            return;
        }

        if (initTask.IsFaulted)
        {
            throw new AdapterNotReadyException(AdapterId, _stateManager.State, "LM Studio readiness initialization failed.", initTask.Exception?.GetBaseException());
        }

        var effective = timeout ?? ReadinessTimeout;
        if (effective <= TimeSpan.Zero)
        {
            throw new AdapterNotReadyException(AdapterId, _stateManager.State, "LM Studio readiness timeout is zero; readiness gating cannot wait.");
        }

        try
        {
            await _stateManager.WaitAsync(effective, ct).ConfigureAwait(false);
        }
        catch (TimeoutException ex)
        {
            throw new AdapterNotReadyException(AdapterId, _stateManager.State, $"Timed out waiting for LM Studio readiness after {effective}.", ex);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (initTask.IsFaulted)
        {
            throw new AdapterNotReadyException(AdapterId, _stateManager.State, "LM Studio adapter failed during readiness initialization.", initTask.Exception?.GetBaseException() ?? ex);
        }

        if (!_stateManager.IsReady)
        {
            throw new AdapterNotReadyException(AdapterId, _stateManager.State, "LM Studio adapter is not ready after waiting for readiness.");
        }
    }

    public ReadinessPolicy Policy => _readiness.Policy;
    public TimeSpan Timeout => _readiness.Timeout > TimeSpan.Zero ? _readiness.Timeout : _readinessDefaults.DefaultTimeout;
    public bool EnableReadinessGating => _readiness.EnableReadinessGating;

    protected override async Task InitializeAdapterAsync(CancellationToken cancellationToken = default)
    {
        ApplyConfiguredBaseAddress();
        _ = EnsureInitializationStarted();

        try
        {
            await WaitForReadinessAsync(ReadinessTimeout, cancellationToken).ConfigureAwait(false);
        }
        catch (AdapterNotReadyException ex)
        {
            Logger.LogWarning(ex, "[{AdapterId}] LM Studio adapter not ready after initialization (state={State})", AdapterId, ReadinessState);
        }
    }

    [OrchestrationAware]
    public async Task InitializeWithOrchestrationAsync(UnifiedServiceMetadata orchestrationContext, CancellationToken cancellationToken = default)
    {
        _orchestrationContext = orchestrationContext;
        ApplyConfiguredBaseAddress();

        _ = EnsureInitializationStarted();
        try
        {
            await WaitForReadinessAsync(ReadinessTimeout, cancellationToken).ConfigureAwait(false);
        }
        catch (AdapterNotReadyException ex)
        {
            Logger.LogWarning(ex, "[{AdapterId}] LM Studio adapter not ready after orchestration initialization (state={State})", AdapterId, ReadinessState);
        }
    }

    protected override async Task<IReadOnlyDictionary<string, object?>?> CheckAdapterHealthAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            using var request = new HttpRequestMessage(HttpMethod.Get, Constants.Discovery.ModelsPath);
            AttachAuth(request);
            using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            stopwatch.Stop();

            var payload = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            var metadata = new Dictionary<string, object?>
            {
                ["status"] = response.IsSuccessStatusCode ? "healthy" : "unhealthy",
                ["response_time_ms"] = stopwatch.ElapsedMilliseconds,
                ["base_url"] = _http.BaseAddress?.ToString(),
                ["default_model"] = _defaultModel,
                ["orchestration_aware"] = _orchestrationContext != null,
                ["readiness_state"] = ReadinessState.ToString()
            };

            try
            {
                var doc = JsonConvert.DeserializeObject<ModelsResponse>(payload);
                metadata["available_models"] = doc?.data?.Count ?? 0;
                metadata["model_list"] = doc?.data?.Take(5).Select(m => m.id).ToArray();
            }
            catch (Exception ex)
            {
                metadata["models_error"] = ex.Message;
            }

            return metadata;
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "[{AdapterId}] Health check failed", AdapterId);
            return new Dictionary<string, object?>
            {
                ["status"] = "unhealthy",
                ["error"] = ex.Message
            };
        }
    }

    protected override Task<IReadOnlyDictionary<string, object?>?> GetAdapterBootstrapMetadataAsync(CancellationToken cancellationToken = default)
    {
        var metadata = new Dictionary<string, object?>
        {
            ["base_url"] = _http.BaseAddress?.ToString(),
            ["default_model"] = _defaultModel,
            ["provider"] = "LM Studio",
            ["features"] = new[] { "chat", "streaming", "embeddings", "openai_compatible" },
            ["runtime_capabilities"] = Capabilities.GetCapabilitySummary(),
            ["readiness_state"] = ReadinessState.ToString()
        };

        return Task.FromResult<IReadOnlyDictionary<string, object?>?>(metadata);
    }

    private Task EnsureInitializationStarted()
    {
        var task = _initializationTask;
        if (task is not null)
        {
            return task;
        }

        lock (_initGate)
        {
            task = _initializationTask;
            if (task is null)
            {
                _stateManager.TransitionTo(AdapterReadinessState.Initializing);
                task = _initializationTask = InitializeReadinessAsync();
            }
        }

        return task;
    }

    private async Task InitializeReadinessAsync()
    {
        ApplyConfiguredBaseAddress();

        try
        {
            using var timeoutCts = new CancellationTokenSource();
            if (ReadinessTimeout > TimeSpan.Zero)
            {
                timeoutCts.CancelAfter(ReadinessTimeout);
            }

            await VerifyModelsEndpointAsync(timeoutCts.Token).ConfigureAwait(false);
            var defaultReady = await EnsureDefaultModelAvailableAsync(timeoutCts.Token).ConfigureAwait(false);

            var newState = defaultReady ? AdapterReadinessState.Ready : AdapterReadinessState.Degraded;
            _stateManager.TransitionTo(newState);
        }
        catch (OperationCanceledException ex)
        {
            _stateManager.TransitionTo(AdapterReadinessState.Failed);
            Logger.LogError(ex, "[{AdapterId}] LM Studio readiness timed out after {Timeout}", AdapterId, ReadinessTimeout);
            throw new InvalidOperationException($"LM Studio readiness timed out after {ReadinessTimeout}.", ex);
        }
        catch (Exception ex)
        {
            _stateManager.TransitionTo(AdapterReadinessState.Failed);
            Logger.LogError(ex, "[{AdapterId}] LM Studio readiness failed", AdapterId);
            throw;
        }
    }

    private async Task VerifyModelsEndpointAsync(CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, Constants.Discovery.ModelsPath);
        AttachAuth(request);

        using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
    }

    private async Task<bool> EnsureDefaultModelAvailableAsync(CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_defaultModel))
        {
            return true;
        }

        try
        {
            var models = await ListModelsAsync(ct).ConfigureAwait(false);
            return models.Any(m => string.Equals(m.Name, _defaultModel, StringComparison.OrdinalIgnoreCase) ||
                                   string.Equals(m.Family, _defaultModel, StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "[{AdapterId}] Failed to verify default model '{Model}'", AdapterId, _defaultModel);
            return false;
        }
    }

    private HttpClient GetHttpClientForRequest(string? connectionString)
    {
        if (!string.IsNullOrWhiteSpace(connectionString))
        {
            var timeoutSeconds = _options.RequestTimeoutSeconds > 0 ? _options.RequestTimeoutSeconds : 120;
            return new HttpClient
            {
                BaseAddress = new Uri(NormalizeBase(connectionString)),
                Timeout = TimeSpan.FromSeconds(timeoutSeconds)
            };
        }

        return _http;
    }

    private void ApplyConfiguredBaseAddress()
    {
        var configured = ResolveConfiguredBaseUrl();
        if (string.IsNullOrWhiteSpace(configured))
        {
            return;
        }

        if (!Uri.TryCreate(configured, UriKind.Absolute, out var resolved))
        {
            Logger.LogWarning("LM Studio adapter: Ignoring invalid base URL '{BaseUrl}'", configured);
            return;
        }

        if (_http.BaseAddress == null || !_http.BaseAddress.Equals(resolved))
        {
            _http.BaseAddress = resolved;
        }
    }

    private string? ResolveConfiguredBaseUrl()
    {
        if (!string.IsNullOrWhiteSpace(_options.ConnectionString) &&
            !string.Equals(_options.ConnectionString, "auto", StringComparison.OrdinalIgnoreCase))
        {
            return NormalizeBase(_options.ConnectionString);
        }

        if (!string.IsNullOrWhiteSpace(_options.BaseUrl))
        {
            return NormalizeBase(_options.BaseUrl);
        }

        var legacy = GetConnectionString();
        if (!string.IsNullOrWhiteSpace(legacy) &&
            !string.Equals(legacy, "auto", StringComparison.OrdinalIgnoreCase))
        {
            return NormalizeBase(legacy);
        }

        return null;
    }

    private string NormalizeBase(string baseUrl)
    {
        var trimmed = baseUrl.TrimEnd('/');
        if (trimmed.EndsWith("/v1", StringComparison.OrdinalIgnoreCase))
        {
            trimmed = trimmed[..^3].TrimEnd('/');
        }

        return trimmed;
    }

    private string ResolveModel(string? requested)
    {
        if (!string.IsNullOrWhiteSpace(requested))
        {
            return requested;
        }

        if (!string.IsNullOrWhiteSpace(_defaultModel))
        {
            return _defaultModel;
        }

        throw new InvalidOperationException("LM Studio adapter requires a model name when no default is configured.");
    }

    private string BuildChatPayload(AiChatRequest request, string model, bool stream)
    {
        var content = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["model"] = model,
            ["stream"] = stream,
            ["messages"] = request.Messages.Select(MapMessage).ToArray()
        };

        var options = request.Options;
        if (options != null)
        {
            if (options.Temperature is double temperature)
            {
                content["temperature"] = temperature;
            }

            if (options.TopP is double topP)
            {
                content["top_p"] = topP;
            }

            if (options.MaxOutputTokens is int maxTokens)
            {
                content["max_tokens"] = maxTokens;
            }

            if (options.Stop is { Length: > 0 } stop)
            {
                content["stop"] = stop;
            }

            if (options.Seed is int seed)
            {
                content["seed"] = seed;
            }

            if (options.VendorOptions is { Count: > 0 } vendorOptions)
            {
                foreach (var kvp in vendorOptions)
                {
                    content[kvp.Key] = kvp.Value;
                }
            }
        }

        return JsonConvert.SerializeObject(content, new JsonSerializerSettings
        {
            NullValueHandling = NullValueHandling.Ignore
        });
    }

    private static object MapMessage(AiMessage message)
    {
        if (message.Parts is { Count: > 0 })
        {
            var parts = message.Parts.Select(p => new Dictionary<string, string>
            {
                ["type"] = p.Type,
                ["text"] = p.Text ?? string.Empty
            }).ToArray();

            return new
            {
                role = message.Role,
                content = parts
            };
        }

        return new
        {
            role = message.Role,
            content = message.Content
        };
    }

    private void AttachAuth(HttpRequestMessage request)
    {
        var key = _options.ApiKey;
        if (string.IsNullOrWhiteSpace(key))
        {
            key = Environment.GetEnvironmentVariable(Constants.Discovery.EnvKey);
        }

        if (!string.IsNullOrWhiteSpace(key))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        }
    }

    private async IAsyncEnumerable<ChatCompletionChunk?> ReadServerSentEvents(HttpResponseMessage response, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var reader = new StreamReader(stream, Encoding.UTF8);

        while (!ct.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync().ConfigureAwait(false);
            if (line is null)
            {
                yield break;
            }

            if (line.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            {
                var payload = line.Substring("data:".Length).Trim();
                if (payload == "[DONE]")
                {
                    yield break;
                }

                if (string.IsNullOrWhiteSpace(payload))
                {
                    continue;
                }

                ChatCompletionChunk? chunk = null;
                try
                {
                    chunk = JsonConvert.DeserializeObject<ChatCompletionChunk>(payload);
                }
                catch (JsonException ex)
                {
                    Logger.LogDebug(ex, "Failed to deserialize LM Studio stream payload: {Payload}", payload);
                }

                if (chunk != null)
                {
                    yield return chunk;
                }
            }
        }
    }

    private sealed class ChatCompletionResponse
    {
        public string? id { get; set; }
        public string? @object { get; set; }
        public long created { get; set; }
        public string? model { get; set; }
        public List<ChatChoice>? choices { get; set; }
    }

    private sealed class ChatChoice
    {
        public int index { get; set; }
        public ChatMessage? message { get; set; }
        public string? finish_reason { get; set; }
    }

    private sealed class ChatMessage
    {
        public string? role { get; set; }
        public string? content { get; set; }
    }

    private sealed class ChatCompletionChunk
    {
        public string? id { get; set; }
        public string? @object { get; set; }
        public long created { get; set; }
        public string? model { get; set; }
        public List<ChatChoiceDelta>? choices { get; set; }
    }

    private sealed class ChatChoiceDelta
    {
        public int index { get; set; }
        public ChatDelta? delta { get; set; }
        public string? finish_reason { get; set; }
    }

    private sealed class ChatDelta
    {
        public string? role { get; set; }
        public string? content { get; set; }
    }

    private sealed class EmbeddingResponse
    {
        public string? @object { get; set; }
        public List<EmbeddingData>? data { get; set; }
        public string? model { get; set; }
    }

    private sealed class EmbeddingData
    {
        public string? @object { get; set; }
        public int index { get; set; }
        public IList<float>? embedding { get; set; }
    }

    private sealed class ModelsResponse
    {
        public string? @object { get; set; }
        public List<ModelItem>? data { get; set; }
    }

    private sealed class ModelItem
    {
        public string? id { get; set; }
        public string? owned_by { get; set; }
    }
}

