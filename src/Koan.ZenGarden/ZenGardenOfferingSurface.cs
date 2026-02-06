using Koan.ZenGarden.Models;

namespace Koan.ZenGarden;

public sealed class ZenGardenOfferingSurface
{
    public IDisposable On(
        string offering,
        Func<ZenGardenAvailabilityEvent, CancellationToken, ValueTask> handler,
        ZenGardenWatchOptions? options = null)
    {
        var subscription = ZenGardenSubscription.ForOffering(offering);
        return ZenGarden.Client.Subscribe(subscription, handler, options);
    }

    public IDisposable On(
        string offering,
        IReadOnlyList<string> requires,
        Func<ZenGardenAvailabilityEvent, CancellationToken, ValueTask> handler,
        ZenGardenWatchOptions? options = null)
    {
        var subscription = ZenGardenSubscription.ForOffering(offering)
            .Require(requires.ToArray());
        return ZenGarden.Client.Subscribe(subscription, handler, options);
    }

    public Task<IReadOnlyList<ZenGardenToolSnapshot>> Catalog(
        CancellationToken cancellationToken = default)
    {
        var subscription = new ZenGardenSubscription
        {
            ToolType = ZenGardenToolType.Offering
        };
        return ZenGarden.Client.CatalogAsync(subscription, cancellationToken);
    }

    public async Task<ZenGardenToolSnapshot?> Catalog(
        string offering,
        CancellationToken cancellationToken = default)
    {
        var subscription = ZenGardenSubscription.ForOffering(offering);
        var tools = await ZenGarden.Client.CatalogAsync(subscription, cancellationToken);
        return tools.FirstOrDefault();
    }

    public Task<IReadOnlyList<ZenGardenToolSnapshot>> Catalog(
        string offering,
        IReadOnlyList<string> requires,
        CancellationToken cancellationToken = default)
    {
        var subscription = ZenGardenSubscription.ForOffering(offering)
            .Require(requires.ToArray());
        return ZenGarden.Client.CatalogAsync(subscription, cancellationToken);
    }
}
