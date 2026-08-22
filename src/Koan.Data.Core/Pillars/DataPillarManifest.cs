using Koan.Core.Modules.Pillars;

namespace Koan.Data.Core.Pillars;

public static class DataPillarManifest
{
    public const string PillarCode = "data";
    public const string PillarLabel = "Data";
    public const string PillarColorHex = "#38bdf8";
    public const string PillarIcon = "🗄️";

    private static readonly PillarManifest Pillar = new(
        PillarCode, PillarLabel, PillarColorHex, PillarIcon,
        "Koan.Data",
        "Koan.Connectors.Data",
        "Koan.Storage");

    public static void EnsureRegistered() => Pillar.EnsureRegistered();

    public static KoanPillarCatalog.PillarDescriptor Descriptor => Pillar.Descriptor;

    public static void AssociateNamespace(string namespacePrefix) => Pillar.AssociateNamespace(namespacePrefix);
}
