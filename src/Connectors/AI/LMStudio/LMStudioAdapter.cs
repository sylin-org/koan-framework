using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Koan.AI.Contracts.Adapters;
using Koan.AI.Contracts.Models;
using Koan.AI.Contracts.Sources;
using Koan.Core.Adapters;
using Koan.AI.Contracts;
using Koan.AI.Connector.LMStudio.Options;
using Koan.AI.Connector.LMStudio.Infrastructure;

namespace Koan.AI.Connector.LMStudio;

internal sealed class LMStudioAdapter :
    IChatAdapter,
    IEmbedAdapter,
    IAiSourceInspector,
    IAdapterReadiness,
    IAdapterReadinessConfiguration,
    IAsyncAdapterInitializer,
    IDisposable
{
    private readonly HttpClient _http;
    private readonly ILogger _logger;
    private readonly LMStudioOptions _options;
    private readonly string _defaultModel;
    private readonly AdapterReadinessConfiguration _readiness;
    private readonly AdaptersReadinessOptions _readinessDefaults;
    private readonly ReadinessStateManager _stateManager = new();
    private readonly object _initGate = new();
    private Task? _initializationTask;
    private readonly ConcurrentDictionary<string, HttpClient> _endpointClients =
        new(StringComparer.OrdinalIgnoreCase);
    private int _disposed;

    private string AdapterId => Constants.Adapter.Type;
    public string DisplayName => "LM Studio AI Provider";

    public string Id => AdapterId;
    public string Name => DisplayName;
    public string Type => Constants.Adapter.Type;
    public IAiModelManager? ModelManager => null;

    /// <summary>AI-level capabilities declared for AdapterResolver routing.</summary>
    IReadOnlySet<string> IAiAdapter.Capabilities { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        AiCapability.Chat,
        AiCapability.Embed,
        AiCapability.Streaming,
        AiCapability.ModelList,
        AiCapability.ServeGGUF,
    };

    public LMStudioAdapter(
        HttpClient http,
        ILogger<LMStudioAdapter> logger,
        AdaptersReadinessOptions? readinessDefaults = null,
        LMStudioOptions? resolvedOptions = null)
    {
        _http = http;
        _logger = logger;
        _readinessDefaults = readinessDefaults ?? new AdaptersReadinessOptions();
        _options = resolvedOptions ?? new LMStudioOptions();
        _readiness = (AdapterReadinessConfiguration)(_options.Readiness ?? new AdapterReadinessConfiguration());

        if (_readiness.Timeout <= TimeSpan.Zero)
        {
            _readiness.Timeout = _readinessDefaults.DefaultTimeout;
        }

        _defaultModel = _options.DefaultModel ?? "";

        ApplyConfiguredBaseAddress();

        _stateManager.StateChanged += (_, args) => ReadinessStateChanged?.Invoke(this, args);
    }

    public bool CanServe(AiChatRequest request)
    {
        return true;
    }

    public async Task<AiChatResponse> Chat(AiChatRequest request, CancellationToken ct = default)
    {
        var httpClient = GetHttpClientForRequest(request.InternalConnectionString);
        var model = ResolveModel(request.Model);

        var payload = BuildChatPayload(request, model, stream: false);
        using var message = new HttpRequestMessage(HttpMethod.Post, Constants.Discovery.ChatPath)
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };

        AttachAuth(message);

        using var response = await httpClient.SendAsync(message, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("LM Studio chat request failed ({Status}): {Body}", (int)response.StatusCode, body);
            response.EnsureSuccessStatusCode();
        }

        var doc = JsonConvert.DeserializeObject<ChatCompletionResponse>(body)
                  ?? throw new InvalidOperationException("LM Studio returned an empty response.");

        var first = doc.choices?.FirstOrDefault();
        var text = first?.message?.content ?? "";

        return new AiChatResponse
        {
            Text = text,
            FinishReason = first?.finish_reason,
            Model = doc.model,
            AdapterId = AdapterId
        };
    }

    public async IAsyncEnumerable<AiChatChunk> Stream(
        AiChatRequest request,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        await WaitForReadiness(null, ct);

        var httpClient = GetHttpClientForRequest(request.InternalConnectionString);
        var model = ResolveModel(request.Model);

        var payload = BuildChatPayload(request, model, stream: true);
        using var message = new HttpRequestMessage(HttpMethod.Post, Constants.Discovery.ChatPath)
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };

        message.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        AttachAuth(message);

        using var response = await httpClient.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        await foreach (var chunk in ReadServerSentEvents(response, ct))
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

    public async Task<AiEmbeddingsResponse> Embed(AiEmbeddingsRequest request, CancellationToken ct = default)
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

        using var response = await httpClient.SendAsync(message, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("LM Studio embeddings request failed ({Status}): {Body}", (int)response.StatusCode, body);
            response.EnsureSuccessStatusCode();
        }

        var doc = JsonConvert.DeserializeObject<EmbeddingResponse>(body)
                  ?? throw new InvalidOperationException("LM Studio returned an empty embedding response.");

        var vectors = doc.data?.Select(d => d.embedding?.ToArray() ?? []).ToList() ?? new List<float[]>();
        var dimension = vectors.FirstOrDefault()?.Length ?? 0;

        return new AiEmbeddingsResponse
        {
            Vectors = vectors,
            Model = doc.model,
            Dimension = dimension
        };
    }

    public async Task<IReadOnlyList<AiModelDescriptor>> ListModels(CancellationToken ct = default)
        => await ListModels(GetHttpClientForRequest(null), ct);

    public async Task<AiSourceInspection> InspectAsync(
        AiSourceCandidate candidate,
        CancellationToken ct = default)
    {
        try
        {
            var models = await ListModels(GetHttpClientForRequest(candidate.Endpoint), ct);
            return new AiSourceInspection
            {
                Provider = Type,
                Endpoint = candidate.Endpoint,
                Available = true,
                Models = models.Select(model => model.Name).Where(name => name.Length > 0).ToArray(),
                ModelsAvailable = true,
                Capabilities = new HashSet<string>(
                    ((IAiAdapter)this).Capabilities,
                    StringComparer.OrdinalIgnoreCase)
            };
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new AiSourceInspection
            {
                Provider = Type,
                Endpoint = candidate.Endpoint,
                Available = false,
                Capabilities = new HashSet<string>(
                    ((IAiAdapter)this).Capabilities,
                    StringComparer.OrdinalIgnoreCase),
                Detail = ex.Message
            };
        }
    }

    private async Task<IReadOnlyList<AiModelDescriptor>> ListModels(
        HttpClient httpClient,
        CancellationToken ct)
    {
        using var message = new HttpRequestMessage(HttpMethod.Get, Constants.Discovery.ModelsPath);
        AttachAuth(message);

        using var response = await httpClient.SendAsync(message, ct);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync(ct);

        var doc = JsonConvert.DeserializeObject<ModelsResponse>(body);
        if (doc?.data is null)
        {
            return [];
        }

        return doc.data.Select(m => new AiModelDescriptor
        {
            Name = m.id ?? "",
            Family = m.owned_by,
            AdapterId = AdapterId,
            AdapterType = Type
        }).ToList();
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

    public async Task WaitForReadiness(TimeSpan? timeout = null, CancellationToken ct = default)
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
            await _stateManager.Wait(effective, ct);
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

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        ApplyConfiguredBaseAddress();
        _ = EnsureInitializationStarted();

        try
        {
            await WaitForReadiness(ReadinessTimeout, cancellationToken);
        }
        catch (AdapterNotReadyException ex)
        {
            _logger.LogWarning(ex, "[{AdapterId}] LM Studio adapter not ready after initialization (state={State})", AdapterId, ReadinessState);
        }
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
                task = _initializationTask = InitializeReadiness();
            }
        }

        return task;
    }

    private async Task InitializeReadiness()
    {
        ApplyConfiguredBaseAddress();

        try
        {
            using var timeoutCts = new CancellationTokenSource();
            if (ReadinessTimeout > TimeSpan.Zero)
            {
                timeoutCts.CancelAfter(ReadinessTimeout);
            }

            await VerifyModelsEndpoint(timeoutCts.Token);
            var defaultReady = await EnsureDefaultModelAvailable(timeoutCts.Token);

            var newState = defaultReady ? AdapterReadinessState.Ready : AdapterReadinessState.Degraded;
            _stateManager.TransitionTo(newState);
        }
        catch (OperationCanceledException ex)
        {
            _stateManager.TransitionTo(AdapterReadinessState.Failed);
            _logger.LogError(ex, "[{AdapterId}] LM Studio readiness timed out after {Timeout}", AdapterId, ReadinessTimeout);
            throw new InvalidOperationException($"LM Studio readiness timed out after {ReadinessTimeout}.", ex);
        }
        catch (Exception ex)
        {
            _stateManager.TransitionTo(AdapterReadinessState.Failed);
            _logger.LogError(ex, "[{AdapterId}] LM Studio readiness failed", AdapterId);
            throw;
        }
    }

    private async Task VerifyModelsEndpoint(CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, Constants.Discovery.ModelsPath);
        AttachAuth(request);

        using var response = await _http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
    }

    private async Task<bool> EnsureDefaultModelAvailable(CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_defaultModel))
        {
            return true;
        }

        try
        {
            var models = await ListModels(ct);
            return models.Any(m => string.Equals(m.Name, _defaultModel, StringComparison.OrdinalIgnoreCase) ||
                                   string.Equals(m.Family, _defaultModel, StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[{AdapterId}] Failed to verify default model '{Model}'", AdapterId, _defaultModel);
            return false;
        }
    }

    private HttpClient GetHttpClientForRequest(string? connectionString)
    {
        if (!string.IsNullOrWhiteSpace(connectionString))
        {
            var timeoutSeconds = _options.RequestTimeoutSeconds > 0 ? _options.RequestTimeoutSeconds : 120;
            var endpoint = NormalizeBase(connectionString);
            if (_http.BaseAddress is not null &&
                string.Equals(
                    NormalizeBase(_http.BaseAddress.ToString()),
                    endpoint,
                    StringComparison.OrdinalIgnoreCase))
            {
                return _http;
            }

            return _endpointClients.GetOrAdd(endpoint, value => new HttpClient
            {
                BaseAddress = new Uri(value),
                Timeout = TimeSpan.FromSeconds(timeoutSeconds)
            });
        }

        return _http;
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        foreach (var client in _endpointClients.Values) client.Dispose();
        _endpointClients.Clear();
        _http.Dispose();
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
            _logger.LogWarning("LM Studio adapter: Ignoring invalid base URL '{BaseUrl}'", configured);
            return;
        }

        if (_http.BaseAddress == null || !_http.BaseAddress.Equals(resolved))
        {
            _http.BaseAddress = resolved;
        }
    }

    private string? ResolveConfiguredBaseUrl()
    {
        var endpoint = _options.Endpoints
            .FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value));
        return string.IsNullOrWhiteSpace(endpoint) ? null : NormalizeBase(endpoint);
    }

    internal void SetDefaultEndpoint(Uri? endpoint)
    {
        if (endpoint is null) return;
        var normalized = new Uri(NormalizeBase(endpoint.ToString()), UriKind.Absolute);
        if (_http.BaseAddress is null || !_http.BaseAddress.Equals(normalized))
        {
            _http.BaseAddress = normalized;
        }
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
                ["text"] = p.Text ?? ""
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
        if (!string.IsNullOrWhiteSpace(key))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        }
    }

    private async IAsyncEnumerable<ChatCompletionChunk?> ReadServerSentEvents(HttpResponseMessage response, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream, Encoding.UTF8);

        while (!ct.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync();
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
                    _logger.LogDebug(ex, "Failed to deserialize LM Studio stream payload: {Payload}", payload);
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
