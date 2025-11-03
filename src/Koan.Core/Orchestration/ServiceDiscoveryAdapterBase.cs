using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Koan.Core.Logging;
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
        KoanLog.ConfigDebug(_logger, LogActions.Start, null, ("service", ServiceName));

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
            KoanLog.ConfigDebug(_logger, LogActions.Try, null,
                ("service", ServiceName),
                ("method", candidate.Method),
                ("url", candidate.Url));

            if (await ValidateCandidate(candidate.Url, context, cancellationToken))
            {
                KoanLog.ConfigInfo(_logger, LogActions.Decide, LogOutcomes.Success,
                    ("service", ServiceName),
                    ("method", candidate.Method),
                    ("url", candidate.Url));

                return AdapterDiscoveryResult.Success(ServiceName, candidate.Url, candidate.Method, true);
            }
        }

        KoanLog.ConfigWarning(_logger, LogActions.Decide, LogOutcomes.Failed,
            ("service", ServiceName));
        return AdapterDiscoveryResult.Failed(ServiceName, "All discovery methods failed");
    }

    /// <summary>Override to specify which factory type contains KoanServiceAttribute</summary>
    protected abstract Type GetFactoryType();

    /// <summary>Override to implement service-specific health validation</summary>
    protected abstract Task<bool> ValidateServiceHealth(string serviceUrl, DiscoveryContext context, CancellationToken cancellationToken);

    /// <summary>
    /// Template method for building discovery candidates with service-specific customization.
    /// ARCH-0068: Moved container/local/Aspire orchestration logic into base class to eliminate
    /// 60-70 lines of duplication from each of 12 discovery adapters.
    /// </summary>
    protected virtual IEnumerable<DiscoveryCandidate> BuildDiscoveryCandidates(KoanServiceAttribute attribute, DiscoveryContext context)
    {
        var candidates = new List<DiscoveryCandidate>();

        // 1. Add service-specific environment variable candidates (highest priority)
        candidates.AddRange(GetEnvironmentCandidates());

        // 2. Add explicit configuration candidates
        var explicitConfig = ReadExplicitConfiguration();
        if (!string.IsNullOrWhiteSpace(explicitConfig))
        {
            candidates.Add(new DiscoveryCandidate(explicitConfig, "explicit-config", 1));
        }

        // 3. Container vs Local detection logic (60-70 lines previously duplicated in each adapter)
        if (KoanEnv.InContainer)
        {
            // In container: Try container instance first, then local fallback
            if (!string.IsNullOrWhiteSpace(attribute.Host))
            {
                var containerUrl = $"{attribute.Scheme}://{attribute.Host}:{attribute.EndpointPort}";
                candidates.Add(new DiscoveryCandidate(containerUrl, "container-instance", 2));
                KoanLog.ConfigDebug(_logger, "discovery.candidate", null,
                    ("service", ServiceName),
                    ("method", "container-instance"),
                    ("url", containerUrl));
            }

            // Local fallback when in container
            if (!string.IsNullOrWhiteSpace(attribute.LocalHost))
            {
                var localhostUrl = $"{attribute.LocalScheme}://{attribute.LocalHost}:{attribute.LocalPort}";
                candidates.Add(new DiscoveryCandidate(localhostUrl, "local-fallback", 3));
                KoanLog.ConfigDebug(_logger, "discovery.candidate", null,
                    ("service", ServiceName),
                    ("method", "local-fallback"),
                    ("url", localhostUrl));
            }
        }
        else
        {
            // Standalone (not in container): Local only
            if (!string.IsNullOrWhiteSpace(attribute.LocalHost))
            {
                var localhostUrl = $"{attribute.LocalScheme}://{attribute.LocalHost}:{attribute.LocalPort}";
                candidates.Add(new DiscoveryCandidate(localhostUrl, "local", 2));
                KoanLog.ConfigDebug(_logger, "discovery.candidate", null,
                    ("service", ServiceName),
                    ("method", "local"),
                    ("url", localhostUrl));
            }
        }

        // 4. Special handling for Aspire (previously duplicated in each adapter)
        if (context.OrchestrationMode == OrchestrationMode.AspireAppHost)
        {
            var aspireUrl = ReadAspireServiceDiscovery();
            if (!string.IsNullOrWhiteSpace(aspireUrl))
            {
                // Aspire takes priority over container/local discovery
                candidates.Insert(0, new DiscoveryCandidate(aspireUrl, "aspire-discovery", 1));
                KoanLog.ConfigDebug(_logger, "discovery.candidate", null,
                    ("service", ServiceName),
                    ("method", "aspire-discovery"),
                    ("url", aspireUrl));
            }
        }

        // 5. Apply service-specific connection parameters and normalization
        // Always call ApplyConnectionParameters to allow adapters to normalize URLs even without parameters
        var parameters = context.Parameters ?? new Dictionary<string, object>();
        for (int i = 0; i < candidates.Count; i++)
        {
            if (!string.IsNullOrWhiteSpace(candidates[i].Url))
            {
                var enhancedUrl = ApplyConnectionParameters(candidates[i].Url, parameters);
                if (enhancedUrl != candidates[i].Url)
                {
                    candidates[i] = candidates[i] with { Url = enhancedUrl };
                }
            }
        }

        return candidates.Where(c => !string.IsNullOrWhiteSpace(c.Url));
    }

    /// <summary>
    /// Override to provide service-specific environment variable candidates.
    /// Example: Check MONGO_URLS, POSTGRES_URLS, REDIS_URLS environment variables.
    /// </summary>
    protected virtual IEnumerable<DiscoveryCandidate> GetEnvironmentCandidates()
    {
        return Enumerable.Empty<DiscoveryCandidate>();
    }

    /// <summary>
    /// Override to apply service-specific connection parameters to discovered URLs.
    /// Example: Add database name, credentials, or connection options to base URL.
    /// </summary>
    /// <param name="baseUrl">The discovered base URL</param>
    /// <param name="parameters">Service-specific parameters from context</param>
    /// <returns>Enhanced URL with parameters applied, or original URL if no changes needed</returns>
    protected virtual string ApplyConnectionParameters(string baseUrl, IDictionary<string, object> parameters)
    {
        return baseUrl; // Default: no parameter application
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
            KoanLog.ConfigDebug(_logger, LogActions.Health, LogOutcomes.Timeout,
                ("service", ServiceName),
                ("url", serviceUrl));
            return false;
        }
        catch (Exception ex)
        {
            KoanLog.ConfigDebug(_logger, LogActions.Health, LogOutcomes.Failure,
                ("service", ServiceName),
                ("url", serviceUrl),
                ("error", ex.Message));
            return false;
        }
    }

    /// <summary>Override to customize configuration reading</summary>
    protected virtual string? ReadExplicitConfiguration() => null;

    /// <summary>Override to implement Aspire service discovery</summary>
    protected virtual string? ReadAspireServiceDiscovery() => null;

    private static class LogActions
    {
        public const string Start = "discovery.start";
        public const string Try = "discovery.try";
        public const string Decide = "discovery.decide";
        public const string Health = "discovery.health";
    }

    private static class LogOutcomes
    {
        public const string Success = "success";
        public const string Failed = "failed";
        public const string Timeout = "timeout";
        public const string Failure = "failure";
    }
}