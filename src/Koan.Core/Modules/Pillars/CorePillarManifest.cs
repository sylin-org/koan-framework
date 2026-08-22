namespace Koan.Core.Modules.Pillars;

public static class CorePillarManifest
{
    public const string PillarCode = "core";
    public const string PillarLabel = "Core";
    public const string PillarColorHex = "#64748b";
    public const string PillarIcon = "⚙️";

    private static readonly PillarManifest Pillar = new(
        PillarCode, PillarLabel, PillarColorHex, PillarIcon,
        "Koan.Core");

    public static void EnsureRegistered() => Pillar.EnsureRegistered();

    public static KoanPillarCatalog.PillarDescriptor Descriptor => Pillar.Descriptor;
}
