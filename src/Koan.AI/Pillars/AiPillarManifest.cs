using Koan.Core.Modules.Pillars;


namespace Koan.AI.Pillars;

public static class AiPillarManifest
{
    public const string PillarCode = "ai";
    public const string PillarLabel = "AI";
    public const string PillarColorHex = "#ec4899";
    public const string PillarIcon = "🧠";

    private static readonly PillarManifest Pillar = new(
        PillarCode, PillarLabel, PillarColorHex, PillarIcon,
        "Koan.AI",
        "Koan.Vector");

    public static void EnsureRegistered() => Pillar.EnsureRegistered();

    public static KoanPillarCatalog.PillarDescriptor Descriptor => Pillar.Descriptor;

    public static void AssociateNamespace(string namespacePrefix) => Pillar.AssociateNamespace(namespacePrefix);
}
