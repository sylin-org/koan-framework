using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Koan.AI.Contracts.Adapters;
using Koan.AI.Contracts.Models;
using Koan.Core.Adapters;
using Koan.Orchestration;
using Koan.Orchestration.Attributes;
using Koan.Orchestration.Models;
using Koan.AI.Connector.Ollama.Options;
using Koan.AI.Connector.Ollama.Infrastructure;

namespace Koan.AI.Connector.Ollama;

[AiAdapterDescriptor(priority: 10, Weight = 2)]
internal sealed class OllamaAdapter : BaseKoanAdapter,
    IAiAdapter,
    IAdapterReadiness,
    IAdapterReadinessConfiguration,
    IAsyncAdapterInitializer
{
    private readonly HttpClient _http;
    private readonly string _defaultModel;
    private readonly AdapterReadinessConfiguration _readiness;
    private readonly AdaptersReadinessOptions _readinessDefaults;
    private readonly ReadinessStateManager _stateManager = new();
    private readonly OllamaModelManager _modelManager;
    private readonly object _initGate = new();
    private Task? _initializationTask;
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
    public IAiModelManager? ModelManager => _modelManager;

    public OllamaAdapter(
        HttpClient http,
        ILogger<OllamaAdapter> logger,
        IConfiguration configuration,
        AdaptersReadinessOptions? readinessDefaults = null)
        : base(logger, configuration)
    {
        _http = http;
        _readinessDefaults = readinessDefaults ?? new AdaptersReadinessOptions();

        Logger.LogDebug("Ollama adapter: Constructor called - HttpClient.BaseAddress={BaseAddress}",
            http.BaseAddress?.ToString() ?? "(null)");

        var options = GetOptions<OllamaOptions>();
        var serviceDefault = GetServiceDefaultModel();
        _readiness = (AdapterReadinessConfiguration)(options.Readiness ?? new AdapterReadinessConfiguration());
        if (_readiness.Timeout <= TimeSpan.Zero)
        {
            _readiness.Timeout = _readinessDefaults.DefaultTimeout;
        }

        _defaultModel = options.DefaultModel ?? serviceDefault ?? "all-minilm";
        _modelManager = new OllamaModelManager(_http, Logger, AdapterId, Type);

        Logger.LogDebug("Ollama adapter: Configuration - BaseUrl={BaseUrl} DefaultModel={DefaultModel}",
            options.BaseUrl ?? "(null)",
            _defaultModel);

        Logger.LogInformation(
            "Ollama adapter '{AdapterId}' model resolution: options.DefaultModel='{OptionsDefault}', serviceDefault='{ServiceDefault}', final='{Final}'",
            Id,
            options.DefaultModel,
            serviceDefault,
            _defaultModel);

        _stateManager.StateChanged += (_, args) =>
        {
            if (args.CurrentState == AdapterReadinessState.Degraded)
            {
                Logger.LogWarning(
                    "[{AdapterId}] Readiness degraded: default model '{Model}' is unavailable",
                    AdapterId,
                    _defaultModel);
            }

            ReadinessStateChanged?.Invoke(this, args);
        };
    }

    public bool CanServe(AiChatRequest request)
    {
        // Accept if no model specified or model looks like an ollama tag; be permissive by default.
        return true;
    }

    public async Task<AiChatResponse> ChatAsync(AiChatRequest request, CancellationToken ct = default)
        => await this.WithReadinessAsync(async () =>
        {
            var model = request.Model ?? _defaultModel;
            if (string.IsNullOrWhiteSpace(model))
            {
                throw new InvalidOperationException("Ollama adapter requires a model name.");
            }

            var prompt = BuildPrompt(request);
            var body = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["model"] = model,
                ["prompt"] = prompt,
                ["stream"] = false,
                ["options"] = MapOptions(request.Options)
            };

            if (request.Options?.Think is bool thinkFlag)
            {
                body["think"] = thinkFlag;
            }

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
        }, ct).ConfigureAwait(false);

    public async IAsyncEnumerable<AiChatChunk> StreamAsync(
        AiChatRequest request,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        await WaitForReadinessAsync(null, ct).ConfigureAwait(false);

        var model = request.Model ?? _defaultModel;
        if (string.IsNullOrWhiteSpace(model))
        {
            throw new InvalidOperationException("Ollama adapter requires a model name.");
        }

        var prompt = BuildPrompt(request);
        var streamBody = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["model"] = model,
            ["prompt"] = prompt,
            ["stream"] = true,
            ["options"] = MapOptions(request.Options)
        };

        if (request.Options?.Think is bool thinkFlag)
        {
            streamBody["think"] = thinkFlag;
        }

        var body = JsonConvert.SerializeObject(streamBody, new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore });
        using var httpReq = new HttpRequestMessage(HttpMethod.Post, "/api/generate")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };

        Logger.LogDebug("Ollama: STREAM {Path} model={Model}", "/api/generate", model);
        using var resp = await _http.SendAsync(httpReq, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();

        await foreach (var part in ReadJsonLinesAsync<OllamaGenerateResponse>(resp, ct).ConfigureAwait(false))
        {
            if (part is null)
            {
                continue;
            }

            if (!string.IsNullOrEmpty(part.response))
            {
                yield return new AiChatChunk { DeltaText = part.response };
            }
        }
    }

    public async Task<AiEmbeddingsResponse> EmbedAsync(AiEmbeddingsRequest request, CancellationToken ct = default)
        => await this.WithReadinessAsync(async () =>
        {
            var model = request.Model ?? _defaultModel;
            if (string.IsNullOrWhiteSpace(model))
            {
                throw new InvalidOperationException("Ollama adapter requires a model name.");
            }

            var vectors = new List<float[]>();
            foreach (var input in request.Input)
            {
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

            var dimension = vectors.FirstOrDefault()?.Length ?? 0;
            return new AiEmbeddingsResponse { Vectors = vectors, Model = model, Dimension = dimension };
        }, ct).ConfigureAwait(false);

    public async Task<IReadOnlyList<AiModelDescriptor>> ListModelsAsync(CancellationToken ct = default)
        => await this.WithReadinessAsync(async () =>
        {
            var doc = await FetchTagsAsync(ct).ConfigureAwait(false);
            var models = new List<AiModelDescriptor>();

            foreach (var model in doc?.models ?? Enumerable.Empty<OllamaTag>())
            {
                models.Add(new AiModelDescriptor
                {
                    Name = model.name ?? string.Empty,
                    Family = model.model,
                    AdapterId = Id,
                    AdapterType = Type
                });
            }

            return (IReadOnlyList<AiModelDescriptor>)models;
        }, ct).ConfigureAwait(false);

    public Task<AiCapabilities> GetCapabilitiesAsync(CancellationToken ct = default)
    {
        var modelMetadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["default_model"] = _defaultModel,
            ["supports_pull"] = "true"
        };

        return Task.FromResult(new AiCapabilities
        {
            AdapterId = Id,
            AdapterType = Type,
            SupportsChat = true,
            SupportsStreaming = true,
            SupportsEmbeddings = true,
            ModelManagement = new AiModelManagementCapabilities
            {
                SupportsInstall = true,
                SupportsRemove = true,
                SupportsRefresh = true,
                SupportsProvenance = true,
                ProvisioningModes = new[] { "pull" },
                ProviderMetadata = modelMetadata
            }
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
            throw new AdapterNotReadyException(AdapterId, _stateManager.State, "Ollama readiness initialization failed.", initTask.Exception?.GetBaseException());
        }

        var effective = timeout ?? ReadinessTimeout;
        if (effective <= TimeSpan.Zero)
        {
            throw new AdapterNotReadyException(AdapterId, _stateManager.State, "Ollama readiness timeout is zero; readiness gating cannot wait.");
        }

        try
        {
            await _stateManager.WaitAsync(effective, ct).ConfigureAwait(false);
        }
        catch (TimeoutException ex)
        {
            throw new AdapterNotReadyException(AdapterId, _stateManager.State, $"Timed out waiting for Ollama readiness after {effective}.", ex);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (initTask.IsFaulted)
        {
            throw new AdapterNotReadyException(AdapterId, _stateManager.State, "Ollama adapter failed during readiness initialization.", initTask.Exception?.GetBaseException() ?? ex);
        }

        if (!_stateManager.IsReady)
        {
            throw new AdapterNotReadyException(AdapterId, _stateManager.State, "Ollama adapter is not ready after waiting for readiness.");
        }
    }

    public ReadinessPolicy Policy => _readiness.Policy;

    public TimeSpan Timeout => _readiness.Timeout > TimeSpan.Zero ? _readiness.Timeout : _readinessDefaults.DefaultTimeout;

    public bool EnableReadinessGating => _readiness.EnableReadinessGating;

    protected override async Task InitializeAdapterAsync(CancellationToken cancellationToken = default)
    {
        Logger.LogDebug("[{AdapterId}] InitializeAdapter: Start - CurrentBaseAddress={CurrentBaseAddress}",
            AdapterId, _http.BaseAddress?.ToString() ?? "(null)");

        var baseUrl = GetConnectionString();
        Logger.LogDebug("[{AdapterId}] InitializeAdapter: GetConnectionString returned {ConnectionString}",
            AdapterId, baseUrl ?? "(null)");

        if (!string.IsNullOrEmpty(baseUrl) && _http.BaseAddress == null)
        {
            _http.BaseAddress = new Uri(baseUrl);
            Logger.LogInformation("[{AdapterId}] InitializeAdapter: Set BaseAddress to {BaseUrl}", AdapterId, baseUrl);
        }
        else if (!string.IsNullOrEmpty(baseUrl))
        {
            Logger.LogDebug("[{AdapterId}] InitializeAdapter: BaseAddress already set to {Existing}, ignoring GetConnectionString result {New}",
                AdapterId, _http.BaseAddress, baseUrl);
        }
        else
        {
            Logger.LogDebug("[{AdapterId}] InitializeAdapter: No connection string found, using existing BaseAddress {BaseAddress}",
                AdapterId, _http.BaseAddress?.ToString() ?? "(null)");
        }

        _ = EnsureInitializationStarted();

        try
        {
            await WaitForReadinessAsync(ReadinessTimeout, cancellationToken).ConfigureAwait(false);
            Logger.LogInformation("[{AdapterId}] Ollama connection established - FinalBaseUrl={BaseUrl}",
                AdapterId, _http.BaseAddress);
        }
        catch (AdapterNotReadyException ex)
        {
            Logger.LogWarning(ex, "[{AdapterId}] Ollama adapter not ready after initialization (state={State}) - BaseUrl={BaseUrl}",
                AdapterId, ReadinessState, _http.BaseAddress);
        }
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
            await WaitForContainerReadiness(cancellationToken).ConfigureAwait(false);
        }

        _ = EnsureInitializationStarted();
        try
        {
            await WaitForReadinessAsync(ReadinessTimeout, cancellationToken).ConfigureAwait(false);
            Logger.LogInformation("[{AdapterId}] Ollama connection established using orchestration-aware initialization", AdapterId);
        }
        catch (AdapterNotReadyException ex)
        {
            Logger.LogWarning(ex, "[{AdapterId}] Ollama adapter not ready after orchestration initialization (state={State})", AdapterId, ReadinessState);
        }
    }

    protected override async Task<IReadOnlyDictionary<string, object?>?> CheckAdapterHealthAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            using var response = await _http.GetAsync("/", cancellationToken).ConfigureAwait(false);
            stopwatch.Stop();

            var healthData = new Dictionary<string, object?>
            {
                ["status"] = response.IsSuccessStatusCode ? "healthy" : "unhealthy",
                ["response_time_ms"] = stopwatch.ElapsedMilliseconds,
                ["base_url"] = _http.BaseAddress?.ToString(),
                ["default_model"] = _defaultModel,
                ["orchestration_aware"] = _orchestrationContext != null,
                ["readiness_state"] = ReadinessState.ToString()
            };

            if (_orchestrationContext != null)
            {
                healthData["orchestration_mode"] = _orchestrationContext.IsOrchestrationAware ? "managed" : "standalone";
                healthData["capabilities"] = _orchestrationContext.Capabilities;
            }

            try
            {
                var models = await ListModelsAsync(cancellationToken).ConfigureAwait(false);
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
            ["runtime_capabilities"] = Capabilities.GetCapabilitySummary(),
            ["readiness_state"] = ReadinessState.ToString()
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
        Logger.LogDebug("[{AdapterId}] InitializeReadiness: Starting - BaseUrl={BaseUrl} Timeout={Timeout}",
            AdapterId, _http.BaseAddress?.ToString() ?? "(null)", ReadinessTimeout);

        try
        {
            using var timeoutCts = new CancellationTokenSource();
            if (ReadinessTimeout > TimeSpan.Zero)
            {
                timeoutCts.CancelAfter(ReadinessTimeout);
            }

            await TestConnectivityAsync(timeoutCts.Token).ConfigureAwait(false);
            Logger.LogDebug("[{AdapterId}] InitializeReadiness: Connectivity test passed", AdapterId);

            var defaultReady = await EnsureDefaultModelAvailabilityAsync(timeoutCts.Token).ConfigureAwait(false);
            var newState = defaultReady ? AdapterReadinessState.Ready : AdapterReadinessState.Degraded;
            _stateManager.TransitionTo(newState);

            Logger.LogInformation("[{AdapterId}] InitializeReadiness: Complete - State={State} BaseUrl={BaseUrl}",
                AdapterId, newState, _http.BaseAddress);
        }
        catch (OperationCanceledException ex)
        {
            _stateManager.TransitionTo(AdapterReadinessState.Failed);
            Logger.LogError(ex, "[{AdapterId}] InitializeReadiness: Timed out after {Timeout} - BaseUrl={BaseUrl}",
                AdapterId, ReadinessTimeout, _http.BaseAddress);
            throw new InvalidOperationException($"Ollama readiness timed out after {ReadinessTimeout}.", ex);
        }
        catch (Exception ex)
        {
            _stateManager.TransitionTo(AdapterReadinessState.Failed);
            Logger.LogError(ex, "[{AdapterId}] InitializeReadiness: Failed - BaseUrl={BaseUrl}", AdapterId, _http.BaseAddress);
            throw;
        }
    }

    private async Task<bool> EnsureDefaultModelAvailabilityAsync(CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_defaultModel))
        {
            return true;
        }

        var provenance = new AiModelProvenance
        {
            RequestedBy = AdapterId,
            Reason = "readiness:default-model",
            RequestedAt = DateTimeOffset.UtcNow,
            Metadata = new Dictionary<string, string>
            {
                ["adapter_id"] = AdapterId,
                ["phase"] = "readiness"
            }
        };

        var request = new AiModelOperationRequest
        {
            Model = _defaultModel,
            Provenance = provenance
        };

        var result = await _modelManager.EnsureInstalledAsync(request, ct).ConfigureAwait(false);
        if (!result.Success)
        {
            Logger.LogWarning("[{AdapterId}] Failed to ensure default model '{Model}' is available: {Message}", AdapterId, _defaultModel, result.Message);
        }
        else if (result.OperationPerformed)
        {
            Logger.LogInformation("[{AdapterId}] Installed default model '{Model}' during readiness initialization", AdapterId, _defaultModel);
        }

        return result.Success;
    }

    private async Task<OllamaTagsResponse?> FetchTagsAsync(CancellationToken ct)
    {
        using var resp = await _http.GetAsync("/api/tags", ct).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode)
        {
            var text = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            Logger.LogWarning("Ollama: tags failed ({Status}) body={Body}", (int)resp.StatusCode, text);
            resp.EnsureSuccessStatusCode();
        }

        var json = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        return JsonConvert.DeserializeObject<OllamaTagsResponse>(json);
    }

    private static string BuildPrompt(AiChatRequest req)
    {
        if (req.Messages.Count == 1 && string.Equals(req.Messages[0].Role, "user", StringComparison.OrdinalIgnoreCase))
        {
            return ResolveMessageContent(req.Messages[0]);
        }

        var sb = new StringBuilder();
        foreach (var m in req.Messages)
        {
            if (string.Equals(m.Role, "system", StringComparison.OrdinalIgnoreCase))
            {
                sb.AppendLine($"[system]\n{ResolveMessageContent(m)}\n");
            }
            else if (string.Equals(m.Role, "user", StringComparison.OrdinalIgnoreCase))
            {
                sb.AppendLine($"[user]\n{ResolveMessageContent(m)}\n");
            }
            else if (string.Equals(m.Role, "assistant", StringComparison.OrdinalIgnoreCase))
            {
                sb.AppendLine($"[assistant]\n{ResolveMessageContent(m)}\n");
            }
        }

        return sb.ToString();
    }

    private static string ResolveMessageContent(AiMessage message)
    {
        if (message.Parts is { Count: > 0 })
        {
            var builder = new StringBuilder();
            foreach (var part in message.Parts)
            {
                if (!string.IsNullOrWhiteSpace(part.Text))
                {
                    builder.Append(part.Text);
                    continue;
                }

                if (part.Data is not null)
                {
                    builder.Append(JsonConvert.SerializeObject(part.Data));
                }
            }

            var text = builder.ToString();
            if (!string.IsNullOrWhiteSpace(text))
            {
                return text;
            }
        }

        return message.Content;
    }

    private static IDictionary<string, object?> MapOptions(AiPromptOptions? o)
    {
        if (o is null)
        {
            return new Dictionary<string, object?>();
        }

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
                dict[normKey] = kv.Value.Type switch
                {
                    JTokenType.String => kv.Value.Value<string>(),
                    JTokenType.Integer => kv.Value.Value<long>(),
                    JTokenType.Float => kv.Value.Value<double>(),
                    JTokenType.Boolean => kv.Value.Value<bool>(),
                    JTokenType.Array => kv.Value,
                    JTokenType.Object => kv.Value,
                    _ => null
                };
            }
        }

        return dict;
    }

    private static string NormalizeOllamaOptionKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return key;
        }

        var k = key.Trim();
        if (k.Equals("temp", StringComparison.OrdinalIgnoreCase) || k.Equals("temperature", StringComparison.OrdinalIgnoreCase))
        {
            return "temperature";
        }

        if (k.Equals("top_p", StringComparison.OrdinalIgnoreCase) || k.Equals("topp", StringComparison.OrdinalIgnoreCase) || k.Equals("topP", StringComparison.Ordinal))
        {
            return "top_p";
        }

        if (k.Equals("max_tokens", StringComparison.OrdinalIgnoreCase) ||
            k.Equals("max_new_tokens", StringComparison.OrdinalIgnoreCase) ||
            k.Equals("maxOutputTokens", StringComparison.Ordinal) ||
            k.Equals("max_output_tokens", StringComparison.OrdinalIgnoreCase) ||
            k.Equals("num_predict", StringComparison.OrdinalIgnoreCase))
        {
            return "num_predict";
        }

        if (k.Equals("stop_sequences", StringComparison.OrdinalIgnoreCase) ||
            k.Equals("stopSequences", StringComparison.Ordinal) ||
            k.Equals("stops", StringComparison.OrdinalIgnoreCase) ||
            k.Equals("stop", StringComparison.OrdinalIgnoreCase))
        {
            return "stop";
        }

        if (k.Equals("seed", StringComparison.OrdinalIgnoreCase))
        {
            return "seed";
        }

        return k;
    }

    private static async IAsyncEnumerable<T?> ReadJsonLinesAsync<T>(HttpResponseMessage resp, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        await using var stream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        while (!reader.EndOfStream && !ct.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync().ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            T? obj;
            try
            {
                obj = JsonConvert.DeserializeObject<T>(line);
            }
            catch
            {
                continue;
            }

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

    private async Task TestConnectivityAsync(CancellationToken cancellationToken)
    {
        Logger.LogDebug("[{AdapterId}] TestConnectivity: Testing connection to {BaseUrl}",
            AdapterId, _http.BaseAddress?.ToString() ?? "(null)");

        using var response = await _http.GetAsync("/", cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        Logger.LogDebug("[{AdapterId}] TestConnectivity: Success - Status={Status}", AdapterId, response.StatusCode);
    }

    private async Task WaitForContainerReadiness(CancellationToken cancellationToken)
    {
        const int maxRetries = 30;
        const int delayMs = 1000;

        for (int i = 0; i < maxRetries; i++)
        {
            try
            {
                using var response = await _http.GetAsync("/", cancellationToken).ConfigureAwait(false);
                if (response.IsSuccessStatusCode)
                {
                    Logger.LogInformation("[{AdapterId}] Container is ready (attempt {Attempt})", AdapterId, i + 1);
                    return;
                }
            }
            catch (Exception ex) when (i < maxRetries - 1)
            {
                Logger.LogDebug(ex, "[{AdapterId}] Container not ready yet (attempt {Attempt}), retrying...", AdapterId, i + 1);
                await Task.Delay(delayMs, cancellationToken).ConfigureAwait(false);
            }
        }

        throw new InvalidOperationException($"Container failed to become ready after {maxRetries} attempts");
    }

    private string? GetServiceDefaultModel()
    {
        try
        {
            var serviceDescriptorType = typeof(OllamaServiceDescriptor);
            var koanServiceAttribute = serviceDescriptorType.GetCustomAttributes(typeof(KoanServiceAttribute), false)
                .FirstOrDefault() as KoanServiceAttribute;

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

