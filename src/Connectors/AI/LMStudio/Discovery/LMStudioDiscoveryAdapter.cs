using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using Koan.AI.Connector.LMStudio.Infrastructure;
using Koan.Core;
using Koan.Core.Orchestration;
using Koan.Core.Orchestration.Abstractions;
using Koan.Orchestration.Attributes;

namespace Koan.AI.Connector.LMStudio.Discovery;

internal sealed class LMStudioDiscoveryAdapter : ServiceDiscoveryAdapterBase
{
    public LMStudioDiscoveryAdapter(IConfiguration configuration, ILogger<LMStudioDiscoveryAdapter> logger)
        : base(configuration, logger)
    {
    }

    public override string ServiceName => Infrastructure.Constants.Discovery.WellKnownServiceName;

    public override string[] Aliases => new[] { "lm-studio", "openai-compatible", "lmstudio" };

    protected override Type GetFactoryType() => typeof(LMStudioServiceDescriptor);

    protected override IEnumerable<DiscoveryCandidate> BuildDiscoveryCandidates(KoanServiceAttribute attribute, DiscoveryContext context)
    {
        var candidates = new List<DiscoveryCandidate>();

        candidates.AddRange(GetEnvironmentCandidates());

        var explicitConfig = ReadExplicitConfiguration();
        if (!string.IsNullOrWhiteSpace(explicitConfig))
        {
            candidates.Add(new DiscoveryCandidate(explicitConfig, "explicit-config", 1));
        }

        if (KoanEnv.InContainer)
        {
            if (!string.IsNullOrWhiteSpace(attribute.LocalHost))
            {
                var hostUrl = $"{attribute.LocalScheme}://{attribute.LocalHost}:{attribute.LocalPort}";
                candidates.Add(new DiscoveryCandidate(hostUrl, "host-first", 2));
            }

            if (!string.IsNullOrWhiteSpace(attribute.Host))
            {
                var containerUrl = $"{attribute.Scheme}://{attribute.Host}:{attribute.EndpointPort}";
                candidates.Add(new DiscoveryCandidate(containerUrl, "container-fallback", 3));
            }
        }
        else
        {
            if (!string.IsNullOrWhiteSpace(attribute.LocalHost))
            {
                var localUrl = $"{attribute.LocalScheme}://{attribute.LocalHost}:{attribute.LocalPort}";
                candidates.Add(new DiscoveryCandidate(localUrl, "local", 2));
            }
        }

        if (context.OrchestrationMode == OrchestrationMode.AspireAppHost)
        {
            var aspireUrl = _configuration[$"services:{ServiceName}:default:0"] ??
                            _configuration[$"services:{ServiceName}-ai:default:0"];
            if (!string.IsNullOrWhiteSpace(aspireUrl))
            {
                candidates.Insert(0, new DiscoveryCandidate(aspireUrl, "aspire", 0));
            }
        }

        return candidates;
    }

    protected override async Task<bool> ValidateServiceHealth(string serviceUrl, DiscoveryContext context, CancellationToken cancellationToken)
    {
        try
        {
            using var httpClient = new HttpClient { Timeout = context.HealthCheckTimeout };
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(context.HealthCheckTimeout);

            var baseUri = new Uri(serviceUrl.TrimEnd('/'));
            var modelsUri = new Uri(baseUri, Infrastructure.Constants.Discovery.ModelsPath);

            using var request = new HttpRequestMessage(HttpMethod.Get, modelsUri);
            AttachAuthHeader(context, request);

            var response = await httpClient.SendAsync(request, cts.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogDebug("LM Studio health check failed for {Url}: {Status}", modelsUri, response.StatusCode);
                return false;
            }

            var payload = await response.Content.ReadAsStringAsync(cts.Token).ConfigureAwait(false);
            if (context.Parameters != null &&
                context.Parameters.TryGetValue("requiredModel", out var requiredModelObj) &&
                requiredModelObj is string requiredModel &&
                !string.IsNullOrWhiteSpace(requiredModel))
            {
                if (!EndpointHasModel(payload, requiredModel))
                {
                    _logger.LogDebug("LM Studio model '{Model}' not reported by {Url}", requiredModel, modelsUri);
                    return false;
                }
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "LM Studio health check failed for {Url}", serviceUrl);
            return false;
        }
    }

    protected override string? ReadExplicitConfiguration()
    {
        return _configuration.GetConnectionString("LMStudio") ??
            _configuration[Constants.Configuration.Keys.ConnectionString] ??
            _configuration[$"{Constants.Section}:BaseUrl"];
    }

    private IEnumerable<DiscoveryCandidate> GetEnvironmentCandidates()
    {
        var candidates = new List<DiscoveryCandidate>();

        var baseUrl = Environment.GetEnvironmentVariable(Constants.Discovery.EnvBaseUrl);
        if (!string.IsNullOrWhiteSpace(baseUrl))
        {
            candidates.Add(new DiscoveryCandidate(baseUrl, "env-base-url", 0));
        }

        var list = Environment.GetEnvironmentVariable(Constants.Discovery.EnvList);
        if (!string.IsNullOrWhiteSpace(list))
        {
            candidates.AddRange(list.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(url => new DiscoveryCandidate(url.Trim(), "env-list", 0)));
        }

        return candidates;
    }

    private static bool EndpointHasModel(string payload, string requiredModel)
    {
        try
        {
            var json = JToken.Parse(payload);
            var data = json["data"] as JArray;
            if (data is null) return false;

            foreach (var model in data)
            {
                var id = model?["id"]?.ToString();
                if (string.IsNullOrWhiteSpace(id)) continue;

                if (string.Equals(id, requiredModel, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                var basename = id.Split(':')[0];
                if (string.Equals(basename, requiredModel, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }
        catch
        {
            return false;
        }

        return false;
    }

    private static void AttachAuthHeader(DiscoveryContext context, HttpRequestMessage request)
    {
        if (context.Parameters != null &&
            context.Parameters.TryGetValue("apiKey", out var keyObj) &&
            keyObj is string apiKey &&
            !string.IsNullOrWhiteSpace(apiKey))
        {
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);
        }
    }
}

