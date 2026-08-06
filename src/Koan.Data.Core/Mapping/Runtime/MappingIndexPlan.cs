using Koan.Data.Abstractions;

namespace Koan.Data.Core;

/// <summary>An index declaration resolved onto the same compiled bindings used by reads and writes.</summary>
public sealed class MappingIndexPlan
{
    internal MappingIndexPlan(
        string name,
        IEnumerable<MappingBindingPlan> bindings,
        bool unique,
        bool primary,
        bool ttl,
        string planId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var copy = bindings.ToArray();
        if (copy.Length == 0) throw new ArgumentException("An index requires at least one binding.", nameof(bindings));
        Name = name;
        Bindings = Array.AsReadOnly(copy);
        Unique = unique;
        Primary = primary;
        Ttl = ttl;
        Receipt = new MappingReceipt(planId, MappingConsumer.Index, copy.Select(static binding => binding.Id), nativeProofRequired: true);
    }

    public string Name { get; }
    public IReadOnlyList<MappingBindingPlan> Bindings { get; }
    public bool Unique { get; }
    public bool Primary { get; }
    public bool Ttl { get; }
    public MappingReceipt Receipt { get; }
}
