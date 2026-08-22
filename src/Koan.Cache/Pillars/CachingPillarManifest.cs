using Koan.Core.Modules.Pillars;


namespace Koan.Cache.Pillars;

public static class CachingPillarManifest
{
    public const string PillarCode = "caching";
    public const string PillarLabel = "Caching";
    public const string PillarColorHex = "#06b6d4";
    public const string PillarIcon = "🧊";

    private static readonly PillarManifest Pillar = new(
        PillarCode, PillarLabel, PillarColorHex, PillarIcon,
        "Koan.Cache",
        "Koan.DistributedCache");

    public static void EnsureRegistered() => Pillar.EnsureRegistered();

    public static KoanPillarCatalog.PillarDescriptor Descriptor => Pillar.Descriptor;

    public static void AssociateNamespace(string namespacePrefix) => Pillar.AssociateNamespace(namespacePrefix);
}
