using Koan.ZenGarden.Models;

namespace Koan.ZenGarden;

/// <summary>
/// Tools-domain runtime client for Zen Garden.
/// </summary>
public interface IZenGardenClient : IDisposable
{
    IDisposable Subscribe(
        ZenGardenSubscription subscription,
        Func<ZenGardenAvailabilityEvent, CancellationToken, ValueTask> handler,
        ZenGardenWatchOptions? options = null);

    Task<IReadOnlyList<ZenGardenToolSnapshot>> CatalogAsync(
        ZenGardenSubscription subscription,
        CancellationToken cancellationToken = default);

    bool TryGetCurrent(string toolFqid, out ZenGardenToolSnapshot snapshot);
}
