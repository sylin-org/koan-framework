namespace Sora.Messaging.Provisioning;

/// <summary>
/// Provides access to the provider-specific client for topology inspection/diff/apply.
/// </summary>
public interface IProviderClientAccessor
{
    /// <summary>
    /// Returns the provider client for the given bus code, or null if not available.
    /// </summary>
    object? GetProviderClient(string busCode);
}
