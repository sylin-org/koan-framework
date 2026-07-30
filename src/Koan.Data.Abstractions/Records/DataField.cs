namespace Koan.Data.Abstractions;

/// <summary>One provider-neutral field in the fixed shared shape of a <see cref="RecordSet"/>.</summary>
public sealed record DataField
{
    public DataField(
        int ordinal,
        string name,
        Type? clrType = null,
        string? providerTypeName = null,
        bool? isNullable = null)
    {
        if (ordinal < 0) throw new ArgumentOutOfRangeException(nameof(ordinal));
        ArgumentNullException.ThrowIfNull(name);
        Ordinal = ordinal;
        Name = name;
        ClrType = clrType;
        ProviderTypeName = providerTypeName;
        IsNullable = isNullable;
    }

    public int Ordinal { get; init; }
    public string Name { get; init; }
    public Type? ClrType { get; init; }
    public string? ProviderTypeName { get; init; }
    public bool? IsNullable { get; init; }
}
