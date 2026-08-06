using Koan.Data.Abstractions;

namespace Koan.Data.Relational.Mapping;

/// <summary>A complete provider-independent relational command decision ready for dialect lowering.</summary>
public sealed class RelationalCommandPlan
{
    public RelationalCommandPlan(
        RelationalOperationKind operation,
        StorageAddress container,
        IEnumerable<RelationalValue>? values,
        IEnumerable<RelationalValue>? identity,
        IEnumerable<RelationalValue>? conditions,
        IEnumerable<RelationalPathBinding>? reads,
        IEnumerable<RelationalPathBinding>? filters,
        IEnumerable<RelationalPathBinding>? orders,
        QueryDefinition? query,
        MappingReceipt receipt)
    {
        ArgumentNullException.ThrowIfNull(container);
        ArgumentNullException.ThrowIfNull(receipt);
        Operation = operation;
        Container = container;
        Values = Array.AsReadOnly((values ?? []).ToArray());
        Identity = Array.AsReadOnly((identity ?? []).ToArray());
        Conditions = Array.AsReadOnly((conditions ?? []).ToArray());
        Reads = Array.AsReadOnly((reads ?? []).DistinctBy(static item => item.BindingId, StringComparer.Ordinal).ToArray());
        Filters = Array.AsReadOnly((filters ?? []).DistinctBy(static item => item.BindingId, StringComparer.Ordinal).ToArray());
        Orders = Array.AsReadOnly((orders ?? []).DistinctBy(static item => item.BindingId, StringComparer.Ordinal).ToArray());
        Query = query;
        Receipt = receipt;
    }

    public RelationalOperationKind Operation { get; }
    public StorageAddress Container { get; }
    public IReadOnlyList<RelationalValue> Values { get; }
    public IReadOnlyList<RelationalValue> Identity { get; }
    public IReadOnlyList<RelationalValue> Conditions { get; }
    public IReadOnlyList<RelationalPathBinding> Reads { get; }
    public IReadOnlyList<RelationalPathBinding> Filters { get; }
    public IReadOnlyList<RelationalPathBinding> Orders { get; }
    public QueryDefinition? Query { get; }
    public MappingReceipt Receipt { get; }
}
