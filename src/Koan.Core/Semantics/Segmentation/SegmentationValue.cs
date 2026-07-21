namespace Koan.Core.Semantics.Segmentation;

/// <summary>
/// Runtime value of one segmentation dimension. Values are operation data and never composition evidence.
/// </summary>
public readonly record struct SegmentationValue
{
    private SegmentationValue(SegmentationValueKind kind, string? value)
    {
        Kind = kind;
        Value = value;
    }

    public SegmentationValueKind Kind { get; }

    public string? Value { get; }

    public static SegmentationValue Missing { get; } = new(SegmentationValueKind.Missing, null);

    public static SegmentationValue Host { get; } = new(SegmentationValueKind.Host, null);

    public static SegmentationValue For(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        return new SegmentationValue(SegmentationValueKind.Concrete, value.Trim());
    }

    public override string ToString() => Kind switch
    {
        SegmentationValueKind.Concrete => "Concrete(<redacted>)",
        _ => Kind.ToString()
    };
}
