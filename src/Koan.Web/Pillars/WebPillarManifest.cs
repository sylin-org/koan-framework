using Koan.Core.Modules.Pillars;

namespace Koan.Web.Pillars;

public static class WebPillarManifest
{
    public const string PillarCode = "web";
    public const string PillarLabel = "Web";
    public const string PillarColorHex = "#8b5cf6";
    public const string PillarIcon = "🌐";

    private static readonly PillarManifest Pillar = new(
        PillarCode, PillarLabel, PillarColorHex, PillarIcon,
        "Koan.Web",
        "Koan.AspNetCore");

    public static void EnsureRegistered() => Pillar.EnsureRegistered();

    public static KoanPillarCatalog.PillarDescriptor Descriptor => Pillar.Descriptor;

    public static void AssociateNamespace(string namespacePrefix) => Pillar.AssociateNamespace(namespacePrefix);
}
