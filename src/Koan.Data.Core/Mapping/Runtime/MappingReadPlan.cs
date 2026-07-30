using Koan.Data.Abstractions;

namespace Koan.Data.Core;

/// <summary>The minimal distinct physical roots required for a logical projection.</summary>
public sealed class MappingReadPlan
{
    internal MappingReadPlan(IEnumerable<MappingBindingPlan> bindings, MappingReceipt receipt)
    {
        var copy = bindings.DistinctBy(static binding => binding.Id, StringComparer.Ordinal).ToArray();
        Bindings = Array.AsReadOnly(copy);
        PhysicalRoots = Array.AsReadOnly(copy.Select(static binding => binding.PhysicalPath.Name)
            .Distinct(StringComparer.Ordinal)
            .ToArray());
        Receipt = receipt;
    }

    public IReadOnlyList<MappingBindingPlan> Bindings { get; }
    public IReadOnlyList<string> PhysicalRoots { get; }
    public MappingReceipt Receipt { get; }
}
