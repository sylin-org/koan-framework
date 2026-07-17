using Koan.Core.Orchestration;
using Koan.Core.Orchestration.Composition;
using Koan.ZenGarden.Core;
using Microsoft.Extensions.Logging;

namespace Koan.ZenGarden.Discovery;

/// <summary>
/// Resolves live Zen Garden topology for the immutable discovery plan compiled from package intent.
/// Automatic lookups are advisory and health-checked by the selected adapter; explicit Zen Garden intents
/// remain required and therefore cannot weaken into autonomous fallback.
/// </summary>
internal sealed class ZenGardenDiscoverySource : IDiscoveryCandidateSource
{
    private readonly IZenGardenInitializationProvider _provider;
    private readonly ILogger<ZenGardenDiscoverySource> _logger;

    public ZenGardenDiscoverySource(
        IZenGardenInitializationProvider provider,
        ILogger<ZenGardenDiscoverySource> logger)
    {
        _provider = provider;
        _logger = logger;
    }

    public async Task<IReadOnlyList<DiscoveryCandidate>> GetCandidates(
        DiscoveryCandidateRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!string.IsNullOrWhiteSpace(request.Intent))
        {
            if (!ZenGardenConnectionIntent.TryParse(request.Intent, out var explicitIntent)) return [];
            return await Resolve(explicitIntent!, request.ServiceName, cancellationToken).ConfigureAwait(false);
        }

        foreach (var selector in request.ServiceSelectors)
        {
            var candidates = await Resolve(
                    ZenGardenConnectionIntent.ForOffering(selector),
                    request.ServiceName,
                    cancellationToken)
                .ConfigureAwait(false);
            if (candidates.Count > 0) return candidates;
        }

        return [];
    }

    private async Task<IReadOnlyList<DiscoveryCandidate>> Resolve(
        ZenGardenConnectionIntent intent,
        string serviceName,
        CancellationToken cancellationToken)
    {
        try
        {
            var resolved = await _provider.Resolve(intent, cancellationToken).ConfigureAwait(false);
            var connection = resolved?.GetConnectionString();
            if (string.IsNullOrWhiteSpace(connection)) return [];

            _logger.LogDebug(
                "ZenGarden supplied a discovery candidate for {Service} through selector {Selector}",
                serviceName,
                intent.ToOfferingSelector());
            return
            [
                new DiscoveryCandidate(
                    connection,
                    Constants.Composition.SourceId,
                    DiscoveryCandidatePriority.Automatic),
            ];
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogDebug(
                "ZenGarden discovery source could not resolve {Service} through selector {Selector} ({ErrorType})",
                serviceName,
                intent.ToOfferingSelector(),
                exception.GetType().Name);
            return [];
        }
    }
}
