using Microsoft.Extensions.Configuration;
using Koan.Core.Orchestration.Abstractions;

namespace Koan.Core.Orchestration;

/// <summary>
/// Resolves a service endpoint for startup provenance without creating a second discovery policy.
/// </summary>
public static class ServiceDiscoveryReporting
{
    public static string ResolveConnectionString(
        IConfiguration? configuration,
        IServiceDiscoveryAdapter adapter,
        IDictionary<string, object>? parameters,
        Func<string> fallback,
        TimeSpan? healthCheckTimeout = null)
    {
        ArgumentNullException.ThrowIfNull(adapter);
        ArgumentNullException.ThrowIfNull(fallback);

        var safeConfiguration = configuration ?? new ConfigurationBuilder().AddInMemoryCollection().Build();
        var context = new DiscoveryContext
        {
            Configuration = safeConfiguration,
            OrchestrationMode = KoanEnv.OrchestrationMode,
            HealthCheckTimeout = healthCheckTimeout ?? TimeSpan.FromMilliseconds(500),
            Parameters = parameters is { Count: > 0 } ? parameters : null
        };

        try
        {
            var result = adapter.Discover(context).GetAwaiter().GetResult();
            return result.IsSuccessful && !string.IsNullOrWhiteSpace(result.ServiceUrl)
                ? result.ServiceUrl!
                : fallback();
        }
        catch
        {
            return fallback();
        }
    }
}
