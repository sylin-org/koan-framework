using Koan.ZenGarden.Models;

namespace Koan.ZenGarden;

public sealed class ZenGardenStorageSurface
{
    public IDisposable On(
        string seedBank,
        Func<ZenGardenAvailabilityEvent, CancellationToken, ValueTask> handler,
        ZenGardenWatchOptions? options = null)
    {
        var subscription = ZenGardenSubscription.ForStorage(seedBank);
        return ZenGarden.Client.Subscribe(subscription, handler, options);
    }

    public IDisposable OnAny(
        Func<ZenGardenAvailabilityEvent, CancellationToken, ValueTask> handler,
        ZenGardenWatchOptions? options = null)
    {
        var subscription = new ZenGardenSubscription
        {
            ToolType = ZenGardenToolType.SeedBank
        };
        return ZenGarden.Client.Subscribe(subscription, handler, options);
    }

    public Task<IReadOnlyList<ZenGardenToolSnapshot>> Catalog(
        CancellationToken cancellationToken = default)
    {
        var subscription = new ZenGardenSubscription
        {
            ToolType = ZenGardenToolType.SeedBank
        };
        return ZenGarden.Client.Catalog(subscription, cancellationToken);
    }

    public async Task<ZenGardenToolSnapshot?> Catalog(
        string seedBank,
        CancellationToken cancellationToken = default)
    {
        var subscription = ZenGardenSubscription.ForStorage(seedBank);
        var tools = await ZenGarden.Client.Catalog(subscription, cancellationToken);
        return tools.FirstOrDefault();
    }
}
