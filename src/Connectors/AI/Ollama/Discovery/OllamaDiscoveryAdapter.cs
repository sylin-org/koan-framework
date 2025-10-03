using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using Koan.Core;
using Koan.Core.Orchestration;
using Koan.Core.Orchestration.Abstractions;

namespace Koan.AI.Connector.Ollama.Discovery;

/// <summary>
/// Ollama autonomous discovery adapter.
/// Contains ALL Ollama-specific knowledge - core orchestration knows nothing about Ollama.
/// Reads own KoanServiceAttribute and handles Ollama-specific health checks with model validation.
/// </summary>
internal sealed class OllamaDiscoveryAdapter : ServiceDiscoveryAdapterBase
{
    public override string ServiceName => "ollama";
    public override string[] Aliases => new[] { "ollama-ai", "ai", "llm" };

    public OllamaDiscoveryAdapter(IConfiguration configuration, ILogger<OllamaDiscoveryAdapter> logger)
        : base(configuration, logger) { }

    /// <summary>Ollama adapter knows which factory contains its KoanServiceAttribute</summary>
    protected override Type GetFactoryType() => typeof(OllamaServiceDescriptor);

    /// <summary>Ollama-specific health validation using API health checks with model verification</summary>
    protected override async Task<bool> ValidateServiceHealth(string serviceUrl, DiscoveryContext context, CancellationToken cancellationToken)
    {
        try
        {
            using var httpClient = new HttpClient { Timeout = context.HealthCheckTimeout };
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(context.HealthCheckTimeout);

            // Check Ollama API health using /api/tags endpoint
            var tagsUrl = new Uri(new Uri(serviceUrl), Infrastructure.Constants.Discovery.TagsPath).ToString();
            var response = await httpClient.GetAsync(tagsUrl, cts.Token);

            if (response.IsSuccessStatusCode)
            {
                // If a required model is specified in context, validate it's available
                if (context.Parameters != null && context.Parameters.TryGetValue("requiredModel", out var modelObj))
                {
                    var requiredModel = modelObj.ToString();
                    if (!string.IsNullOrWhiteSpace(requiredModel))
                    {
                        var content = await response.Content.ReadAsStringAsync(cts.Token);
                        var hasModel = EndpointHasModel(content, requiredModel);

                        if (!hasModel)
                        {
                            _logger.LogDebug("Ollama health check failed: required model '{Model}' not found at {Url}", requiredModel, serviceUrl);

                            // Optionally attempt to download the model if auto-download is enabled
                            if (context.Parameters.TryGetValue("autoDownloadModels", out var autoDownloadObj) &&
                                autoDownloadObj is bool autoDownload && autoDownload)
                            {
                                _logger.LogInformation("Attempting to download required model '{Model}' at {Url}", requiredModel, serviceUrl);
                                var downloadSuccess = await PullModelAsync(serviceUrl, requiredModel, cts.Token);
                                if (downloadSuccess)
                                {
                                    _logger.LogInformation("Successfully downloaded model '{Model}' at {Url}", requiredModel, serviceUrl);
                                    return true;
                                }
                                else
                                {
                                    _logger.LogWarning("Failed to download required model '{Model}' at {Url}", requiredModel, serviceUrl);
                                    return false;
                                }
                            }

                            return false;
                        }

                        _logger.LogDebug("Ollama health check passed with required model '{Model}' at {Url}", requiredModel, serviceUrl);
                    }
                }

                _logger.LogDebug("Ollama health check passed using {TagsPath} for {Url}", Infrastructure.Constants.Discovery.TagsPath, serviceUrl);
                return true;
            }

            _logger.LogDebug("Ollama health check failed for {Url}: {StatusCode}", serviceUrl, response.StatusCode);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogDebug("Ollama health check failed for {Url}: {Error}", serviceUrl, ex.Message);
            return false;
        }
    }

    /// <summary>Ollama adapter reads its own configuration sections</summary>
    protected override string? ReadExplicitConfiguration()
    {
        // Check Ollama-specific configuration paths
        return _configuration.GetConnectionString("Ollama") ??
               _configuration["Koan:Ai:Provider:Ollama:ConnectionString"] ??
               _configuration["Koan:Ai:Ollama:BaseUrl"] ??
               _configuration["Koan:Data:ConnectionString"];
    }

    /// <summary>Ollama-specific discovery candidates with proper container-first priority</summary>
    protected override IEnumerable<DiscoveryCandidate> BuildDiscoveryCandidates(Koan.Orchestration.Attributes.KoanServiceAttribute attribute, DiscoveryContext context)
    {
        var candidates = new List<DiscoveryCandidate>();

        // Add Ollama-specific candidates from environment variables (highest priority)
        candidates.AddRange(GetEnvironmentCandidates());

        // Add explicit configuration candidates
        var explicitConfig = ReadExplicitConfiguration();
        if (!string.IsNullOrWhiteSpace(explicitConfig))
        {
            candidates.Add(new DiscoveryCandidate(explicitConfig, "explicit-config", 1));
        }

        // Host-first detection logic for AI services (models persist better on host)
        if (KoanEnv.InContainer)
        {
            // In container: Try host instance first (with models), then container fallback
            if (!string.IsNullOrWhiteSpace(attribute.LocalHost))
            {
                var hostUrl = $"{attribute.LocalScheme}://{attribute.LocalHost}:{attribute.LocalPort}";
                candidates.Add(new DiscoveryCandidate(hostUrl, "host-first", 2));
                _logger.LogDebug("Ollama adapter: Added host candidate {HostUrl} (host-first priority)", hostUrl);
            }

            // Container fallback when in container
            if (!string.IsNullOrWhiteSpace(attribute.Host))
            {
                var containerUrl = $"{attribute.Scheme}://{attribute.Host}:{attribute.EndpointPort}";
                candidates.Add(new DiscoveryCandidate(containerUrl, "container-fallback", 3));
                _logger.LogDebug("Ollama adapter: Added container fallback {ContainerUrl}", containerUrl);
            }
        }
        else
        {
            // Standalone (not in container): Local only
            if (!string.IsNullOrWhiteSpace(attribute.LocalHost))
            {
                var localhostUrl = $"{attribute.LocalScheme}://{attribute.LocalHost}:{attribute.LocalPort}";
                candidates.Add(new DiscoveryCandidate(localhostUrl, "local", 2));
                _logger.LogDebug("Ollama adapter: Added local candidate {LocalUrl} (standalone environment)", localhostUrl);
            }
        }

        // Special handling for Aspire
        if (context.OrchestrationMode == OrchestrationMode.AspireAppHost)
        {
            var aspireUrl = ReadAspireServiceDiscovery();
            if (!string.IsNullOrWhiteSpace(aspireUrl))
            {
                // Aspire takes priority over container/local discovery
                candidates.Insert(0, new DiscoveryCandidate(aspireUrl, "aspire-discovery", 1));
                _logger.LogDebug("Ollama adapter: Added Aspire candidate {AspireUrl}", aspireUrl);
            }
        }

        return candidates.Where(c => !string.IsNullOrWhiteSpace(c.Url));
    }

    /// <summary>Ollama-specific environment variable handling</summary>
    private IEnumerable<DiscoveryCandidate> GetEnvironmentCandidates()
    {
        var candidates = new List<DiscoveryCandidate>();

        // Check OLLAMA_BASE_URL (standard Ollama environment variable)
        var ollamaBaseUrl = Environment.GetEnvironmentVariable(Infrastructure.Constants.Discovery.EnvBaseUrl);
        if (!string.IsNullOrWhiteSpace(ollamaBaseUrl))
        {
            candidates.Add(new DiscoveryCandidate(ollamaBaseUrl, "environment-ollama-base-url", 0));
        }

        // Check Koan-specific environment variables
        var ollamaUrls = Environment.GetEnvironmentVariable(Infrastructure.Constants.Discovery.EnvList);
        if (!string.IsNullOrWhiteSpace(ollamaUrls))
        {
            var urls = ollamaUrls.Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries)
                                 .Select(url => new DiscoveryCandidate(url.Trim(), "environment-ollama-urls", 0));
            candidates.AddRange(urls);
        }

        return candidates;
    }

    /// <summary>Ollama adapter handles Aspire service discovery for Ollama</summary>
    protected override string? ReadAspireServiceDiscovery()
    {
        // Check Aspire-specific Ollama service discovery
        return _configuration["services:ollama:default:0"] ??
               _configuration["services:ollama-ai:default:0"];
    }

    /// <summary>Check if endpoint has required model available</summary>
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

    /// <summary>Attempt to download a model from Ollama</summary>
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

    /// <summary>Verify a model was successfully downloaded</summary>
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
}
