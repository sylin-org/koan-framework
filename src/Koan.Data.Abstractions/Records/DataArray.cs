namespace Koan.Data.Abstractions;

/// <summary>Ordered provider-neutral structured sequence.</summary>
public sealed class DataArray
{
    public DataArray(IEnumerable<object?> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        Items = Array.AsReadOnly(items.Select(NeutralDataValue.Normalize).ToArray());
    }

    public IReadOnlyList<object?> Items { get; }
}
