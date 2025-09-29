using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Koan.Core.Orchestration.Abstractions;
using Koan.Orchestration.Attributes;

namespace Koan.Core.Orchestration;

/// <summary>
/// Base implementation providing common discovery patterns.
/// Adapters can inherit this or implement IServiceDiscoveryAdapter directly.
/// </summary>
public abstract class ServiceDiscoveryAdapterBase : IServiceDiscoveryAdapter
{
    protected readonly IConfiguration _configuration;
    protected readonly ILogger _logger;

    protected ServiceDiscoveryAdapterBase(IConfiguration configuration, ILogger logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    public abstract string ServiceName { get; }
    public virtual string[] Aliases => Array.Empty<string>();
    public virtual int Priority => 10;

    public async Task<AdapterDiscoveryResult> DiscoverAsync(
        DiscoveryContext context,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("{ServiceName} adapter starting autonomous discovery", ServiceName);

        // Get our own KoanServiceAttribute
        var attribute = GetServiceAttribute();
        if (attribute == null)
        {
            return AdapterDiscoveryResult.Failed(ServiceName, "No KoanServiceAttribute found on adapter factory");
        }

        // Build discovery candidates based on orchestration mode
        var candidates = BuildDiscoveryCandidates(attribute, context);

        // Try each candidate until one succeeds
        foreach (var candidate in candidates.OrderBy(c => c.Priority))
        {
            _logger.LogDebug("{ServiceName} trying discovery method: {Method} -> {Url}",
                ServiceName, candidate.Method, candidate.Url);

            if (await ValidateCandidate(candidate.Url, context, cancellationToken))
            {
                _logger.LogInformation("{ServiceName} adapter decided: {Url} via {Method}",
                    ServiceName, candidate.Url, candidate.Method);

                return AdapterDiscoveryResult.Success(ServiceName, candidate.Url, candidate.Method, true);
            }
        }

        _logger.LogWarning("{ServiceName} adapter failed all discovery attempts", ServiceName);
        return AdapterDiscoveryResult.Failed(ServiceName, "All discovery methods failed");
    }

    /// <summary>Override to specify which factory type contains KoanServiceAttribute</summary>
    protected abstract Type GetFactoryType();

    /// <summary>Override to implement service-specific health validation</summary>
    protected abstract Task<bool> ValidateServiceHealth(string serviceUrl, DiscoveryContext context, CancellationToken cancellationToken);

    /// <summary>Override to customize discovery candidate generation</summary>
    protected virtual IEnumerable<DiscoveryCandidate> BuildDiscoveryCandidates(KoanServiceAttribute attribute, DiscoveryContext context)
    {
        var candidates = context.OrchestrationMode switch
        {
            OrchestrationMode.DockerCompose or OrchestrationMode.Kubernetes => CreateCandidates(
                (BuildServiceUrl(attribute.Scheme, attribute.Host, attribute.EndpointPort), "container-dns", 1),
                (ReadExplicitConfiguration(), "explicit-config", 2),
                (BuildServiceUrl(attribute.LocalScheme, attribute.LocalHost, attribute.LocalPort), "host-fallback", 3)
            ),
            OrchestrationMode.AspireAppHost => CreateCandidates(
                (ReadAspireServiceDiscovery(), "aspire-discovery", 1),
                (ReadExplicitConfiguration(), "explicit-config", 2)
            ),
            OrchestrationMode.SelfOrchestrating => CreateCandidates(
                (ReadExplicitConfiguration(), "explicit-config", 1),
                (BuildServiceUrl(attribute.LocalScheme, attribute.LocalHost, attribute.LocalPort), "self-orchestrated", 2)
            ),
            _ => CreateCandidates(
                (ReadExplicitConfiguration(), "explicit-config", 1),
                (BuildServiceUrl(attribute.LocalScheme, attribute.LocalHost, attribute.LocalPort), "localhost", 2)
            )
        };

        return candidates;
    }

    private static IEnumerable<DiscoveryCandidate> CreateCandidates(params (string? Url, string Method, int Priority)[] entries)
    {
        foreach (var (url, method, priority) in entries)
        {
            if (!string.IsNullOrWhiteSpace(url))
            {
                yield return new DiscoveryCandidate(url!, method, priority);
            }
        }
    }

    private KoanServiceAttribute? GetServiceAttribute() =>
        GetFactoryType().GetCustomAttribute<KoanServiceAttribute>();

    private string? BuildServiceUrl(string? scheme, string? host, int port) =>
        string.IsNullOrWhiteSpace(scheme) || string.IsNullOrWhiteSpace(host) ? null : $"{scheme}://{host}:{port}";

    private async Task<bool> ValidateCandidate(string serviceUrl, DiscoveryContext context, CancellationToken cancellationToken)
    {
        if (!context.RequireHealthValidation) return true;

        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(context.HealthCheckTimeout);

            return await ValidateServiceHealth(serviceUrl, context, cts.Token);
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("{ServiceName} health check timed out for {Url}", ServiceName, serviceUrl);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogDebug("{ServiceName} health check failed for {Url}: {Error}", ServiceName, serviceUrl, ex.Message);
            return false;
        }
    }

    /// <summary>Override to customize configuration reading</summary>
    protected virtual string? ReadExplicitConfiguration() => null;

    /// <summary>Override to implement Aspire service discovery</summary>
    protected virtual string? ReadAspireServiceDiscovery() => null;
}