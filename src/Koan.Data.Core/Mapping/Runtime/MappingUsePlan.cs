using Koan.Data.Abstractions;

namespace Koan.Data.Core;

/// <summary>The exact compiled bindings and receipt for one logical path consumer.</summary>
public sealed class MappingUsePlan
{
    internal MappingUsePlan(MappingPath logicalPath, MappingConsumer consumer, MappingBindingPlan[] bindings, string planId)
    {
        LogicalPath = logicalPath;
        Consumer = consumer;
        Bindings = Array.AsReadOnly(bindings);
        Receipt = new MappingReceipt(
            planId,
            consumer,
            bindings.Select(static binding => binding.Id),
            consumer is MappingConsumer.Projection or MappingConsumer.Index);
    }

    public MappingPath LogicalPath { get; }
    public MappingConsumer Consumer { get; }
    public IReadOnlyList<MappingBindingPlan> Bindings { get; }
    public MappingReceipt Receipt { get; }

    public object? Encode(object? logical)
    {
        if (Bindings.Count != 1)
            throw new InvalidOperationException(
                $"Logical path '{LogicalPath}' resolves to {Bindings.Count} bindings; encode an identity or subtree through its complete plan.");
        return Bindings[0].Encode(logical);
    }
}
