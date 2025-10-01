using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json.Linq;
using Koan.AI.Connector.Ollama.Options;
using Koan.AI.Contracts.Routing;
using Koan.Core.Adapters;
using Microsoft.Extensions.Options;
using Koan.Core.Orchestration;
using Koan.Core.Logging;

namespace Koan.AI.Connector.Ollama.Initialization;

/// <summary>
/// Orchestration-aware Ollama discovery service using centralized service discovery.
/// Replaces hardcoded candidate logic with unified Koan orchestration patterns.
/// </summary>
internal sealed class OllamaDiscoveryService : IHostedService
{
    private readonly IServiceProvider _sp;
    private readonly IConfiguration _cfg;
    private readonly IAiAdapterRegistry _registry;
    private readonly ILogger<OllamaDiscoveryService> _logger;
    private readonly IOrchestrationAwareServiceDiscovery _serviceDiscovery;

    public OllamaDiscoveryService(IServiceProvider sp, IConfiguration cfg, IAiAdapterRegistry registry)
    {
        _sp = sp;
        _cfg = cfg;
        _registry = registry;
        _logger = sp.GetService<ILogger<OllamaDiscoveryService>>()
                 ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<OllamaDiscoveryService>.Instance;
        _serviceDiscovery = new OrchestrationAwareServiceDiscovery(cfg, null);
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            KoanLog.BootInfo(_logger, LogActions.Discovery, "start");
            KoanLog.BootDebug(_logger, LogActions.Discovery, "context",
                ("mode", _serviceDiscovery.CurrentMode));

            // Check if discovery should be enabled
            if (!ShouldPerformDiscovery())
            {
                KoanLog.BootDebug(_logger, LogActions.Discovery, "skip", ("reason", "auto-discovery-disabled"));
                return;
            }

            // If explicit Ollama services are configured, skip auto-discovery
            if (HasExplicitConfiguration())
            {
                KoanLog.BootDebug(_logger, LogActions.Discovery, "skip", ("reason", "explicit-configuration"));
                return;
            }

            // Check if an adapter is already registered (from OllamaOptionsConfigurator)
            if (HasExistingAdapter())
            {
                KoanLog.BootDebug(_logger, LogActions.Discovery, "skip", ("reason", "adapter-already-registered"));
                return;
            }

            // Get required model for validation
            var defaultModel = GetRequiredModel();
            KoanLog.BootDebug(_logger, LogActions.Discovery, "default-model", ("model", defaultModel ?? "(none)"));

            // Check if OllamaOptionsConfigurator already discovered a connection string
            var ollamaOptions = _sp.GetService<IOptions<OllamaOptions>>()?.Value;
            if (ollamaOptions != null && !string.IsNullOrWhiteSpace(ollamaOptions.ConnectionString) &&
                !string.Equals(ollamaOptions.ConnectionString.Trim(), "auto", StringComparison.OrdinalIgnoreCase))
            {
                // Use the connection string that was already discovered by OllamaOptionsConfigurator
                KoanLog.BootInfo(_logger, LogActions.Discovery, "result",
                    ("method", "OllamaOptionsConfigurator"),
                    ("url", ollamaOptions.ConnectionString),
                    ("healthy", "unknown"));

                await RegisterOllamaAdapter(ollamaOptions.ConnectionString, defaultModel, cancellationToken);
                return;
            }

            // Fallback: Use centralized orchestration-aware service discovery
            var discoveryOptions = CreateOllamaDiscoveryOptions(defaultModel);
            var result = await _serviceDiscovery.DiscoverServiceAsync("ollama", discoveryOptions, cancellationToken);

            KoanLog.BootInfo(_logger, LogActions.Discovery, "result",
                ("method", result.DiscoveryMethod),
                ("url", result.ServiceUrl),
                ("healthy", result.IsHealthy));

            if (!result.IsHealthy)
            {
                KoanLog.BootWarning(_logger, LogActions.Discovery, "health-check-failed", ("url", result.ServiceUrl));
            }

            // Create and register adapter
            await RegisterOllamaAdapter(result.ServiceUrl, defaultModel, cancellationToken);
        }
        catch (Exception ex)
        {
            KoanLog.BootWarning(_logger, LogActions.Discovery, "unexpected-error", ("reason", ex.Message));
            KoanLog.BootDebug(_logger, LogActions.Discovery, "error-detail", ("exception", ex.ToString()));
        }
    }

    private bool ShouldPerformDiscovery()
    {
        var envIsDev = Core.KoanEnv.IsDevelopment;
        var aiOpts = _sp.GetService<Microsoft.Extensions.Options.IOptions<Koan.AI.Contracts.Options.AiOptions>>()?.Value;
        var autoDiscovery = aiOpts?.AutoDiscoveryEnabled ?? envIsDev;
        var allowNonDev = aiOpts?.AllowDiscoveryInNonDev ?? true;

        KoanLog.BootDebug(_logger, LogActions.Discovery, "eligibility",
            ("envIsDev", envIsDev),
            ("autoDiscovery", autoDiscovery),
            ("allowNonDev", allowNonDev));

        if (!autoDiscovery)
        {
            KoanLog.BootDebug(_logger, LogActions.Discovery, "eligibility-denied", ("reason", "autoDiscovery-disabled"));
            return false;
        }

        if (!envIsDev && !allowNonDev)
        {
            KoanLog.BootDebug(_logger, LogActions.Discovery, "eligibility-denied", ("reason", "non-dev-blocked"));
            return false;
        }

        return true;
    }

    private bool HasExplicitConfiguration()
    {
        try
        {
            var configured = _cfg.GetSection(Infrastructure.Constants.Configuration.ServicesRoot)
                .Get<OllamaServiceOptions[]>() ?? Array.Empty<OllamaServiceOptions>();

            var enabledCount = configured.Count(s => s.Enabled);
            KoanLog.BootDebug(_logger, LogActions.Discovery, "configured-services",
                ("configured", configured.Length),
                ("enabled", enabledCount));

            return configured.Any(s => s.Enabled);
        }
        catch (Exception ex)
        {
            KoanLog.BootDebug(_logger, LogActions.Discovery, "configuration-check-error", ("reason", ex.Message));
            KoanLog.BootDebug(_logger, LogActions.Discovery, "configuration-check-detail", ("exception", ex.ToString()));
            return false;
        }
    }

    private bool HasExistingAdapter()
    {
        try
        {
            var adapters = _registry.All;
            var ollamaAdapterCount = adapters.Count;
            KoanLog.BootDebug(_logger, LogActions.Discovery, "existing-adapters",
                ("count", ollamaAdapterCount));
            return ollamaAdapterCount > 0;
        }
        catch (Exception ex)
        {
            KoanLog.BootDebug(_logger, LogActions.Discovery, "adapter-check-error", ("reason", ex.Message));
            return false;
        }
    }

    private string? GetRequiredModel()
    {
        try
        {
            // Bootstrap-time canonical model selection (like data adapters do)
            // 1. Try canonical path first: Koan:Ai:Ollama:DefaultModel
            var canonicalModel = _cfg["Koan:Ai:Ollama:DefaultModel"];
            if (!string.IsNullOrWhiteSpace(canonicalModel))
            {
                KoanLog.BootDebug(_logger, LogActions.Discovery, "model-canonical", ("model", canonicalModel));
                return canonicalModel;
            }

            // 2. Fallback to RequiredModels[0] for backward compatibility
            var requiredModels = _cfg.GetSection("Koan:Ai:Ollama:RequiredModels").Get<string[]>();
            var fallbackModel = requiredModels?.FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(fallbackModel))
            {
                KoanLog.BootDebug(_logger, LogActions.Discovery, "model-fallback", ("model", fallbackModel));
                return fallbackModel;
            }

            KoanLog.BootDebug(_logger, LogActions.Discovery, "model-default", ("mode", "adapter"));
            return null;
        }
        catch (Exception ex)
        {
            KoanLog.BootDebug(_logger, LogActions.Discovery, "model-read-error", ("reason", ex.Message));
            KoanLog.BootDebug(_logger, LogActions.Discovery, "model-read-detail", ("exception", ex.ToString()));
            return null;
        }
    }

    private ServiceDiscoveryOptions CreateOllamaDiscoveryOptions(string? requiredModel)
    {
        // Get legacy environment variable candidates for backward compatibility
        var legacyCandidates = GetLegacyCandidatesFromEnvironment();

        var discoveryOptions = ServiceDiscoveryExtensions.ForOllama();

        return discoveryOptions with
        {
            AdditionalCandidates = legacyCandidates,
            HealthCheck = new HealthCheckOptions
            {
                HealthCheckPath = Infrastructure.Constants.Discovery.TagsPath,
                Timeout = TimeSpan.FromMilliseconds(450),
                Required = !string.IsNullOrWhiteSpace(requiredModel), // Strict if model required
                CustomHealthCheck = !string.IsNullOrWhiteSpace(requiredModel)
                    ? (url, ct) => ValidateModelAvailability(url, requiredModel, ct)
                    : null
            },
            ExplicitConfigurationSections = new[]
            {
                "Koan:AI:Ollama",
                "Koan:Services:Ollama",
                Infrastructure.Constants.Configuration.ServicesRoot
            }
        };
    }

    private string[] GetLegacyCandidatesFromEnvironment()
    {
        var candidates = new List<string>();

        // Legacy environment variable support
        var envBaseUrl = Environment.GetEnvironmentVariable(Infrastructure.Constants.Discovery.EnvBaseUrl);
        if (!string.IsNullOrWhiteSpace(envBaseUrl))
        {
            candidates.Add(envBaseUrl);
        }

        var envList = Environment.GetEnvironmentVariable(Infrastructure.Constants.Discovery.EnvList);
        if (!string.IsNullOrWhiteSpace(envList))
        {
            candidates.AddRange(envList.Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim())
                .Where(s => !string.IsNullOrWhiteSpace(s)));
        }

        return candidates.ToArray();
    }

    private async Task<bool> ValidateModelAvailability(string serviceUrl, string requiredModel, CancellationToken cancellationToken)
    {
        try
        {
            using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            var tagsUrl = new Uri(new Uri(serviceUrl), Infrastructure.Constants.Discovery.TagsPath).ToString();

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromMilliseconds(450));

            var response = await httpClient.GetAsync(tagsUrl, cts.Token);
            if (!response.IsSuccessStatusCode) return false;

            var payload = await response.Content.ReadAsStringAsync(cts.Token);
            var hasModel = EndpointHasModel(payload, requiredModel);

            KoanLog.BootDebug(_logger, LogActions.Discovery, "model-validation",
                ("service", serviceUrl),
                ("model", requiredModel),
                ("present", hasModel));

            // If model is missing, attempt to download it
            if (!hasModel)
            {
                KoanLog.BootInfo(_logger, LogActions.Discovery, "model-missing",
                    ("service", serviceUrl),
                    ("model", requiredModel));

                var downloadSuccess = await PullModelAsync(serviceUrl, requiredModel, cancellationToken);
                if (downloadSuccess)
                {
                    KoanLog.BootInfo(_logger, LogActions.Discovery, "model-download-success",
                        ("service", serviceUrl),
                        ("model", requiredModel));
                    return true;
                }
                else
                {
                    KoanLog.BootWarning(_logger, LogActions.Discovery, "model-download-failed",
                        ("service", serviceUrl),
                        ("model", requiredModel));
                    return false;
                }
            }

            return hasModel;
        }
        catch (Exception ex)
        {
            KoanLog.BootDebug(_logger, LogActions.Discovery, "model-validation-error",
                ("service", serviceUrl),
                ("reason", ex.Message));
            KoanLog.BootDebug(_logger, LogActions.Discovery, "model-validation-detail", ("exception", ex.ToString()));
            return false;
        }
    }

    private async Task<bool> PullModelAsync(string serviceUrl, string modelName, CancellationToken cancellationToken)
    {
        try
        {
            using var httpClient = new HttpClient { Timeout = TimeSpan.FromMinutes(10) }; // Model downloads can be large
            var pullUrl = new Uri(new Uri(serviceUrl), "/api/pull").ToString();

            var pullRequest = new
            {
                name = modelName,
                stream = false // Use non-streaming for simpler implementation
            };

            var requestContent = new StringContent(
                Newtonsoft.Json.JsonConvert.SerializeObject(pullRequest),
                System.Text.Encoding.UTF8,
                "application/json");

            KoanLog.BootInfo(_logger, LogActions.Discovery, "model-download-start",
                ("service", serviceUrl),
                ("model", modelName));

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromMinutes(10)); // Allow up to 10 minutes for download

            var response = await httpClient.PostAsync(pullUrl, requestContent, cts.Token);
            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync(cts.Token);
                KoanLog.BootWarning(_logger, LogActions.Discovery, "model-download-http-failed",
                    ("service", serviceUrl),
                    ("model", modelName),
                    ("status", response.StatusCode),
                    ("error", errorContent));
                return false;
            }

            var responseContent = await response.Content.ReadAsStringAsync(cts.Token);
            KoanLog.BootDebug(_logger, LogActions.Discovery, "model-download-response",
                ("service", serviceUrl),
                ("size", responseContent?.Length ?? 0));

            // Verify the model was actually downloaded by checking tags again
            await Task.Delay(1000, cancellationToken); // Brief delay for Ollama to index the model
            return await VerifyModelDownloaded(serviceUrl, modelName, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            KoanLog.BootWarning(_logger, LogActions.Discovery, "model-download-timeout",
                ("service", serviceUrl),
                ("model", modelName));
            return false;
        }
        catch (Exception ex)
        {
            KoanLog.BootWarning(_logger, LogActions.Discovery, "model-download-error",
                ("service", serviceUrl),
                ("model", modelName),
                ("reason", ex.Message));
            KoanLog.BootDebug(_logger, LogActions.Discovery, "model-download-detail", ("exception", ex.ToString()));
            return false;
        }
    }

    private async Task<bool> VerifyModelDownloaded(string serviceUrl, string modelName, CancellationToken cancellationToken)
    {
        try
        {
            using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            var tagsUrl = new Uri(new Uri(serviceUrl), Infrastructure.Constants.Discovery.TagsPath).ToString();

            var response = await httpClient.GetAsync(tagsUrl, cancellationToken);
            if (!response.IsSuccessStatusCode) return false;

            var payload = await response.Content.ReadAsStringAsync(cancellationToken);
            return EndpointHasModel(payload, modelName);
        }
        catch (Exception ex)
        {
            KoanLog.BootDebug(_logger, LogActions.Discovery, "model-verify-error",
                ("service", serviceUrl),
                ("model", modelName),
                ("reason", ex.Message));
            KoanLog.BootDebug(_logger, LogActions.Discovery, "model-verify-detail", ("exception", ex.ToString()));
            return false;
        }
    }

    private Task RegisterOllamaAdapter(string serviceUrl, string? defaultModel, CancellationToken cancellationToken)
    {
        try
        {
            var baseAddress = new Uri(serviceUrl);
            var client = new HttpClient { BaseAddress = baseAddress, Timeout = TimeSpan.FromSeconds(60) };
            var id = $"ollama@{baseAddress.Host}:{baseAddress.Port}";
            var adapterLogger = _sp.GetService<ILogger<OllamaAdapter>>() ?? NullLogger<OllamaAdapter>.Instance;

            // Preserve bootstrap-time model choice (canonical pattern like data adapters)
            var configBuilder = new Microsoft.Extensions.Configuration.ConfigurationBuilder();

            // If we have a bootstrap-decided default model, embed it in adapter configuration
            // Use the canonical path that OllamaOptionsConfigurator expects
            if (!string.IsNullOrWhiteSpace(defaultModel))
            {
                var configData = new Dictionary<string, string?>
                {
                    ["Koan:Ai:Ollama:DefaultModel"] = defaultModel
                };
                configBuilder.AddInMemoryCollection(configData);
                KoanLog.BootDebug(_logger, LogActions.Discovery, "adapter-config",
                    ("defaultModel", defaultModel));
            }

            // Chain with main configuration to inherit other settings
            configBuilder.AddConfiguration(_cfg);
            var adapterConfig = configBuilder.Build();

            var readinessDefaults = _sp.GetService<IOptions<AdaptersReadinessOptions>>()?.Value;
            var adapter = new OllamaAdapter(client, adapterLogger, adapterConfig, readinessDefaults);

            KoanLog.BootDebug(_logger, LogActions.Discovery, "adapter-register",
                ("adapterId", id),
                ("url", serviceUrl));
            _registry.Add(adapter);
            KoanLog.BootInfo(_logger, LogActions.Discovery, "adapter-registered",
                ("adapterId", id));
        }
        catch (Exception ex)
        {
            KoanLog.BootWarning(_logger, LogActions.Discovery, "adapter-register-error",
                ("service", serviceUrl),
                ("reason", ex.Message));
            KoanLog.BootDebug(_logger, LogActions.Discovery, "adapter-register-detail", ("exception", ex.ToString()));
        }

        return Task.CompletedTask;
    }

    private static class LogActions
    {
        public const string Discovery = "ollama.discovery";
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private static bool EndpointHasModel(string json, string required)
    {
        // Accept either exact name or prefix before ':' tag (e.g., "all-minilm" matches "all-minilm:latest")
        try
        {
            var doc = JToken.Parse(json);
            var models = doc["models"] as JArray;
            if (models is null) return false;
            foreach (var m in models)
            {
                var name = m?["name"]?.ToString();
                if (string.IsNullOrEmpty(name)) continue;
                var baseName = name.Split(':')[0];
                if (string.Equals(name, required, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(baseName, required, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
        }
        catch { }
        return false;
    }
}
