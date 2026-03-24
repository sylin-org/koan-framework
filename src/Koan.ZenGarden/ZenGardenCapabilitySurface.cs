namespace Koan.ZenGarden;

public sealed class ZenGardenCapabilitySurface
{
    public ValueTask<ZenGardenCapabilityWish> Wish(
        string offering,
        IReadOnlyList<string> capabilities,
        ZenGardenCapabilityWishOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        return ZenGarden.Client.Wish(offering, capabilities, options, cancellationToken);
    }

    public IDisposable On(
        ZenGardenCapabilityWish wish,
        Func<ZenGardenCapabilityProgressEvent, CancellationToken, ValueTask> handler,
        ZenGardenCapabilityWatchOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(wish);
        return ZenGarden.Client.SubscribeCapability(wish.RequestId, handler, options);
    }

    public IDisposable On(
        string requestId,
        Func<ZenGardenCapabilityProgressEvent, CancellationToken, ValueTask> handler,
        ZenGardenCapabilityWatchOptions? options = null)
    {
        return ZenGarden.Client.SubscribeCapability(requestId, handler, options);
    }

    public IDisposable OnAny(
        Func<ZenGardenCapabilityProgressEvent, CancellationToken, ValueTask> handler,
        ZenGardenCapabilityWatchOptions? options = null)
    {
        return ZenGarden.Client.SubscribeCapability(handler, options);
    }

    public bool TryGet(string requestId, out ZenGardenCapabilityWish wish)
    {
        return ZenGarden.Client.TryGetCapabilityWish(requestId, out wish);
    }
}
