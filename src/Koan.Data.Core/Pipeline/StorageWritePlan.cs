using System.Runtime.CompilerServices;
using Koan.Core.Hosting.App;
using Koan.Data.Core.Metadata;
using Koan.Data.Core.Transfers;

namespace Koan.Data.Core.Pipeline;

/// <summary>One host-owned compiled write-stamp plan per live entity type.</summary>
internal sealed class StorageWritePlan
{
    private readonly IWriteStamp _identity;
    private readonly IWriteStamp[] _full;

    private StorageWritePlan(IWriteStamp identity, IWriteStamp[] full)
    {
        _identity = identity;
        _full = full;
    }

    public static StorageWritePlan For(Type entityType)
    {
        ArgumentNullException.ThrowIfNull(entityType);
        return (AppHost.Current?.GetService(typeof(StorageWritePlanCache)) as StorageWritePlanCache)?.For(entityType)
            ?? Build(entityType);
    }

    internal static StorageWritePlan Build(Type entityType)
    {
        var identity = new IdentityWriteStamp(entityType);
        var stamps = new List<IWriteStamp> { identity };
        var bag = new TimestampPropertyBag(entityType);
        if (bag.HasTimestamp) stamps.Add(new TimestampWriteStamp(bag));
        foreach (var contributor in StorageWriteContributorRegistry.All)
        {
            var stamp = contributor.Build(entityType);
            if (stamp is not null) stamps.Add(stamp);
        }
        return new StorageWritePlan(identity, stamps.OrderBy(static stamp => stamp.Priority).ToArray());
    }

    /// <summary>
    /// Stamps <paramref name="entity"/> for persistence. An exact transfer's write carries the stored values, so it gets
    /// the identity stamp only: a row without an identity is still given one, and nothing else is rewritten (DATA-0149).
    /// </summary>
    public void ApplyAll(object entity)
    {
        if (TransferFidelityScope.IsExact)
        {
            _identity.Apply(entity);
            return;
        }
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
