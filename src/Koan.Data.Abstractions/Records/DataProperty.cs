namespace Koan.Data.Abstractions;

/// <summary>One ordered, duplicate-name-preserving property in a neutral <see cref="DataObject"/>.</summary>
public sealed record DataProperty
{
    public DataProperty(string name, object? value)
    {
        ArgumentNullException.ThrowIfNull(name);
        Name = name;
        Value = NeutralDataValue.Normalize(value);
    }

    public string Name { get; init; }
    public object? Value { get; init; }
}
