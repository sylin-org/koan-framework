using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Net.Http;
using Koan.Core;
using Koan.Core.Orchestration;

namespace Koan.AI.Connector.Ollama.Orchestration;

/// <summary>
/// Ollama-specific orchestration evaluator that determines if Ollama containers
/// should be provisioned based on configuration and host availability.
/// </summary>
public class OllamaOrchestrationEvaluator : BaseOrchestrationEvaluator
{
    private readonly IHttpClientFactory? _httpClientFactory;

    public OllamaOrchestrationEvaluator(ILogger<OllamaOrchestrationEvaluator>? logger = null, IHttpClientFactory? httpClientFactory = null)
        : base(logger)
    {
        _httpClientFactory = httpClientFactory;
    }

    public override string ServiceName => "ollama";
    public override int StartupPriority => 450; // AI services start after data and vector services

    protected override bool IsServiceEnabled(IConfiguration configuration)
    {
        // Ollama is typically enabled when AI providers reference it or when explicitly configured
        // Conservative approach - only enable if explicitly configured
        return HasExplicitConfiguration(configuration);
    }

    protected override bool HasExplicitConfiguration(IConfiguration configuration)
    {
        // Check for explicit Ollama service configuration
        var servicesSection = configuration.GetSection(Infrastructure.Constants.Configuration.ServicesRoot);
        var hasServices = servicesSection.GetChildren().Any();

        // Also check for environment variable configuration
        var hasEnvConfig = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable(Infrastructure.Constants.Discovery.EnvBaseUrl)) ||
                          !string.IsNullOrEmpty(Environment.GetEnvironmentVariable(Infrastructure.Constants.Discovery.EnvList));

        return hasServices || hasEnvConfig;
    }

    protected override int GetDefaultPort()
    {
        return Infrastructure.Constants.Discovery.DefaultPort; // 11434
    }

    protected override string[] GetAdditionalHostCandidates(IConfiguration configuration)
    {
        var candidates = new List<string>();

        // Check environment variables for backward compatibility
        var baseUrl = Environment.GetEnvironmentVariable(Infrastructure.Constants.Discovery.EnvBaseUrl);
        if (!string.IsNullOrWhiteSpace(baseUrl))
        {
            var host = ExtractHostFromUrl(baseUrl);
            if (!string.IsNullOrWhiteSpace(host))
            {
                candidates.Add(host);
            }
        }

        var urlList = Environment.GetEnvironmentVariable(Infrastructure.Constants.Discovery.EnvList);
        if (!string.IsNullOrWhiteSpace(urlList))
        {
            var urls = urlList.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var url in urls)
            {
                var host = ExtractHostFromUrl(url.Trim());
                if (!string.IsNullOrWhiteSpace(host))
                {
                    candidates.Add(host);
                }
            }
        }

        return candidates.ToArray();
    }

    protected override async Task<bool> ValidateHostCredentials(IConfiguration configuration, HostDetectionResult hostResult)
    {
        try
        {
            Logger?.LogDebug("[Ollama] Validating credentials for host: {Host}", hostResult.HostEndpoint);

            // Build Ollama endpoint URL for validation
            var ollamaUrl = EnsureHttpUrl(hostResult.HostEndpoint!);

            // Try to access the Ollama API
            var isValid = await TryOllamaConnection(ollamaUrl);

            Logger?.LogDebug("[Ollama] Credential validation result: {IsValid}", isValid);
            return isValid;
        }
        catch (Exception ex)
        {
            Logger?.LogDebug(ex, "[Ollama] Error validating host credentials");
            return false;
        }
    }

    protected override async Task<DependencyDescriptor> CreateDependencyDescriptorAsync(IConfiguration configuration, OrchestrationContext context)
    {
        // Create environment variables for the container
        var environment = new Dictionary<string, string>(context.EnvironmentVariables)
        {
            ["KOAN_DEPENDENCY_TYPE"] = "ollama",
            ["OLLAMA_ORIGINS"] = "*",
            ["OLLAMA_HOST"] = "0.0.0.0",
            ["OLLAMA_KEEP_ALIVE"] = "24h"
        };

        return await Task.FromResult(new DependencyDescriptor
        {
            Name = ServiceName,
            Image = "ollama/ollama:latest",
            Port = GetDefaultPort(),
            StartupPriority = StartupPriority,
            HealthTimeout = TimeSpan.FromSeconds(30),
            HealthCheckCommand = null, // Ollama uses HTTP health checks, not command-based
            Environment = environment,
            Volumes = new List<string>
            {
                $"koan-ollama-{context.SessionId}:/root/.ollama"
            }
        });
    }

    private async Task<bool> TryOllamaConnection(string ollamaUrl)
    {
        try
        {
            using var httpClient = _httpClientFactory?.CreateClient() ?? new HttpClient();
            httpClient.Timeout = TimeSpan.FromMilliseconds(2000);

            // Try to access the Ollama /api/tags endpoint for health check
            var response = await httpClient.GetAsync($"{ollamaUrl}{Infrastructure.Constants.Discovery.TagsPath}");
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
            return $"{url}:{Infrastructure.Constants.Discovery.DefaultPort}";
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
