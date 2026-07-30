namespace Koan.Data.Abstractions;

/// <summary>Distinguishes canonical writable values from provider-maintained derived projections.</summary>
public enum MappingAuthority
{
    Canonical,
    Derived
}
