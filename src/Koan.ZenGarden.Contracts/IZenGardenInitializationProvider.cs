namespace Koan.ZenGarden;

/// <summary>
/// Resolves Zen Garden connection intents to concrete offering endpoints.
/// </summary>
public interface IZenGardenInitializationProvider
{
    /// <summary>
    /// Resolves a parsed Zen Garden intent to an offering endpoint.
    /// Returns null when the offering cannot be resolved or is not ready.
    /// </summary>
    ValueTask<ZenGardenOfferingResolution?> Resolve(
        ZenGardenConnectionIntent intent,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Fires a non-blocking wishful capability ensure request and returns a request receipt.
    /// Returns null when capability wish cannot be scheduled.
    /// </summary>
    ValueTask<ZenGardenCapabilityWishReceipt?> WishCapabilities(
        ZenGardenConnectionIntent intent,
        CancellationToken cancellationToken = default);
}
