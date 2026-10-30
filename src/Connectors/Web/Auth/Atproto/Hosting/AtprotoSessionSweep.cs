using Koan.Core.Hosting.App;
using Koan.Web.Auth.Connector.Atproto.Infrastructure;
using Koan.Web.Auth.Connector.Atproto.Storage;
using Koan.Web.Auth.Providers;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Koan.Web.Auth.Connector.Atproto.Hosting;

/// <summary>
/// Removes expired pending authorizations and ends expired sessions, a bounded page at a time, at start and then every
/// <see cref="Constants.SweepInterval"/>, while the provider is eligible: an inactive reference touches no storage. Every
/// process sharing the store may sweep; a row two of them reach is removed once.
/// </summary>
internal sealed class AtprotoSessionSweep(AtprotoSessionStore store, IAuthProviderCatalog catalog, TimeProvider time,
    IServiceProvider services, ILogger<AtprotoSessionSweep> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Constants.SweepInterval, time);
        try
        {
            do
            {
                if (catalog.Find(Constants.Provider)?.Eligible != true) continue;
                try
                {
                    // The rows are this host's, whichever host the process last bound.
                    using var host = AppHost.PushScope(services);
                    await store.Sweep(time.GetUtcNow(), Constants.SweepPageSize, stoppingToken);
                }
                catch (Exception error) when (!stoppingToken.IsCancellationRequested)
                {
                    logger.LogError(error, "The AT session sweep failed; it runs again at its next interval.");
                }
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }
}
