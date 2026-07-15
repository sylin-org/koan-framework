using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Koan.Core.Logging;
using Koan.Core.Orchestration.Abstractions;
using Koan.Orchestration.Attributes;

namespace Koan.Core.Orchestration;

/// <summary>
/// Concern-owned discovery template used by every production service adapter.
/// Adapters describe protocol-specific inputs, normalization, and health without replacing shared election policy.
/// </summary>
public abstract class ServiceDiscoveryAdapterBase : IServiceDiscoveryAdapter
{
    private const string AutomaticConfigurationValue = "auto";

    protected readonly IConfiguration _configuration;
    protected readonly ILogger _logger;

    protected ServiceDiscoveryAdapterBase(IConfiguration configuration, ILogger logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    public abstract string ServiceName { get; }
    public virtual string[] Aliases => [];
    public virtual int Priority => 10;

    public async Task<AdapterDiscoveryResult> Discover(
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
                ("url", Redaction.DeIdentify(candidate.Url)));

            if (await ValidateCandidate(candidate.Url, context, cancellationToken))
            {
                KoanLog.ConfigInfo(_logger, LogActions.Decide, LogOutcomes.Success,
                    ("service", ServiceName),
                    ("method", candidate.Method),
                    ("url", Redaction.DeIdentify(candidate.Url)));

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
    protected IEnumerable<DiscoveryCandidate> BuildDiscoveryCandidates(KoanServiceAttribute attribute, DiscoveryContext context)
    {
        var candidates = new List<DiscoveryCandidate>();

        // 1. Add service-specific environment-variable candidates. These preserve legacy/raw environment
        // conventions that are not already represented by IConfiguration, but never override concrete app intent.
        candidates.AddRange(GetEnvironmentCandidates()
            .Where(static candidate => candidate is not null && !string.IsNullOrWhiteSpace(candidate.Url))
            .Select(static candidate => candidate with { Priority = DiscoveryCandidatePriority.Environment }));

        // 2. Fold in candidates contributed by external discovery sources (Zen Garden / Koi), populated by the
        // coordinator. These are health-checked like any other candidate — tried ahead of the compose/host/local
        // guesses but behind explicit env/config — so an unreachable contributed answer (e.g. a same-host offering
        // advertised on an interface this app can't reach) fails its probe and the loop falls through. The
        // contributor informs discovery; it never short-circuits it.
        if (context.ContributedCandidates is { Count: > 0 })
        {
            foreach (var contributed in context.ContributedCandidates)
            {
                if (contributed is not null && !string.IsNullOrWhiteSpace(contributed.Url))
                {
                    var normalized = contributed with { Priority = DiscoveryCandidatePriority.Automatic };
                    candidates.Add(normalized);
                    KoanLog.ConfigDebug(_logger, "discovery.candidate", null,
                        ("service", ServiceName),
                        ("method", normalized.Method),
                        ("url", Redaction.DeIdentify(normalized.Url)));
                }
            }
        }

        // 3. Add concrete explicit configuration candidates. The literal "auto" is a declaration that the
        // shared pipeline should decide; it is not itself a usable endpoint.
        var explicitConfig = ReadExplicitConfiguration();
        if (IsConcreteExplicitConfiguration(explicitConfig))
        {
            candidates.Add(new DiscoveryCandidate(
                explicitConfig!,
                "explicit-config",
                DiscoveryCandidatePriority.ExplicitConfiguration));
        }

        // 4. Adapter-specific runtime topology. Adapters may describe their topology here, but cannot replace
        // the shared environment/config/contributor/Aspire pipeline around it.
        candidates.AddRange(BuildRuntimeCandidates(attribute)
            .Where(static candidate => candidate is not null && !string.IsNullOrWhiteSpace(candidate.Url))
            .Select(static candidate => candidate.Priority < DiscoveryCandidatePriority.Automatic
                ? candidate with { Priority = DiscoveryCandidatePriority.Automatic }
                : candidate));

        // 5. Aspire is an automatic discovery source. Insert first so the stable priority sort tries it ahead
        // of contributed and topology guesses, while concrete explicit configuration remains authoritative.
        if (context.OrchestrationMode == OrchestrationMode.AspireAppHost)
        {
            var aspireUrl = ReadAspireServiceDiscovery();
            if (!string.IsNullOrWhiteSpace(aspireUrl))
            {
                candidates.Insert(0, new DiscoveryCandidate(
                    aspireUrl,
                    "aspire-discovery",
                    DiscoveryCandidatePriority.Automatic));
                KoanLog.ConfigDebug(_logger, "discovery.candidate", null,
                    ("service", ServiceName),
                    ("method", "aspire-discovery"),
                    ("url", Redaction.DeIdentify(aspireUrl)));
            }
        }

        // 6. Apply service-specific connection parameters and normalization
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
    /// Describe service-specific runtime topology candidates. The shared pipeline retains ownership of
    /// environment, explicit configuration, activated contributors, Aspire precedence, normalization, and health.
    /// </summary>
    protected virtual IEnumerable<DiscoveryCandidate> BuildRuntimeCandidates(KoanServiceAttribute attribute)
    {
        var candidates = new List<DiscoveryCandidate>();

        if (KoanEnv.InContainer)
        {
            // In container: Try container instance first, then local fallback
            if (!string.IsNullOrWhiteSpace(attribute.Host))
            {
                var containerUrl = $"{attribute.Scheme}://{attribute.Host}:{attribute.EndpointPort}";
                candidates.Add(new DiscoveryCandidate(
                    containerUrl,
                    "container-instance",
                    DiscoveryCandidatePriority.Automatic));
                KoanLog.ConfigDebug(_logger, "discovery.candidate", null,
                    ("service", ServiceName),
                    ("method", "container-instance"),
                    ("url", Redaction.DeIdentify(containerUrl)));
            }

            // Reach a service on the Docker HOST from inside a container. `host.docker.internal` is the
            // Docker-provided host gateway (Docker Desktop / WSL2 by default; Linux needs
            // --add-host=host.docker.internal:host-gateway). Tried AFTER the compose service and BEFORE localhost
            // (which, inside a container, is the container itself). An unresolvable host simply fails the health
            // probe and the next candidate is tried.
            if (!string.IsNullOrWhiteSpace(attribute.LocalHost))
            {
                var dockerHostUrl = $"{attribute.LocalScheme}://host.docker.internal:{attribute.LocalPort}";
                candidates.Add(new DiscoveryCandidate(
                    dockerHostUrl,
                    "docker-host",
                    DiscoveryCandidatePriority.HostGateway));
                KoanLog.ConfigDebug(_logger, "discovery.candidate", null,
                    ("service", ServiceName),
                    ("method", "docker-host"),
                    ("url", Redaction.DeIdentify(dockerHostUrl)));
            }

            // Local fallback when in container
            if (!string.IsNullOrWhiteSpace(attribute.LocalHost))
            {
                var localhostUrl = $"{attribute.LocalScheme}://{attribute.LocalHost}:{attribute.LocalPort}";
                candidates.Add(new DiscoveryCandidate(
                    localhostUrl,
                    "local-fallback",
                    DiscoveryCandidatePriority.LoopbackFallback));
                KoanLog.ConfigDebug(_logger, "discovery.candidate", null,
                    ("service", ServiceName),
                    ("method", "local-fallback"),
                    ("url", Redaction.DeIdentify(localhostUrl)));
            }
        }
        else
        {
            // Standalone (not in container): Local only
            if (!string.IsNullOrWhiteSpace(attribute.LocalHost))
            {
                var localhostUrl = $"{attribute.LocalScheme}://{attribute.LocalHost}:{attribute.LocalPort}";
                candidates.Add(new DiscoveryCandidate(
                    localhostUrl,
                    "local",
                    DiscoveryCandidatePriority.Automatic));
                KoanLog.ConfigDebug(_logger, "discovery.candidate", null,
                    ("service", ServiceName),
                    ("method", "local"),
                    ("url", Redaction.DeIdentify(localhostUrl)));
            }
        }

        return candidates;
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

    private KoanServiceAttribute? GetServiceAttribute() =>
        GetFactoryType().GetCustomAttribute<KoanServiceAttribute>();

    private static bool IsConcreteExplicitConfiguration(string? value) =>
        !string.IsNullOrWhiteSpace(value)
        && !string.Equals(value.Trim(), AutomaticConfigurationValue, StringComparison.OrdinalIgnoreCase);

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
                ("url", Redaction.DeIdentify(serviceUrl)));
            return false;
        }
        catch (Exception ex)
        {
            KoanLog.ConfigDebug(_logger, LogActions.Health, LogOutcomes.Failure,
                ("service", ServiceName),
                ("url", Redaction.DeIdentify(serviceUrl)),
                ("error", Redaction.DeIdentify(ex.Message)));
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
