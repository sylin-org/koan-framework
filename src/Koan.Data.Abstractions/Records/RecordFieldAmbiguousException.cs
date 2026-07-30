namespace Koan.Data.Abstractions;

public sealed class RecordFieldAmbiguousException : InvalidOperationException
{
    public RecordFieldAmbiguousException(string name, IReadOnlyList<int> ordinals)
        : base($"Record field name '{name}' is ambiguous at ordinals {string.Join(", ", ordinals)}. Use ordinal access.")
    {
        Name = name;
        Ordinals = ordinals;
    }

    public string Name { get; }
    public IReadOnlyList<int> Ordinals { get; }
}
