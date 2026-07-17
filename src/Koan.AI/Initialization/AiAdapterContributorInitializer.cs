using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Koan.AI.Contracts.Adapters;
using Koan.Core.Logging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Koan.AI.Initialization;

/// <summary>
/// Compiles every referenced AI connector contribution into this host's adapter and source registries.
/// The AI module owns when this runs; connectors only describe their contribution.
/// </summary>
internal sealed class AiAdapterContributorInitializer
{
    private readonly IEnumerable<IAiAdapterContributor> _contributors;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<AiAdapterContributorInitializer> _logger;

    public AiAdapterContributorInitializer(
        IEnumerable<IAiAdapterContributor> contributors,
        IServiceScopeFactory scopeFactory,
        ILogger<AiAdapterContributorInitializer> logger)
    {
        _contributors = contributors ?? throw new ArgumentNullException(nameof(contributors));
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task Initialize(CancellationToken cancellationToken)
    {
        foreach (var contributor in _contributors)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            var contributorName = contributor.GetType().FullName ?? contributor.GetType().Name;
            using var scope = _scopeFactory.CreateScope();

            try
            {
                KoanLog.BootDebug(_logger, "ai.contributors", "start", ("contributor", contributorName));
                await contributor.Contribute(scope.ServiceProvider, cancellationToken).ConfigureAwait(false);
                KoanLog.BootDebug(_logger, "ai.contributors", "complete", ("contributor", contributorName));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                KoanLog.BootWarning(_logger, "ai.contributors", "cancelled", ("contributor", contributorName));
                throw;
            }
            catch (Exception ex)
            {
                KoanLog.BootWarning(_logger, "ai.contributors", "failed", ("contributor", contributorName), ("reason", ex.Message));
                KoanLog.BootDebug(_logger, "ai.contributors", "failed-detail", ("contributor", contributorName), ("exception", ex.ToString()));
                throw;
            }
        }
    }
}
