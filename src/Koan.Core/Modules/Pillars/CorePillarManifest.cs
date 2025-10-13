using System.Threading;

namespace Koan.Core.Modules.Pillars;

public static class CorePillarManifest
{
    public const string PillarCode = "core";
    public const string PillarLabel = "Core";
    public const string PillarColorHex = "#64748b";
    public const string PillarIcon = "⚙️";
    private static int _registered;

    public static void EnsureRegistered()
    {
        if (Volatile.Read(ref _registered) == 1)
        {
            return;
        }

        lock (typeof(CorePillarManifest))
        {
            if (_registered == 1)
            {
                return;
            }

            var descriptor = new KoanPillarCatalog.PillarDescriptor(PillarCode, PillarLabel, PillarColorHex, PillarIcon);
            KoanPillarCatalog.RegisterDescriptor(descriptor);
            KoanPillarCatalog.AssociateNamespace(PillarCode, "Koan.Core.");
            KoanPillarCatalog.AssociateNamespace(PillarCode, "Koan.Core");

            Volatile.Write(ref _registered, 1);
        }
    }

    public static KoanPillarCatalog.PillarDescriptor Descriptor
    {
        get
        {
            EnsureRegistered();
            return KoanPillarCatalog.RequireByCode(PillarCode);
        }
    }
}
