namespace Koan.Data.Abstractions;

/// <summary>Stable evidence that a mapping consumer used bindings from one compiled plan.</summary>
public sealed record MappingReceipt
{
    public MappingReceipt(
        string planId,
        MappingConsumer consumer,
        IEnumerable<string> bindingIds,
        bool nativeProofRequired = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(planId);
        ArgumentNullException.ThrowIfNull(bindingIds);
        PlanId = planId;
        Consumer = consumer;
        BindingIds = Array.AsReadOnly(bindingIds.Distinct(StringComparer.Ordinal).ToArray());
        NativeProofRequired = nativeProofRequired;
    }

    public string PlanId { get; }
    public MappingConsumer Consumer { get; }
    public IReadOnlyList<string> BindingIds { get; }
    public bool NativeProofRequired { get; }
}
