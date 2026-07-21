using Koan.AI.Connector.LMStudio.Infrastructure;
using Koan.Core.Orchestration;
using Koan.Core.Orchestration.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;

namespace Koan.AI.Connector.LMStudio.Discovery;

internal sealed class LMStudioDiscoveryAdapter(
    IConfiguration configuration,
    ILogger<LMStudioDiscoveryAdapter> logger)
    : ServiceDiscoveryAdapterBase(configuration, logger)
{
    public override string ServiceName => Constants.Adapter.Type;
    public override string[] Aliases => ["lm-studio"];

    protected override Type GetFactoryType() => typeof(LMStudioServiceDescriptor);

    protected override string? ReadExplicitConfiguration() =>
        _configuration.GetConnectionString("LMStudio");

    protected override string? ReadAspireServiceDiscovery() =>
        _configuration[$"services:{ServiceName}:default:0"];

    protected override async Task<bool> ValidateServiceHealth(
        string serviceUrl,
        DiscoveryContext context,
        CancellationToken cancellationToken)
    {
        using var client = new HttpClient { Timeout = context.HealthCheckTimeout };
        var endpoint = new Uri(
            new Uri(serviceUrl.TrimEnd('/') + "/"),
            Constants.Discovery.ModelsPath.TrimStart('/'));
        using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);

        if (context.Parameters?.TryGetValue("apiKey", out var keyValue) == true
            && keyValue is string apiKey
            && !string.IsNullOrWhiteSpace(apiKey))
        {
            request.Headers.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);
        }

        using var response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) return false;

        if (context.Parameters?.TryGetValue("requiredModel", out var modelValue) != true
            || modelValue is not string requiredModel
            || string.IsNullOrWhiteSpace(requiredModel))
        {
            return true;
        }

        var payload = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        return EndpointHasModel(payload, requiredModel);
    }

    private static bool EndpointHasModel(string payload, string requiredModel)
    {
        try
        {
            return JToken.Parse(payload)["data"] is JArray models
                && models.Any(model => ModelMatches(model?["id"]?.ToString(), requiredModel));
        }
        catch
        {
            return false;
        }
    }

    private static bool ModelMatches(string? candidate, string requiredModel) =>
        !string.IsNullOrWhiteSpace(candidate)
        && (string.Equals(candidate, requiredModel, StringComparison.OrdinalIgnoreCase)
            || string.Equals(candidate.Split(':')[0], requiredModel, StringComparison.OrdinalIgnoreCase));
}
