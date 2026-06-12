namespace Koan.Core.Capabilities;

/// <summary>
/// Resolves a <see cref="CapabilitySet"/> from a provider. The generic step only knows the native
/// <see cref="IDescribesCapabilities"/> path; each pillar's catalog (<c>DataCaps</c>, <c>VectorCaps</c>, …)
/// layers its legacy enum↔token bridge on top as the fallback. See ARCH-0084.
/// </summary>
public static class CapabilityResolver
{
    /// <summary>
    /// Builds a set from <see cref="IDescribesCapabilities.Describe"/> when <paramref name="source"/>
    /// implements it; otherwise returns <c>null</c> so the caller can fall back to a bridge.
    /// </summary>
    public static CapabilitySet? TryDescribe(object source, string? owner = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        return source is IDescribesCapabilities describer
            ? CapabilitySet.Build(owner, describer.Describe)
            : null;
    }
}
