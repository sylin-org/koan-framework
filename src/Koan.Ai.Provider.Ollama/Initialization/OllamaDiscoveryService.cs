using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using Koan.Ai.Provider.Ollama.Options;
using Koan.AI.Contracts.Routing;
using Koan.Core.Orchestration;

namespace Koan.Ai.Provider.Ollama.Initialization;

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
            _logger.LogDebug("Ollama Orchestration-Aware Discovery starting...");
            _logger.LogDebug("OrchestrationMode: {Mode}", _serviceDiscovery.CurrentMode);

            // Check if discovery should be enabled
            if (!ShouldPerformDiscovery())
            {
                _logger.LogDebug("Auto-discovery disabled or not applicable, skipping");
                return;
            }

            // If explicit Ollama services are configured, skip auto-discovery
            if (HasExplicitConfiguration())
            {
                _logger.LogDebug("Explicit Ollama services configured, skipping auto-discovery");
                return;
            }

            // Get required model for validation
            var defaultModel = GetRequiredModel();
            _logger.LogDebug("Default model for discovered adapters: {DefaultModel}", defaultModel ?? "none");

            // Use centralized orchestration-aware service discovery
            var discoveryOptions = CreateOllamaDiscoveryOptions(defaultModel);
            var result = await _serviceDiscovery.DiscoverServiceAsync("ollama", discoveryOptions, cancellationToken);

            _logger.LogInformation("Ollama discovered via {Method}: {ServiceUrl}",
                result.DiscoveryMethod, result.ServiceUrl);

            if (!result.IsHealthy)
            {
                _logger.LogWarning("Discovered Ollama service failed health check but proceeding anyway");
            }

            // Create and register adapter
            await RegisterOllamaAdapter(result.ServiceUrl, defaultModel, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error in Ollama orchestration-aware discovery");
        }
    }

    private bool ShouldPerformDiscovery()
    {
        var envIsDev = Core.KoanEnv.IsDevelopment;
        var aiOpts = _sp.GetService<Microsoft.Extensions.Options.IOptions<Koan.AI.Contracts.Options.AiOptions>>()?.Value;
        var autoDiscovery = aiOpts?.AutoDiscoveryEnabled ?? envIsDev;
        var allowNonDev = aiOpts?.AllowDiscoveryInNonDev ?? true;

        _logger.LogDebug("Discovery settings: envIsDev={EnvIsDev}, autoDiscovery={AutoDiscovery}, allowNonDev={AllowNonDev}",
            envIsDev, autoDiscovery, allowNonDev);

        if (!autoDiscovery) return false;
        if (!envIsDev && !allowNonDev) return false;

        return true;
    }

    private bool HasExplicitConfiguration()
    {
        try
        {
            var configured = _cfg.GetSection(Infrastructure.Constants.Configuration.ServicesRoot)
                .Get<OllamaServiceOptions[]>() ?? Array.Empty<OllamaServiceOptions>();

            _logger.LogDebug("Found {ConfigCount} configured Ollama services", configured.Length);
            var enabledCount = configured.Count(s => s.Enabled);
            _logger.LogDebug("Found {EnabledCount} enabled configured Ollama services", enabledCount);

            return configured.Any(s => s.Enabled);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Error checking configured services, proceeding with discovery");
            return false;
        }
    }

    private string? GetRequiredModel()
    {
        try
        {
            return _cfg.GetSection("Koan:Ai:Ollama:RequiredModels").Get<string[]>()?.FirstOrDefault();
        }
        catch
        {
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

            _logger.LogDebug("Model validation for {ServiceUrl}: required '{Model}' = {HasModel}",
                serviceUrl, requiredModel, hasModel);

            // If model is missing, attempt to download it
            if (!hasModel)
            {
                _logger.LogInformation("Required model '{Model}' not found at {ServiceUrl}, attempting to download...",
                    requiredModel, serviceUrl);

                var downloadSuccess = await PullModelAsync(serviceUrl, requiredModel, cancellationToken);
                if (downloadSuccess)
                {
                    _logger.LogInformation("Successfully downloaded model '{Model}' at {ServiceUrl}",
                        requiredModel, serviceUrl);
                    return true;
                }
                else
                {
                    _logger.LogWarning("Failed to download required model '{Model}' at {ServiceUrl}",
                        requiredModel, serviceUrl);
                    return false;
                }
            }

            return hasModel;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Error validating model availability at {ServiceUrl}", serviceUrl);
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

            _logger.LogInformation("Initiating model download: {Model} from {ServiceUrl}", modelName, serviceUrl);

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromMinutes(10)); // Allow up to 10 minutes for download

            var response = await httpClient.PostAsync(pullUrl, requestContent, cts.Token);
            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync(cts.Token);
                _logger.LogWarning("Model pull failed ({StatusCode}): {Error}",
                    response.StatusCode, errorContent);
                return false;
            }

            var responseContent = await response.Content.ReadAsStringAsync(cts.Token);
            _logger.LogDebug("Model pull response: {Response}", responseContent);

            // Verify the model was actually downloaded by checking tags again
            await Task.Delay(1000, cancellationToken); // Brief delay for Ollama to index the model
            return await VerifyModelDownloaded(serviceUrl, modelName, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Model download timed out for '{Model}' at {ServiceUrl}", modelName, serviceUrl);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error downloading model '{Model}' from {ServiceUrl}", modelName, serviceUrl);
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
            _logger.LogDebug(ex, "Error verifying model download at {ServiceUrl}", serviceUrl);
            return false;
        }
    }

    private async Task RegisterOllamaAdapter(string serviceUrl, string? defaultModel, CancellationToken cancellationToken)
    {
        try
        {
            var baseAddress = new Uri(serviceUrl);
            var client = new HttpClient { BaseAddress = baseAddress, Timeout = TimeSpan.FromSeconds(60) };
            var id = $"ollama@{baseAddress.Host}:{baseAddress.Port}";
            var adapterLogger = _sp.GetService<Microsoft.Extensions.Logging.ILogger<OllamaAdapter>>();

            // Create minimal configuration for the adapter
            var configBuilder = new Microsoft.Extensions.Configuration.ConfigurationBuilder();
            var adapterConfig = configBuilder.Build();

            var adapter = new OllamaAdapter(client, adapterLogger, adapterConfig);

            _logger.LogDebug("Registering Ollama adapter: {AdapterId} at {Url}", id, serviceUrl);
            _registry.Add(adapter);
            _logger.LogInformation("Successfully registered Ollama adapter via orchestration-aware discovery");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error registering Ollama adapter for {ServiceUrl}", serviceUrl);
        }
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