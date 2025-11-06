using Koan.Core.Modules.Pillars;

namespace Koan.ServiceMesh.Pillars;

/// <summary>
/// Manifest for the Services pillar, representing microservice mesh capabilities.
/// </summary>
public static class ServicesPillarManifest
{
    public const string PillarCode = "services";
    public const string PillarLabel = "Services";
    public const string PillarColorHex = "#22c55e";  // Green for networking/distributed systems
    public const string PillarIcon = "🕸️";          // Web/mesh visual

    private static readonly string[] DefaultNamespaces =
    [
        "Koan.ServiceMesh.",
        "Koan.ServiceMesh.",
        "Koan.ServiceDiscovery."
    ];

    /// <summary>
    /// Ensures the Services pillar is registered in the global pillar catalog.
    /// Should be called during service initialization.
    /// </summary>
    public static void EnsureRegistered()
    {
        var descriptor = new KoanPillarCatalog.PillarDescriptor(
            PillarCode,
            PillarLabel,
            PillarColorHex,
            PillarIcon
        );

        KoanPillarCatalog.RegisterDescriptor(descriptor);

        foreach (var prefix in DefaultNamespaces)
        {
            KoanPillarCatalog.AssociateNamespace(PillarCode, prefix);
        }
    }
}
