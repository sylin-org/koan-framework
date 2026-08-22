using Koan.Core.Modules.Pillars;

namespace Koan.Web.Auth.Pillars;

public static class SecurityPillarManifest
{
    public const string PillarCode = "security";
    public const string PillarLabel = "Security";
    public const string PillarColorHex = "#facc15";
    public const string PillarIcon = "🔐";

    private static readonly PillarManifest Pillar = new(
        PillarCode, PillarLabel, PillarColorHex, PillarIcon,
        "Koan.Auth",
        "Koan.Identity",
        "Koan.Security",
        "Koan.Web.Auth");

    public static void EnsureRegistered() => Pillar.EnsureRegistered();

    public static KoanPillarCatalog.PillarDescriptor Descriptor => Pillar.Descriptor;

    public static void AssociateNamespace(string namespacePrefix) => Pillar.AssociateNamespace(namespacePrefix);
}
