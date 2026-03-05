using Koan.ZenGarden.Models;

namespace Koan.ZenGarden;

/// <summary>
/// Tools-domain runtime client for Zen Garden.
/// </summary>
public interface IZenGardenClient : IDisposable
{
    /// <summary>
    /// The endpoint of the currently bound Moss stone, or null if not yet discovered/bound.
    /// </summary>
    string? BoundEndpoint { get; }

    IDisposable Subscribe(
        ZenGardenSubscription subscription,
        Func<ZenGardenAvailabilityEvent, CancellationToken, ValueTask> handler,
        ZenGardenWatchOptions? options = null);

    IDisposable SubscribeCapability(
        string requestId,
        Func<ZenGardenCapabilityProgressEvent, CancellationToken, ValueTask> handler,
        ZenGardenCapabilityWatchOptions? options = null);

    IDisposable SubscribeCapability(
        Func<ZenGardenCapabilityProgressEvent, CancellationToken, ValueTask> handler,
        ZenGardenCapabilityWatchOptions? options = null);

    Task<IReadOnlyList<ZenGardenToolSnapshot>> CatalogAsync(
        ZenGardenSubscription subscription,
        CancellationToken cancellationToken = default);

    ValueTask<ZenGardenCapabilityWish> WishAsync(
        string offering,
        IReadOnlyList<string> capabilities,
        ZenGardenCapabilityWishOptions? options = null,
        CancellationToken cancellationToken = default);

    bool TryGetCapabilityWish(string requestId, out ZenGardenCapabilityWish wish);

    bool TryGetCurrent(string toolFqid, out ZenGardenToolSnapshot snapshot);
}
