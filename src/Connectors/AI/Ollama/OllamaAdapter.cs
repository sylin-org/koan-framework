using System;
using System.Collections.Generic;
using System.Diagnostics;
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
    private static readonly TimeSpan ConcurrencyWaitLogThreshold = TimeSpan.FromSeconds(1);
    private readonly HttpClient _http;
    private readonly OllamaOptions _options;
    private readonly string _defaultModel;
    private readonly AdapterReadinessConfiguration _readiness;
    private readonly AdaptersReadinessOptions _readinessDefaults;
    private readonly ReadinessStateManager _stateManager = new();
    private readonly OllamaModelManager _modelManager;
    private readonly SemaphoreSlim? _concurrencyLimiter;
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
        AdaptersReadinessOptions? readinessDefaults = null,
        OllamaOptions? resolvedOptions = null)
        : base(logger, configuration)
    {
        _http = http;
        _readinessDefaults = readinessDefaults ?? new AdaptersReadinessOptions();
        _options = resolvedOptions ?? GetOptions<OllamaOptions>();

        Logger.LogDebug("Ollama adapter: Constructor called - HttpClient.BaseAddress={BaseAddress}",
            http.BaseAddress?.ToString() ?? "(null)");

        ApplyConfiguredBaseAddress();

        var serviceDefault = GetServiceDefaultModel();
        _readiness = (AdapterReadinessConfiguration)(_options.Readiness ?? new AdapterReadinessConfiguration());
        if (_readiness.Timeout <= TimeSpan.Zero)
        {
            _readiness.Timeout = _readinessDefaults.DefaultTimeout;
        }

        _defaultModel = _options.DefaultModel ?? serviceDefault ?? "all-minilm";
        _modelManager = new OllamaModelManager(_http, Logger, AdapterId, Type);

        if (_options.MaxConcurrentRequests > 0 && _options.MaxConcurrentRequests < int.MaxValue)
        {
            _concurrencyLimiter = new SemaphoreSlim(_options.MaxConcurrentRequests, _options.MaxConcurrentRequests);
            Logger.LogInformation(
                "Ollama adapter concurrency limited to {Limit} simultaneous requests",
                _options.MaxConcurrentRequests);
        }

        Logger.LogDebug("Ollama adapter: Configuration - BaseUrl={BaseUrl} DefaultModel={DefaultModel}",
            _options.BaseUrl ?? "(null)",
            _defaultModel);

        Logger.LogInformation(
            "Ollama adapter '{AdapterId}' model resolution: options.DefaultModel='{OptionsDefault}', serviceDefault='{ServiceDefault}', final='{Final}'",
            Id,
            _options.DefaultModel,
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

    /// <summary>
    /// ADR-0015: Get HttpClient for request - uses InternalConnectionString if set by router, otherwise default.
    /// </summary>
    private HttpClient GetHttpClientForRequest(string? connectionString)
    {
        if (!string.IsNullOrWhiteSpace(connectionString))
        {
            // Router specified member URL - create client for it with configured timeout
            var timeoutSeconds = _options.RequestTimeoutSeconds > 0 ? _options.RequestTimeoutSeconds : 180;
            return new HttpClient { BaseAddress = new Uri(connectionString), Timeout = TimeSpan.FromSeconds(timeoutSeconds) };
        }

        // Use default HttpClient from DI
        return _http;
    }

    private async Task<SemaphoreReleaser?> AcquireConcurrencySlotAsync(CancellationToken ct)
    {
        if (_concurrencyLimiter is null)
        {
            return null;
        }

        var stopwatch = Stopwatch.StartNew();
        await _concurrencyLimiter.WaitAsync(ct).ConfigureAwait(false);
        stopwatch.Stop();

        if (stopwatch.Elapsed > ConcurrencyWaitLogThreshold && Logger.IsEnabled(LogLevel.Debug))
        {
            Logger.LogDebug(
                "Ollama concurrency gating waited {Elapsed} before issuing request",
                stopwatch.Elapsed);
        }

        return new SemaphoreReleaser(_concurrencyLimiter);
    }

    private sealed class SemaphoreReleaser : IDisposable
    {
        private readonly SemaphoreSlim _semaphore;
        private bool _disposed;

        public SemaphoreReleaser(SemaphoreSlim semaphore)
        {
            _semaphore = semaphore;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _semaphore.Release();
            _disposed = true;
        }
    }

    public async Task<AiChatResponse> ChatAsync(AiChatRequest request, CancellationToken ct = default)
    {
        SemaphoreReleaser? lease = null;
        try
        {
            lease = await AcquireConcurrencySlotAsync(ct).ConfigureAwait(false);

            // ADR-0015: Router handles member health selection - skip singleton readiness check
            var http = GetHttpClientForRequest(request.InternalConnectionString);
            var model = request.Model ?? _defaultModel;
            if (string.IsNullOrWhiteSpace(model))
            {
                throw new InvalidOperationException("Ollama adapter requires a model name.");
            }

            var (prompt, imageBase64List) = BuildPromptWithImages(request);
            var body = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["model"] = model,
                ["prompt"] = prompt,
                ["stream"] = false,
                ["options"] = MapOptions(request.Options)
            };

            // Add images array for vision models (Ollama API format)
            if (imageBase64List.Count > 0)
            {
                body["images"] = imageBase64List;
                Logger.LogDebug("Ollama: Including {Count} image(s) in vision request", imageBase64List.Count);
            }

            if (request.Options?.Think is bool thinkFlag)
            {
                body["think"] = thinkFlag;
            }

            Logger.LogDebug("Ollama: POST {Path} model={Model}", "/api/generate", model);
            var payload = JsonConvert.SerializeObject(body, new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore });
            using var resp = await http.PostAsync("/api/generate", new StringContent(payload, Encoding.UTF8, "application/json"), ct).ConfigureAwait(false);
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
        finally
        {
            lease?.Dispose();
        }
    }

    public async IAsyncEnumerable<AiChatChunk> StreamAsync(
        AiChatRequest request,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        SemaphoreReleaser? lease = null;
        try
        {
            lease = await AcquireConcurrencySlotAsync(ct).ConfigureAwait(false);

            await WaitForReadinessAsync(null, ct).ConfigureAwait(false);

            var http = GetHttpClientForRequest(request.InternalConnectionString);
            var model = request.Model ?? _defaultModel;
            if (string.IsNullOrWhiteSpace(model))
            {
                throw new InvalidOperationException("Ollama adapter requires a model name.");
            }

            var (prompt, imageBase64List) = BuildPromptWithImages(request);
            var streamBody = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["model"] = model,
                ["prompt"] = prompt,
                ["stream"] = true,
                ["options"] = MapOptions(request.Options)
            };

            // Add images array for vision models (Ollama API format)
            if (imageBase64List.Count > 0)
            {
                streamBody["images"] = imageBase64List;
                Logger.LogDebug("Ollama: Including {Count} image(s) in streaming vision request", imageBase64List.Count);
            }

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
            using var resp = await http.SendAsync(httpReq, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
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
        finally
        {
            lease?.Dispose();
        }
    }

    public async Task<AiEmbeddingsResponse> EmbedAsync(AiEmbeddingsRequest request, CancellationToken ct = default)
    {
        SemaphoreReleaser? lease = null;
        try
        {
            lease = await AcquireConcurrencySlotAsync(ct).ConfigureAwait(false);

            // ADR-0015: Router handles member health selection - skip singleton readiness check
            var http = GetHttpClientForRequest(request.InternalConnectionString);
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

                using var resp = await http.PostAsync("/api/embeddings", new StringContent(payload, Encoding.UTF8, "application/json"), ct).ConfigureAwait(false);
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
        }
        finally
        {
            lease?.Dispose();
        }
    }

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

        ApplyConfiguredBaseAddress();
        Logger.LogDebug("[{AdapterId}] InitializeAdapter: Effective BaseAddress={BaseAddress}",
            AdapterId, _http.BaseAddress?.ToString() ?? "(null)");

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

        ApplyConfiguredBaseAddress();

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
        ApplyConfiguredBaseAddress();

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

            // Ensure all required models are available (not just the default)
            var allModelsReady = await EnsureRequiredModelsAvailabilityAsync(timeoutCts.Token).ConfigureAwait(false);
            var newState = allModelsReady ? AdapterReadinessState.Ready : AdapterReadinessState.Degraded;
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

    private async Task<bool> EnsureRequiredModelsAvailabilityAsync(CancellationToken ct)
    {
        // Get RequiredModels from config (Koan:Ai:Ollama:RequiredModels)
        var requiredModels = Configuration.GetSection("Koan:Ai:Ollama:RequiredModels").Get<string[]>();

        // Build complete list: RequiredModels + default model (if not already in list)
        var modelsToEnsure = new List<string>();

        if (requiredModels != null && requiredModels.Length > 0)
        {
            modelsToEnsure.AddRange(requiredModels);
        }

        // Add default model if not already in required list
        if (!string.IsNullOrWhiteSpace(_defaultModel) && !modelsToEnsure.Contains(_defaultModel, StringComparer.OrdinalIgnoreCase))
        {
            modelsToEnsure.Add(_defaultModel);
        }

        if (modelsToEnsure.Count == 0)
        {
            Logger.LogDebug("[{AdapterId}] No required models configured, skipping model installation", AdapterId);
            return true;
        }

        Logger.LogInformation("[{AdapterId}] Ensuring {Count} required model(s) are available: {Models}",
            AdapterId, modelsToEnsure.Count, string.Join(", ", modelsToEnsure));

        var allSucceeded = true;
        var installedCount = 0;

        foreach (var model in modelsToEnsure)
        {
            var provenance = new AiModelProvenance
            {
                RequestedBy = AdapterId,
                Reason = "readiness:required-model",
                RequestedAt = DateTimeOffset.UtcNow,
                Metadata = new Dictionary<string, string>
                {
                    ["adapter_id"] = AdapterId,
                    ["phase"] = "readiness"
                }
            };

            var request = new AiModelOperationRequest
            {
                Model = model,
                Provenance = provenance
            };

            var result = await _modelManager.EnsureInstalledAsync(request, ct).ConfigureAwait(false);

            if (!result.Success)
            {
                Logger.LogWarning("[{AdapterId}] Failed to ensure model '{Model}' is available: {Message}",
                    AdapterId, model, result.Message);
                allSucceeded = false;
            }
            else if (result.OperationPerformed)
            {
                Logger.LogInformation("[{AdapterId}] Installed model '{Model}' during readiness initialization",
                    AdapterId, model);
                installedCount++;
            }
            else
            {
                Logger.LogDebug("[{AdapterId}] Model '{Model}' already available", AdapterId, model);
            }
        }

        if (installedCount > 0)
        {
            Logger.LogInformation("[{AdapterId}] Successfully installed {InstalledCount} of {TotalCount} required models",
                AdapterId, installedCount, modelsToEnsure.Count);
        }

        return allSucceeded;
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

    /// <summary>
    /// Build prompt and extract images for Ollama vision API
    /// </summary>
    private static (string prompt, List<string> images) BuildPromptWithImages(AiChatRequest req)
    {
        var images = new List<string>();

        // Single message optimization
        if (req.Messages.Count == 1 && string.Equals(req.Messages[0].Role, "user", StringComparison.OrdinalIgnoreCase))
        {
            var (content, messageImages) = ResolveMessageContentWithImages(req.Messages[0]);
            images.AddRange(messageImages);
            return (content, images);
        }

        // Multi-message conversation
        var sb = new StringBuilder();
        foreach (var m in req.Messages)
        {
            var (content, messageImages) = ResolveMessageContentWithImages(m);
            images.AddRange(messageImages);

            if (string.Equals(m.Role, "system", StringComparison.OrdinalIgnoreCase))
            {
                sb.AppendLine($"[system]\n{content}\n");
            }
            else if (string.Equals(m.Role, "user", StringComparison.OrdinalIgnoreCase))
            {
                sb.AppendLine($"[user]\n{content}\n");
            }
            else if (string.Equals(m.Role, "assistant", StringComparison.OrdinalIgnoreCase))
            {
                sb.AppendLine($"[assistant]\n{content}\n");
            }
        }

        return (sb.ToString(), images);
    }

    /// <summary>
    /// Extract text content and image data from a message
    /// </summary>
    private static (string content, List<string> images) ResolveMessageContentWithImages(AiMessage message)
    {
        var images = new List<string>();

        if (message.Parts is { Count: > 0 })
        {
            var textBuilder = new StringBuilder();
            foreach (var part in message.Parts)
            {
                // Handle text parts
                if (!string.IsNullOrWhiteSpace(part.Text))
                {
                    textBuilder.Append(part.Text);
                    continue;
                }

                // Handle data parts (could be images)
                if (part.Data is not null)
                {
                    // Check if it's byte array (image data)
                    if (part.Data is byte[] imageBytes)
                    {
                        var base64 = Convert.ToBase64String(imageBytes);
                        images.Add(base64);
                    }
                    // Check if it's already a base64 string
                    else if (part.Data is string str && !string.IsNullOrWhiteSpace(str))
                    {
                        // Only add as image if it looks like base64 (not regular text)
                        if (str.Length > 100 && !str.Contains(' '))
                        {
                            images.Add(str);
                        }
                        else
                        {
                            textBuilder.Append(str);
                        }
                    }
                    else
                    {
                        // Unknown data type - serialize as JSON for backward compatibility
                        textBuilder.Append(JsonConvert.SerializeObject(part.Data));
                    }
                }
            }

            var text = textBuilder.ToString();
            if (!string.IsNullOrWhiteSpace(text))
            {
                return (text, images);
            }
        }

        return (message.Content, images);
    }

    private static string BuildPrompt(AiChatRequest req)
    {
        var (prompt, _) = BuildPromptWithImages(req);
        return prompt;
    }

    private static string ResolveMessageContent(AiMessage message)
    {
        var (content, _) = ResolveMessageContentWithImages(message);
        return content;
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
        while (!ct.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync().ConfigureAwait(false);
            if (line is null) break; // EOF
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

    private void ApplyConfiguredBaseAddress()
    {
        var configured = ResolveConfiguredBaseUrl();
        if (string.IsNullOrWhiteSpace(configured))
        {
            return;
        }

        if (!Uri.TryCreate(configured, UriKind.Absolute, out var resolved))
        {
            Logger.LogWarning("Ollama adapter: Ignoring invalid base URL '{BaseUrl}'", configured);
            return;
        }

        var previous = _http.BaseAddress;
        if (previous is null || !previous.Equals(resolved))
        {
            Logger.LogInformation(
                "Ollama adapter: Applying resolved base URL {NewBase} (was {OldBase})",
                resolved,
                previous?.ToString() ?? "(null)");
            _http.BaseAddress = resolved;
        }
    }

    private string? ResolveConfiguredBaseUrl()
    {
        if (!string.IsNullOrWhiteSpace(_options.ConnectionString) &&
            !string.Equals(_options.ConnectionString, "auto", StringComparison.OrdinalIgnoreCase))
        {
            return _options.ConnectionString;
        }

        if (!string.IsNullOrWhiteSpace(_options.BaseUrl) &&
            !string.Equals(_options.BaseUrl, "auto", StringComparison.OrdinalIgnoreCase))
        {
            return _options.BaseUrl;
        }

        var legacy = GetConnectionString();
        if (!string.IsNullOrWhiteSpace(legacy) &&
            !string.Equals(legacy, "auto", StringComparison.OrdinalIgnoreCase))
        {
            return legacy;
        }

        return null;
    }
}

