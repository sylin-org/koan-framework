namespace Koan.Core.Capabilities;

/// <summary>
/// Optional contract a provider implements to declare its capabilities directly against the unified
/// model. The signature deliberately matches the future <c>KoanModule.Describe</c> (Facet 2), so a
/// provider migrated now needs no further change when the module lands. Providers that have not yet
/// migrated are read through their pillar's enum↔token bridge instead. See ARCH-0084.
/// </summary>
public interface IDescribesCapabilities
{
    /// <summary>Declare every capability this provider supports onto <paramref name="caps"/>.</summary>
    void Describe(ICapabilities caps);
}
