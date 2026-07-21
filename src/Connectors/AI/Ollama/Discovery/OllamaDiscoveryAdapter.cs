using Koan.AI.Connector.Ollama.Infrastructure;
using Koan.Core.Orchestration;
using Koan.Core.Orchestration.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Koan.AI.Connector.Ollama.Discovery;

internal sealed class OllamaDiscoveryAdapter(
    IConfiguration configuration,
    ILogger<OllamaDiscoveryAdapter> logger)
    : ServiceDiscoveryAdapterBase(configuration, logger)
{
    public override string ServiceName => Constants.Adapter.Type;
    public override string[] Aliases => ["ollama-ai"];

    protected override Type GetFactoryType() => typeof(OllamaServiceDescriptor);

    protected override async Task<bool> ValidateServiceHealth(
        string serviceUrl,
        DiscoveryContext context,
        CancellationToken cancellationToken)
    {
        using var client = new HttpClient { Timeout = context.HealthCheckTimeout };
        var endpoint = new Uri(new Uri(serviceUrl.TrimEnd('/') + "/"), Constants.Discovery.ModelsPath.TrimStart('/'));
        using var response = await client.GetAsync(endpoint, cancellationToken).ConfigureAwait(false);
        return response.IsSuccessStatusCode;
    }

    protected override string? ReadAspireServiceDiscovery() =>
        _configuration[$"services:{ServiceName}:default:0"];
}
