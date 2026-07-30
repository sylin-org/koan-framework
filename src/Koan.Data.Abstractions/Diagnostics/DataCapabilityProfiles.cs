using Koan.Core.Capabilities;
using Koan.Data.Abstractions.Capabilities;

namespace Koan.Data.Abstractions;

/// <summary>The sole Data capability-to-primer-profile projection used by runtime and certification.</summary>
public static class DataCapabilityProfiles
{
    private static readonly IReadOnlyDictionary<Capability, string> Map =
        new Dictionary<Capability, string>
        {
            [DataCaps.Query.String] = DataClaimProfiles.EntityPersistence,
            [DataCaps.Query.Linq] = DataClaimProfiles.EntityPersistence,
            [DataCaps.Query.FastCount] = DataClaimProfiles.EntityPersistence,
            [DataCaps.Query.OptimizedCount] = DataClaimProfiles.EntityPersistence,
            [DataCaps.Query.Filter] = DataClaimProfiles.EntityPersistence,
            [DataCaps.Query.FilterExecution] = DataClaimProfiles.EntityPersistence,
            [DataCaps.Query.ProviderBoundedPaging] = DataClaimProfiles.ProviderBoundedPaging,
            [DataCaps.Write.BulkUpsert] = DataClaimProfiles.EntityPersistence,
            [DataCaps.Write.BulkDelete] = DataClaimProfiles.EntityPersistence,
            [DataCaps.Write.AtomicBatch] = DataClaimProfiles.AtomicBatch,
            [DataCaps.Write.MutationOutcomes] = DataClaimProfiles.EntityPersistence,
            [DataCaps.Write.FastRemove] = DataClaimProfiles.EntityPersistence,
            [DataCaps.Write.ConditionalReplace] = DataClaimProfiles.ConditionalReplace,
            [DataCaps.Isolation.RowScoped] = DataClaimProfiles.Isolation,
            [DataCaps.Isolation.ContainerScoped] = DataClaimProfiles.Isolation,
            [DataCaps.Isolation.DatabaseScoped] = DataClaimProfiles.Isolation,
            [DataCaps.Retention.TtlIndex] = DataClaimProfiles.NativeTtl,
        };

    public static IReadOnlyDictionary<Capability, string> All => Map;

    public static bool TryGet(Capability capability, out string profile)
    {
        return Map.TryGetValue(capability, out profile!);
    }
}
