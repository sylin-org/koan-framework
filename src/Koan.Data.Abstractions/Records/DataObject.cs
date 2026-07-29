namespace Koan.Data.Abstractions;

/// <summary>Ordered provider-neutral structured value. Property names may repeat.</summary>
public sealed class DataObject
{
    public DataObject(IEnumerable<DataProperty> properties)
    {
        ArgumentNullException.ThrowIfNull(properties);
        Properties = Array.AsReadOnly(properties.ToArray());
    }

    public IReadOnlyList<DataProperty> Properties { get; }
}
