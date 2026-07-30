using System.Runtime.CompilerServices;
using Koan.Core.Hosting.App;
using Koan.Data.Core.Metadata;

namespace Koan.Data.Core.Pipeline;

/// <summary>One host-owned compiled write-stamp plan per live entity type.</summary>
internal sealed class StorageWritePlan
{
    private readonly IWriteStamp[] _full;
    private StorageWritePlan(IWriteStamp[] full) => _full = full;

    public static StorageWritePlan For(Type entityType)
    {
        ArgumentNullException.ThrowIfNull(entityType);
        return (AppHost.Current?.GetService(typeof(StorageWritePlanCache)) as StorageWritePlanCache)?.For(entityType)
            ?? Build(entityType);
    }

    internal static StorageWritePlan Build(Type entityType)
    {
        var stamps = new List<IWriteStamp> { new IdentityWriteStamp(entityType) };
        var bag = new TimestampPropertyBag(entityType);
        if (bag.HasTimestamp) stamps.Add(new TimestampWriteStamp(bag));
        foreach (var contributor in StorageWriteContributorRegistry.All)
        {
            var stamp = contributor.Build(entityType);
            if (stamp is not null) stamps.Add(stamp);
        }
        return new StorageWritePlan(stamps.OrderBy(static stamp => stamp.Priority).ToArray());
    }

    public void ApplyAll(object entity)
    {
        foreach (var stamp in _full) stamp.Apply(entity);
    }

    public void ApplyBatch(object entity) => ApplyAll(entity);
}

internal sealed class StorageWritePlanCache
{
    private ConditionalWeakTable<Type, StorageWritePlan> _plans = new();
    public StorageWritePlan For(Type type) => _plans.GetValue(type, StorageWritePlan.Build);
    public void Invalidate() => _plans = new ConditionalWeakTable<Type, StorageWritePlan>();
}
