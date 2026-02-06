namespace Koan.ZenGarden.Core;

/// <summary>
/// Resolves Zen Garden connection intents to concrete offering endpoints.
/// </summary>
public interface IZenGardenInitializationProvider
{
    /// <summary>
    /// Attempts to get the adapter's default offering name.
    /// </summary>
    bool TryGetDefaultOffering(string adapterId, out string offering);

    /// <summary>
    /// Resolves a parsed Zen Garden intent to an offering endpoint.
    /// Returns null when the offering cannot be resolved or is not ready.
    /// </summary>
    ValueTask<ZenGardenOfferingResolution?> ResolveAsync(
        ZenGardenConnectionIntent intent,
        CancellationToken cancellationToken = default);
}
