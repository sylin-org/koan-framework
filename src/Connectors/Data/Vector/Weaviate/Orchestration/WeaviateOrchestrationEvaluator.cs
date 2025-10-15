using System.Linq;
using System.Net.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Koan.Core;
using Koan.Core.Orchestration;
using WeaviateItems = Koan.Data.Vector.Connector.Weaviate.Infrastructure.WeaviateProvenanceItems;

namespace Koan.Data.Vector.Connector.Weaviate.Orchestration;

/// <summary>
/// Weaviate-specific orchestration evaluator that determines if Weaviate containers
/// should be provisioned based on configuration and host availability.
/// </summary>
public class WeaviateOrchestrationEvaluator : BaseOrchestrationEvaluator
{
    private readonly IHttpClientFactory? _httpClientFactory;

    public WeaviateOrchestrationEvaluator(ILogger<WeaviateOrchestrationEvaluator>? logger = null, IHttpClientFactory? httpClientFactory = null)
        : base(logger)
    {
        _httpClientFactory = httpClientFactory;
    }

    public override string ServiceName => "weaviate";
    public override int StartupPriority => 350; // Vector databases after traditional data services

    protected override bool IsServiceEnabled(IConfiguration configuration)
    {
        // Weaviate is typically enabled when vector data adapters reference it or when explicitly configured
        // Conservative approach - only enable if explicitly configured
        return HasExplicitConfiguration(configuration);
    }

    protected override bool HasExplicitConfiguration(IConfiguration configuration)
    {
        // Check for explicit Weaviate endpoint configuration
        if (WeaviateItems.EndpointKeys.Any(key => !string.IsNullOrEmpty(configuration[key])))
        {
            return true;
        }

        if (WeaviateItems.ConnectionStringKeys.Any(key => !string.IsNullOrEmpty(configuration[key])))
        {
            return true;
        }

        return false;
    }

    protected override int GetDefaultPort()
    {
        return 8080; // Standard Weaviate port
    }

    protected override string[] GetAdditionalHostCandidates(IConfiguration configuration)
    {
        var candidates = new List<string>();

        // Check legacy environment variables for backward compatibility
        var weaviateUrl = Environment.GetEnvironmentVariable("WEAVIATE_URL");
        if (!string.IsNullOrWhiteSpace(weaviateUrl))
        {
            var host = ExtractHostFromUrl(weaviateUrl);
            if (!string.IsNullOrWhiteSpace(host))
            {
                candidates.Add(host);
            }
        }

        var koanWeaviateUrl = Environment.GetEnvironmentVariable("Koan_WEAVIATE_URL");
        if (!string.IsNullOrWhiteSpace(koanWeaviateUrl))
        {
            var host = ExtractHostFromUrl(koanWeaviateUrl);
            if (!string.IsNullOrWhiteSpace(host))
            {
                candidates.Add(host);
            }
        }

        return candidates.ToArray();
    }

    protected override async Task<bool> ValidateHostCredentials(IConfiguration configuration, HostDetectionResult hostResult)
    {
        try
        {
            Logger?.LogDebug("[Weaviate] Validating credentials for host: {Host}", hostResult.HostEndpoint);

            // Build Weaviate endpoint URL for validation
            var weaviateUrl = EnsureHttpUrl(hostResult.HostEndpoint!);

            // Try to access the Weaviate API
            var isValid = await TryWeaviateConnection(weaviateUrl);

            Logger?.LogDebug("[Weaviate] Credential validation result: {IsValid}", isValid);
            return isValid;
        }
        catch (Exception ex)
        {
            Logger?.LogDebug(ex, "[Weaviate] Error validating host credentials");
            return false;
        }
    }

    protected override async Task<DependencyDescriptor> CreateDependencyDescriptorAsync(IConfiguration configuration, OrchestrationContext context)
    {
        // Create environment variables for the container
        var environment = new Dictionary<string, string>(context.EnvironmentVariables)
        {
            ["KOAN_DEPENDENCY_TYPE"] = "weaviate",
            ["QUERY_DEFAULTS_LIMIT"] = "25",
            ["DEFAULT_VECTORIZER_MODULE"] = "none",
            ["ENABLE_MODULES"] = "text2vec-openai,text2vec-cohere,text2vec-huggingface,ref2vec-centroid,generative-openai,qna-openai",
            ["CLUSTER_HOSTNAME"] = "node1"
        };

        // Check for additional configuration from WeaviateOptions
        var options = new WeaviateOptions();
        new WeaviateOptionsConfigurator(configuration).Configure(options);

        return await Task.FromResult(new DependencyDescriptor
        {
            Name = ServiceName,
            Image = "semitechnologies/weaviate:1.25.1",
            Port = GetDefaultPort(),
            StartupPriority = StartupPriority,
            HealthTimeout = TimeSpan.FromSeconds(45),
            HealthCheckCommand = null, // Weaviate uses HTTP health checks, not command-based
            Environment = environment,
            Volumes = new List<string>
            {
                $"koan-weaviate-{context.SessionId}:/var/lib/weaviate"
            }
        });
    }

    private async Task<bool> TryWeaviateConnection(string weaviateUrl)
    {
        try
        {
            using var httpClient = _httpClientFactory?.CreateClient() ?? new HttpClient();
            httpClient.Timeout = TimeSpan.FromMilliseconds(2000);

            // Try to access the Weaviate v1/meta endpoint for health check
            var response = await httpClient.GetAsync($"{weaviateUrl}/v1/meta");
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    private static string? ExtractHostFromUrl(string url)
    {
        try
        {
            if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
            {
                return $"{uri.Host}:{uri.Port}";
            }

            // Try parsing as host:port directly
            if (url.Contains(':'))
            {
                return url;
            }

            // Default port
            return $"{url}:8080";
        }
        catch
        {
            return null;
        }
    }

    private static string EnsureHttpUrl(string hostPort)
    {
        if (hostPort.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            hostPort.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            return hostPort;
        }

        return $"http://{hostPort}";
    }
}
