using System.ComponentModel;

namespace Koan.Core.Semantics.Segmentation;

/// <summary>Host-compiled hard segmentation meaning contributed by one active capability.</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class SegmentationDimension
{
    internal SegmentationDimension(
        string owner,
        SemanticId id,
        Func<SegmentationValue> read,
        Func<Type, bool>? appliesTo,
        string correction)
    {
        Owner = owner;
        Id = id.Value;
        Read = read;
        AppliesTo = appliesTo;
        Correction = correction;
    }

    public string Owner { get; }

    public string Id { get; }

    internal Func<SegmentationValue> Read { get; }

    internal Func<Type, bool>? AppliesTo { get; }

    public string Correction { get; }
}
