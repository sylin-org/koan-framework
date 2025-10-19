using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Koan.Core;
using Koan.Core.Orchestration;
using Koan.AI.Connector.LMStudio.Infrastructure;

namespace Koan.AI.Connector.LMStudio.Orchestration;

public sealed class LMStudioOrchestrationEvaluator : BaseOrchestrationEvaluator
{
    private readonly IHttpClientFactory? _httpClientFactory;

    public LMStudioOrchestrationEvaluator(ILogger<LMStudioOrchestrationEvaluator>? logger = null, IHttpClientFactory? httpClientFactory = null)
        : base(logger)
    {
        _httpClientFactory = httpClientFactory;
    }

    public override string ServiceName => Constants.Discovery.WellKnownServiceName;

    public override int StartupPriority => 455;

    protected override bool IsServiceEnabled(IConfiguration configuration)
    {
        return HasExplicitConfiguration(configuration);
    }

    protected override bool HasExplicitConfiguration(IConfiguration configuration)
    {
        var servicesSection = configuration.GetSection(Constants.Configuration.ServicesRoot);
        var hasServices = servicesSection.GetChildren().Any();

        var hasEnvUrl = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(Constants.Discovery.EnvBaseUrl)) ||
                        !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(Constants.Discovery.EnvList));

        return hasServices || hasEnvUrl;
    }

    protected override int GetDefaultPort()
        => Constants.Discovery.DefaultPort;

    protected override string[] GetAdditionalHostCandidates(IConfiguration configuration)
    {
        var candidates = new List<string>();
        var baseUrl = Environment.GetEnvironmentVariable(Constants.Discovery.EnvBaseUrl);
        if (!string.IsNullOrWhiteSpace(baseUrl) && Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri))
        {
            candidates.Add($"{uri.Host}:{uri.Port}");
        }

        var urlList = Environment.GetEnvironmentVariable(Constants.Discovery.EnvList);
        if (!string.IsNullOrWhiteSpace(urlList))
        {
            foreach (var url in urlList.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (Uri.TryCreate(url.Trim(), UriKind.Absolute, out var candidate))
                {
                    candidates.Add($"{candidate.Host}:{candidate.Port}");
                }
            }
        }

        return candidates.ToArray();
    }

    protected override async Task<bool> ValidateHostCredentials(IConfiguration configuration, HostDetectionResult hostResult)
    {
        try
        {
            Logger?.LogDebug("[LMStudio] Validating host {Host}", hostResult.HostEndpoint);
            var baseUrl = EnsureHttpUrl(hostResult.HostEndpoint!);
            return await TryLmStudioConnectionAsync(baseUrl).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Logger?.LogDebug(ex, "[LMStudio] Credential validation failure");
            return false;
        }
    }

    protected override async Task<DependencyDescriptor> CreateDependencyDescriptorAsync(IConfiguration configuration, OrchestrationContext context)
    {
        var environment = new Dictionary<string, string>(context.EnvironmentVariables)
        {
            ["KOAN_DEPENDENCY_TYPE"] = Constants.Discovery.WellKnownServiceName
        };

        if (!environment.ContainsKey(Constants.Discovery.EnvBaseUrl.ToUpperInvariant()))
        {
            environment[Constants.Discovery.EnvBaseUrl] = $"http://{Constants.Discovery.WellKnownServiceName}:{Constants.Discovery.DefaultPort}";
        }

        if (!environment.ContainsKey(Constants.Discovery.EnvKey))
        {
            environment[Constants.Discovery.EnvKey] = string.Empty;
        }

        return await Task.FromResult(new DependencyDescriptor
        {
            Name = ServiceName,
            Image = "lmstudio/lmstudio:latest",
            Port = Constants.Discovery.DefaultPort,
            StartupPriority = StartupPriority,
            HealthTimeout = TimeSpan.FromSeconds(30),
            Environment = environment,
            Volumes = new List<string>
            {
                $"koan-lmstudio-{context.SessionId}:/data"
            }
        }).ConfigureAwait(false);
    }

    private async Task<bool> TryLmStudioConnectionAsync(string baseUrl)
    {
        try
        {
            using var client = _httpClientFactory?.CreateClient() ?? new HttpClient();
            client.Timeout = TimeSpan.FromMilliseconds(2000);
            var response = await client.GetAsync(new Uri(new Uri(baseUrl.TrimEnd('/')), Constants.Discovery.ModelsPath));
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
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

