using System.Collections.Generic;
using System.Threading;
using Koan.Core.Modules.Pillars;

namespace Koan.Messaging.Core.Pillars;

public static class MessagingPillarManifest
{
    public const string PillarCode = "messaging";
    public const string PillarLabel = "Messaging";
    public const string PillarColorHex = "#f97316";
    public const string PillarIcon = "🛰️";
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
            yield return "Koan.Messaging.";
            yield return "Koan.Messaging";
            yield return "Koan.Bus.";
            yield return "Koan.Bus";
            yield return "Koan.Broker.";
            yield return "Koan.Broker";
        }
    }
}
