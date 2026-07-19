namespace Koan.ZenGarden;

/// <summary>
/// Application-facing Zen Garden intent surfaces.
/// The runtime client is owned by the host and registered automatically through <c>AddKoan()</c>.
/// </summary>
public static class ZenGarden
{
    public static ZenGardenOfferingSurface Offering { get; } = new();
    public static ZenGardenStorageSurface Storage { get; } = new();
    public static ZenGardenCapabilitySurface Capability { get; } = new();

    internal static IZenGardenClient Client =>
        (Koan.Core.Hosting.App.AppHost.Current?.GetService(typeof(IZenGardenClient)) as IZenGardenClient)
        ?? throw new InvalidOperationException(
            "Zen Garden is not available before the host starts. Reference Sylin.Koan.ZenGarden, call AddKoan(), " +
            "and use the ZenGarden surfaces from a running host (for example, a BackgroundService).");
}
