using Koan.Core.Orchestration;
using Koan.Core.Orchestration.Abstractions;
using Koan.ZenGarden.Core;
using Microsoft.Extensions.Logging;

namespace Koan.ZenGarden.Discovery;

/// <summary>
/// Contributes the Zen Garden-resolved endpoint for a service as a health-checked discovery candidate — the
/// "Koi contributes IF PRESENT" seam. Rather than short-circuiting an adapter's probe with ZG's answer (which
/// strands the app when a same-host offering is advertised on an interface this app can't reach — e.g. the
/// docker bridge gateway <c>172.19.0.1</c> for a host MongoDB bound to loopback), ZG offers its resolved
/// address as ONE candidate. The adapter health-checks it; if unreachable, the probe falls through to the
/// standard compose-name / host.docker.internal / localhost candidates. Registered only when Koan.ZenGarden
/// is referenced, so a non-ZG app never sees it.
/// </summary>
internal sealed class ZenGardenDiscoveryCandidateContributor : IDiscoveryCandidateContributor
{
    // Tried ahead of the compose/host/local guesses (priority 2/3/4) but behind explicit env (0) / config (1).
    private const int ZenGardenCandidatePriority = 2;

    private readonly IZenGardenInitializationProvider _provider;
    private readonly ILogger<ZenGardenDiscoveryCandidateContributor> _logger;

    public ZenGardenDiscoveryCandidateContributor(
        IZenGardenInitializationProvider provider,
        ILogger<ZenGardenDiscoveryCandidateContributor> logger)
    {
        _provider = provider;
        _logger = logger;
    }

    public async Task<IReadOnlyList<DiscoveryCandidate>> ContributeCandidates(
        string serviceName, DiscoveryContext context, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(serviceName)) return [];

        // Map the discovery service id (e.g. "mongo") to its Zen Garden offering (e.g. "mongodb"). No binding
        // => this service isn't a ZG offering => contribute nothing.
        if (!_provider.TryGetDefaultOffering(serviceName, out var offering) || string.IsNullOrWhiteSpace(offering))
            return [];

        try
        {
            var intent = ZenGardenConnectionIntent.ForOffering(offering);
            var resolved = await _provider.Resolve(intent, cancellationToken);

            // Connection-string form (not GetUri): tolerates non-URI strings like Mongo replica sets. The scheme
            // comes from the offering's own advertised metadata; a wrong/unreachable one just fails the health probe.
            var url = resolved?.GetConnectionString();
            if (string.IsNullOrWhiteSpace(url)) return [];

            _logger.LogDebug(
                "ZenGarden contributed a discovery candidate for {Service} (offering {Offering}): {Url}",
                serviceName, offering, url);
            return [new DiscoveryCandidate(url!, "zengarden-offering", ZenGardenCandidatePriority)];
        }
        catch (Exception ex)
        {
            // Best-effort: the offering may not be ready yet; discovery falls through to the standard candidates.
            _logger.LogDebug(ex, "ZenGarden candidate contribution failed for {Service}", serviceName);
            return [];
        }
    }
}
