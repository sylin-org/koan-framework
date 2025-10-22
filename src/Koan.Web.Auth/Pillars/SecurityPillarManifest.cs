using System.Collections.Generic;
using System.Threading;
using Koan.Core.Modules.Pillars;

namespace Koan.Web.Auth.Pillars;

public static class SecurityPillarManifest
{
    public const string PillarCode = "security";
    public const string PillarLabel = "Security";
    public const string PillarColorHex = "#facc15";
    public const string PillarIcon = "🔐";
    private static int _registered;
    private static readonly object Sync = new();

    public static void EnsureRegistered()
    {
        if (Volatile.Read(ref _registered) == 1)
        {
            return;
        }

        lock (Sync)
        {
            if (_registered == 1)
            {
                return;
            }

            var descriptor = new KoanPillarCatalog.PillarDescriptor(PillarCode, PillarLabel, PillarColorHex, PillarIcon);
            KoanPillarCatalog.RegisterDescriptor(descriptor);

            foreach (var prefix in DefaultNamespaces)
            {
                KoanPillarCatalog.AssociateNamespace(PillarCode, prefix);
            }

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

    public static void AssociateNamespace(string namespacePrefix)
    {
        EnsureRegistered();
        KoanPillarCatalog.AssociateNamespace(PillarCode, namespacePrefix);
    }

    private static IEnumerable<string> DefaultNamespaces
    {
        get
        {
            yield return "Koan.Auth.";
            yield return "Koan.Auth";
            yield return "Koan.Identity.";
            yield return "Koan.Identity";
            yield return "Koan.Security.";
            yield return "Koan.Security";
            yield return "Koan.Web.Auth.";
            yield return "Koan.Web.Auth";
        }
    }
}
